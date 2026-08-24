# Sim Engine Modding Requirements

Last updated: 2026-08-21

This document maps the planned engine and drivetrain mods to the RType-owned Sim Engine systems they require. The purpose is to keep the engine port aligned with future gameplay before it becomes the physics authority.

## Rule

Do not make the RType engine runtime the final RPM, torque, or driveline authority until the engine can be built from a base engine plus installed parts.

The runtime should receive a resolved `RTypeEngineAssembly` and should not contain special cases like "if B16B with race cams". Mods should change profile data; the simulator should respond naturally.

## Required Part Model

- `EngineBlock`: factory block geometry, displacement defaults, bore, stroke, rod length, deck height, compression baseline, inertia, and compatibility tags.
- `EngineHead`: factory head/valvetrain data, VTEC capability, runner geometry, flow tables, and default rev behavior.
- `Engine`: a known factory-style combination of one block plus one head. Installed parts modify this selected base.
- `BlockUpgrade`: block construction changes such as reinforced cast, sleeved closed-deck, and billet aluminum racing block.
- `HeadUpgrade`: head construction changes such as reinforced cast, race cast, and billet aluminum racing head.
- `RotatingAssembly`: crank, rods, pistons, flywheel inertia, mass effects, friction.
- `Head`: port geometry, valve sizes later, lift-flow tables, flow scaling.
- `CamSet`: intake/exhaust duration, lift, lobe centers, lobe shape/gamma, VTEC low/high pairing.
- `ValveSpringSet`: spring pressure, valve float threshold, high-RPM stability.
- `ThrottleBody`: throttle area, response curve, flow limit.
- `Intake`: plenum volume, runner length, runner cross section, runner flow, pressure behavior.
- `Header`: per-cylinder primary length, area, collector layout, pulse delay, attenuation.
- `Exhaust`: collector/muffler volume, outlet flow, damping, impulse response/acoustic profile, backpressure.
- `Ignition`: timing curve, limiter RPM, limiter cut pattern.
- `Fueling`: AFR target, fuel dose, combustion efficiency.
- `Clutch`: torque capacity, bite point, engagement curve, slip response.

## Trello Item Mapping

| Planned Mod | Simulator Coverage Needed |
| --- | --- |
| Engine Selection | Load a different base `Engine` and compatible stock/default part set for the car. |
| Cam Swapping | Swap `CamSet`; affects valve lift, duration, overlap, VTEC behavior, flow, sound timbre, and torque curve. |
| Displacement Increase | Change bore/stroke/chamber volume/compression, piston speed, airflow demand, fuel demand, torque pulses, and inertia. |
| Port Polishing | Change head lift-flow tables or flow scale by lift, affecting gas flow and exhaust pulse strength. |
| Throttle Body Enlargement | Change throttle area/flow and manifold pressure response without simply multiplying power. |
| Intake Swaps | Change plenum volume, runner geometry, pressure behavior, and intake contribution to sound/load. |
| Intake Length Changes | Change runner volume plus pressure/resonance/delay behavior; current parser has length but runtime use is still too shallow. |
| Lifter Spring Swapping | Add valve-float/spring stability behavior so high RPM can become unstable if springs are mismatched. |
| Exhaust Headers Swapping | Change primary tube length/area/collector routing, exhaust delay, backpressure, and pulse character. |
| Exhaust Swapping | Change outlet flow, collector/muffler volume, damping, acoustic impulse response, and backpressure. |
| Flywheel Swapping | Change rotating inertia, rev rise/drop, shift recovery, clutch kick feel, and launch behavior. |
| Clutch Swapping | Change clutch torque capacity, bite point, slip, shift interruption, and launch coupling. |

## Current Coverage

Already represented in the current RType profile/runtime:

- cylinders and firing order
- bore, stroke, rod length, compression ratio
- idle, redline, limiter, timing curve
- low/high cam values and VTEC blend
- head flow tables and flow scales
- intake plenum, runner flow, and manifold pressure limits
- exhaust primary length, flow constants, collector volume, pulse delay, and DSP impulse response
- simplified combustion and gas transfer
- published runtime state for diagnostics

Still missing before physics authority:

- engine assembly resolver
- separate installed part records
- compatibility tags
- meaningful throttle-body model
- meaningful intake-runner length effect
- valve spring and valve-float model
- per-cylinder header geometry
- RType-owned flywheel/clutch/driveline solver
- torque output from the same combustion state used for sound
- validation tools for part combinations

## Data Catalogs

The first pass generic part catalogs live in `Data/RTypeEngineProfiles/PartCatalogs/`.

Each catalog contains four tiers:

- `stock`
- `street`
- `clubSport`
- `proRacing`

Catalog index:

- `part_catalog_index.json`

Individual catalogs:

- `engines.json`
- `cam_sets.json`
- `displacement_kits.json`
- `port_polishing.json`
- `throttle_bodies.json`
- `intakes.json`
- `intake_lengths.json`
- `valve_springs.json`
- `headers.json`
- `exhausts.json`
- `flywheels.json`
- `clutches.json`

The engine catalogs are not part catalogs in the gameplay sense:

- `engine_blocks.json` lists factory blocks.
- `engine_heads.json` lists factory heads.
- `engines.json` lists known factory-style engine combinations.
- `block_upgrades.json` lists block construction upgrades, ending with billet aluminum racing blocks.
- `head_upgrades.json` lists head construction upgrades, ending with billet aluminum racing heads.

The other catalogs list parts that modify the selected base engine. Head swaps should eventually select a different compatible `EngineHead` for the chosen `EngineBlock`.

Mass and durability are first-class data:

- `weightKg` is used for complete parts or factory components.
- `weightDeltaKg` is used for construction upgrades that modify an existing block/head.
- `durability.rating` is the general reliability score.
- `durability.heatTolerance` is resistance to heat-related degradation.
- `durability.fatigueResistance` is resistance to repeated RPM/load cycling.
- `durability.safePowerMultiplier` is the intended safe-load margin before damage/reliability penalties.
- `durability.repairCostMultiplier` is for future economy/garage balancing.

These fields should later feed total vehicle mass, front weight distribution, rotational response where applicable, damage, reliability, and repair cost.

Current engine coverage:

- B-series VTEC: B16A, B16B, B18C
- B-series non-VTEC: B18A, B18B
- D-series: D16Y4 non-VTEC, D16Y8 VTEC
- K-series: K20A, K24A3

Some part catalog values are already consumable by the current `RTypeEnginePartDefinition` resolver through `modifies`. Other values are intentionally recorded under `data` until the typed runtime systems exist, especially clutch, valve spring, per-cylinder header detail, and engine selection.

## Next Implementation Order

1. Add an engine catalog loader for blocks, heads, and engine combinations.
2. Add compatibility validation between block/head/parts.
3. Move the B16B stock assembly to resolve from `engine_b16b` plus stock installed parts.
4. Make the runtime consume the resolved assembly without knowing which parts created it.
5. Add missing part systems in this order: throttle body, intake length, headers, valve springs, flywheel, clutch.
6. Add validation/probe output that reports assembled values before audio renders.
7. Only then gate the RType engine into tach and vehicle torque.
