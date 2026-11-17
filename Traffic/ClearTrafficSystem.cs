using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Creatures;
using Game.Vehicles;
using Unity.Collections;
using Colossal.Entities;
using Unity.Entities;

namespace FedewsTweaks.Traffic
{
    /// <summary>
    /// "Clear traffic jams" button. Removes every MOVING private car, taxi and delivery/cargo truck
    /// (including the ones queued at highway entrances and through-traffic), the same way the game removes
    /// a vehicle that reached the end of its trip.
    ///
    /// Not touched: parked cars, buses/trams/trains, emergency and service vehicles, planes, ships.
    ///
    /// Anyone riding in a removed car is not deleted from the city: they simply "arrive" where they were
    /// going (or at home if that is unknown), just like when the game gives up on a stuck citizen.
    /// </summary>
    public partial class ClearTrafficSystem : GameSystemBase
    {
        private static bool s_Requested;
        public static void Request() => s_Requested = true;

        private EndFrameBarrier m_EndFrameBarrier;
        private EntityQuery m_VehicleQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_VehicleQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Vehicle>() },
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
        }

        protected override void OnUpdate()
        {
            if (!s_Requested) return;
            s_Requested = false;

            int vehicles = 0, people = 0;
            EntityCommandBuffer ecb = m_EndFrameBarrier.CreateCommandBuffer();
            using (NativeArray<Entity> found = m_VehicleQuery.ToEntityArray(Allocator.Temp))
            {
                var done = new NativeParallelHashSet<Entity>(found.Length * 2 + 16, Allocator.Temp);
                foreach (Entity vehicle in found)
                {
                    // Trailers are removed together with the truck pulling them
                    Entity root = vehicle;
                    if (EntityManager.TryGetComponent(vehicle, out Controller controller) && controller.m_Controller != Entity.Null
                        && EntityManager.Exists(controller.m_Controller))
                        root = controller.m_Controller;
                    if (!done.Add(root)) continue;
                    if (EntityManager.HasComponent<ParkedCar>(root)) continue;

                    if (EntityManager.TryGetBuffer(root, true, out DynamicBuffer<LayoutElement> layout) && layout.Length != 0)
                    {
                        for (int i = 0; i < layout.Length; i++)
                        {
                            Entity part = layout[i].m_Vehicle;
                            if (part == root || done.Add(part))
                                people += SendPassengersOnTheirWay(ecb, part);
                            ecb.AddComponent(part, default(Deleted));
                        }
                    }
                    else
                    {
                        people += SendPassengersOnTheirWay(ecb, root);
                        ecb.AddComponent(root, default(Deleted));
                    }
                    vehicles++;
                }
                done.Dispose();
            }

            Mod.log.Info($"Clear traffic jams: removed {vehicles} moving cars/taxis/trucks, {people} passengers sent straight to where they were going");
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
