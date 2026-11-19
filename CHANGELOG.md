# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.2] - 2026-11-19

### Fixed
- **Strict bus lanes** no longer makes cars and taxis vanish. When a car or taxi with people inside planned its route again (for example because its parking spot was taken on arrival), the multiplied penalty could push the route over the game's cost limit. The game then dropped the passengers where they were and moved the car away: home, or back to the map edge for visitors. Those re-plans now work like the base game.
- **Strict bus lanes** no longer makes cars U-turn mid-street. Every private car planned its route again right after pulling out and kept driving while the new route was worked out. When the route planner was busy, the new route arrived several blocks later and the car turned around to get back to where it had asked, even where U-turns are banned. Now a car is only asked while it is still in its parking spot, garage or driveway, and it waits there for the new route. Cars already on the road are never asked, and only cars whose route goes through a bus lane re-plan at all.

### Changed
- The log now shows how many cars leaving a spot were headed through a bus lane and planned their route again, and how many were already on the road and kept theirs.

## [1.1.1] - 2026-11-19

### Fixed
- **Strict bus lanes** no longer empties the streets of private cars. The penalty also applied while citizens were choosing how to travel, so any trip to a building on a street with bus lanes looked many minutes slower by car. Citizens switched to taxis (which may use bus lanes) and walking, and left their own cars at home. People now choose how to travel exactly as in the base game.

### Changed
- **Strict bus lanes** reaches private cars a new way: each car with people inside plans its route once more right after pulling out, this time with the penalty. That route can't switch to a taxi or transit, and the car keeps its reserved parking spot. Park-and-ride trips keep their original plan.
- The log now shows how many cars planned their route again after pulling out, about once per in-game minute.

## [1.1.0] - 2026-11-19

### Added
- **Crematoriums empty cemeteries:** a crematorium with no bodies waiting from the city cremates bodies from the fullest cemetery instead of sitting idle, so cemeteries finally empty. Bodies from the city always come first. On by default, under the new **City services** tab.
- **Landfills stop emptying when empty:** a landfill's "Empty" button switches itself off once the landfill is empty, so it goes back to collecting garbage instead of staying out of service forever.
- **Faster service calls:** a building waiting for a garbage truck, hearse, ambulance, fire truck or police never waits more than about 20 in-game minutes before calling again. In the base game this wait doubles after every miss, up to about 6 in-game hours, and never goes back down.
- **Parking spot reservations** now cover parking garages and parking buildings too. Once enough cars are on their way to fill a garage, new routes pick somewhere else, while the cars already heading there still drive in.

### Changed
- Parking reservations are cheaper to run: each car's route is now searched from the end, where the parking spot is, instead of from the start.
- The log now shows how much CPU time the parking reservations use, about once per in-game minute.

### Fixed
- A car pulling out of a parking spot no longer counts as reserving the spot it is leaving.

## [1.0.0] - 2026-11-17

### Added
- **Volume per sound type:** ten sliders (helicopters, space rocket, airplanes, road traffic, sirens & horns, trains, ships, buildings, people & animals, everything else). All start at 100%, the game's normal volume.
- **Write sound list to log** button for checking which group each sound is in.
- **Parking spot reservations:** a parking spot that a car is already heading to counts as taken for every other car's route.
- **Strict bus lanes:** private vehicles avoid bus lanes unless there's no other way. Adjustable strictness from 5× to 100×.
- **Remove stuck traffic** button: removes only cars, taxis and delivery trucks that barely moved over an adjustable time. Unpauses the game if needed and shows a progress bar.
- **Remove ALL moving traffic** button: emergency reset for every driving car, taxi and delivery truck.
- Both traffic buttons keep parked cars, public transport and service vehicles, and send passengers straight to their destination.

[Unreleased]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/compare/v1.1.2...HEAD
[1.1.2]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/releases/tag/v1.0.0
