<div align="center">

<img src=".github/assets/banner.svg" alt="Fedews' Tweaks and Fixes, a Cities: Skylines II mod" width="100%">

<br>

[![Game](https://img.shields.io/badge/Cities%3A%20Skylines%20II-1.6.4f1-6f42c1?style=for-the-badge)](https://www.paradoxinteractive.com/games/cities-skylines-ii)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white)](#installation)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#building)
[![License: MIT](https://img.shields.io/badge/license-MIT-ff8a4c?style=for-the-badge)](LICENSE)

**Quieter skies, smarter parking, bus lanes that stay clear, and a rescue button for gridlocked highways.**
<br>
Every feature is optional and can be adjusted in-game.

[Features](#features) · [Installation](#installation) · [Settings](#settings) · [FAQ](#faq) · [Report a bug](../../issues/new/choose) · [Changelog](CHANGELOG.md)

</div>

---

<a id="features"></a>

## ✨ Features

<table>
<tr>
<td width="50%" valign="top">

### 🔇 Volume per sound type
Ten sliders, one for each kind of sound: helicopters, the space rocket, planes, road traffic, sirens, trains, ships, buildings, people & animals, and everything else.
**Helicopters start muted.**

</td>
<td width="50%" valign="top">

### 🅿️ Parking spot reservations
Once a car is heading to a parking spot, that spot counts as taken for everyone else. When a spot frees up, **one** car goes for it, and the rest of the city doesn't stampede towards it.

</td>
</tr>
<tr>
<td width="50%" valign="top">

### 🚌 Strict bus lanes
Private cars stop hopping into bus lanes to skip a queue and then blocking everything on the way out. Buses, taxis and emergency vehicles are unaffected.

</td>
<td width="50%" valign="top">

### 🚨 Jam rescue
**Remove stuck traffic** clears only the cars that truly aren't moving. **Remove ALL moving traffic** is the emergency reset. **Parked cars are never touched.**

</td>
</tr>
</table>

---

### 🔇 Sounds: a volume slider per sound type

| Slider | What it covers | Default |
| :-- | :-- | :-: |
| 🚁 **Helicopters** | Police, medical, fire and passenger helicopters | **0%** |
| 🚀 **Space rocket** | The space center launch | 100% |
| ✈️ **Airplanes** | Passenger, cargo and firefighting planes | 100% |
| 🚗 **Cars, buses & trucks** | Every road vehicle (including bikes) plus the city's background traffic hum | 100% |
| 🚨 **Sirens & horns** | Emergency sirens and vehicle horns | 100% |
| 🚆 **Trains, trams & metro** | Everything on rails | 100% |
| 🚢 **Ships & boats** | Ferries, cargo ships and other watercraft | 100% |
| 🏭 **Buildings & props** | Factories, construction, services, fountains, plus neighbourhood, shop and industry ambience | 100% |
| 🐦 **People & animals** | Citizens, pets, birds and wildlife | 100% |
| 🌲 **Everything else** | Forest, rain, seagulls, wind and anything not listed above | 100% |

Sounds are grouped by **what is making them**, not guessed from file names. Changes apply instantly, even to sounds that are already playing.

> [!TIP]
> If one sound is shared by two kinds of objects (say, the same engine on a bus and a tram), it follows whichever of the two sliders is **lower**, so turning a slider down always works. The **Write sound list to log** button shows exactly which group every sound landed in.

### 🅿️ Traffic: parking spot reservations

<details open>
<summary><b>The problem in the base game</b></summary>
<br>

A car only checks whether a parking spot is free *right now* when it plans its route. It has no idea that 30 other cars are already on their way to that same spot. So when one spot frees up, every car nearby goes for it, one wins, and the rest freeze, U-turn and re-route over and over.

</details>

**With this on:** once a car is heading to a parking spot, the route planner treats that spot as taken for everyone else. Only new routes are affected. Cars are never removed or teleported, and the game's own parking behaviour stays the same.

### 🚌 Traffic: strict bus lanes

<details open>
<summary><b>The problem in the base game</b></summary>
<br>

Bus lanes aren't actually forbidden for private cars. They only carry a small route-planning penalty, so impatient drivers hop in to skip a queue and then block traffic trying to merge back out.

</details>

**With this on:** that penalty is multiplied (**25×** by default, adjustable from 5× to 100×) for every vehicle that isn't allowed in bus lanes.

> [!NOTE]
> This is deliberately **not** a hard ban. On many roads the only way to turn right, or to reach the buildings on that side, is through the bus lane, and a hard ban would cut those off. Cars now only use a bus lane when there's truly no other way. As a bonus, drivers also avoid illegal U-turns and banned roads more.

### 🚨 Traffic: jam rescue buttons

For when your highway entrances are jammed solid with thousands of cars that will never get in. You'll find these on the **Traffic** tab, under **Emergency tools**.

<table>
<tr>
<th width="50%">🧹 Remove stuck traffic</th>
<th width="50%">💥 Remove ALL moving traffic</th>
</tr>
<tr>
<td valign="top">

1. Notes where every moving car, taxi and delivery truck is.
2. Lets the game run for a short while (**40 in-game seconds** by default, adjustable from 10 to 180). If you had paused, it unpauses for you.
3. Removes only the vehicles that moved **less than 10 m** in that time.

Cars driving normally, or waiting at a red light, are kept. A live progress bar shows in the game's notifications (🔔) and on the options page.

</td>
<td valign="top">

The emergency reset. After an "are you sure?" popup it instantly removes **every driving** private car, taxi and delivery truck, stuck or not, including the queues at the city entrances and through-traffic.

</td>
</tr>
</table>

> [!IMPORTANT]
> **Parked cars are never removed by either button.** Cars in parking spots, lots, garages and driveways all stay where they are. Buses, trams, trains, emergency and service vehicles, planes and ships are never touched either.

- 🧍 **Nobody gets lost:** people riding in a removed car simply arrive where they were going (or at home), the same way the game handles a stuck citizen.
- 📦 **Trucks:** they lose the load they were carrying, and the businesses just order again.
- 🪶 **Zero background cost:** nothing runs unless you press a button.

---

<a id="settings"></a>

## ⚙️ Settings at a glance

All options live in **Options → Fedews' Tweaks and Fixes**.

| Tab | Option | Default |
| :-- | :-- | :-: |
| Sounds | 10 volume sliders | 100% (helicopters 0%) |
| Sounds | Write sound list to log | button |
| Traffic | Parking spot reservations | ✅ On |
| Traffic | Strict bus lanes | ✅ On |
| Traffic | Bus lane strictness | 25× |
| Traffic | Remove stuck traffic (parked cars stay) | button |
| Traffic | Counts as stuck after | 40 s |
| Traffic | Remove ALL moving traffic (parked cars stay) | button, asks first |

---

<a id="installation"></a>

## 📦 Installation

> [!NOTE]
> The mod isn't on Paradox Mods yet. For now it installs as a local mod.

1. Download `FedewsTweaks.zip` from the [latest release](../../releases/latest), or [build it yourself](#building).
2. Extract `FedewsTweaks.dll` and `0Harmony.dll` into:
   ```text
   %USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\FedewsTweaks\
   ```
3. Start the game, load a city, and open **Options → Fedews' Tweaks and Fixes**.

**Log file:** `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\FedewsTweaks.Mod.log`

---

<a id="building"></a>

## 🛠️ Building from source

<details>
<summary><b>Show build instructions</b></summary>
<br>

**You'll need** the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer) and Cities: Skylines II installed.

```powershell
dotnet build -c Release
```

The project compiles against the game's own DLLs and looks for the game in the default Steam folder (`C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II`). If yours is somewhere else, either pass the path:

```powershell
dotnet build -c Release -p:GameDir="X:\Your\Path\Cities Skylines II"
```

or create a `local.props` file next to the project (git ignores it):

```xml
<Project>
  <PropertyGroup>
    <GameDir>X:\Your\Path\Cities Skylines II</GameDir>
  </PropertyGroup>
</Project>
```

A successful build copies the mod straight into the game's local `Mods` folder.

#### Project layout

| Path | What it is |
| :-- | :-- |
| `Mod.cs` | Entry point: registers the options page and all systems |
| `Setting.cs` | Options page and its English text |
| `Audio/SoundVolumeSystem.cs` | Sorts every sound into a group and applies the sliders |
| `Traffic/ParkingReservationSystem.cs` | Parking spot reservations |
| `Traffic/BusLanePatch.cs` | Strict bus lanes (Harmony patch on route requests) |
| `Traffic/ClearTrafficSystem.cs` | Jam rescue buttons |
| `lib/0Harmony.dll` | [Harmony](https://github.com/pardeike/Harmony) 2.2.2 (MIT) |

</details>

---

<a id="faq"></a>

## ❓ FAQ

<details>
<summary><b>Does it work with existing saves? Can I remove it later?</b></summary>
<br>

Yes to both. The mod doesn't store any data of its own in your saves. If you remove it, the game goes back to its normal behaviour (a few parking spots may look full for a moment until the next car parks or leaves there).

</details>

<details>
<summary><b>Will it hurt performance?</b></summary>
<br>

The sound sliders and bus lanes cost practically nothing. Parking reservations do a light check of moving cars every few frames. The jam rescue buttons only do work while you're using them.

</details>

<details>
<summary><b>I lowered a slider but a sound is still loud.</b></summary>
<br>

Press **Write sound list to log** and check `FedewsTweaks.Mod.log`. Every sound is listed with its group and the object it was found on. Please [open an issue](../../issues/new/choose) with that log and we'll sort it.

</details>

<details>
<summary><b>Is it compatible with other traffic mods?</b></summary>
<br>

Probably, but it hasn't been tested with them. Other mods that change parking or bus lane behaviour can overlap. If something looks off, switch the matching option off here.

</details>

<details>
<summary><b>The game just updated and something acts weird.</b></summary>
<br>

Big game updates can break code mods. Switch the affected option off and [report it](../../issues/new/choose) with your game version.

</details>

---

## 🐞 Reporting bugs

Please use the [bug report form](../../issues/new/choose) and attach `FedewsTweaks.Mod.log`. It makes problems far easier to track down.

## 📜 License & credits

- Released under the [MIT License](LICENSE).
- Uses [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike (MIT).

<sub>Cities: Skylines II is a trademark of Paradox Interactive AB. This is an unofficial fan-made mod and is not affiliated with or endorsed by Colossal Order or Paradox Interactive.</sub>
