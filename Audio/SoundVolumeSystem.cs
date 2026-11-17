using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Prefabs.Effects;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace FedewsTweaks.Audio
{
    /// <summary>
    /// Gives every world sound a group (helicopters, cars, buildings...) based on which kind of object
    /// plays it, and scales the sound's base volume by that group's slider. The game re-reads these
    /// volumes every frame, so changes apply instantly, even to sounds that are already playing.
    /// </summary>
    public partial class SoundVolumeSystem : GameSystemBase
    {
        // Order only matters for how the sound list is printed.
        private enum Group { Siren = 0, Rocket, Helicopter, Plane, Train, Ship, Car, Creature, Building, Other }

        private class Entry
        {
            public SFX Sfx;
            public float Original;
            public bool Siren;
            public string EffectName;
            public readonly HashSet<Group> Groups = new HashSet<Group>();
            public readonly Dictionary<Group, string> OwnerExamples = new Dictionary<Group, string>();

            // Sirens/horns always follow the siren slider. A sound shared by several kinds of objects
            // (for example one engine sound used by both buses and trams) follows the lowest of their
            // sliders, so turning any of them down always works.
            public float Factor()
            {
                if (Siren) return SoundVolumeSystem.Factor(Group.Siren);
                float f = 1f;
                foreach (Group g in Groups) f = Math.Min(f, SoundVolumeSystem.Factor(g));
                return f;
            }

            public Group MainGroup => Siren ? Group.Siren : (Groups.Count > 0 ? Groups.Min() : Group.Other);
        }

        private static bool s_DumpRequested;
        public static void RequestDump() => s_DumpRequested = true;

        private readonly Dictionary<SFX, Entry> m_Entries = new Dictionary<SFX, Entry>();
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_OwnerQuery;
        private EntityQuery m_GroupAmbienceQuery;
        private EntityQuery m_CameraAmbienceQuery;
        private bool m_Dirty;
        private bool m_AutoDumped;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_OwnerQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<Effect>());
            m_GroupAmbienceQuery = GetEntityQuery(ComponentType.ReadOnly<AudioGroupingSettingsData>());
            m_CameraAmbienceQuery = GetEntityQuery(ComponentType.ReadOnly<AmbientAudioEffect>());
            Setting.Changed += OnSettingChanged;
        }

        protected override void OnDestroy()
        {
            Setting.Changed -= OnSettingChanged;
            base.OnDestroy();
        }

        private void OnSettingChanged() => m_Dirty = true;

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            Scan();
            Apply();
            if (!m_AutoDumped && m_Entries.Count > 0)
            {
                m_AutoDumped = true;   // one sound list per session in the log, handy for troubleshooting
                Dump();
            }
        }

        protected override void OnUpdate()
        {
            if (m_Dirty)
            {
                m_Dirty = false;
                Apply();
            }
            if (s_DumpRequested)
            {
                s_DumpRequested = false;
                if (m_Entries.Count == 0) Scan();
                Dump();
            }
        }

        private void Scan()
        {
            int before = m_Entries.Count;

            // Sounds attached to objects: vehicles, buildings, props, people, animals...
            using (NativeArray<Entity> owners = m_OwnerQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity owner in owners)
                {
                    if (!m_PrefabSystem.TryGetPrefab(owner, out PrefabBase ownerPrefab)) continue;
                    Group ownerGroup = GroupFor(ownerPrefab);
                    var visited = new HashSet<Entity>();
                    foreach (Effect effect in EntityManager.GetBuffer<Effect>(owner, true))
                        Collect(effect.m_Effect, ownerGroup, ownerPrefab.name, visited, 0);
                }
            }

            // Background "group" sounds: the city traffic hum, neighbourhood/industry ambience, forests, rain...
            using (NativeArray<Entity> settings = m_GroupAmbienceQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in settings)
                {
                    foreach (AudioGroupingSettingsData a in EntityManager.GetBuffer<AudioGroupingSettingsData>(e, true))
                    {
                        Group g = GroupForAmbience(a.m_Type);
                        string name = "background " + a.m_Type;
                        Collect(a.m_GroupSoundFar, g, name, new HashSet<Entity>(), 0);
                        Collect(a.m_GroupSoundNear, g, name, new HashSet<Entity>(), 0);
                    }
                }
            }

            // Camera ambience (wind etc.)
            using (NativeArray<Entity> settings = m_CameraAmbienceQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in settings)
                {
                    foreach (AmbientAudioEffect a in EntityManager.GetBuffer<AmbientAudioEffect>(e, true))
                        Collect(a.m_Effect, Group.Other, "camera ambience", new HashSet<Entity>(), 0);
                }
            }

            Mod.log.Info($"Sound scan: {m_Entries.Count} sounds grouped ({m_Entries.Count - before} new)");
        }

        private void Collect(Entity effectEntity, Group ownerGroup, string ownerName, HashSet<Entity> visited, int depth)
        {
            if (effectEntity == Entity.Null || depth > 5 || !EntityManager.Exists(effectEntity) || !visited.Add(effectEntity)) return;

            m_PrefabSystem.TryGetPrefab(effectEntity, out PrefabBase effectPrefab);
            string effectName = effectPrefab != null ? effectPrefab.name : "?";

            if (EntityManager.HasBuffer<AudioSourceData>(effectEntity))
            {
                foreach (AudioSourceData src in EntityManager.GetBuffer<AudioSourceData>(effectEntity, true))
                {
                    if (!m_PrefabSystem.TryGetPrefab(src.m_SFXEntity, out PrefabBase sfxPrefab)) continue;
                    if (!sfxPrefab.TryGet(out SFX sfx) || sfx == null) continue;

                    if (!m_Entries.TryGetValue(sfx, out Entry entry))
                    {
                        entry = new Entry { Sfx = sfx, Original = sfx.m_Volume, EffectName = sfxPrefab.name };
                        m_Entries[sfx] = entry;
                    }
                    entry.Siren |= IsSiren(effectName) || IsSiren(sfxPrefab.name);
                    if (entry.Groups.Add(ownerGroup)) entry.OwnerExamples[ownerGroup] = ownerName;
                }
            }

            // Sounds that pick a random clip from a list (doors, brakes, horns, ...)
            if (EntityManager.HasBuffer<AudioRandomizeData>(effectEntity))
            {
                foreach (AudioRandomizeData r in EntityManager.GetBuffer<AudioRandomizeData>(effectEntity, true))
                    Collect(r.m_SFXEntity, ownerGroup, ownerName, visited, depth + 1);
            }

            // Some effects are bundles of other effects
            if (EntityManager.HasBuffer<Effect>(effectEntity))
            {
                foreach (Effect sub in EntityManager.GetBuffer<Effect>(effectEntity, true))
                    Collect(sub.m_Effect, ownerGroup, ownerName, visited, depth + 1);
            }
        }

        private static bool IsSiren(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("siren", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("horn", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Group GroupFor(PrefabBase prefab)
        {
            switch (prefab)
            {
                case RocketPrefab _: return Group.Rocket;           // must come before helicopter (rocket is a kind of helicopter in code)
                case HelicopterPrefab _: return Group.Helicopter;
                case AircraftPrefab _: return Group.Plane;
                case TrainPrefab _: return Group.Train;
                case WatercraftPrefab _: return Group.Ship;
                case CarBasePrefab _: return Group.Car;             // cars, buses, trucks, trailers
                case VehiclePrefab _: return Group.Car;             // bicycles and any other road vehicle
                case CreaturePrefab _: return Group.Creature;
                case StaticObjectPrefab _: return Group.Building;   // buildings, extensions, props
                default: return Group.Other;
            }
        }

        private static Group GroupForAmbience(GroupAmbienceType type)
        {
            switch (type)
            {
                case GroupAmbienceType.Traffic: return Group.Car;
                case GroupAmbienceType.Forest:
                case GroupAmbienceType.NightForest:
                case GroupAmbienceType.Rain:
                case GroupAmbienceType.SeagullAmbience:
                    return Group.Other;
                default: return Group.Building;   // neighbourhoods, shops, offices, industry, farms
            }
        }

        private static float Factor(Group group)
        {
            Setting s = Mod.Settings;
            if (s == null) return 1f;
            int pct;
            switch (group)
            {
                case Group.Helicopter: pct = s.HelicopterVolume; break;
                case Group.Rocket: pct = s.RocketVolume; break;
                case Group.Plane: pct = s.PlaneVolume; break;
                case Group.Car: pct = s.CarVolume; break;
                case Group.Siren: pct = s.SirenVolume; break;
                case Group.Train: pct = s.TrainVolume; break;
                case Group.Ship: pct = s.ShipVolume; break;
                case Group.Building: pct = s.BuildingVolume; break;
                case Group.Creature: pct = s.CreatureVolume; break;
                default: pct = s.OtherVolume; break;
            }
            return Math.Max(0, Math.Min(100, pct)) / 100f;
        }

        private void Apply()
        {
            foreach (Entry e in m_Entries.Values)
            {
                if (e.Sfx == null) continue;
                e.Sfx.m_Volume = e.Original * e.Factor();
            }
        }

        private void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== Sound list ({m_Entries.Count} sounds) =====");
            foreach (var g in m_Entries.Values.GroupBy(e => e.MainGroup).OrderBy(g => g.Key))
            {
                sb.AppendLine($"--- {g.Key} ({g.Count()})");
                foreach (Entry e in g.OrderBy(e => e.EffectName))
                {
                    string owners = string.Join(", ", e.OwnerExamples.OrderBy(o => o.Key).Select(o => $"{o.Key}: {o.Value}"));
                    sb.AppendLine($"    {e.EffectName}   volume {e.Original:0.00} x{e.Factor():0.00}{(e.Siren ? " [siren/horn]" : "")}   ({owners})");
                }
            }
            Mod.log.Info(sb.ToString());
        }
    }
}
