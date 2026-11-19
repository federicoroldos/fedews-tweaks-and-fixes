using System.Collections.Generic;
using Game.Common;
using Game.Pathfind;
using Game.Vehicles;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FedewsTweaks.Traffic
{
    /// <summary>
    /// Strict bus lanes for private cars: one extra route plan right after a car pulls out.
    ///
    /// A citizen chooses between their car, a taxi, transit and walking in the same route request that
    /// picks the car's route, so the strict bus-lane penalty can't go into that request without scaring
    /// people out of their cars (see <see cref="BusLanePatch"/>). The car then drives that route, and thanks
    /// to the parking reservations it rarely has a reason to plan again. So once the car is on the road
    /// with people in it, it is asked to plan its route once more. That request is made by the car, so it
    /// gets the strict penalty, and taxi and transit are taken out of it: the people already chose to
    /// drive, so the only choices left are which roads to take, where to park and the walk from there.
    ///
    /// While a car is waiting for that new route it doesn't count as a reservation, so the spot it was
    /// heading to doesn't look taken by itself and it can pick the same spot again.
    ///
    /// Runs inside <see cref="ParkingReservationSystem"/> because both look at the same moving cars every
    /// few frames, and the reservation count needs to know which cars are re-planning.
    /// </summary>
    public partial class ParkingReservationSystem
    {
        // Per-car progress. A car is only in the map while it is on the road; it is removed when it parks.
        internal const byte kSeen = 1;       // has pulled out; waiting for the main thread to arm it
        internal const byte kArmed = 2;      // its next route request gets the departure treatment
        internal const byte kRequested = 3;  // asked to plan again; waiting for the new route
        internal const byte kDone = 4;       // route is final for this trip

        /// <summary>
        /// Cars whose next route request is their departure re-plan. Read and cleared by
        /// <see cref="BusLanePatch"/> on the main thread when the request reaches the route planner.
        /// </summary>
        internal static readonly HashSet<Entity> s_Departing = new HashSet<Entity>();

        private NativeParallelHashMap<Entity, byte> m_Departures;
        private NativeList<Entity> m_NewDepartures;
        private NativeList<Entity> m_GoneDepartures;
        private NativeArray<int> m_ReplanCounter;
        private readonly List<Entity> m_Armed = new List<Entity>();
        private bool m_SeedDepartures = true;
        private bool m_DepartWasEnabled;
        private long m_ReplansSinceReport;

        private void CreateDepartures()
        {
            m_Departures = new NativeParallelHashMap<Entity, byte>(1024, Allocator.Persistent);
            m_NewDepartures = new NativeList<Entity>(Allocator.Persistent);
            m_GoneDepartures = new NativeList<Entity>(Allocator.Persistent);
            m_ReplanCounter = new NativeArray<int>(1, Allocator.Persistent);
        }

        private void DisposeDepartures()
        {
            m_Departures.Dispose();
            m_NewDepartures.Dispose();
            m_GoneDepartures.Dispose();
            m_ReplanCounter.Dispose();
            s_Departing.Clear();
        }

        /// <summary>Forget every car. The next scan treats all cars already on the road as done, so nothing re-plans in bulk.</summary>
        private void ResetDepartures()
        {
            m_Handle.Complete();
            m_Departures.Clear();
            m_NewDepartures.Clear();
            m_GoneDepartures.Clear();
            m_Armed.Clear();
            s_Departing.Clear();
            m_SeedDepartures = true;
        }

        /// <summary>Main thread, after the jobs finished: arms new departures and notices requests the route planner already took.</summary>
        private void ApplyDepartures()
        {
            for (int i = 0; i < m_GoneDepartures.Length; i++)
                s_Departing.Remove(m_GoneDepartures[i]);

            for (int i = 0; i < m_NewDepartures.Length; i++)
            {
                Entity car = m_NewDepartures[i];
                if (!m_Departures.TryGetValue(car, out byte state) || state != kSeen) continue;
                m_Departures[car] = kArmed;
                s_Departing.Add(car);
                m_Armed.Add(car);
            }

            for (int i = m_Armed.Count - 1; i >= 0; i--)
            {
                Entity car = m_Armed[i];
                if (!m_Departures.TryGetValue(car, out byte state) || state != kArmed)
                {
                    m_Armed.RemoveAt(i);            // the job asked it to plan again, or it's gone
                }
                else if (!s_Departing.Contains(car))
                {
                    m_Departures[car] = kRequested; // the game re-planned it on its own and that request got the treatment
                    m_Armed.RemoveAt(i);
                }
            }

            m_ReplansSinceReport += m_ReplanCounter[0];
            m_ReplanCounter[0] = 0;
        }

        private JobHandle ScheduleDepartures(JobHandle dependency)
        {
            m_NewDepartures.Clear();
            m_GoneDepartures.Clear();

            var depart = new DepartureJob
            {
                m_EntityType = GetEntityTypeHandle(),
                m_CurrentLaneType = GetComponentTypeHandle<CarCurrentLane>(true),
                m_PersonalCarType = GetComponentTypeHandle<Game.Vehicles.PersonalCar>(true),
                m_BicycleType = GetComponentTypeHandle<Bicycle>(true),
                m_PathOwnerType = GetComponentTypeHandle<PathOwner>(false),
                m_PathElementType = GetBufferTypeHandle<PathElement>(true),
                m_CarLaneData = GetComponentLookup<Game.Net.CarLane>(true),
                m_WaypointData = GetComponentLookup<Game.Routes.Waypoint>(true),
                m_TaxiStandData = GetComponentLookup<Game.Routes.TaxiStand>(true),
                m_Departures = m_Departures,
                m_New = m_NewDepartures,
                m_Gone = m_GoneDepartures,
                m_Counter = m_ReplanCounter,
                m_Seed = m_SeedDepartures,
            };
            m_SeedDepartures = false;
            JobHandle handle = JobChunkExtensions.Schedule(depart, m_CarQuery, dependency);

            var cleanup = new DepartureCleanupJob
            {
                m_EntityStorage = GetEntityStorageInfoLookup(),
                m_ParkedCarData = GetComponentLookup<ParkedCar>(true),
                m_DeletedData = GetComponentLookup<Deleted>(true),
                m_Departures = m_Departures,
                m_Gone = m_GoneDepartures,
            };
            return cleanup.Schedule(handle);
        }

        /// <summary>Single-threaded: walks the moving cars and moves each one along kSeen -> kArmed -> kRequested -> kDone.</summary>
        private struct DepartureJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle m_EntityType;
            [ReadOnly] public ComponentTypeHandle<CarCurrentLane> m_CurrentLaneType;
            [ReadOnly] public ComponentTypeHandle<Game.Vehicles.PersonalCar> m_PersonalCarType;
            [ReadOnly] public ComponentTypeHandle<Bicycle> m_BicycleType;
            public ComponentTypeHandle<PathOwner> m_PathOwnerType;
            [ReadOnly] public BufferTypeHandle<PathElement> m_PathElementType;
            [ReadOnly] public ComponentLookup<Game.Net.CarLane> m_CarLaneData;
            [ReadOnly] public ComponentLookup<Game.Routes.Waypoint> m_WaypointData;
            [ReadOnly] public ComponentLookup<Game.Routes.TaxiStand> m_TaxiStandData;
            public NativeParallelHashMap<Entity, byte> m_Departures;
            public NativeList<Entity> m_New;
            public NativeList<Entity> m_Gone;
            public NativeArray<int> m_Counter;
            public bool m_Seed;

            /// <summary>Transit and taxi stops show up on a route as a line waypoint or a taxi stand (same check the game's walkers use).</summary>
            private bool ContinuesByTransit(DynamicBuffer<PathElement> path, int from)
            {
                for (int k = math.max(0, from); k < path.Length; k++)
                {
                    Entity target = path[k].m_Target;
                    if (m_WaypointData.HasComponent(target) || m_TaxiStandData.HasComponent(target)) return true;
                }
                return false;
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<CarCurrentLane> currentLanes = chunk.GetNativeArray(ref m_CurrentLaneType);
                NativeArray<Game.Vehicles.PersonalCar> cars = chunk.GetNativeArray(ref m_PersonalCarType);
                NativeArray<PathOwner> owners = chunk.GetNativeArray(ref m_PathOwnerType);
                BufferAccessor<PathElement> paths = chunk.GetBufferAccessor(ref m_PathElementType);
                bool bicycles = chunk.Has(ref m_BicycleType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity car = entities[i];
                    if (!m_Departures.TryGetValue(car, out byte state))
                    {
                        // Already driving when the save loaded or the option was switched on, or a bicycle
                        // (bicycles don't use car lanes' rules): leave its route alone for this trip.
                        if (m_Seed || bicycles) { m_Departures.TryAdd(car, kDone); continue; }

                        // Wait until people are inside and the car has left the parking spot for a real road lane.
                        PersonalCarFlags flags = cars[i].m_State;
                        if ((flags & PersonalCarFlags.Transporting) == 0) continue;
                        if ((flags & (PersonalCarFlags.Boarding | PersonalCarFlags.Disembarking)) != 0) continue;
                        if (!m_CarLaneData.HasComponent(currentLanes[i].m_Lane)) continue;
                        if ((owners[i].m_State & PathFlags.Pending) != 0) continue;

                        // Park and ride: the rest of the trip (which the car carries, and hands to the people
                        // when they get out) continues by transit or taxi. A re-plan without transit would make
                        // them drive all the way, so keep the plan they made.
                        if (ContinuesByTransit(paths[i], owners[i].m_ElementIndex)) { m_Departures.TryAdd(car, kDone); continue; }

                        m_Departures.TryAdd(car, kSeen);
                        m_New.Add(car);
                        continue;
                    }

                    PathOwner owner = owners[i];
                    if (state == kArmed)
                    {
                        if ((owner.m_State & PathFlags.Divert) != 0)
                        {
                            // Heading somewhere special (e.g. an emergency): don't touch it
                            m_Departures[car] = kDone;
                            m_Gone.Add(car);
                            continue;
                        }
                        // Already waiting for a route, or the game is about to re-plan it anyway: try next time
                        if ((owner.m_State & (PathFlags.Pending | PathFlags.Failed | PathFlags.Obsolete | PathFlags.DivertObsolete | PathFlags.Stuck)) != 0)
                            continue;

                        owner.m_State |= PathFlags.Obsolete;   // the car AI plans a new route on its next update
                        owners[i] = owner;
                        m_Departures[car] = kRequested;
                        m_Counter[0]++;
                    }
                    else if (state == kRequested)
                    {
                        if ((owner.m_State & (PathFlags.Pending | PathFlags.Obsolete)) == 0)
                            m_Departures[car] = kDone;      // new route is in; it counts as a reservation again
                    }
                }
            }
        }

        /// <summary>Drops cars that parked or were removed, so their next trip starts fresh.</summary>
        private struct DepartureCleanupJob : IJob
        {
            [ReadOnly] public EntityStorageInfoLookup m_EntityStorage;
            [ReadOnly] public ComponentLookup<ParkedCar> m_ParkedCarData;
            [ReadOnly] public ComponentLookup<Deleted> m_DeletedData;
            public NativeParallelHashMap<Entity, byte> m_Departures;
            public NativeList<Entity> m_Gone;

            public void Execute()
            {
                using (NativeArray<Entity> cars = m_Departures.GetKeyArray(Allocator.Temp))
                {
                    for (int i = 0; i < cars.Length; i++)
                    {
                        Entity car = cars[i];
                        if (!m_EntityStorage.Exists(car) || m_ParkedCarData.HasComponent(car) || m_DeletedData.HasComponent(car))
                        {
                            m_Departures.Remove(car);
                            m_Gone.Add(car);
                        }
                    }
                }
            }
        }
    }
}
