# RType Engine Runtime Contract

Last updated: 2026-08-21

This is the boundary for the RType-owned engine simulator. It exists so the simulator can be rebuilt without mixing old sample audio, old EngineSimulator approximation code, or vehicle-physics shortcuts into the same layer.

## Runtime Input

The simulator receives one resolved engine assembly and one control frame.

Resolved assembly data comes from:

- `Data/VehicleBuilds/*.json`
- engine block/head/catalog data
- installed engine parts
- selected engine tune

The runtime must not branch on garage ids or specific upgrade names. The resolver turns parts and tune data into plain simulator values first.

Per-frame control input:

- throttle pedal, 0-1
- brake/load hint where available
- clutch engagement, 0-1
- selected gear
- gearbox ratio
- final drive ratio
- driven wheel speed
- vehicle forward speed
- limiter permission/state hints only where the simulator is not yet authority
- frame delta time

## Runtime Output

The simulator publishes one state snapshot per audio/game update:

- active flag
- profile/build id
- crank RPM
- crank phase
- cylinder event index
- combustion pressure/load
- intake pressure/load
- exhaust runner pressure/flow
- VTEC/cam blend
- limiter cut state
- fuel cut/afterfire state
- flywheel/clutch slip state
- engine brake torque
- positive engine torque
- net crank torque
- audio output peak/RMS diagnostics

Until the simulator becomes physics authority, only audio and diagnostic HUD should consume this state directly.

## Authority Handoff Order

1. Audio only.
2. Tach/RPM display source behind an explicit debug/runtime gate.
3. Engine braking source.
4. Positive torque source.
5. Flywheel and clutch coupling.
6. Gearbox/driveline coupling.

Do not skip directly to vehicle torque authority. The first stable target is a start-line engine simulation that sounds and reports state correctly while the car movement remains independent.

## Legacy Boundaries

The old `Audio/EngineSimulatorSound.cs` and `Vehicle/EngineSimPowerUnit.cs` paths are reference/probe-only.

The old profile JSON files live under:

- `Data/Legacy/EngineProfiles/`

They may be used for comparison probes, but they are not live gameplay data.

## Next Simulator Work

The next implementation chunk should strengthen the actual simulator core, not presentation layers:

- crank/piston/rod/inertia model
- chamber pressure history
- intake and exhaust gas flow
- cam and VTEC valve-flow behavior
- combustion event timing and fuel burn
- flywheel/clutch data handoff

