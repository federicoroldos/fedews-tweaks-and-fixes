using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Pathfind;
using HarmonyLib;
using Unity.Entities;

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
    ///
    /// Only requests made by a vehicle that is already on the road are changed. A citizen planning a trip
    /// also chooses between their car, a taxi, transit and walking in that same request, and the game
    /// charges bus-lane use per metre, so on a street whose driveways and curb come off the bus lane the
    /// multiplied cost made driving there look many minutes slower. Taxis ignore the rule, so citizens
    /// switched to taxis and walking and their own cars stayed home. A car that is already driving can't
    /// switch modes, so for it the multiplier only changes the route.
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
                            && m.GetParameters().Length > 1
                            && m.GetParameters()[0].ParameterType == typeof(PathfindAction)
                            && m.GetParameters()[1].ParameterType == typeof(Entity));
        }

        private static void Prefix(PathfindAction action, Entity owner)
        {
            // A private car planning its route again right after pulling out (see DepartureReplan.cs)
            bool departure = owner != Entity.Null && ParkingReservationSystem.s_Departing.Remove(owner);

            Setting s = Mod.Settings;
            if (s == null || !s.StrictBusLanes || !action.m_Data.IsCreated)
                return;

            ref PathfindParameters p = ref action.data.m_Parameters;

            if ((p.m_Methods & PathMethod.Road) == 0) return;                          // not a driving route
            if ((p.m_IgnoredRules & RuleFlags.ForbidPrivateTraffic) != 0) return;     // allowed in bus lanes
            if (p.m_Weights.m_Value.y <= 0f) return;                                   // doesn't care about rules at all
            if (!IsVehicle(owner)) return;                                             // a trip being planned, not a car re-routing

            // The people in the car already chose to drive. Keep it that way: only roads, parking and the
            // walk from the spot are left to choose from, so the penalty can't push them into a taxi or a bus.
            // The citizens' cost limit (17000) is also lifted: the multiplied penalty alone passes it after a few
            // hundred metres of bus lane, and a failed request makes everyone get out of the car on the spot.
            // The car already has a working route there, so this one can't come back empty-handed.
            if (departure)
            {
                p.m_Methods &= ~(PathMethod.Taxi | PathMethod.PublicTransportDay | PathMethod.PublicTransportNight);
                p.m_MaxCost = 0f;   // 0 = no limit
            }

            p.m_Weights.m_Value.y *= s.BusLaneStrictness;

            if (++s_Adjusted == 1 || s_Adjusted % 50000 == 0)
                Mod.log.Info($"Strict bus lanes active: {s_Adjusted} car routes adjusted so far");
        }

        private static bool IsVehicle(Entity owner)
        {
            if (owner == Entity.Null) return false;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            EntityManager em = world.EntityManager;
            return em.Exists(owner) && em.HasComponent<Game.Vehicles.Vehicle>(owner);
        }
    }
}
