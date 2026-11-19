using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FedewsTweaks.Traffic
{
    /// <summary>
    /// Parking spot reservations.
    ///
    /// In the base game, when a car plans a route it only checks whether a parking spot is free *right now*.
    /// It does not know that 30 other cars are already driving to that same spot. So when one spot frees up,
    /// every car nearby that plans (or re-plans) a route picks it, they all race there, one wins, and the
    /// rest re-plan again - which is the gridlock / U-turn / route-flipping mess.
    ///
    /// This system looks at every moving car, works out which parking lane or parking garage it is heading to,
    /// and counts those "reservations". When a parking lane or garage has as many cars heading to it as it has
    /// free spots, it is shown as full to the route planner, so new routes go somewhere else. Cars that already reserved it are
    /// not touched: they keep driving there normally, and the game's own parking logic is unchanged.
    /// </summary>
    public partial class ParkingReservationSystem : GameSystemBase
    {
        public const int kInterval = 8;
        private const int kTailScan = 256;          // route steps checked from the end when looking for the parking spot
        private const int kChecksPerReport = 450;   // 450 checks x 8 frames = about one in-game minute at 1x speed

        // Cost measurement, written to the log about once per in-game minute
        private static long s_CpuTicks;
        private static long s_CarsScanned;
        private int m_Checks;

        // Lanes we are currently showing as full, with the real value the game computed for them
        // (free space for street/lot parking, number of cars inside for garages).
        internal readonly Dictionary<Entity, float> m_Blocked = new Dictionary<Entity, float>();

        // Garages shown as full for this frame's route update only, with their real car count.
        // Drivers check the same number when they arrive, so it is put back right after the route planner read it.
        internal readonly List<KeyValuePair<Entity, ushort>> m_GarageRestore = new List<KeyValuePair<Entity, ushort>>();

        private EndFrameBarrier m_EndFrameBarrier;
        private EntityQuery m_CarQuery;
        private EntityQuery m_ParkingLaneQuery;

        private NativeQueue<Entity> m_Reservations;
        private NativeParallelHashMap<Entity, int> m_Counts;
        private NativeParallelHashMap<Entity, float> m_BlockedNative;
        private NativeList<Entity> m_ToBlock;
        private NativeList<float> m_ToBlockFree;
        private NativeList<Entity> m_ToUnblock;
        private JobHandle m_Handle;
        private bool m_Scheduled;
        private bool m_WasEnabled;
        private int m_LastLoggedBlocked = -1;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_CarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Vehicles.PersonalCar>(),
                    ComponentType.ReadOnly<CarCurrentLane>(),
                    ComponentType.ReadOnly<PathOwner>(),
                    ComponentType.ReadOnly<PathElement>(),
                    ComponentType.ReadOnly<CarNavigationLane>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<ParkedCar>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_ParkingLaneQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Net.ParkingLane>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());

            m_Reservations = new NativeQueue<Entity>(Allocator.Persistent);
            m_Counts = new NativeParallelHashMap<Entity, int>(1024, Allocator.Persistent);
            m_BlockedNative = new NativeParallelHashMap<Entity, float>(1024, Allocator.Persistent);
            m_ToBlock = new NativeList<Entity>(Allocator.Persistent);
            m_ToBlockFree = new NativeList<float>(Allocator.Persistent);
            m_ToUnblock = new NativeList<Entity>(Allocator.Persistent);
            CreateDepartures();
        }

        protected override void OnDestroy()
        {
            m_Handle.Complete();
            DisposeDepartures();
            m_Reservations.Dispose();
            m_Counts.Dispose();
            m_BlockedNative.Dispose();
            m_ToBlock.Dispose();
            m_ToBlockFree.Dispose();
            m_ToUnblock.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Handle.Complete();
            m_Scheduled = false;
            m_Blocked.Clear();
            m_LastLoggedBlocked = -1;
            ResetDepartures();

            if (!mode.IsGame()) return;

            // A save made while some spots were hidden would remember them as full. Ask the game to
            // recalculate every parking lane that currently looks full, so nothing stays stuck.
            using (NativeArray<Entity> lanes = m_ParkingLaneQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Game.Net.ParkingLane> data = m_ParkingLaneQuery.ToComponentDataArray<Game.Net.ParkingLane>(Allocator.Temp))
            {
                var refresh = new NativeList<Entity>(Allocator.Temp);
                for (int i = 0; i < lanes.Length; i++)
                {
                    if ((data[i].m_Flags & ParkingLaneFlags.VirtualLane) == 0 && data[i].m_FreeSpace <= 0f)
                        refresh.Add(lanes[i]);
                }
                if (refresh.Length > 0)
                    EntityManager.AddComponent<PathfindUpdated>(refresh.AsArray());
                Mod.log.Info($"Parking reservations: refreshed {refresh.Length} full-looking parking lanes after load");
                refresh.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            if (m_Scheduled)
            {
                m_Handle.Complete();
                m_Scheduled = false;
                ApplyResults();
                ApplyDepartures();
                ReportCost();
            }
            m_ToBlock.Clear();
            m_ToBlockFree.Clear();
            m_ToUnblock.Clear();

            bool enabled = Mod.Settings != null && Mod.Settings.SmartParking;
            if (enabled != m_WasEnabled)
            {
                m_WasEnabled = enabled;
                Mod.log.Info(enabled ? "Parking reservations: ON" : "Parking reservations: OFF (original game behaviour)");
            }
            if (!enabled && m_Blocked.Count > 0) UnblockAll();

            // Strict bus lanes: private cars plan their route once more after pulling out (DepartureReplan.cs)
            bool depart = Mod.Settings != null && Mod.Settings.StrictBusLanes;
            if (depart != m_DepartWasEnabled)
            {
                m_DepartWasEnabled = depart;
                ResetDepartures();
            }

            if (!enabled && !depart) return;

            JobHandle handle = Dependency;
            if (depart) handle = ScheduleDepartures(handle);

            if (enabled) handle = ScheduleReservations(handle);

            m_Handle = handle;
            m_Scheduled = true;
            Dependency = m_Handle;
        }

        private JobHandle ScheduleReservations(JobHandle dependency)
        {
            // Hand the current state to the jobs
            m_BlockedNative.Clear();
            if (m_BlockedNative.Capacity < m_Blocked.Count * 2) m_BlockedNative.Capacity = m_Blocked.Count * 2;
            foreach (var kv in m_Blocked) m_BlockedNative.TryAdd(kv.Key, kv.Value);
            m_Reservations.Clear();

            var scan = new FindReservationsJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_Departures = m_Departures,
                m_CurrentLaneType = GetComponentTypeHandle<CarCurrentLane>(true),
                m_PathOwnerType = GetComponentTypeHandle<PathOwner>(true),
                m_NavigationLaneType = GetBufferTypeHandle<CarNavigationLane>(true),
                m_PathElementType = GetBufferTypeHandle<PathElement>(true),
                m_ParkingLaneData = GetComponentLookup<Game.Net.ParkingLane>(true),
                m_GarageLaneData = GetComponentLookup<GarageLane>(true),
                m_ConnectionLaneData = GetComponentLookup<Game.Net.ConnectionLane>(true),
                m_Output = m_Reservations.AsParallelWriter(),
            };
            JobHandle scanHandle = JobChunkExtensions.ScheduleParallel(scan, m_CarQuery, dependency);

            var evaluate = new EvaluateJob
            {
                m_Reservations = m_Reservations,
                m_Counts = m_Counts,
                m_Blocked = m_BlockedNative,
                m_ParkingLaneData = GetComponentLookup<Game.Net.ParkingLane>(true),
                m_GarageLaneData = GetComponentLookup<GarageLane>(true),
                m_ConnectionLaneData = GetComponentLookup<Game.Net.ConnectionLane>(true),
                m_CurveData = GetComponentLookup<Curve>(true),
                m_PrefabRefData = GetComponentLookup<PrefabRef>(true),
                m_PrefabParkingLaneData = GetComponentLookup<ParkingLaneData>(true),
                m_ParkedCarData = GetComponentLookup<ParkedCar>(true),
                m_UnspawnedData = GetComponentLookup<Game.Objects.Unspawned>(true),
                m_DeletedData = GetComponentLookup<Deleted>(true),
                m_LaneObjects = GetBufferLookup<LaneObject>(true),
                m_ToBlock = m_ToBlock,
                m_ToBlockFree = m_ToBlockFree,
                m_ToUnblock = m_ToUnblock,
            };
            return evaluate.Schedule(scanHandle);
        }

        private void ApplyResults()
        {
            if (m_ToBlock.Length == 0 && m_ToUnblock.Length == 0) return;

            EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();
            for (int i = 0; i < m_ToUnblock.Length; i++)
            {
                Entity lane = m_ToUnblock[i];
                m_Blocked.Remove(lane);
                if (EntityManager.Exists(lane) && !EntityManager.HasComponent<Deleted>(lane))
                    ecb.AddComponent(lane, default(PathfindUpdated));   // game recalculates the real value
            }
            for (int i = 0; i < m_ToBlock.Length; i++)
            {
                Entity lane = m_ToBlock[i];
                if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)) continue;
                m_Blocked[lane] = m_ToBlockFree[i];
                ecb.AddComponent(lane, default(PathfindUpdated));       // re-sent to the route planner, as full
            }

            int n = m_Blocked.Count;
            if (m_LastLoggedBlocked < 0 || n >= m_LastLoggedBlocked * 2 || n * 2 <= m_LastLoggedBlocked && m_LastLoggedBlocked > 10)
            {
                m_LastLoggedBlocked = n;
                Mod.log.Info($"Parking reservations: {n} parking areas fully reserved right now");
            }
        }

        /// <summary>Writes the average CPU time per check to the log, so the cost can be seen on a real city.</summary>
        private void ReportCost()
        {
            if (++m_Checks < kChecksPerReport) return;
            if (m_WasEnabled)
            {
                double ms = s_CpuTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / m_Checks;
                long cars = s_CarsScanned / m_Checks;
                Mod.log.Info($"Parking reservations cost: {ms:0.00} ms of CPU per check (spread over worker threads), {cars} moving cars checked each time, one check every {kInterval} frames");
            }
            if (m_DepartWasEnabled)
                Mod.log.Info($"Strict bus lanes: {m_ReplansSinceReport} cars planned their route again after pulling out, since the last report");
            m_ReplansSinceReport = 0;
            m_Checks = 0;
            s_CpuTicks = 0;
            s_CarsScanned = 0;
        }

        private void UnblockAll()
        {
            m_Handle.Complete();
            EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();
            foreach (Entity lane in m_Blocked.Keys)
            {
                if (EntityManager.Exists(lane) && !EntityManager.HasComponent<Deleted>(lane))
                    ecb.AddComponent(lane, default(PathfindUpdated));
            }
            m_Blocked.Clear();
            m_LastLoggedBlocked = -1;
        }

        /// <summary>For every moving car: which parking lane is it on its way to (if any)?</summary>
        private struct FindReservationsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public NativeParallelHashMap<Entity, byte> m_Departures;
            [ReadOnly] public ComponentTypeHandle<CarCurrentLane> m_CurrentLaneType;
            [ReadOnly] public ComponentTypeHandle<PathOwner> m_PathOwnerType;
            [ReadOnly] public BufferTypeHandle<CarNavigationLane> m_NavigationLaneType;
            [ReadOnly] public BufferTypeHandle<PathElement> m_PathElementType;
            [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> m_ParkingLaneData;
            [ReadOnly] public ComponentLookup<GarageLane> m_GarageLaneData;
            [ReadOnly] public ComponentLookup<Game.Net.ConnectionLane> m_ConnectionLaneData;
            public NativeQueue<Entity>.ParallelWriter m_Output;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<CarCurrentLane> currentLanes = chunk.GetNativeArray(ref m_CurrentLaneType);
                NativeArray<PathOwner> pathOwners = chunk.GetNativeArray(ref m_PathOwnerType);
                BufferAccessor<CarNavigationLane> navigationLanes = chunk.GetBufferAccessor(ref m_NavigationLaneType);
                BufferAccessor<PathElement> paths = chunk.GetBufferAccessor(ref m_PathElementType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    // About to plan its route again after pulling out (strict bus lanes): don't let its old
                    // spot look taken by itself, so the new route can pick the same spot.
                    if (m_Departures.TryGetValue(entities[i], out byte departure) && departure != kDone) continue;

                    DynamicBuffer<CarNavigationLane> nav = navigationLanes[i];

                    // Already on the parking lane and nothing after it: this car is parking there right now.
                    // (A car that is pulling OUT of a spot has the road ahead of it, so it doesn't count here.)
                    Entity current = currentLanes[i].m_Lane;
                    if (nav.Length == 0 && IsParking(current)) { m_Output.Enqueue(current); continue; }

                    bool found = false;
                    for (int j = 0; j < nav.Length; j++)
                    {
                        if (IsParking(nav[j].m_Lane)) { m_Output.Enqueue(nav[j].m_Lane); found = true; break; }
                    }
                    if (found) continue;

                    PathOwner owner = pathOwners[i];
                    if ((owner.m_State & PathFlags.Failed) != 0) continue;

                    // The parking spot is where the driving part of the trip ends, so look from the END of the
                    // route backwards: it is found within a few steps instead of walking the whole route.
                    // Routes that don't end in parking (e.g. leaving the city) give up after kTailScan steps.
                    DynamicBuffer<PathElement> path = paths[i];
                    int first = math.max(0, owner.m_ElementIndex);
                    int stop = math.max(first, path.Length - kTailScan);
                    for (int k = path.Length - 1; k >= stop; k--)
                    {
                        Entity target = path[k].m_Target;
                        if (IsParking(target)) { m_Output.Enqueue(target); break; }
                    }
                }

                System.Threading.Interlocked.Add(ref s_CpuTicks, System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
                System.Threading.Interlocked.Add(ref s_CarsScanned, chunk.Count);
            }

            private bool IsParking(Entity lane)
            {
                if (lane == Entity.Null) return false;
                if (m_ParkingLaneData.TryGetComponent(lane, out Game.Net.ParkingLane p))
                    return (p.m_Flags & ParkingLaneFlags.VirtualLane) == 0;
                return IsGarage(lane, ref m_GarageLaneData, ref m_ConnectionLaneData);
            }
        }

        /// <summary>A parking garage / parking building entrance inside the city (not an outside connection).</summary>
        internal static bool IsGarage(Entity lane, ref ComponentLookup<GarageLane> garages, ref ComponentLookup<Game.Net.ConnectionLane> connections)
        {
            return garages.HasComponent(lane)
                && connections.TryGetComponent(lane, out Game.Net.ConnectionLane c)
                && (c.m_Flags & ConnectionLaneFlags.Parking) != 0
                && (c.m_Flags & ConnectionLaneFlags.Outside) == 0;
        }

        /// <summary>Counts reservations per parking lane and decides which lanes to show as full.</summary>
        private struct EvaluateJob : IJob
        {
            public NativeQueue<Entity> m_Reservations;
            public NativeParallelHashMap<Entity, int> m_Counts;
            [ReadOnly] public NativeParallelHashMap<Entity, float> m_Blocked;
            [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> m_ParkingLaneData;
            [ReadOnly] public ComponentLookup<GarageLane> m_GarageLaneData;
            [ReadOnly] public ComponentLookup<Game.Net.ConnectionLane> m_ConnectionLaneData;
            [ReadOnly] public ComponentLookup<Curve> m_CurveData;
            [ReadOnly] public ComponentLookup<PrefabRef> m_PrefabRefData;
            [ReadOnly] public ComponentLookup<ParkingLaneData> m_PrefabParkingLaneData;
            [ReadOnly] public ComponentLookup<ParkedCar> m_ParkedCarData;
            [ReadOnly] public ComponentLookup<Game.Objects.Unspawned> m_UnspawnedData;
            [ReadOnly] public ComponentLookup<Deleted> m_DeletedData;
            [ReadOnly] public BufferLookup<LaneObject> m_LaneObjects;
            public NativeList<Entity> m_ToBlock;
            public NativeList<float> m_ToBlockFree;
            public NativeList<Entity> m_ToUnblock;

            public void Execute()
            {
                long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                m_Counts.Clear();
                int needed = m_Reservations.Count;
                if (m_Counts.Capacity < needed) m_Counts.Capacity = needed;
                while (m_Reservations.TryDequeue(out Entity lane))
                {
                    if (m_Counts.TryGetValue(lane, out int c)) m_Counts[lane] = c + 1;
                    else m_Counts.TryAdd(lane, 1);
                }

                var keep = new NativeParallelHashSet<Entity>(math.max(16, m_Blocked.Count()), Allocator.Temp);

                using (NativeKeyValueArrays<Entity, int> pairs = m_Counts.GetKeyValueArrays(Allocator.Temp))
                {
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        Entity lane = pairs.Keys[i];
                        int reserved = pairs.Values[i];
                        if (m_DeletedData.HasComponent(lane)) continue;

                        bool isBlocked = m_Blocked.TryGetValue(lane, out float realValue);
                        int free;
                        if (m_ParkingLaneData.TryGetComponent(lane, out Game.Net.ParkingLane parking))
                        {
                            // Street / parking lot: m_FreeSpace stays at 0 while blocked, so use the remembered real value
                            if (!isBlocked) realValue = parking.m_FreeSpace;
                            free = FreeSpots(lane, parking, realValue);
                        }
                        else if (IsGarage(lane, ref m_GarageLaneData, ref m_ConnectionLaneData))
                        {
                            // Garage: the "full" value only lasts for an instant, so the stored count is always the real one
                            if ((m_ConnectionLaneData[lane].m_Flags & ConnectionLaneFlags.Disabled) != 0) continue;
                            GarageLane garage = m_GarageLaneData[lane];
                            realValue = garage.m_VehicleCount;
                            free = garage.m_VehicleCapacity - garage.m_VehicleCount;
                        }
                        else continue;

                        if (free > 0 && reserved >= free)
                        {
                            keep.Add(lane);
                            if (!isBlocked)
                            {
                                m_ToBlock.Add(lane);
                                m_ToBlockFree.Add(realValue);
                            }
                        }
                    }
                }

                using (NativeArray<Entity> blocked = m_Blocked.GetKeyArray(Allocator.Temp))
                {
                    for (int i = 0; i < blocked.Length; i++)
                    {
                        if (!keep.Contains(blocked[i])) m_ToUnblock.Add(blocked[i]);
                    }
                }
                keep.Dispose();
                System.Threading.Interlocked.Add(ref s_CpuTicks, System.Diagnostics.Stopwatch.GetTimestamp() - startTicks);
            }

            /// <summary>How many cars could still physically park on this lane right now.</summary>
            private int FreeSpots(Entity lane, Game.Net.ParkingLane parking, float freeGap)
            {
                if (!m_PrefabRefData.TryGetComponent(lane, out PrefabRef prefabRef)
                    || !m_PrefabParkingLaneData.TryGetComponent(prefabRef.m_Prefab, out ParkingLaneData laneData))
                    return 0;

                if (laneData.m_SlotInterval != 0f && m_CurveData.TryGetComponent(lane, out Curve curve))
                {
                    // Marked parking spots: spots minus parked cars
                    int slots = NetUtils.GetParkingSlotCount(curve, parking, laneData);
                    int parked = 0;
                    if (m_LaneObjects.TryGetBuffer(lane, out DynamicBuffer<LaneObject> objects))
                    {
                        for (int i = 0; i < objects.Length; i++)
                        {
                            Entity o = objects[i].m_LaneObject;
                            if (m_ParkedCarData.HasComponent(o) && !m_UnspawnedData.HasComponent(o)) parked++;
                        }
                    }
                    return slots - parked;
                }

                // Unmarked curb parking: estimate from the biggest free gap
                float carLength = VehicleUtils.GetParkingSize(laneData).y;
                if (carLength < 1f) carLength = 5f;
                if (freeGap < carLength) return 0;
                return math.max(1, (int)(freeGap / carLength));
            }
        }
    }

    /// <summary>
    /// Runs every frame right after the game recalculates free parking space and right before that
    /// information is sent to the route planner. For lanes that are fully reserved it remembers the real
    /// value and then reports the lane as full.
    /// </summary>
    public partial class ParkingReservationApplySystem : GameSystemBase
    {
        private ParkingReservationSystem m_Reservations;
        private readonly List<Entity> m_Touched = new List<Entity>();
        private readonly List<Entity> m_Gone = new List<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Reservations = World.GetOrCreateSystemManaged<ParkingReservationSystem>();
        }

        protected override void OnUpdate()
        {
            Dictionary<Entity, float> blocked = m_Reservations.m_Blocked;
            if (blocked.Count == 0) return;

            m_Touched.Clear();
            m_Gone.Clear();
            foreach (Entity lane in blocked.Keys)
            {
                if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)) { m_Gone.Add(lane); continue; }
                if (EntityManager.HasComponent<PathfindUpdated>(lane) || EntityManager.HasComponent<Updated>(lane))
                    m_Touched.Add(lane);
            }
            foreach (Entity lane in m_Gone) blocked.Remove(lane);
            if (m_Touched.Count == 0) return;

            foreach (Entity lane in m_Touched)
            {
                if (EntityManager.HasComponent<Game.Net.ParkingLane>(lane))
                {
                    // Street / parking lot. Drivers look at the actual cars on the lane, not at this number,
                    // so it can stay at 0 until the lane is refreshed again.
                    Game.Net.ParkingLane parking = EntityManager.GetComponentData<Game.Net.ParkingLane>(lane);
                    blocked[lane] = parking.m_FreeSpace;   // fresh real value from the game
                    parking.m_FreeSpace = 0f;              // what the route planner will see
                    EntityManager.SetComponentData(lane, parking);
                }
                else if (EntityManager.HasComponent<GarageLane>(lane))
                {
                    // Garage. Drivers on their way DO check this number, so it is only shown as full while the
                    // route planner reads it; ParkingReservationRestoreSystem puts the real number back right after.
                    GarageLane garage = EntityManager.GetComponentData<GarageLane>(lane);
                    blocked[lane] = garage.m_VehicleCount;
                    m_Reservations.m_GarageRestore.Add(new KeyValuePair<Entity, ushort>(lane, garage.m_VehicleCount));
                    garage.m_VehicleCount = garage.m_VehicleCapacity;
                    EntityManager.SetComponentData(lane, garage);
                }
                else blocked.Remove(lane);
            }
        }
    }

    /// <summary>
    /// Runs right after the route planner has read the lanes: gives garages their real car count back, so cars
    /// that already reserved a spot there see it as free when they arrive and drive in normally.
    /// </summary>
    public partial class ParkingReservationRestoreSystem : GameSystemBase
    {
        private ParkingReservationSystem m_Reservations;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Reservations = World.GetOrCreateSystemManaged<ParkingReservationSystem>();
        }

        protected override void OnUpdate()
        {
            List<KeyValuePair<Entity, ushort>> restore = m_Reservations.m_GarageRestore;
            if (restore.Count == 0) return;

            foreach (KeyValuePair<Entity, ushort> item in restore)
            {
                if (!EntityManager.Exists(item.Key) || !EntityManager.HasComponent<GarageLane>(item.Key)) continue;
                GarageLane garage = EntityManager.GetComponentData<GarageLane>(item.Key);
                garage.m_VehicleCount = item.Value;
                EntityManager.SetComponentData(item.Key, garage);
            }
            restore.Clear();
        }
    }
}
