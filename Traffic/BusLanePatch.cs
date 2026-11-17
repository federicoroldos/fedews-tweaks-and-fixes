using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Pathfind;
using HarmonyLib;

namespace FedewsTweaks.Traffic
{
    /// <summary>
    /// In the base game a bus lane is not forbidden for private cars, it only adds a small
    /// "rule-breaking" cost while planning a route (about the same as a few seconds of waiting for an
    /// impatient driver). So cars jump into bus lanes to skip queues and then have to merge back out.
    ///
    /// Every route request passes through PathfindQueueSystem.Enqueue. For requests from vehicles that
    /// do NOT have bus-lane permission (anything that does not ignore the ForbidPrivateTraffic rule),
    /// this multiplies how much the driver cares about rule-breaking. In the game's own cost tables that
    /// only covers bus lanes, banned roads/vehicle bans, illegal U-turns, entering restricted areas and
    /// unsafe pedestrian crossings - normal driving costs are untouched.
    /// Buses, taxis and emergency/service vehicles ignore the rule, so they are skipped entirely.
    /// </summary>
    [HarmonyPatch]
    public static class BusLanePatch
    {
        private static int s_Adjusted;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(PathfindQueueSystem)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == nameof(PathfindQueueSystem.Enqueue)
                            && m.GetParameters().Length > 0
                            && m.GetParameters()[0].ParameterType == typeof(PathfindAction));
        }

        private static void Prefix(PathfindAction action)
        {
            Setting s = Mod.Settings;
            if (s == null || !s.StrictBusLanes || !action.m_Data.IsCreated)
                return;

            ref PathfindParameters p = ref action.data.m_Parameters;

            if ((p.m_Methods & PathMethod.Road) == 0) return;                          // not a driving route
            if ((p.m_IgnoredRules & RuleFlags.ForbidPrivateTraffic) != 0) return;     // allowed in bus lanes
            if (p.m_Weights.m_Value.y <= 0f) return;                                   // doesn't care about rules at all

            p.m_Weights.m_Value.y *= s.BusLaneStrictness;

            if (++s_Adjusted == 1 || s_Adjusted % 50000 == 0)
                Mod.log.Info($"Strict bus lanes active: {s_Adjusted} car routes adjusted so far");
        }
    }
}
