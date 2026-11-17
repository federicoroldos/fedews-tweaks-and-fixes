using Colossal.Entities;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Creatures;
using Game.PSI;
using Game.Simulation;
using Game.UI.Localization;
using Game.Vehicles;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FedewsTweaks.Traffic
{
    /// <summary>
    /// Two traffic emergency buttons.
    ///
    /// "Remove stuck traffic": takes a snapshot of where every moving private car, taxi and delivery truck
    /// is, lets the game run for a short while (unpausing it if needed), then removes only the ones that
    /// barely moved. Nothing runs in the background when the button isn't being used.
    ///
    /// "Remove ALL moving traffic": removes every moving private car, taxi and delivery truck right away.
    ///
    /// In both cases vehicles are removed the same way the game removes a vehicle at the end of its trip,
    /// and anyone riding inside simply "arrives" where they were going (or at home if that is unknown).
    /// Parked cars, buses/trams/trains, emergency and service vehicles, planes and ships are never touched.
    /// </summary>
    public partial class ClearTrafficSystem : GameSystemBase
    {
        private const string kNotificationId = "FedewsTweaks.ClearTraffic";
        private const int kFramesPerSecond = 60;     // simulation frames per in-game second at 1x speed
        private const float kStuckDistance = 10f;    // moved less than this (metres) during the watch = stuck

        private enum State { Idle, Watching }

        private static bool s_StuckRequested;
        private static bool s_AllRequested;
        public static void RequestStuck() => s_StuckRequested = true;
        public static void RequestAll() => s_AllRequested = true;

        // Shown live on the options page
        public static string Status { get; private set; } = "Ready.";
        public static int StatusVersion { get; private set; }

        private EndFrameBarrier m_EndFrameBarrier;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_VehicleQuery;

        private State m_State;
        private NativeParallelHashMap<Entity, float3> m_Snapshot;
        private uint m_StartFrame;
        private uint m_WatchFrames;
        private bool m_WeUnpaused;
        private int m_LastShownPercent = -1;
        private bool m_LastShownPaused;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_VehicleQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Vehicle>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                Any = new[]
                {
                    ComponentType.ReadOnly<Game.Vehicles.PersonalCar>(),
                    ComponentType.ReadOnly<Game.Vehicles.Taxi>(),
                    ComponentType.ReadOnly<Game.Vehicles.DeliveryTruck>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<ParkedCar>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            m_Snapshot = new NativeParallelHashMap<Entity, float3>(1024, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_Snapshot.Dispose();
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (m_State != State.Idle) Cancel("Cancelled: a city was loaded.");
            s_StuckRequested = s_AllRequested = false;
        }

        protected override void OnUpdate()
        {
            if (s_AllRequested)
            {
                s_AllRequested = false;
                RemoveAll();
            }

            if (s_StuckRequested)
            {
                s_StuckRequested = false;
                if (m_State == State.Idle) StartWatching();
            }

            if (m_State == State.Watching) Watch();
        }

        // ---------------- Remove stuck traffic ----------------

        private void StartWatching()
        {
            if (m_VehicleQuery.IsEmptyIgnoreFilter)
            {
                SetStatus("Nothing to do: no moving cars found (is a city loaded?).");
                return;
            }

            m_Snapshot.Clear();
            using (NativeArray<Entity> entities = m_VehicleQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Game.Objects.Transform> transforms = m_VehicleQuery.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
            {
                if (m_Snapshot.Capacity < entities.Length) m_Snapshot.Capacity = entities.Length;
                for (int i = 0; i < entities.Length; i++)
                    m_Snapshot.TryAdd(entities[i], transforms[i].m_Position);
            }

            int seconds = Mod.Settings != null ? Mod.Settings.StuckSeconds : 40;
            m_WatchFrames = (uint)(math.max(5, seconds) * kFramesPerSecond);
            m_StartFrame = m_SimulationSystem.frameIndex;
            m_State = State.Watching;
            m_LastShownPercent = -1;

            m_WeUnpaused = Unpause();

            Mod.log.Info($"Remove stuck traffic: watching {m_Snapshot.Count()} moving vehicles for {seconds} in-game seconds{(m_WeUnpaused ? " (game unpaused)" : "")}");
            ShowProgress(0, paused: false);
        }

        private void Watch()
        {
            uint elapsed = m_SimulationSystem.frameIndex - m_StartFrame;
            if (elapsed < m_WatchFrames)
            {
                int percent = (int)(elapsed * 100 / m_WatchFrames);
                bool paused = m_SimulationSystem.selectedSpeed <= 0f;
                if (percent != m_LastShownPercent || paused != m_LastShownPaused) ShowProgress(percent, paused);
                return;
            }

            // Time's up: remove what barely moved
            int vehicles = 0, people = 0;
            EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var done = new NativeParallelHashSet<Entity>(m_Snapshot.Count() + 16, Allocator.Temp);
            using (NativeKeyValueArrays<Entity, float3> pairs = m_Snapshot.GetKeyValueArrays(Allocator.Temp))
            {
                for (int i = 0; i < pairs.Length; i++)
                {
                    Entity vehicle = pairs.Keys[i];
                    if (!EntityManager.Exists(vehicle) || EntityManager.HasComponent<Deleted>(vehicle) || EntityManager.HasComponent<ParkedCar>(vehicle))
                        continue;
                    if (!EntityManager.TryGetComponent(vehicle, out Game.Objects.Transform now)) continue;
                    if (math.distance(now.m_Position, pairs.Values[i]) >= kStuckDistance) continue;

                    if (RemoveVehicle(ecb, vehicle, done, ref people)) vehicles++;
                }
            }
            done.Dispose();
            m_Snapshot.Clear();
            m_State = State.Idle;

            string result = vehicles == 0
                ? "Done: no stuck vehicles found."
                : $"Done: removed {vehicles} stuck vehicles. Parked cars were not touched. {people} passengers were sent straight to where they were going.";
            Mod.log.Info("Remove stuck traffic: " + result);
            SetStatus(result);
            NotificationSystem.Pop(kNotificationId, 6f,
                title: LocalizedString.Value("Remove stuck traffic"),
                text: LocalizedString.Value(result),
                progressState: ProgressState.Complete, progress: 100);
        }

        /// <summary>
        /// Unpauses the game if the player paused it. While a menu (like Options) is open the game is paused
        /// by itself and resumes on its own when the menu closes; in that case nothing is changed. Resumes at
        /// the speed the player was using before pausing.
        /// </summary>
        private bool Unpause()
        {
            if (m_SimulationSystem.selectedSpeed > 0f) return false;
            var timeUI = World.GetExistingSystemManaged<Game.UI.InGame.TimeUISystem>();
            Traverse t = timeUI != null ? Traverse.Create(timeUI) : null;
            bool resumesByItself = false;
            float speed = 1f;
            try
            {
                if (t != null)
                {
                    resumesByItself = t.Field("m_UnpausedBeforeForcedPause").GetValue<bool>();
                    speed = t.Field("m_SpeedBeforePause").GetValue<float>();
                }
            }
            catch { }
            if (resumesByItself) return false;
            m_SimulationSystem.selectedSpeed = speed > 0f ? speed : 1f;
            return true;
        }

        private void ShowProgress(int percent, bool paused)
        {
            m_LastShownPercent = percent;
            m_LastShownPaused = paused;
            string text = paused
                ? $"Waiting for the game to run (close the menu)... {percent}%"
                : $"Watching which cars don't move... {percent}%";
            SetStatus(text);
            NotificationSystem.Push(kNotificationId,
                title: LocalizedString.Value("Remove stuck traffic"),
                text: LocalizedString.Value(text),
                progressState: ProgressState.Progressing, progress: percent);
        }

        private void Cancel(string why)
        {
            m_Snapshot.Clear();
            m_State = State.Idle;
            m_WeUnpaused = false;
            SetStatus(why);
            NotificationSystem.Pop(kNotificationId, 4f,
                title: LocalizedString.Value("Remove stuck traffic"),
                text: LocalizedString.Value(why),
                progressState: ProgressState.Cancelled);
        }

        private static void SetStatus(string text)
        {
            Status = text;
            StatusVersion++;
        }

        // ---------------- Remove ALL moving traffic ----------------

        private void RemoveAll()
        {
            int vehicles = 0, people = 0;
            EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();
            using (NativeArray<Entity> found = m_VehicleQuery.ToEntityArray(Allocator.Temp))
            {
                var done = new NativeParallelHashSet<Entity>(found.Length * 2 + 16, Allocator.Temp);
                foreach (Entity vehicle in found)
                {
                    if (RemoveVehicle(ecb, vehicle, done, ref people)) vehicles++;
                }
                done.Dispose();
            }

            string result = $"Removed all {vehicles} driving cars, taxis and trucks. Parked cars were not touched. {people} passengers were sent straight to where they were going.";
            Mod.log.Info("Remove all moving traffic: " + result);
            SetStatus(result);
            NotificationSystem.Pop(kNotificationId + ".All", 6f,
                title: LocalizedString.Value("Remove all moving traffic"),
                text: LocalizedString.Value(result),
                progressState: ProgressState.Complete, progress: 100);
        }

        // ---------------- Shared ----------------

        /// <summary>Removes a vehicle together with its trailers, and sends its passengers on their way.</summary>
        private bool RemoveVehicle(EntityCommandBuffer ecb, Entity vehicle, NativeParallelHashSet<Entity> done, ref int people)
        {
            Entity root = vehicle;
            if (EntityManager.TryGetComponent(vehicle, out Controller controller) && controller.m_Controller != Entity.Null
                && EntityManager.Exists(controller.m_Controller))
                root = controller.m_Controller;
            if (!done.Add(root)) return false;
            if (EntityManager.HasComponent<ParkedCar>(root) || EntityManager.HasComponent<Deleted>(root)) return false;

            if (EntityManager.TryGetBuffer(root, true, out DynamicBuffer<LayoutElement> layout) && layout.Length != 0)
            {
                for (int i = 0; i < layout.Length; i++)
                {
                    Entity part = layout[i].m_Vehicle;
                    if (part != root) done.Add(part);
                    people += SendPassengersOnTheirWay(ecb, part);
                    ecb.AddComponent(part, default(Deleted));
                }
            }
            else
            {
                people += SendPassengersOnTheirWay(ecb, root);
                ecb.AddComponent(root, default(Deleted));
            }
            return true;
        }

        private int SendPassengersOnTheirWay(EntityCommandBuffer ecb, Entity vehicle)
        {
            if (!EntityManager.TryGetBuffer(vehicle, true, out DynamicBuffer<Passenger> passengers)) return 0;

            int count = 0;
            for (int i = 0; i < passengers.Length; i++)
            {
                Entity creature = passengers[i].m_Passenger;
                if (!EntityManager.Exists(creature) || EntityManager.HasComponent<Deleted>(creature)) continue;

                if (EntityManager.TryGetComponent(creature, out Resident resident) && EntityManager.Exists(resident.m_Citizen))
                {
                    Entity citizen = resident.m_Citizen;
                    Entity destination = Entity.Null;
                    if (EntityManager.TryGetComponent(creature, out Target target) && IsPlace(target.m_Target))
                        destination = target.m_Target;
                    if (destination == Entity.Null)
                        destination = HomeOf(citizen);

                    if (EntityManager.HasComponent<CurrentTransport>(citizen))
                        ecb.RemoveComponent<CurrentTransport>(citizen);
                    if (destination != Entity.Null)
                    {
                        ecb.AddComponent(citizen, new CurrentBuilding(destination));
                        if (EntityManager.HasComponent<Arrived>(citizen))
                            ecb.SetComponentEnabled<Arrived>(citizen, true);
                    }
                    count++;
                }
                else if (EntityManager.TryGetComponent(creature, out Pet pet) && EntityManager.Exists(pet.m_HouseholdPet))
                {
                    if (EntityManager.HasComponent<CurrentTransport>(pet.m_HouseholdPet))
                        ecb.RemoveComponent<CurrentTransport>(pet.m_HouseholdPet);
                    if (EntityManager.TryGetComponent(pet.m_HouseholdPet, out HouseholdPet householdPet))
                    {
                        Entity home = HomeOfHousehold(householdPet.m_Household);
                        if (home != Entity.Null) ecb.AddComponent(pet.m_HouseholdPet, new CurrentBuilding(home));
                    }
                }

                ecb.AddComponent(creature, default(Deleted));
            }
            return count;
        }

        private bool IsPlace(Entity e)
        {
            return e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e)
                && (EntityManager.HasComponent<Building>(e) || EntityManager.HasComponent<Game.Objects.OutsideConnection>(e));
        }

        private Entity HomeOf(Entity citizen)
        {
            if (!EntityManager.TryGetComponent(citizen, out HouseholdMember member)) return Entity.Null;
            return HomeOfHousehold(member.m_Household);
        }

        private Entity HomeOfHousehold(Entity household)
        {
            if (household != Entity.Null && EntityManager.TryGetComponent(household, out PropertyRenter renter) && IsPlace(renter.m_Property))
                return renter.m_Property;
            return Entity.Null;
        }
    }
}
