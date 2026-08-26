# RType Project Stabilization Audit - 2026-08-26

## Current Stable Baseline

RType is in a good playable checkpoint. The active runtime path is now the race-focused vehicle model:

- assembled vehicle data from purchase cars, owned garage vehicles, part catalogs, tunes, and mass resolution
- GT-style sample-loop engine audio shared by race mode and the engine room
- FF-first vehicle physics with per-wheel surfaces, load transfer, LSD torque biasing, and curb/grass blend zones
- procedural High Speed Ring as the default track
- 1080p racing HUD with procedural tachometer geometry

## Active Runtime Data Path

The preferred car path starts at `Data/PurchaseCars/2000_Ek9_Stock.json` or an owned vehicle under `Data/Garage/OwnedVehicles/`.

Runtime resolution flows through:

1. `VehicleRuntimeLoader`
2. `VehicleAssemblyResolver`
3. `VehicleBuildDefinitionLoader`
4. `EngineAssemblyResolver`
5. `VehicleMassResolver`
6. `VehicleSimulationParameters`

The old monolithic vehicle files are no longer the target system.

## Active Engine Audio Direction

The race game should use the sample-loop model, not the heavy experimental Sim Engine runtime. The current active recipe uses:

- `idle_0900.wav`
- `normal_3500.wav`
- `vtec_6200.wav`

The old `Audio/RTypeEngine` experiment remains excluded from compilation. It should be treated as reference/legacy unless deliberately revived.

## Active Physics Direction

The current vehicle solver should keep these responsibilities separated:

- physical loads, tyre grip, LSD, surfaces, and track gravity drive the car
- visual body roll, camera movement, tach bounce, and audio shutter are presentation
- speedometer reads chassis/ground speed, not visual RPM bounce
- limiter physics uses hard cut/resume limits from vehicle data

## Track Direction

High Speed Ring is the default track and replaces the older Velocity Loop direction. It is generated from the authored SVG spline and currently uses:

- 3100 m length
- 18 m road width
- flat first pass
- non-inverted SVG X/Z mapping
- labelled start marker for lap-distance zero
- flipped default travel direction as of this audit

## Legacy / Non-Runtime Areas

These should not be used for new work unless intentionally migrated:

- `Data/Legacy/**`
- `Audio/RTypeEngine/**`
- old `Data/RTypeEngineProfiles/**` path
- old `Data/Setups/**` path
- old `Data/Vehicles/ek9_reference_2000.json`
- old `Data/VehicleBuilds/ek9_showroom_stock.json`

`RType.csproj` excludes `Data/Legacy/**` from runtime content copying. Compatibility aliases may still accept old active-looking paths and redirect them to the current purchase-car data.

## Validation Commands

Use these as the minimum stabilization pass:

```powershell
dotnet build RType.csproj
dotnet run --no-build --project RType.csproj -- --physics-smoke-test
dotnet run --no-build --project RType.csproj -- --track-geometry-probe
dotnet run --no-build --project RType.csproj -- --tachometer-geometry-probe
dotnet run --no-build --project RType.csproj -- --vehicle-assembly-probe
dotnet run --no-build --project RType.csproj -- --part-catalog-integrity-probe
dotnet run --no-build --project RType.csproj -- --vehicle-catalog-probe
dotnet run --no-build --project RType.csproj -- --garage-profile-integrity-probe
```

## Commit Grouping Recommendation

Split the current large working tree into commits by system:

1. data architecture and garage assembly
2. engine audio sample-loop baseline
3. vehicle physics, surfaces, LSD, clutch, and limiter behavior
4. racing HUD and procedural tachometer
5. High Speed Ring track generation
6. docs and probes
