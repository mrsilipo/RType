# Universal Tyre Force Pipeline

This is the runtime contract for vehicle tyre forces. The goal is one tyre model for FF, FR, AWD and future layouts; drivetrain layout only changes which wheels receive torque.

## Force Ownership

The vehicle simulator should preserve this order:

1. Driver input and assist filters produce throttle, brake, handbrake and steer requests.
2. Drivetrain/clutch/gearbox produces drive torque requests for configured driven wheels.
3. Service brakes and handbrake produce brake torque requests. The handbrake is rear brake torque only.
4. Per-wheel surface sampling supplies friction, drag, vibration and blend data.
5. Physical normal load is calculated from static mass, weight transfer, aero load, surface vibration and clamping.
6. `TyreForceRequest` combines grip budget, requested longitudinal force and relaxed slip state.
7. `UnifiedTyreForceModel` clamps the requested longitudinal/lateral force through one grip budget.
8. Passive rolling, displacement and scrub forces are added only as resistive forces that oppose local motion.
9. Final wheel force vectors are applied to chassis force/yaw accumulation.
10. Wheel angular velocity, clutch state, RPM, limiter state, audio and HUD read from the resulting physical state.

## Core Rules

- No wheel may generate propulsion without drive torque.
- Cornering may consume drive grip; it must not create extra drive force.
- Braking and lateral force must share the same tyre budget during trail braking.
- Handbrake lock should emerge from rear brake torque consuming longitudinal tyre budget, not a hidden yaw button.
- Passive surface forces must oppose local wheel motion.
- Presentation systems can smooth or shake output, but must not feed visual-only values back into physical tyre loads.

## Data Flow

Vehicle assembly and part catalogs feed:

- mass, front weight distribution and CG height
- wheelbase, front/rear track width and yaw inertia
- drivetrain layout, torque split and differential parameters
- brake torque, bias, ABS and handbrake rear torque
- tyre peak friction, stiffness, relaxation length, peak slip, sliding multipliers, rolling radius and rolling resistance
- suspension spring/ARB rates and alignment/camber behavior
- aero drag/lift/downforce terms

The simulator should not use hardcoded car-specific tyre, limiter, drivetrain or brake facts when vehicle data exists.

## Calibration Order

1. Validate data integrity and backward-compatible defaults.
2. Validate static and dynamic weight transfer.
3. Validate drivetrain torque routing for FF, FR and AWD.
4. Validate universal tyre request clamping.
5. Validate braking, trail braking and handbrake recovery.
6. Validate surface transitions, curb/grass blend and passive drag.
7. Validate limiter/RPM/clutch/audio/HUD separation.
8. Validate each car on High Speed Ring and flat probes.

## Required Probes

- `--universal-tyre-force-probe`
- `--friction-ellipse-probe`
- `--power-balance-probe`
- `--physics-smoke-test`
- `--weight-transfer-probe`
- `--drivetrain-layout-probe`
- `--tire-relaxation-probe`
- `--launch-probe`
- `--surface-probe`
- `--race-condition-probe`
- `--audio-probe`

## Telemetry

Race CSV logs should include both requested and delivered per-wheel tyre forces. This lets a bug report distinguish drivetrain/clutch/limiter request failures from tyre-budget rejection.
