# Fedews' Tweaks and Fixes

A small Cities: Skylines II code mod that fixes a few things that drove me crazy: helicopter noise, the parking gridlock, and cars sneaking into bus lanes.

Everything can be switched on, off or adjusted in **Options → Fedews' Tweaks and Fixes**.

## What it does

### Sounds: volume per sound type
A separate volume slider for each kind of sound in the world:

| Slider | Covers |
| --- | --- |
| Helicopters | Police, medical, fire and passenger helicopters (**starts at 0% = silent**) |
| Space rocket | The space center launch |
| Airplanes | Passenger, cargo and firefighting planes |
| Cars, buses & trucks | Every road vehicle (including buses and bikes) plus the background traffic hum of the city |
| Sirens & horns | Emergency sirens and vehicle horns |
| Trains, trams & metro | Everything on rails |
| Ships & boats | Ferries, cargo ships and other watercraft |
| Buildings & props | Factories, construction, service buildings, fountains, plus the background sound of neighbourhoods, shops and industry |
| People & animals | Citizens, pets, birds and wildlife |
| Everything else | Nature background (forest, rain, seagulls), wind and anything else |

Sounds are sorted by **what is making them**, not by guessing from file names. Changes apply instantly, even to sounds that are already playing. If one sound is shared by two kinds of objects (for example the same engine sound on a bus and a tram), it follows whichever of the two sliders is lower, so turning a slider down always works.

There's also a **"Write sound list to log"** button that writes every sound and the group it landed in to `Logs/FedewsTweaks.Mod.log`.

### Traffic: parking spot reservations
In the base game a car only checks whether a parking spot is free *right now* when it plans its route. It has no idea that 30 other cars are already driving to that same spot. So when a spot frees up, every car nearby goes for it, one wins, and the rest freeze, U-turn and re-route over and over.

With this on, once a car is heading to a parking spot, that spot counts as **taken** for everyone else. When a spot frees up, only one car goes for it and the rest of the city carries on. Cars are never removed or teleported by this. It only changes which spots the route planner offers to *new* routes, and the game's own parking behaviour stays the same.

### Traffic: strict bus lanes
In the base game bus lanes aren't actually forbidden for private cars. They only carry a small penalty, so impatient drivers hop in to skip a queue and then block everything trying to merge back out.

With this on, that penalty is multiplied (default 25×, adjustable from 5× to 100×) for every vehicle that isn't allowed in bus lanes. Buses, taxis and emergency vehicles are not affected. It's not a hard ban on purpose: on many roads the only way to turn right, or to reach the buildings on that side, is through the bus lane, and a hard ban would cut those off. Cars now only use a bus lane when there is truly no other way.

Side effect: the same penalty covers the game's other "rule-breaking" costs, so drivers also avoid illegal U-turns and banned roads more.

## Installing (local mod)

1. Build it (see below), or copy `FedewsTweaks.dll` and `0Harmony.dll` into
   `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\FedewsTweaks\`
2. Start the game and load a city.
3. Open **Options → Fedews' Tweaks and Fixes**.

Log file: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\FedewsTweaks.Mod.log`

## Building

Requirements: the .NET SDK (8 or newer) and Cities: Skylines II installed.

```
dotnet build -c Release
```

The project compiles against the game's own DLLs. It looks for the game in the default Steam folder (`C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II`). If yours is somewhere else, pass the path:

```
dotnet build -c Release -p:GameDir="X:\Your\Path\Cities Skylines II"
```

Or create a `local.props` file next to the project (it is ignored by git):

```xml
<Project>
  <PropertyGroup>
    <GameDir>X:\Your\Path\Cities Skylines II</GameDir>
  </PropertyGroup>
</Project>
```

A successful build copies the mod straight into the game's local `Mods` folder.

## Project layout

| File | What it is |
| --- | --- |
| `Mod.cs` | Entry point: registers the settings page and the systems |
| `Setting.cs` | Options page and its English text |
| `Audio/SoundVolumeSystem.cs` | Sorts every sound into a group and applies the sliders |
| `Traffic/ParkingReservationSystem.cs` | Parking spot reservations |
| `Traffic/BusLanePatch.cs` | Strict bus lanes (Harmony patch on route requests) |
| `lib/0Harmony.dll` | [Harmony](https://github.com/pardeike/Harmony) 2.2.2 (MIT license), used for the bus lane patch |

Tested on game version 1.6.4f1. A big game update can break code mods. If something acts weird after an update, switch the affected option off.
