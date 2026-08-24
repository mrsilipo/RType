# RType Data Architecture Audit

Last updated: 2026-08-21

## Current Runtime Truth

Probe defaults and some legacy tools still load the active car from:

- `Data/Vehicles/ek9_reference_2000.json`

The car picker now starts races from:

- `Data/VehicleBuilds/ek9_showroom_stock.json`

The global simulation/handling configuration currently loads from:

- `Data/Simulation/arcade_physics.json`

The stock EK9 build definition is consumed by the car picker and the RType engine/audio resolver:

- `Data/VehicleBuilds/ek9_showroom_stock.json`

`VehicleBuildDefinitionLoader` converts the selected build into `VehicleSimulationParameters` while keeping legacy-only fields from the reference vehicle file until those fields are migrated into catalogs.

Live gameplay engine sound now uses the RType-owned engine path only. The old `EngineSimulatorSound` fallback is disconnected from `VehicleAudioSystem`.

Live gameplay vehicle physics now stays on the current torque-curve power unit. The old `EngineSimPowerUnit` selector is disconnected from `EnginePowerUnitFactory` and kept only for probes/reference while the RType-owned engine simulator is rebuilt.

The current build validator can verify both sides of this transitional state:

- RType engine/audio resolves from `VehicleBuilds` plus engine catalogs/tunes.
- Chassis, drivetrain, suspension, brakes, wheels, tyres, and vehicle mass now resolve through the build bridge for the car picker.
- Brake system, tyre model, aero package, steering setup, and handling setup now resolve through part/tune data.
- Some legacy-only fields are still read from `Data/Vehicles/ek9_reference_2000.json`, mainly audio, torque curves, old EngineSim probe compatibility, detailed suspension geometry, inertia, fuel, and electronics.
- `dotnet run -- --compare-vehicle-build` compares catalog-resolved build fields against the current vehicle runtime file.

The older setup file exists, but is not consumed by runtime:

- `Data/Setups/ek9_factory.json`

## Naming Problem

`Data/gt1_engine.json` was misleading. It was not an engine definition. It controlled the global vehicle simulation layer:

- fixed physics tick rate
- maximum frame time and tick clamp
- stability assist
- throttle assist
- brake assist and ABS behavior
- steering assist
- RPM response helper values
- vehicle safety limits

Completed rename:

- from `Data/gt1_engine.json`
- to `Data/Simulation/arcade_physics.json`

Optional future variants:

- `Data/Simulation/simulation_index.json`
- `Data/Simulation/arcade_physics.json`
- `Data/Simulation/simulation_physics.json`
- `Data/Simulation/debug_physics.json`

## Proposed Future Layout

```text
Data/
  Controls/
    racing_xbox360_default.json

  Simulation/
    arcade_physics.json

  Surfaces/
    default_surfaces.json

  Tracks/
    lakeside_park.json

  Vehicles/
    ek9_chassis.json

  VehicleBuilds/
    ek9_showroom_stock.json

  Garage/
    future_player_profile.json
    future_owned_cars.json

  Parts/
    part_catalog_index.json
    Engines/
      engine_blocks.json
      engine_heads.json
      engines.json
      block_upgrades.json
      head_upgrades.json
      cam_sets.json
      displacement_kits.json
      port_polishing.json
      throttle_bodies.json
      intakes.json
      intake_lengths.json
      valve_springs.json
      headers.json
      exhausts.json
      flywheels.json
      clutches.json
    Chassis/
      body_shells.json
    Drivetrain/
      gearboxes.json
      final_drives.json
      differentials.json
    Suspension/
      suspension_sets.json
      alignments.json
    Brakes/
      brakes.json
    Tyres/
      tyres.json
    Wheels/
      wheels.json

  EngineRuntime/
    rtype_engine_assemblies/
    rtype_resolved_profiles/

  Tunes/
    Engines/
      engine_tunes.json

  Legacy/
    EngineSimProfiles/
    Setups/
    Upgrades/
```

## Proposed Ownership Boundaries

### Vehicle Chassis Data

Future file:

- `Data/Vehicles/ek9_chassis.json`

Owns:

- body dimensions
- wheelbase and track width
- base curb shell mass
- CG reference
- inertia reference
- suspension pickup/geometry defaults
- brake hard points
- drivetrain layout
- render/model identity later

Does not own:

- installed engine build
- user-owned upgrade state
- player purchase/garage state

### Vehicle Build Data

Current file:

- `Data/VehicleBuilds/ek9_showroom_stock.json`

Owns:

- selected chassis
- selected engine
- selected block/head
- installed stock parts
- selected drivetrain, suspension, brakes, wheels, tyres
- showroom/test ownership status until player profiles exist

