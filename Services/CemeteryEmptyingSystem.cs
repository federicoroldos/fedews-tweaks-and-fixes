using System.Collections.Generic;
using Colossal.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FedewsTweaks.Services
{
    /// <summary>
    /// Lets crematoriums work through the bodies stored in cemeteries.
    ///
    /// In the base game a cemetery only stores bodies (DeathcareFacility.m_LongTermStoredCount, a plain number,
    /// the citizens themselves are already deleted) and nothing ever takes them out, so cemeteries fill up for good
    /// while crematoriums sit idle. There is no "Empty" button either: that only exists for landfills.
    ///
    /// Each crematorium already processes its own m_LongTermStoredCount before fresh bodies
    /// (DeathcareFacilityAISystem: "while (m_LongTermStoredCount > 0 && m_ProcessingState >= 1)"), even though the
    /// game never puts anything there for a crematorium. So when a crematorium has nothing waiting from the city, this
    /// moves a small batch from the fullest cemetery into that number and the crematorium burns through it with its
    /// normal speed. Fresh bodies from the city come first: a crematorium with any body waiting gets nothing extra.
    ///
    /// No vehicles drive between the buildings; the bodies are moved as a number. The moved bodies are part of the
    /// crematorium's normal save data and the game finishes them on its own, so the mod can be removed at any time.
    /// </summary>
    public partial class CemeteryEmptyingSystem : GameSystemBase
    {
        // DeathcareFacilityAISystem ticks every building every 256 frames; 262144 frames = 1 in-game day
        public const int kInterval = 1024;
        private const float kChecksPerDay = 262144f / kInterval;

        private EntityQuery m_FacilityQuery;
        private SimulationSystem m_SimulationSystem;
        private bool m_WasEnabled;
        private bool m_ReportedBuildings;
        private int m_MovedToday;
        private uint m_NextReportFrame;

        private struct Cemetery
        {
            public Entity entity;
            public int stored;
            public int capacity;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_FacilityQuery = GetEntityQuery(
                ComponentType.ReadWrite<Game.Buildings.DeathcareFacility>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Building>(),
                ComponentType.Exclude<Game.Buildings.ServiceUpgrade>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            RequireForUpdate(m_FacilityQuery);
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_ReportedBuildings = false;
            m_MovedToday = 0;
            m_NextReportFrame = 0;
        }

        protected override void OnUpdate()
        {
            bool enabled = Mod.Settings != null && Mod.Settings.CremateCemeteryBodies;
            if (enabled != m_WasEnabled)
            {
                m_WasEnabled = enabled;
                Mod.log.Info(enabled ? "Crematoriums empty cemeteries: ON" : "Crematoriums empty cemeteries: OFF (original game behaviour)");
            }
            if (!enabled)
                return;

            var cemeteries = new List<Cemetery>();
            var crematoriums = new List<(Entity entity, float rate, int capacity)>();

            using (NativeArray<Entity> entities = m_FacilityQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (!TryGetStats(entity, out DeathcareFacilityData data))
                        continue;
                    var facility = EntityManager.GetComponentData<Game.Buildings.DeathcareFacility>(entity);

                    if (data.m_LongTermStorage)
                    {
                        if (facility.m_LongTermStoredCount > 0)
                            cemeteries.Add(new Cemetery { entity = entity, stored = facility.m_LongTermStoredCount, capacity = math.max(1, data.m_StorageCapacity) });
                    }
                    else if (data.m_ProcessingRate > 0f && data.m_StorageCapacity > 0 && EntityManager.HasBuffer<Patient>(entity))
                    {
                        crematoriums.Add((entity, data.m_ProcessingRate * GetEfficiency(entity), data.m_StorageCapacity));
                    }
                }
            }

            if (!m_ReportedBuildings)
            {
                m_ReportedBuildings = true;
                int stored = 0;
                foreach (var c in cemeteries) stored += c.stored;
                float speed = 0f;
                foreach (var c in crematoriums) speed += c.rate;
                Mod.log.Info($"Crematoriums empty cemeteries: {cemeteries.Count} cemeteries holding bodies ({stored} in total), {crematoriums.Count} crematoriums able to burn {speed:0.#} bodies per in-game day together");
            }

            if (cemeteries.Count > 0)
            {
                foreach (var (entity, rate, capacity) in crematoriums)
                {
                    if (rate <= 0f)
                        continue; // closed, no workers, no power...
                    if (EntityManager.GetBuffer<Patient>(entity, true).Length > 0)
                        continue; // bodies from the city are waiting here; they come first

                    // Enough to keep it busy until the next check (with a bit of slack), never more than half its room,
                    // so it always has space for hearses arriving from the city.
                    int target = math.min((int)math.ceil(rate / kChecksPerDay * 2f), capacity / 2);
                    target = math.max(1, target);

                    var facility = EntityManager.GetComponentData<Game.Buildings.DeathcareFacility>(entity);
                    int wanted = target - facility.m_LongTermStoredCount;
                    if (wanted <= 0)
                        continue;

                    int moved = TakeFromFullestCemetery(cemeteries, wanted);
                    if (moved == 0)
                        break; // every cemetery is empty now
                    facility.m_LongTermStoredCount += moved;
                    EntityManager.SetComponentData(entity, facility);
                    m_MovedToday += moved;
                }
            }

            uint frame = m_SimulationSystem.frameIndex;
            if (frame >= m_NextReportFrame)
            {
                if (m_NextReportFrame != 0 && m_MovedToday > 0)
                {
                    int left = 0;
                    foreach (var c in cemeteries) left += c.stored;
                    Mod.log.Info($"Crematoriums empty cemeteries: {m_MovedToday} cemetery bodies sent to crematoriums in the last in-game day, {left} still in cemeteries");
                }
                m_MovedToday = 0;
                m_NextReportFrame = frame + 262144;
            }
        }

        private int TakeFromFullestCemetery(List<Cemetery> cemeteries, int wanted)
        {
            int taken = 0;
            while (taken < wanted && cemeteries.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < cemeteries.Count; i++)
                {
                    if ((float)cemeteries[i].stored / cemeteries[i].capacity > (float)cemeteries[best].stored / cemeteries[best].capacity)
                        best = i;
                }

                Cemetery c = cemeteries[best];
                int n = math.min(wanted - taken, c.stored);
                c.stored -= n;
                taken += n;

                var facility = EntityManager.GetComponentData<Game.Buildings.DeathcareFacility>(c.entity);
                facility.m_LongTermStoredCount = math.max(0, facility.m_LongTermStoredCount - n);
                EntityManager.SetComponentData(c.entity, facility);

                if (c.stored <= 0)
                    cemeteries.RemoveAt(best);
                else
                    cemeteries[best] = c;
            }
            return taken;
        }

        /// <summary>Same formula as BuildingUtils.GetEfficiency (workers, power, budget... multiplied together).</summary>
        private float GetEfficiency(Entity entity)
        {
            if (!EntityManager.HasBuffer<Efficiency>(entity))
                return 1f;
            DynamicBuffer<Efficiency> buffer = EntityManager.GetBuffer<Efficiency>(entity, true);
            float value = 1f;
            for (int i = 0; i < buffer.Length; i++)
                value *= math.max(0f, buffer[i].m_Efficiency);
            return value > 0f ? math.max(0.01f, math.round(100f * value) / 100f) : 0f;
        }

        /// <summary>The building's deathcare stats including its active upgrades, same as the game computes them.</summary>
        private bool TryGetStats(Entity entity, out DeathcareFacilityData data)
        {
            data = default;
            PrefabRef prefab = EntityManager.GetComponentData<PrefabRef>(entity);
            if (!EntityManager.TryGetComponent(prefab.m_Prefab, out data))
                return false;
            if (EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<InstalledUpgrade> upgrades))
                UpgradeUtils.CombineStats(EntityManager, ref data, upgrades);
            return true;
        }
    }
}
