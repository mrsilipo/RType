# Classic Four-Wheel Checkpoint - 2026-08-31

## Current Road-Test Status

Joe's latest test after the yaw-recovery redesign:

- Huge improvement. The car now drives and feels like a real car instead of a broken physics object.
- High Speed Ring cornering, slowing, and braking are now broadly in the GT1/GT2 usability range.
- Lift-off no longer feels railroaded.
- Trail braking feels more alive than before.
- The car is still too safe and dry at the limit.
- There is not enough consequence for poor brake/steer/throttle decisions.
- There is not yet a convincing "on the threshold" balancing state.
- Provoking FF rear rotation is still not really possible.
- The current result is perhaps only about 10% of the way toward controllable FF rotation.

Interpretation:

The major broken-control problems are no longer the priority. The next problem is not "can the car turn?" It can. The next problem is whether the simulation can reproduce the moving-limit behaviour of a front-heavy FF road car.

## Frozen Wins

Treat these as fixed unless a specific regression is observed:

- Front/rear steering convention: the car now steers from the front, not like a forklift.
- Runtime generated EK9 reference rig orientation and wheel pivots.
- Steering input sign for analog and D-pad.
- Low-speed caster swing.
- Low-speed rest/sleep creep.
- Low-speed camera/chase jolt.
- RPM/gear speed runaway at limiter.
- Limiter cadence is improved enough to stop blocking handling work.
- Steering authority is strong enough for GT-style high-speed use.
- Tyre relaxation is short enough that steering no longer feels like an invisible slow driver.
- Legacy yaw recovery no longer suppresses normal cornering in production.

## Yaw Recovery Result

The old yaw recovery behaved like a broad yaw-rate servo. It was active during ordinary cornering and dramatically suppressed the car:

- `100 km/h 0.5g` validation target reached only about `0.14g`.
- `120 km/h 0.9g` validation target reached only about `0.64g`.

Disabling yaw recovery proved the base tyre/chassis had enough authority, but it also exposed excessive beta/front overdrive in some higher-energy cases.

The new production yaw recovery is now conditional:

- Normal low-beta cornering: effectively inactive.
- Moderate controllable rotation: mostly left alone.
- Higher beta / yaw excess / rear slip / beta growth: progressive bounded damping.
- Countersteer reduces recovery intervention so the driver can catch the car.
- Legacy recovery remains available only as a probe comparison.

Current validation summary:

- `legacy`: safe but dead; suppresses natural cornering.
- `off`: alive but too loose in some high-energy cases.
- `conditional`: keeps most of the recovered turning response and trims higher-beta cases without returning to the dead legacy feel.

## Current Architectural Suspect

The remaining missing behaviour is not simply grip amount.

Joe's description points to missing limit-state dynamics:

```text
lift / brake / turn
-> weight and tyre state should move
-> rear should become conditionally lighter
-> yaw attitude should become adjustable
-> driver should be able to provoke and catch rotation
```

The current car instead tends to:

```text
steer / brake
-> corner cleanly
-> remain composed
-> give little consequence or threshold balance
```

That means the next pass should look for a missing physical chain, not another broad assist tweak.

## Real EK9 / FF Behaviour Target

A stock EK9 on AD09-class tyres should not be a drift car. It should be stable and front-led in ordinary driving. However, it should be adjustable:

- Power-on: mild FF understeer as the front tyres share lateral and drive force.
- Lift-off: line tightens and rear attitude changes.
- Trail braking: rear unloads and the car becomes more willing to rotate.
- Aggressive transition: load should have to move across the chassis.
- Countersteer: should matter once the rear has rotated.

The important distinction:

```text
not: globally loose rear grip
yes: conditional rear rotation caused by load, tyre state, brake/lift state, and suspension balance
```

## Next Investigation Order

Keep current production values frozen for the next diagnostic:

- steering authority
- tyre grip and tyre curves
- tyre relaxation
- brake regulator
- yaw damping
- yaw recovery
- low-speed contact logic
- camera

Recommended next branch:

1. Run a targeted FF limit-state validation pass.
2. Compare steady throttle, lift-off, and trail braking at the same entry speed/radius.
3. Log per-wheel load, tyre usage, front/rear yaw contribution, beta, betaDot, and assist contribution.
4. Confirm whether the rear axle actually loses enough lateral authority during lift/trail braking.
5. If rear load changes but yaw balance barely changes, inspect tyre load sensitivity and combined-slip shape.
6. If loads and tyre capacity change correctly but attitude still does not, inspect suspension/per-corner state and roll stiffness influence.
7. Only after the physical chain is confirmed should assists be reduced or rebalanced.

## Specific Next Probes

Add or reuse deterministic manoeuvres:

- `100 km/h` steady throttle sweeper around `0.5-0.7g`.
- `120 km/h` committed sweeper around `0.9-1.1g`.
- Same corner with abrupt throttle lift at mid-corner.
- Same corner with trail braking and progressive release.
- Same corner with lift/brake then countersteer.
- Left-right transition with steady throttle, then with lift during transition.

For each, report:

- front/rear slip angle
- beta and betaDot
- yaw rate and yaw acceleration
- front/rear yaw moment
- FL/FR/RL/RR normal load
- inside rear load minimum
- front/rear lateral capacity
- front/rear lateral grip usage
- front/rear longitudinal grip usage
- yaw recovery activation
- rear-follow/body-slip/lateral damping contributions

## Decision Criteria

If the car cannot rotate even when:

- rear load is low,
- rear lateral capacity falls,
- driver lifts or trail brakes,
- yaw recovery is inactive,
- and rear slip remains low,

then the next gap is likely suspension/per-corner load dynamics or tyre combined-slip shape.

If the car rotates in telemetry but Joe cannot feel or catch it, then the next gap is likely presentation, force-feedback substitute cues, camera motion, or assist masking.

If yaw recovery activation appears during ordinary lift/trail events, it is still too broad.

## Commit Scope

This checkpoint should be committed with the current generated EK9 reference rig, probes, control fixes, limiter work, low-speed fixes, steering/yaw work, and documentation so the project has a stable recovery point before the next handling branch.