This should become the runtime entry point for selecting a car.

### Engine And Part Catalog Data

Current files:

- `Data/RTypeEngineProfiles/PartCatalogs/*.json`

Future path:

- `Data/Parts/Engines/*.json`

Owns:

- factory Honda engines
- blocks and heads
- engine construction upgrades
- cams, valvetrain, intake, exhaust, flywheel, clutch
- part weight and durability
- compatibility tags

The catalogs describe available parts. They should not describe a specific player-owned car.

### Vehicle Part Catalog Data

Current files:

- `Data/Parts/part_catalog_index.json`
- `Data/Parts/Chassis/body_shells.json`
- `Data/Parts/Drivetrain/gearboxes.json`
- `Data/Parts/Drivetrain/final_drives.json`
- `Data/Parts/Drivetrain/differentials.json`
- `Data/Parts/Suspension/suspension_sets.json`
- `Data/Parts/Suspension/alignments.json`
- `Data/Parts/Brakes/brakes.json`
- `Data/Parts/Brakes/brake_systems.json`
- `Data/Parts/Wheels/wheels.json`
- `Data/Parts/Tyres/tyres.json`
- `Data/Parts/Tyres/tyre_models.json`
- `Data/Parts/Aero/aero_packages.json`

Owns:

- non-engine stock parts and future upgrade parts
- gearbox ratios, final drive ratios, differential behavior
- suspension rates, damping, ride heights, anti-roll data, and alignment presets
- brake geometry, pad friction, and thermal capacity
- brake line pressure, bias, ABS behavior, and handbrake force
- wheel size, offset, bolt pattern, and mass
- tyre size, loaded radius, peak grip, rolling resistance, slip targets, and wear/heat metadata
- detailed tyre slip, relaxation, camber, scrub, and sliding-friction behavior
- aero drag/lift/frontal-area packages
- body shell dimensions, base mass, torsional rigidity, and durability

The current validator resolves these ids, but the game runtime still reads the actual physics numbers from `Data/Vehicles/ek9_reference_2000.json`.

Catalog weight is not yet final vehicle mass. The current stock vehicle-side catalog estimate excludes some mass buckets still owned by the vehicle file, such as fluids, fuel, cabin details, accessories, and player/driver mass.

### Engine Tune Data

Current file:

- `Data/RTypeEngineProfiles/Tunes/engine_tunes.json`
- `Data/Tunes/Chassis/steering_setups.json`
- `Data/Tunes/Chassis/handling_setups.json`

Future path:

- `Data/Tunes/Engines/engine_tunes.json`

Owns:

- idle target
- throttle response map/gamma
- VTEC activation and transition behavior
- rev limiter RPM and cut rhythm
- ignition timing maps
- AFR/fuel target values
- combustion/fuel dose calibration

Does not own:

- block/head identity
- bore, stroke, rod length, or displacement hardware
- cam lift/duration hardware
- intake/header/exhaust geometry
- flywheel/clutch hardware

The resolver applies tune data after hardware parts. That lets the same B16B hardware run factory, street, club sport, race, or bad/safe custom tunes later.

Chassis tune data owns adjustable/runtime behavior that is not a purchased physical component:

- steering ratio/lock/assist/input response
- arcade handling helpers
- launch helper calibration
- wall collision response
- visual suspension/body motion tuning

### RType Engine Runtime Data

Current runtime source:

- `Data/VehicleBuilds/ek9_showroom_stock.json`
- `Data/RTypeEngineProfiles/PartCatalogs/*.json`

The transitional stock B16B assembly files were removed after the build/catalog resolver landed. A `VehicleBuild` plus catalogs now resolves into one runtime `RTypeEngineAssembly`.

### Player/Garage Data

Not implemented yet.

Future ownership:

- player profile
- cash/progression
- owned cars
- installed upgrades
- purchased but uninstalled parts
- car mileage/wear/durability
- saved setups

## Legacy / Transitional Systems

### `Data/Setups/ek9_factory.json`

Status: legacy/transitional.

Problem:

- Uses old `stage_0` upgrade naming.
- Does not reference the new detailed engine part catalogs except through the newly added build pointer.
- Not consumed by runtime.

Recommendation:

- Keep only until `VehicleBuilds` fully replace setup files.
- Then delete or migrate to a garage/setup layer.

### `Data/Legacy/Upgrades/*.json`

Status: legacy gameplay upgrade model.

Problem:

- Uses broad stage-based upgrade thinking.
- Does not match the new component assembly model.
- Engine upgrade concepts overlap with new part catalogs.

