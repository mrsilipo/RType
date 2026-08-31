# Classic Four-Wheel Suspension Load Ownership

This note freezes the intended ownership boundaries for the classic four-wheel suspension/load path.

## Coordinate And Wheel Convention

- Game forward is `+Z`.
- Game right is `+X`.
- Game up is `+Y`.
- Front axle positions are positive local-forward.
- Right wheels are positive local-right.

## Load Ownership

- Static wheel load is owned by resolved vehicle mass and front weight distribution.
- Longitudinal transfer target is owned by the classic load-transfer calculation from longitudinal acceleration, CG height, mass, and wheelbase.
- Lateral transfer targets are owned by the classic load-transfer calculation from lateral acceleration, CG height, mass, track width, and front/rear roll-stiffness distribution.
- Spring force is owned per corner by the FL/FR/RL/RR suspension state.
- Damper force is owned per corner by the FL/FR/RL/RR suspension state, using bump damping when travel velocity is compressing and rebound damping when extending.
- ARB contribution is represented in the front/rear lateral-transfer split through axle roll stiffness. There is not yet an independent left/right ARB twist state.
- Tyre normal load is owned by the per-corner suspension state after spring/damper response and total-load preservation.

## Double-Counting Rule

Aggregate transfer state may define per-corner target loads, but it must not also be applied directly to tyre load after the suspension state. The tyre model should receive one normal load per wheel from the suspension state.

The current first-pass architecture is:

```text
vehicle acceleration
-> longitudinal/lateral transfer targets
-> roll-stiffness front/rear lateral split
-> per-corner target loads
-> per-corner spring/damper travel state
-> per-corner normal load
-> tyre force model
```

Future refinement can split lateral transfer into geometric, unsprung, and elastic components, and can add a true ARB twist state. Those should replace or subdivide the current target-load ownership rather than adding a second load path.
