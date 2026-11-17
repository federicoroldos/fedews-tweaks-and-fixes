using System;
using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace FedewsTweaks
{
    [FileLocation(Mod.Id)]
    [SettingsUIGroupOrder(kSoundGroup, kSoundToolsGroup, kTrafficGroup, kTrafficToolsGroup)]
    [SettingsUIShowGroupName(kSoundGroup, kSoundToolsGroup, kTrafficGroup, kTrafficToolsGroup)]
    public class Setting : ModSetting
    {
        public const string kSoundSection = "Sounds";
        public const string kTrafficSection = "Traffic";
        public const string kSoundGroup = "SoundVolumes";
        public const string kSoundToolsGroup = "SoundTools";
        public const string kTrafficGroup = "TrafficFixes";
        public const string kTrafficToolsGroup = "TrafficTools";

        /// <summary>Raised whenever any option changes.</summary>
        public static event Action Changed;

        public Setting(IMod mod) : base(mod) { SetDefaults(); }

        // ---------- Sounds ----------
        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int HelicopterVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int RocketVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int PlaneVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int CarVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int SirenVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int TrainVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int ShipVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int BuildingVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int CreatureVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundGroup)]
        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kPercentage)]
        public int OtherVolume { get; set; }

        [SettingsUISection(kSoundSection, kSoundToolsGroup)]
        [SettingsUIButton]
        public bool DumpSoundList
        {
            set { Audio.SoundVolumeSystem.RequestDump(); }
        }

        // ---------- Traffic ----------
        [SettingsUISection(kTrafficSection, kTrafficGroup)]
        public bool SmartParking { get; set; }

        [SettingsUISection(kTrafficSection, kTrafficGroup)]
        public bool StrictBusLanes { get; set; }

        [SettingsUISection(kTrafficSection, kTrafficGroup)]
        [SettingsUISlider(min = 5, max = 100, step = 5, unit = Unit.kInteger)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(BusLanesOff))]
        public int BusLaneStrictness { get; set; }

        public bool BusLanesOff() => !StrictBusLanes;

        [SettingsUISection(kTrafficSection, kTrafficToolsGroup)]
        [SettingsUIButton]
        public bool RemoveStuckTraffic
        {
            set { Traffic.ClearTrafficSystem.RequestStuck(); }
        }

        [SettingsUISection(kTrafficSection, kTrafficToolsGroup)]
        [SettingsUISlider(min = 10, max = 180, step = 5, unit = Unit.kInteger)]
        public int StuckSeconds { get; set; }

        [SettingsUISection(kTrafficSection, kTrafficToolsGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool RemoveAllTraffic
        {
            set { Traffic.ClearTrafficSystem.RequestAll(); }
        }

        [Exclude]
        [SettingsUISection(kTrafficSection, kTrafficToolsGroup)]
        [SettingsUIValueVersion(typeof(Setting), nameof(TrafficToolStatusVersion))]
        public string TrafficToolStatus => Traffic.ClearTrafficSystem.Status;

        public int TrafficToolStatusVersion() => Traffic.ClearTrafficSystem.StatusVersion;

        public override void SetDefaults()
        {
            HelicopterVolume = 0;
            RocketVolume = 100;
            PlaneVolume = 100;
            CarVolume = 100;
            SirenVolume = 100;
            TrainVolume = 100;
            ShipVolume = 100;
            BuildingVolume = 100;
            CreatureVolume = 100;
            OtherVolume = 100;
            SmartParking = true;
            StrictBusLanes = true;
            BusLaneStrictness = 25;
            StuckSeconds = 40;
        }

        public override void Apply()
        {
            base.Apply();
            Changed?.Invoke();
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m;
        public LocaleEN(Setting setting) { m = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m.GetSettingsLocaleID(), Mod.DisplayName },
                { m.GetOptionTabLocaleID(Setting.kSoundSection), "Sounds" },
                { m.GetOptionTabLocaleID(Setting.kTrafficSection), "Traffic" },
                { m.GetOptionGroupLocaleID(Setting.kSoundGroup), "Volume per sound type" },
                { m.GetOptionGroupLocaleID(Setting.kSoundToolsGroup), "Tools" },
                { m.GetOptionGroupLocaleID(Setting.kTrafficGroup), "Traffic fixes" },

                { m.GetOptionLabelLocaleID(nameof(Setting.HelicopterVolume)), "Helicopters" },
                { m.GetOptionDescLocaleID(nameof(Setting.HelicopterVolume)), "Police, medical, fire and passenger helicopters. 0% = completely silent." },
                { m.GetOptionLabelLocaleID(nameof(Setting.RocketVolume)), "Space rocket" },
                { m.GetOptionDescLocaleID(nameof(Setting.RocketVolume)), "The space center rocket launch." },
                { m.GetOptionLabelLocaleID(nameof(Setting.PlaneVolume)), "Airplanes" },
                { m.GetOptionDescLocaleID(nameof(Setting.PlaneVolume)), "Passenger and cargo planes, and firefighting planes." },
                { m.GetOptionLabelLocaleID(nameof(Setting.CarVolume)), "Cars, buses & trucks" },
                { m.GetOptionDescLocaleID(nameof(Setting.CarVolume)), "Engines and road noise of every road vehicle, including buses and bicycles, plus the background traffic hum of the city (sirens and horns have their own slider)." },
                { m.GetOptionLabelLocaleID(nameof(Setting.SirenVolume)), "Sirens & horns" },
                { m.GetOptionDescLocaleID(nameof(Setting.SirenVolume)), "Police, ambulance and fire truck sirens, and vehicle horns." },
                { m.GetOptionLabelLocaleID(nameof(Setting.TrainVolume)), "Trains, trams & metro" },
                { m.GetOptionDescLocaleID(nameof(Setting.TrainVolume)), "Everything that runs on rails." },
                { m.GetOptionLabelLocaleID(nameof(Setting.ShipVolume)), "Ships & boats" },
                { m.GetOptionDescLocaleID(nameof(Setting.ShipVolume)), "Ferries, cargo ships and other watercraft." },
                { m.GetOptionLabelLocaleID(nameof(Setting.BuildingVolume)), "Buildings & props" },
                { m.GetOptionDescLocaleID(nameof(Setting.BuildingVolume)), "Factories, construction, service buildings, power plants, fountains and other placed objects, plus the background sound of neighbourhoods, shops and industry." },
                { m.GetOptionLabelLocaleID(nameof(Setting.CreatureVolume)), "People & animals" },
                { m.GetOptionDescLocaleID(nameof(Setting.CreatureVolume)), "Citizens, pets, birds and wildlife." },
                { m.GetOptionLabelLocaleID(nameof(Setting.OtherVolume)), "Everything else" },
                { m.GetOptionDescLocaleID(nameof(Setting.OtherVolume)), "Nature background (forest, rain, seagulls), wind and any other world sound that does not belong to the groups above." },
                { m.GetOptionLabelLocaleID(nameof(Setting.DumpSoundList)), "Write sound list to log" },
                { m.GetOptionDescLocaleID(nameof(Setting.DumpSoundList)), "Writes every sound in the game and the group it was put in to Logs/FedewsTweaks.Mod.log. Load a city first." },

                { m.GetOptionLabelLocaleID(nameof(Setting.SmartParking)), "Parking spot reservations" },
                { m.GetOptionDescLocaleID(nameof(Setting.SmartParking)), "Once a car is heading to a parking spot, that spot counts as taken for every other car. When a spot frees up, only one car goes for it and the rest of the city carries on, instead of everybody rushing there and re-routing. Cars are never removed or sent home by this." },
                { m.GetOptionLabelLocaleID(nameof(Setting.StrictBusLanes)), "Strict bus lanes" },
                { m.GetOptionDescLocaleID(nameof(Setting.StrictBusLanes)), "Private cars and trucks stop using bus lanes as a shortcut. They only enter one when there is no other way (for example a turn that only the bus lane connects to). Buses, taxis and emergency vehicles are not affected. Applies to new routes, so give it a minute." },
                { m.GetOptionLabelLocaleID(nameof(Setting.BusLaneStrictness)), "Bus lane strictness" },
                { m.GetOptionDescLocaleID(nameof(Setting.BusLaneStrictness)), "How many times worse breaking traffic rules (bus lanes, banned roads, illegal turns) feels to drivers. Game default is 1. 25 is plenty for most cities; raise it if you still see cars sneaking in." },

                { m.GetOptionGroupLocaleID(Setting.kTrafficToolsGroup), "Emergency tools" },
                { m.GetOptionLabelLocaleID(nameof(Setting.RemoveStuckTraffic)), "Remove stuck traffic" },
                { m.GetOptionDescLocaleID(nameof(Setting.RemoveStuckTraffic)), "Watches the traffic for a short while (see the time below) and then removes only the cars, taxis and delivery trucks that barely moved. Cars driving normally are kept. Unpauses the game if you paused it; the check runs once you close this menu. Progress shows here and in the notifications (bell icon). People riding in removed cars simply arrive where they were going. Parked cars, buses, trams, trains, emergency and service vehicles are never touched." },
                { m.GetOptionLabelLocaleID(nameof(Setting.StuckSeconds)), "Counts as stuck after (in-game seconds)" },
                { m.GetOptionDescLocaleID(nameof(Setting.StuckSeconds)), "How long a car has to sit still (moving less than 10 m) to count as stuck. Long enough that cars waiting at a red light are not removed. 40 is a good start." },
                { m.GetOptionLabelLocaleID(nameof(Setting.RemoveAllTraffic)), "Remove ALL moving traffic" },
                { m.GetOptionDescLocaleID(nameof(Setting.RemoveAllTraffic)), "Emergency reset: instantly removes every moving private car, taxi and delivery truck, stuck or not, including the queues at highway entrances and through-traffic. People riding in them simply arrive where they were going. Parked cars, buses, trams, trains, emergency and service vehicles are not touched." },
                { m.GetOptionWarningLocaleID(nameof(Setting.RemoveAllTraffic)), "Remove ALL moving private cars, taxis and delivery trucks, even ones driving normally? Parked cars and public/emergency vehicles stay." },
                { m.GetOptionLabelLocaleID(nameof(Setting.TrafficToolStatus)), "Status" },
                { m.GetOptionDescLocaleID(nameof(Setting.TrafficToolStatus)), "What the traffic tools are doing right now, or the result of the last run." },
            };
        }

        public void Unload() { }
    }
}