Recommendation:

- Moved to `Data/Legacy/Upgrades/`.
- Rebuild future upgrade UI from `Data/Parts/**` instead.

### `Data/Legacy/EngineProfiles/*.json`

Status: legacy Engine Sim approximation profiles.

Problem:

- Used by old `EngineSimulator*` tooling/probes.
- Parallel to the new RType Sim Engine profile system.
- No longer selected by live vehicle audio or live vehicle physics.

Recommendation:

- Keep read-only for reference/probe comparison until then.

### `Audio/EngineSimulatorSound.cs` And `Vehicle/EngineSimPowerUnit.cs`

Status: legacy runtime approximation, disconnected from gameplay.

Problem:

- These were pre-reset approximation paths from before the current RType-owned port direction.
- They can hide problems by acting as an unplanned fallback beside the new RType engine runtime.

Recommendation:

- Keep compileable for comparison probes only.
- Do not reconnect to live gameplay.
- Delete or archive after equivalent RType-owned comparison tools replace the old probes.

### Removed `Data/RTypeEngineProfiles/BaseEngines` And `Parts`

Status: removed transitional RType engine assembly scaffold.

Reason:

- Duplicated data now represented by `PartCatalogs`.
- Hand-built the B16B stock runtime assembly outside the new vehicle build system.

Replacement:

- `VehicleBuilds/ek9_showroom_stock.json`
- engine/part catalogs
- build/catalog resolver producing runtime profile output

### `Data/Vehicles/ek9_reference_2000.json`

Status: active runtime file, but overloaded.

Problem:

- Contains chassis, physics, engine, audio, tyres, setup-like values, and current prototype behavior.
- Runtime depends on it directly.

Recommendation:

- Rename conceptually to `ek9_chassis.json`.
- Gradually move installed engine/parts to `VehicleBuilds`.
- Keep vehicle physics fields until the build resolver can assemble runtime parameters.

## Recommended Migration Order

1. Done: rename `Data/gt1_engine.json` to `Data/Simulation/arcade_physics.json`.
2. Done: update `GameLaunchOptions.DefaultSimulationEngineDefinitionPath`.
3. Add compatibility fallback for the old path only if needed for command-line scripts.
4. Done: add `VehicleBuildDefinitionLoader`.
5. Done for RType engine/audio: add build/catalog resolver for block/head/part/tune IDs.
6. Done: add `dotnet run -- --validate-rtype-builds`.
7. Done for transitional validation: validator now reports chassis, gearbox, final drive, tyre, brake, suspension, mass, CG, and driven-wheel data from the active vehicle file.
8. Done: add catalogs for drivetrain, suspension, brakes, tyres, wheels, and chassis/body shells.
9. Done for validation: build validator resolves those non-engine catalog ids.
10. Done for the bridge: add `VehicleBuildDefinitionLoader` that resolves chassis, drivetrain, suspension, brakes, wheels, tyres, mass buckets, and RType engine assembly from build/catalog data.
11. Done for parity: add `dotnet run -- --compare-vehicle-build`.
12. Done: add conversion from resolved build data into `VehicleSimulationParameters`.
13. Done: make `RacingGame` select `VehicleBuilds/ek9_showroom_stock.json`, not the chassis JSON directly.
14. Done: migrate brake system, tyre model, aero package, steering setup, and handling setup into parts/tunes.
15. Done for live runtime selection: disconnect old `EngineSimulatorSound` and `EngineSimPowerUnit` gameplay hooks.
16. Done for legacy profile data: move old `Data/EngineProfiles` files under `Data/Legacy/EngineProfiles`.
17. Migrate remaining legacy-only runtime fields into catalogs/tunes or explicit build/runtime buckets.
18. Rename `ek9_reference_2000.json` to `ek9_chassis.json` once remaining legacy-only fields are migrated.
19. Move `Data/RTypeEngineProfiles/PartCatalogs` to `Data/Parts/Engines`.
20. Done for upgrades: move old `Data/Upgrades` under `Data/Legacy/Upgrades`.
21. Move old `Data/Setups` under `Data/Legacy` when replacements are ready.
22. Delete legacy files only after probes/build/runtime no longer reference them.

## Target Runtime Flow

```text
selected vehicle build
  -> chassis definition
  -> engine block/head/parts catalogs
  -> selected engine tune
  -> drivetrain/suspension/brake/tyre catalogs
  -> resolved vehicle simulation parameters
  -> resolved RType engine assembly
  -> game runtime
```

This keeps showroom cars, user-owned cars, bought parts, installed parts, and resolved physics/audio data separate.
