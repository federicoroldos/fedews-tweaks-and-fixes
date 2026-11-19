using Game;
using Game.Pathfind;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace FedewsTweaks.Services
{
    /// <summary>
    /// Stops city service calls from waiting hours before trying again.
    ///
    /// A building that needs a service (garbage pickup, a hearse, an ambulance, a fire truck, police at an accident or
    /// crime) creates one request entity and keeps it until a vehicle arrives. Each service's dispatch system
    /// (GarbageCollectorDispatchSystem, HealthcareDispatchSystem, FireRescueDispatchSystem,
    /// PoliceEmergencyDispatchSystem) checks a request once every 256 frames. Every time a check finds no vehicle (all
    /// busy, or the vehicle it got dropped it), SimulationUtils.ResetFailedRequest doubles the request's wait: 1, 3, 7,
    /// 15 ... up to 255 checks, about 6 in-game hours. The counter is never reset, so after one busy moment a building
    /// can go hours without a vehicle while vehicles sit idle at the station. The stations' own "looking for work"
    /// requests back off the same way when they keep finding nothing.
    ///
    /// This caps the wait at 15 checks (about 20 in-game minutes) for those services. Police patrols, mail and road
    /// maintenance are left alone: there are many of those requests and a late one does little harm, so retrying them
    /// sooner would only cost route searches. The game's own logic is untouched otherwise.
    /// </summary>
    public partial class ServiceRetrySystem : GameSystemBase
    {
        public const int kInterval = 256;
        private const byte kMaxFailCount = 4;   // next wait after a miss: (1 << 4) - 1 = 15 checks
        private const byte kMaxCooldown = 15;

        private static readonly string[] kNames = { "garbage", "hearse/ambulance", "fire", "police" };

        private EntityQuery[] m_Queries;
        private int[] m_ShortenedToday;
        private SimulationSystem m_SimulationSystem;
        private bool m_WasEnabled;
        private uint m_NextReportFrame;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => kInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Queries = new[]
            {
                WaitingRequests<GarbageCollectionRequest>(),
                WaitingRequests<HealthcareRequest>(),
                WaitingRequests<FireRescueRequest>(),
                WaitingRequests<PoliceEmergencyRequest>(),
            };
            m_ShortenedToday = new int[m_Queries.Length];
        }

        /// <summary>Requests that are waiting: not handed to a vehicle yet and not in the middle of a route search.</summary>
        private EntityQuery WaitingRequests<T>() where T : unmanaged, IComponentData
        {
            return GetEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadWrite<ServiceRequest>(),
                ComponentType.Exclude<Dispatched>(),
                ComponentType.Exclude<PathInformation>());
        }

        protected override void OnUpdate()
        {
            bool enabled = Mod.Settings != null && Mod.Settings.FasterServiceRetries;
            if (enabled != m_WasEnabled)
            {
                m_WasEnabled = enabled;
                Mod.log.Info(enabled ? "Faster service call retries: ON" : "Faster service call retries: OFF (original game behaviour)");
            }
            if (!enabled)
                return;

            for (int q = 0; q < m_Queries.Length; q++)
                m_ShortenedToday[q] += CapWaits(m_Queries[q]);

            uint frame = m_SimulationSystem.frameIndex;
            if (frame >= m_NextReportFrame)
            {
                if (m_NextReportFrame != 0)
                {
                    var parts = new string[m_Queries.Length];
                    for (int q = 0; q < m_Queries.Length; q++)
                        parts[q] = $"{kNames[q]} {m_ShortenedToday[q]}";
                    Mod.log.Info($"Faster service call retries: long waits cut short in the last in-game day: {string.Join(", ", parts)} (each one would have waited over 20 in-game minutes to call again)");
                }
                for (int q = 0; q < m_ShortenedToday.Length; q++)
                    m_ShortenedToday[q] = 0;
                m_NextReportFrame = frame + 262144;
            }
        }

        private static int CapWaits(EntityQuery query)
        {
            if (query.IsEmptyIgnoreFilter)
                return 0;

            int shortened = 0;
            NativeArray<ServiceRequest> requests = query.ToComponentDataArray<ServiceRequest>(Allocator.TempJob);
            try
            {
                bool changed = false;
                for (int i = 0; i < requests.Length; i++)
                {
                    ServiceRequest r = requests[i];
                    if (r.m_FailCount <= kMaxFailCount && r.m_Cooldown <= kMaxCooldown)
                        continue;
                    if (r.m_Cooldown > kMaxCooldown)
                        shortened++;
                    if (r.m_FailCount > kMaxFailCount) r.m_FailCount = kMaxFailCount;
                    if (r.m_Cooldown > kMaxCooldown) r.m_Cooldown = kMaxCooldown;
                    requests[i] = r;
                    changed = true;
                }
                if (changed)
                    query.CopyFromComponentDataArray(requests);
            }
            finally
            {
                requests.Dispose();
            }
            return shortened;
        }
    }
}
