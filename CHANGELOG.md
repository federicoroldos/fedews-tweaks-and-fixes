# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Parking spot reservations** now cover parking garages and parking buildings too. Once enough cars are on their way to fill a garage, new routes pick somewhere else, while the cars already heading there still drive in.

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

[Unreleased]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/federicoroldos/fedews-tweaks-and-fixes/releases/tag/v1.0.0
