# Changelog

## [1.1.0] - 2026-01-07

### Added
- Drone Relay types: Standard, FastCharge, DockingBay, and Hub with unique charging capabilities
- Power-based charging system with configurable relay power draw
- DroneChargeRate and DroneBatteryCapacity configuration options
- Improved URP material compatibility with full texture map preservation (albedo, normal, metallic, emission, occlusion)
- Fallback primitive rendering for drones and relays when asset bundles unavailable

### Changed
- Drone pads now function as powered relays with type-specific behaviors
- Enhanced prefab loading with better partial name matching

## [1.0.0] - 2025-01-05

### Added
- Initial release
- Scout Drone: Fast reconnaissance, 10 unit capacity
- Cargo Drone: Standard transport, 50 unit capacity
- Heavy Lifter: Large cargo transport, 100 unit capacity
- Combat Drone: Armed transport with turret integration, 25 unit capacity
- Drone Pad: Home base for drones with spawning and queue management
- Packing Station: Converts items into cargo crates
- Unpacking Station: Extracts items from cargo crates
- Route Manager: Automatic route optimization and priority processing
- Standalone mode: Direct inventory transport
- Enhanced mode: Cargo crate transport system
- BioProcessing integration for biofuel-powered drones
- TurretDefense integration for combat drone targeting
