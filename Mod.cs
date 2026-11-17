using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Pathfind;
using Game.SceneFlow;
using Game.Simulation;
using HarmonyLib;
using FedewsTweaks.Audio;
using FedewsTweaks.Traffic;

namespace FedewsTweaks
{
    public class Mod : IMod
    {
        public const string Id = "FedewsTweaks";
        public const string DisplayName = "Fedews' Tweaks and Fixes";
        public static ILog log = LogManager.GetLogger($"{Id}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }

        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"Loading {DisplayName}");

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            var strings = new LocaleEN(Settings);
            foreach (var locale in GameManager.instance.localizationManager.GetSupportedLocales())
                GameManager.instance.localizationManager.AddSource(locale, strings);
            AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));

            // Sound sliders
            updateSystem.UpdateAt<SoundVolumeSystem>(SystemUpdatePhase.MainLoop);

            // Parking spot reservations:
            //  - the scanner finds which parking spots moving cars are already heading to (runs after the car AI)
            //  - the applier hides fully reserved spots from the route planner (runs after the game recalculates
            //    free parking space and right before that information is handed to the route planner)
            updateSystem.UpdateAfter<ParkingReservationSystem, PersonalCarAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ParkingReservationApplySystem, LanesModifiedSystem>(SystemUpdatePhase.ModificationEnd);

            // "Clear traffic jams" button (main loop, so it also works while the game is paused)
            updateSystem.UpdateAt<ClearTrafficSystem>(SystemUpdatePhase.MainLoop);

            // Bus lanes: adjust each car's route request before the route planner sees it
            m_Harmony = new Harmony(Id);
            m_Harmony.PatchAll(typeof(Mod).Assembly);
            log.Info("Harmony patches applied");
        }

        public void OnDispose()
        {
            m_Harmony?.UnpatchAll(Id);
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
