# DroneLogistics

**Automated drone transport and logistics system for Techtonica**

DroneLogistics adds a fully-featured automated item transport system to Techtonica using aerial drones. Deploy drone relays, spawn different drone types for various tasks, and set up automated delivery routes to move resources across your factory without manual intervention.

---

## Table of Contents

- [Features](#features)
  - [Drone Types](#drone-types)
  - [Drone Relays](#drone-relays)
  - [Power and Charging](#power-and-charging)
  - [Packing System](#packing-system)
  - [Route Management](#route-management)
- [How to Use](#how-to-use)
  - [Getting Started](#getting-started)
  - [Setting Up Drones](#setting-up-drones)
  - [Configuring Deliveries](#configuring-deliveries)
  - [Managing Charging](#managing-charging)
- [Installation](#installation)
- [Configuration](#configuration)
  - [Drone Settings](#drone-settings)
  - [Power Settings](#power-settings)
  - [Advanced Settings](#advanced-settings)
- [Requirements](#requirements)
- [Mod Integration](#mod-integration)
- [Troubleshooting](#troubleshooting)
- [Credits](#credits)
- [License](#license)
- [Links](#links)

---

## Features

### Drone Types

DroneLogistics includes four specialized drone types, each optimized for different logistics tasks:

| Drone Type | Speed | Capacity | Range | Best Use |
|------------|-------|----------|-------|----------|
| **Scout Drone** | 150% | 1 stack | 50% | Quick reconnaissance and small deliveries |
| **Cargo Drone** | 100% | Configurable | 100% | General-purpose resource transport |
| **Heavy Lifter** | 60% | 4x capacity | 150% | Bulk transport of large quantities |
| **Combat Drone** | 120% | 2 stacks | 100% | Ammo resupply for turret systems |

Each drone type has distinct visual appearances with color-coded lights:
- **Scout**: Blue glow
- **Cargo**: Green glow
- **Heavy Lifter**: Orange glow
- **Combat**: Red glow

### Drone Relays

Relays serve as home bases for drones, handling spawning, charging, and delivery queue management:

| Relay Type | Charging Slots | Charge Speed | Power Draw | Description |
|------------|----------------|--------------|------------|-------------|
| **Standard** | 1 | 1x | 50 kW | Basic charging station |
| **FastCharge** | 2 | 2x | 100 kW | Rapid charging for high-traffic areas |
| **DockingBay** | 4 | 1.5x | 150 kW | Multi-drone hub with extended capacity |
| **Hub** | 1 | 1x | 25 kW | Control and routing optimization station |

Relay features include:
- Visual range indicators showing drone operating radius
- Status lights indicating charging activity
- Queue management for multiple drones waiting to charge
- Automatic drone assignment for incoming delivery requests

### Power and Charging

Drones operate on rechargeable batteries and require periodic charging:

- **Battery Capacity**: Configurable maximum charge (default 100 units)
- **Charge Rate**: Units per second when docked at a powered relay
- **Drain Rate**: 1% per second while moving, 1.5% when carrying cargo
- **Low Battery Behavior**: Drones automatically return to charge at 30% battery
- **Disabled State**: At 0% charge, drones become disabled until manually recovered

Charging queue system:
- Drones request charging slots from their home relay
- If all slots are occupied, drones hover nearby and wait in queue
- Queue position is displayed in relay status
- Estimated wait time is calculated based on charge rates

### Packing System

The cargo crate system enables more efficient transport (optional, disabled by default):

**Packing Stations**:
- Convert loose items into cargo crates
- Each crate holds 2 stacks worth of items
- Configurable packing time (default 2 seconds)
- Maximum 8 output crates buffer
- Status lights: Yellow (processing), Green (ready), Red (full), Gray (idle)

**Unpacking Stations**:
- Extract items from cargo crates back to loose form
- Faster unpacking time (default 1 second)
- Maximum 8 input crates buffer
- Integrates with inventory systems

### Route Management

The intelligent route management system optimizes logistics across your factory:

**Logistics Nodes**:
- Register locations as pickup/delivery points
- Node types: Storage, Machine, Packing, Unpacking, TurretAmmo, Custom
- Filter nodes to accept only specific resource types
- Nodes are automatically cleaned up when destroyed

**Delivery Routes**:
- Define persistent routes between source and destination nodes
- Set resource type, quantity per trip, and priority level
- Routes can be enabled/disabled dynamically
- Automatic route optimization every 5 seconds

**Request Processing**:
- Priority-based request queue (higher priority = first served)
- Automatic pad selection based on distance and availability
- Stale request cleanup after 5 minutes
- Multi-stop route support for complex logistics chains

**Drone States**:
- `Idle`: Hovering at home pad, ready for assignment
- `Departing`: Rising up before travel
- `Traveling`: En route to pickup or delivery
- `Loading`: Descending and collecting cargo
- `Unloading`: Descending and depositing cargo
- `Returning`: Flying back to home pad
- `Charging`: Docked and recharging battery
- `Disabled`: Out of power, requires assistance

---

## How to Use

### Getting Started

1. **Build a Drone Relay**: Place a drone relay at a central location in your factory. The relay will serve as the command center for your drone fleet.

2. **Spawn Drones**: Use the relay to spawn drones. Choose the drone type based on your needs:
   - Scout drones for quick, small deliveries
   - Cargo drones for general transport
   - Heavy lifters for bulk material movement
   - Combat drones if using TurretDefense integration

3. **Configure Operating Mode**: Decide whether to use standalone mode (direct item transport) or enhanced mode (cargo crates via packing stations).

### Setting Up Drones

Each relay can manage up to the configured maximum drones (default 4). Drones will:
- Automatically hover near their home relay when idle
- Respond to delivery requests within their operating range
- Return home when deliveries complete
- Seek charging when battery drops below 30%

### Configuring Deliveries

**Manual Requests**: Create delivery requests by specifying:
- Pickup location (source inventory/machine)
- Delivery location (destination inventory/machine)
- Resource type to transport
- Quantity to move
- Priority level (0 = normal, higher = more urgent)

**Automated Routes**: Set up persistent routes for continuous resource flow:
1. Register logistics nodes at key locations
2. Create routes between nodes with resource filters
3. The route manager automatically generates delivery requests
4. Drones fulfill requests based on availability and priority

### Managing Charging

- Relays automatically manage charging queues
- Drones display charge level via light color (red = low, normal color = charged)
- Place FastCharge relays in high-traffic areas
- Use DockingBay relays for drone fleet bases
- Monitor charging status via relay status text

---

## Installation

### Prerequisites

1. Install [BepInEx 5.4.2100](https://github.com/BepInEx/BepInEx/releases) or newer for Techtonica
2. Install [EquinoxsModUtils 6.1.3](https://github.com/Equinox-/EquinoxsModUtils) or newer

### Installing DroneLogistics

1. Download the latest release of DroneLogistics
2. Extract `DroneLogistics.dll` to your `BepInEx/plugins` folder
3. (Optional) Extract the `Bundles` folder to the same location as the DLL for enhanced drone models
4. Launch Techtonica

### Folder Structure

```
BepInEx/
  plugins/
    DroneLogistics.dll
    Bundles/
      drones_voodooplay
      drones_scifi
      drones_simple
      robot_sphere
      scifi_machines
      icons_skymon
```

---

## Configuration

All configuration options are available in `BepInEx/config/com.certifried.dronelogistics.cfg` after first launch.

### Drone Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxDronesPerPad` | 4 | Maximum drones per relay |
| `DroneSpeed` | 15 | Base flight speed in m/s |
| `DroneRange` | 200 | Maximum operating range from pad in meters |
| `DroneCapacity` | 1 | Number of stacks/crates per drone |
| `ChargingTime` | 10 | Seconds for full recharge |

### Power Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `RelayPowerDraw` | 50 | Power consumption (kW) when charging |
| `DroneChargeRate` | 10 | Battery charge per second when docked |
| `DroneBatteryCapacity` | 100 | Maximum battery capacity |

### Advanced Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `UseCargoCrates` | false | Enable cargo crate system (requires packing stations) |
| `EnableBiofuel` | false | Enable biofuel-powered drones (requires BioProcessing) |

---

## Requirements

| Dependency | Version | Type |
|------------|---------|------|
| BepInEx | 5.4.2100+ | Required |
| EquinoxsModUtils | 6.1.3+ | Required |
| BioProcessing | Any | Optional (for biofuel feature) |
| TurretDefense | Any | Optional (for combat drone integration) |

---

## Mod Integration

### BioProcessing

When `EnableBiofuel` is set to `true` and BioProcessing is installed, combat drones can utilize biofuel for extended operations.

### TurretDefense

Combat drones integrate with TurretDefense for automated ammo resupply:
- Combat drones can transport ammunition to turret positions
- TurretAmmo logistics nodes work with turret targeting systems
- Automatic resupply routes keep turrets operational

---

## Troubleshooting

### Drones Not Spawning

- Verify the relay hasn't reached maximum drone capacity
- Check that the relay is powered (if power is required)
- Ensure asset bundles are in the correct location

### Pink/Missing Drone Models

- The mod includes automatic URP material fixing
- If models appear pink, asset bundles may be missing
- Fallback primitive drones will be created automatically

### Drones Getting Stuck

- Use `RecallAllDrones()` on a pad to bring all drones home
- Use `EmergencyStop()` on individual drones
- Check for obstacles in flight paths

### Charging Not Working

- Verify relay has power connection
- Check charging slots aren't full
- Review queue position in relay status

### Deliveries Not Processing

- Ensure pickup and delivery locations are within drone range
- Verify drones have sufficient charge for round trip
- Check priority settings if urgent deliveries are delayed

---

## Credits

- **Author**: Certifried
- **Development Assistance**: Claude Code (Anthropic) - AI-assisted development and documentation
- **Asset Bundles**: Various Unity asset pack creators
  - VoodooPlay drone models
  - Sci-Fi machine models
  - Skymon icon assets

### Special Thanks

- The Techtonica modding community
- BepInEx team for the modding framework
- Equinox for EquinoxsModUtils

---

## License

This mod is licensed under the **GNU General Public License v3.0 (GPL-3.0)**.

You are free to:
- Use this mod for personal and commercial purposes
- Modify and distribute this mod
- Include this mod in modpacks

Under the following conditions:
- Source code must be made available when distributing
- Modifications must be released under the same license
- Original author must be credited

Full license text: [GNU GPL v3.0](https://www.gnu.org/licenses/gpl-3.0.en.html)

---

## Links

- **Source Code**: [GitHub Repository](https://github.com/certifried/DroneLogistics)
- **Bug Reports**: [GitHub Issues](https://github.com/certifried/DroneLogistics/issues)
- **Techtonica**: [Official Website](https://techtonica.com)
- **BepInEx**: [GitHub](https://github.com/BepInEx/BepInEx)
- **EquinoxsModUtils**: [GitHub](https://github.com/Equinox-/EquinoxsModUtils)
- **Modding Discord**: [Techtonica Modding Community](https://discord.gg/techtonica)

---

## Changelog

### [1.1.0] - Current
- Added power-based charging system
- New relay types: FastCharge, DockingBay, Hub
- Drone battery system with configurable capacity
- Charging queue management
- Enhanced sci-fi relay models
- Improved material handling for URP compatibility

### [1.0.0] - 2025-01-05
- Initial release
- 4 drone types with unique capabilities
- Drone pads with spawning and queue management
- Packing/unpacking stations for cargo crate mode
- Route optimization system
- Standalone and enhanced operating modes
