using System.Collections.Generic;
using Colossal.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Economy;
using Game.Policies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FedewsTweaks.Services
{
    /// <summary>
    /// Switches a landfill's "Empty" mode off once it has been emptied.
    ///
    /// In the base game the "Empty" option (BuildingOption.Empty) is only ever read by GarbageFacilityAISystem and
    /// nothing ever clears it. While it is set, the landfill's available space counts as 0, so its garbage trucks stay
    /// parked and it accepts no garbage. A landfill that was emptied and then forgotten stays out of service forever.
    ///
    /// This sends the same policy change the info panel's button sends (a Modify event handled by
    /// Game.Policies.ModifiedSystem), so the building ends up exactly as if the player had clicked it off.
    /// </summary>
    public partial class LandfillRefillSystem : GameSystemBase
    {
        public const int kInterval = 1024;

        // A landfill counts as empty once less than this share of its room is still in use
        private const float kEmptyShare = 0.01f;

        private EntityQuery m_LandfillQuery;
        private EntityQuery m_PolicyQuery;
        private EndFrameBarrier m_EndFrameBarrier;
        private Entity m_EmptyPolicy;
        private bool m_WasEnabled;
        private readonly HashSet<Entity> m_SentLastTime = new HashSet<Entity>();

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_LandfillQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.GarbageFacility>(),
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Game.Buildings.ServiceUpgrade>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
            m_PolicyQuery = GetEntityQuery(ComponentType.ReadOnly<PolicyData>(), ComponentType.ReadOnly<BuildingOptionData>());
            RequireForUpdate(m_LandfillQuery);
        }

        protected override void OnUpdate()
        {
            bool enabled = Mod.Settings != null && Mod.Settings.AutoStopLandfillEmptying;
            if (enabled != m_WasEnabled)
            {
                m_WasEnabled = enabled;
                Mod.log.Info(enabled ? "Landfills stop emptying when empty: ON" : "Landfills stop emptying when empty: OFF (original game behaviour)");
            }
            if (!enabled)
                return;

            if (m_EmptyPolicy == Entity.Null || !EntityManager.Exists(m_EmptyPolicy))
            {
                m_EmptyPolicy = FindEmptyPolicy();
                if (m_EmptyPolicy == Entity.Null)
                    return;
            }

            var sentNow = new HashSet<Entity>();
            using (NativeArray<Entity> entities = m_LandfillQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    Building building = EntityManager.GetComponentData<Building>(entity);
                    if (!BuildingUtils.CheckOption(building, BuildingOption.Empty))
                        continue;
                    if (!TryGetStats(entity, out GarbageFacilityData data) || !data.m_LongTermStorage)
                        continue;

                    GetContents(entity, data, out int garbage, out int capacity);
                    if (garbage > capacity * kEmptyShare)
                        continue;

                    // The event is handled a frame later; don't send it twice in a row if the game is still catching up
                    if (m_SentLastTime.Contains(entity))
                        continue;

                    EntityCommandBuffer buffer = m_EndFrameBarrier.CreateCommandBuffer();
                    Entity e = buffer.CreateEntity();
                    buffer.AddComponent(e, default(Game.Common.Event));
                    buffer.AddComponent(e, new Modify(entity, m_EmptyPolicy, active: false, adjustment: 0f));
                    sentNow.Add(entity);
                    Mod.log.Info($"Landfill {entity.Index} is empty ({garbage} of {capacity} garbage left): switched its \"Empty\" mode off so it collects garbage again");
                }
            }
            m_SentLastTime.Clear();
            m_SentLastTime.UnionWith(sentNow);
        }

        private Entity FindEmptyPolicy()
        {
            using (NativeArray<Entity> policies = m_PolicyQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity policy in policies)
                {
                    if (BuildingUtils.HasOption(EntityManager.GetComponentData<BuildingOptionData>(policy), BuildingOption.Empty))
                        return policy;
                }
            }
            return Entity.Null;
        }

        /// <summary>Garbage in the building plus its dumping areas, the same places GarbageFacilityAISystem counts.</summary>
        private void GetContents(Entity entity, GarbageFacilityData data, out int garbage, out int capacity)
        {
            garbage = 0;
            capacity = data.m_GarbageCapacity;
            if (EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<Resources> resources))
                garbage += EconomyUtils.GetResources(Resource.Garbage, resources);

            if (!EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<Game.Areas.SubArea> subAreas))
                return;
            for (int i = 0; i < subAreas.Length; i++)
            {
                Entity area = subAreas[i].m_Area;
                if (!EntityManager.TryGetComponent(area, out Storage storage))
                    continue;
                garbage += storage.m_Amount;
                if (EntityManager.TryGetComponent(area, out PrefabRef areaPrefab)
                    && EntityManager.TryGetComponent(areaPrefab.m_Prefab, out StorageAreaData storageData)
                    && EntityManager.TryGetComponent(area, out Geometry geometry))
                {
                    capacity += AreaUtils.CalculateStorageCapacity(geometry, storageData);
                }
            }
            capacity = math.max(1, capacity);
        }

        private bool TryGetStats(Entity entity, out GarbageFacilityData data)
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
