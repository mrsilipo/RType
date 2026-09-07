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

## Deterministic FF Limit-State Probe

Added executable probes:

- `--classic-tyre-envelope-probe`
- `--classic-ff-limit-state-probe`

These are diagnostic only. They do not change production tuning.

Current tyre-envelope result:

- Front tyre: `muRef=1.360`, peak around `9.4 deg`, sliding grip `0.97`, load sensitivity `0.120`.
- Rear tyre: `muRef=1.080`, peak around `8.2 deg`, sliding grip `0.96`, load sensitivity `0.120`.
- At a fixed axle load, changing wheel load split from `50/50` to `90/10` only reduces total axle lateral capacity to about `95.7%` of the even-load case.
- Post-peak force is extremely broad: the tyre still carries roughly `96-97%` of peak force by `20 deg` slip.

Current FF limit-state result:

- `steady-low`: reaches about `0.55/0.58g`, stays in `LOADED`, verdict `too-safe`.
- `steady-near`: reaches about `1.29/1.31g`, inside rear falls below `900 N`, but rear capacity loss is only about `5.2%`; verdict `rear-load-no-capacity-loss`.
- `power-exit`: reaches about `0.94/0.99g`, front drive/cornering state is plausible; verdict `limit-state-present`.
- `lift-mid`: reaches about `1.19/1.23g`, rear unloads but rear capacity loss is only about `8.5%`; verdict `rear-load-no-capacity-loss`.
- `trail-release`: reaches about `1.40/1.44g`, inside rear becomes very light and front tyres overdrive under combined braking/cornering; verdict currently `assist-leash` with a strong `combined-slip-front` signature.
- `bad-lift`: reaches about `1.45/1.49g`, but cleanup/yaw recovery still contributes materially; verdict `assist-leash`.
- `catch-lift`: countersteer case shows strong cleanup/yaw recovery contribution; verdict `assist-leash`.
- `left-right`: reaches about `0.81/1.37g`; rear unloads, but capacity loss is still only about `5.2%`; verdict `rear-load-no-capacity-loss`.

Tyre candidate matrix result:

- `progressive-A` post-peak: `slidingGrip=0.78`, `falloff=18 deg`.
- `stronger-B` post-peak: `slidingGrip=0.68`, `falloff=16 deg`.
- Stronger load sensitivity probe: `loadSensitivity=0.22`.
- These candidates improve the static tyre envelope on paper, but they barely change the current FF manoeuvre results.
- Reason: in the ordinary lift, steady, power-exit, and left-right cases the rear tyre is usually operating around `4-6 deg` slip, below or near peak, not deep in the post-peak region.
- Even stronger load sensitivity only raises typical rear capacity loss from roughly `5.2%` to about `6.0%` in the left-right case and from roughly `10.1%` to about `12.2%` in the bad-lift case.
- Therefore post-peak falloff is still worth fixing later, but it is not the first missing link for Joe's current "no provokable FF rotation" feedback.

Earlier-chain FF rotation audit result from `--classic-ff-rotation-chain-audit`:

- `lift-mid`: inside rear drops to about `870 N`, rear left/right load difference reaches about `1942 N`, but rear capacity loss peaks at only `8.5%` and rear slip stays around `5.1 deg`. Classification: `rear-unloads-but-capacity-barely-falls`.
- `left-right`: inside rear drops to about `911 N`, rear load difference reaches about `1966 N`, but rear capacity loss peaks at only `5.1%` and rear slip stays around `5.2 deg`. Classification: `rear-unloads-but-capacity-barely-falls`.
- `trail-release`: inside rear drops to about `357 N` and rear capacity loss reaches `51.5%`, but yaw recovery/cleanup and brake regulator activity are material. Classification: `natural-state-may-be-assist-leashed`.
- Rear unloading rate is not obviously too slow. The lift case shows unload rates over `3000 N/s`; trail braking exceeds `10000 N/s`.
- The weak arrow for ordinary lift/reversal is not "rear load does not move." It is "rear load moves, but rear axle capacity and rear slip do not change enough to create attitude."

Tyre cornering-stiffness audit result from `--classic-tyre-cornering-stiffness-audit`:

- The production rear tyre's normalized slip requirement is load-invariant: `25/50/75/90%` of available lateral force occurs at about `2.54/3.91/5.31/6.39 deg` whether the rear wheel is at `350 N` or `2000 N`.
- Probe-only stiffness-load candidates behave correctly in isolation. With a strong candidate at `350 N`, the rear tyre needs about `3.73/5.49/6.92/7.70 deg` for `25/50/75/90%` of available force.
- However, those candidates barely change the dynamic lift and left-right cases:
  - `lift-mid` rear slip remains about `5.1 deg`.
  - `left-right` rear slip remains about `5.2 deg`.
  - rear capacity loss remains about `5-9%`.
- Trail braking does raise rear slip slightly, but it also increases yaw recovery/cleanup activity, so it is not the clean case to tune first.

Interpretation:

The rear tyre law can be made less authoritative at low load, but that alone does not create the missing FF rotation. In the actual manoeuvres, the rear axle is not being forced into a meaningfully different slip/yaw role. The next weak link is probably rear yaw balance or assist/cleanup suppression, not just rear cornering stiffness.

Rear yaw-balance / assist-suppression audit result from `--classic-rear-yaw-assist-suppression-audit`:

- Yaw recovery alone is not the main leash in clean lift/left-right cases.
- Body-slip damping alone is not the main leash.
- Rear-follow alone is not the main leash.
- Speed retention alone is not the main leash.
- Lateral-velocity damping is the first assist that materially changes the state:
  - `lift-mid`: rear slip rises from about `5.06 deg` to `5.97 deg`, beta rises from `3.92 deg` to `4.97 deg`.
  - `left-right`: rear slip rises from about `5.21 deg` to `6.06 deg`, beta rises from `4.13 deg` to `5.13 deg`.
- All cleanup off exposes the larger underlying rotation:
  - `lift-mid`: rear slip rises to about `15.67 deg`, beta to about `13.98 deg`.
  - `left-right`: rear slip rises to about `6.69 deg`, beta to about `5.87 deg`.

Interpretation:

The physical model can generate more rotation than Joe currently feels, but lateral-velocity damping is acting like a broad chassis stabiliser during normal limit-driving states. At the same time, the physical rear capacity change remains small in ordinary lift/reversal, so there are two issues:

```text
primary leash: lateral-velocity damping is too active during intended controllable FF rotation
secondary physical gap: rear load/capacity consequence is still mild outside trail braking
```

This suggests the next production candidate should not remove all cleanup. It should redesign lateral-velocity damping into an intervention system, similar to yaw recovery: mostly inactive during normal lift/reversal, active only for runaway lateral velocity/beta states.

The important classification:

```text
driver input
-> load transfer: present
-> axle capacity changes: too weak in ordinary lift/reversal
-> front/rear force balance: present but very forgiving
-> yaw/beta consequence: partly suppressed by cleanup/recovery in hot transients
-> provokable/catchable FF rotation: not yet present enough
```

## Revised Next Branch

Recommended next branch:

1. Run a targeted FF limit-state validation pass.
2. Compare steady throttle, lift-off, and trail braking at the same entry speed/radius.
3. Log per-wheel load, tyre usage, front/rear yaw contribution, beta, betaDot, and assist contribution.
4. Confirm whether the rear axle actually loses enough lateral authority during lift/trail braking.
5. Treat post-peak shape as a necessary tyre-quality fix, but not the immediate answer to the current rotation gap.
6. Redesign lateral-velocity damping as conditional recovery rather than always-on chassis cleanup.
7. Keep body-slip damping, rear-follow, speed retention, and yaw recovery frozen during that candidate so Joe can isolate the feel change.
8. Then inspect combined-slip shape so braking/throttle changes lateral stiffness and peak behaviour progressively, not only by final force clamping.
9. If the lateral damping redesign exposes too much or too little rotation, revisit rear load/capacity consequence with a clearer road-test signal.
10. If loads, tyre capacity, and assists all check out but Joe still cannot feel chassis state moving, return to per-corner suspension/ARB dynamics.

Do not start by lowering rear grip globally. The missing target is conditional FF rotation from load, tyre state, brake/lift state, and suspension balance.

## Lateral Velocity Damping Redesign

Production lateral-velocity damping is now conditional rather than always-on.

The legacy path is still available only as a probe switch through `UseLegacyLateralVelocityDampingForProbe`, but normal production behaviour gates the damping by:

- vehicle speed
- beta magnitude
- beta/betaDot divergence
- lateral-velocity growth
- rear slip
- driver countersteer/corrective intent

The intent is:

```text
normal cornering / stable beta
-> no lateral-velocity damping

moderate lift-off or left-right rotation
-> mostly free

large and growing beta/lateral velocity
-> progressive bounded recovery
```

Focused audit result from `--classic-rear-yaw-assist-suppression-audit`:

- `lift-mid`
  - conditional: rear slip `5.97 deg`, beta `4.97 deg`, lateral damping force `0 N`, activation `0.00`
  - legacy lateral damping: rear slip `5.06 deg`, beta `3.92 deg`, lateral damping force about `3651 N`
  - lateral damping off: matches conditional
- `left-right`
  - conditional: rear slip `6.06 deg`, beta `5.13 deg`, lateral damping force `0 N`, activation `0.00`
  - legacy lateral damping: rear slip `5.21 deg`, beta `4.13 deg`, lateral damping force about `3269 N`
  - lateral damping off: matches conditional

Interpretation:

The old lateral-velocity damping was acting as a broad chassis leash during exactly the manoeuvres where the EK9 should be allowed to move. The new conditional path is currently invisible in the clean lift and left-right validation cases, so Joe should feel the liveliness of the `latVel-off` diagnostic without deleting the safety-system architecture outright.

Remaining caution:

The full FF limit-state probe still reports assist activity in high-load cases, mostly through other cleanup/yaw-recovery paths. This pass intentionally did not retune those systems. Road testing should decide whether the now-unleashed lateral state creates enough provokable FF attitude before any further assist work.

Follow-up road feedback from Joe:

- braking felt much weaker, like a hydraulic leak rather than a hard initial hit
- turning lost bite and again felt like steering was preventing corner entry
- chassis felt less glued, but without a useful rotation/countersteer alternative
- no provoking or countersteer was available

Front path-authority / brake-bite audit result from `--classic-front-path-authority-audit`:

- Peak braking did not change:
  - `brake-turn`: conditional `1.15g`, legacy `1.15g`
  - `trail-release`: conditional `1.15g`, legacy `1.15g`
- Brake onset did not change:
  - both profiles reach `0.25g`, `0.50g`, and `0.80g` in about `0.01s`
  - both profiles reach `1.00g` in about `0.02s`
  - peak longitudinal jerk is the same at about `110 g/s`
- Brake pressure regulator behaviour did not change:
  - both profiles bottom at brake pressure ratio `0.10` in the hard brake cases
- The big change is lateral/path authority:
  - `steady-near`: conditional `1.17g`, legacy `1.31g`
  - `brake-turn`: conditional `1.25g`, legacy `1.38g`
  - `trail-release`: conditional `1.27g`, legacy `1.42g`
  - legacy lateral damping contributes about `3.4-3.8 kN` of direct chassis lateral force in these cases
- Time-aligned resultant-g sanity check:
  - `brake-turn`: conditional resultant peak `1.29g`, legacy `1.49g`
  - `trail-release`: conditional resultant peak `1.30g`, legacy `1.47g`
  - peak braking occurs early around `0.06s`
  - peak lateral/resultant acceleration occurs later around `0.7-0.8s` for conditional
  - front tyres are already at `1.00` grip usage in brake-turn/trail-release

Interpretation:

The current build has not lost actual brake force or brake-pressure response. It has lost the old artificial lateral/path force that made braking and turn-in feel urgent. That old force was not tyre force and should not be restored as the final answer, but it proves the honest tyre/chassis path is not yet producing enough perceived bite on its own.

The next weak link is front path authority and brake/turn feel, not peak brake force. The important question is now:

```text
what physical tyre/chassis mechanism should create the initial bite that legacy lateral damping was faking?
```

Candidates:

- front tyre cornering stiffness / low-slip force buildup is too weak for the AD09 target
- combined slip leaves the front at `1.00` grip usage too early under braking, making turn-in feel blocked
- yaw/body response is still relying on chassis-side forces rather than front tyre yaw moment
- previous legacy lateral-g numbers were partly unrealistic and should not be chased exactly

Do not increase peak brake force to fix this. The measured missing piece is not peak decel; it is the coupling between hard braking, front tyre bite, yaw response, and chassis attitude.

Also do not simply add front lateral authority while braking. In the current brake-turn/trail-release cases the front tyres are already at `1.00` combined usage, and the current resultant acceleration is already around `1.30g`. The legacy feel reached nearly `1.5g` resultant by adding non-tyre lateral force, which is likely beyond the intended stock EK9 + AD09 reference envelope.

The next probe should inspect the combined-slip shape itself:

```text
same front tyre budget
but different Fx/Fy sharing and force buildup through trail-brake release
```

The desired behaviour is not more total force. It is a more believable transition:

```text
hard brake
-> front tyres spend much of budget longitudinally
-> brake release progressively returns lateral force
-> chassis/load/yaw attitude changes enough that the event feels urgent
```

Combined-slip shape audit result from `--classic-front-path-authority-audit` and `--classic-front-brake-combined-grip-probe`:

- Current combined-grip equation is:

```text
demand = abs(Fx / Fmax)^p + abs(Fy / Fmax)^p
```

with `p=2.35`; if demand exceeds `1.0`, the force vector is scaled unless brake+steer lateral priority is active.

- Diagnostic combined-slip shape candidates did not recover the lost bite:
  - `shape-stiff`
  - `shape-peak`
  - `shape-both`
- All three reduced brake-turn/trail-release lateral authority instead of creating a better loaded transition.
- Current brake-turn/trail-release already have front tyres at `1.00` combined usage.
- In the detailed front brake/combined-grip probe, once steering/lateral demand is established, the brake-pressure regulator drives front/rear pressure ratios down to the `0.10` minimum while front lateral force sits near capacity.

Interpretation:

Joe's brake feel likely has two phases:

```text
initial brake hit
-> still strong numerically

brake + steer overlap
-> regulator strongly backs service brake pressure away
-> front lateral force is preserved
-> old lateral damping no longer adds fake path force
-> driver perceives this as weak braking and weak turn-in
```

The current problem is therefore not a simple peak-grip shortage, and the first combined-slip candidates are not a production fix. The next audit should focus on the brake pressure regulator and brake+steer priority as part of the combined-slip system:

- Is the regulator reducing pressure too aggressively once lateral demand appears?
- Is `0.10` minimum pressure too low during hard brake+turn for the intended GT-style feel?
- Is brake+steer lateral priority preserving lateral force in a way that feels calm rather than loaded?
- Can a more physical combined-slip/brake-pressure controller share Fx/Fy progressively without collapsing service brake pressure?

Do not solve this by restoring legacy lateral velocity damping. Also do not solve it by increasing peak brake force. The fix likely belongs in the interaction between brake pressure state, combined tyre usage, and lateral-force priority during brake+steer overlap.

Brake-pressure / brake-steer priority audit result from `--classic-brake-pressure-priority-audit`:

- Current config:
  - brake pressure release rate: `38.0/s`
  - brake pressure minimum ratio: `0.10`
  - brake-steer lateral priority: `0.65`
  - brake-steer front/rear brake multipliers: `0.72 / 0.18`
- The pressure regulator does not instantly collapse front pressure at steering onset:
  - `brake-turn`: front regulator active after about `0.01s`, but front pressure reaches minimum only around `0.78s`
  - `trail-release`: front reaches minimum much later, around `1.53s`
  - rear pressure reaches minimum sooner, around `0.58s`
- At `0.5s`, current brake-turn pressure is still about `0.86 / 0.90` front/rear.
- The larger immediate brake-force loss is the brake-steer overlap scaling before the pressure regulator finishes reacting:
  - current brake-turn removes about `29.2%` of requested service brake
  - the less-aggressive diagnostic profile removes about `15.7%`
- Diagnostic comparison:
  - `slower-release` changes the result only mildly
  - `higher-min` has little early effect because front pressure has not reached minimum yet
  - `less-priority` preserves substantially more service brake at `0.5s` (`10550 N` vs `8949 N`) while keeping peak decel at `1.15g`
  - `no-reg-cap` is not useful as configured; the sane cap is too low and drops peak decel to about `0.56g`

Interpretation:

Joe's hydraulic-leak feel is more likely coming from the brake-steer priority/proportioning layer than from the pressure regulator alone. The code is deliberately softening service brake demand during brake+steer overlap, especially at the rear, before the tyre/brake pressure state gets a chance to feel loaded.

The next production candidate should therefore be narrow:

```text
reduce brake-steer service-brake reduction
keep pressure regulator state
keep peak brake force unchanged
keep tyres/steering/yaw/suspension/assists unchanged
```

A sensible first road-test candidate is close to the diagnostic `less-priority` profile:

- lower brake-steer lateral priority from `0.65` toward about `0.20`
- raise brake-steer front/rear brake multipliers from `0.72 / 0.18` toward about `0.88 / 0.42`
- do not change pressure regulator release/minimum yet

Expected road-test difference:

- braking while steering should feel more loaded and less like the car is quietly releasing the brakes
- front tyres should still share Fx/Fy rather than receiving magic grip
- cornering may feel slightly more constrained under full brake, which is acceptable if brake release returns authority progressively

Production candidate applied:

- `brakingSteeringLateralPriority`: `0.65 -> 0.20`
- `brakingSteeringFrontBrakeMultiplier`: `0.72 -> 0.88`
- `brakingSteeringRearBrakeMultiplier`: `0.18 -> 0.42`
- unchanged: peak brake force, brake pressure release/apply rates, brake pressure minimum, steering, tyres, suspension, yaw recovery, lateral damping, low-speed/camera systems

Validation after applying candidate:

- `--classic-brake-pressure-priority-audit`
  - current brake-turn service-brake removal now `15.7%` instead of the previous `29.2%`
  - brake force at `0.5s` in brake-turn is now about `10550 N` instead of about `8949 N`
  - peak decel remains `1.15g`
  - peak resultant remains about `1.29g`
  - rear does not immediately return to the old continuous saturation failure
- `--classic-front-path-authority-audit`
  - brake-turn current resultant peak about `1.29g`
  - trail-release current resultant peak about `1.28g`
  - legacy lateral damping still reaches about `1.45-1.46g`, confirming that the old feel remains partly fake lateral force

Road-test questions for this candidate:

- Does hard braking feel like it hits again?
- While braking and steering, does the car feel genuinely loaded rather than politely slowing?
- Does too much brake now produce understandable understeer?
- Does releasing brake progressively give steering back?
- Does trail braking feel more like something you can balance?

Follow-up road feedback from Joe:

- braking is still too subtle
- understeer begins to appear, but only about `5%` of the expected effect
- releasing brake gives steering back, but braking never seems to take much away in the first place
- Controller Mode A may be contributing because the brake input feels too slowly built rather than immediately commanded
- no low-speed/camera weirdness regressed

Brake-stack separation audit result from `--classic-brake-stack-separation-probe`:

- Straight-line raw mechanical braking:
  - peak decel `1.35g`
  - front/rear longitudinal tyre threat `1.40 / 2.21`
  - both axles reach `1.00` grip usage
  - classification: `tyre-budget-threat-present`
- Straight-line regulator-only / full-production:
  - peak decel about `1.32g`
  - regulator trims pressure to about `0.37 / 0.27` front/rear
  - full production matches regulator-only because no steering overlap is present
- Fixed-steer raw mechanical braking:
  - peak decel `1.31g`, lateral peak `1.02g`, resultant peak `1.46g`
  - front/rear longitudinal tyre threat `1.38 / 6.85`
  - this confirms the raw brake system can massively over-ask the tyres, especially the unloaded rear
- Fixed-steer regulator-only:
  - peak decel `1.24g`, lateral peak `1.21g`, resultant peak `1.30g`
  - pressure reaches `0.10 / 0.10`
  - front/rear tyres reach `1.00 / 1.00` usage
  - classification: `regulator-civilises-event`
- Fixed-steer full-production:
  - peak decel `1.21g`, lateral peak `1.22g`, resultant peak `1.31g`
  - brake-steer layer removes `15.7%` of requested service brake
  - front/rear usage `1.00 / 0.88`
  - pressure reaches `0.10 / 0.10`

Interpretation:

The EK9 brake hardware path is not too weak in the physics model. With assists removed, it immediately threatens and exceeds tyre budget. The soft/polite feel is caused by the control layers above raw braking:

```text
raw mechanical brake demand: strong, even excessive
pressure regulator: strongly civilises the event
brake-steer priority: still removes additional service brake during steering overlap
Controller Mode A: digital brake request takes too long to reach full
```

Controller Mode A digital-brake shaping currently matches `RacingInputReader`:

```text
initial digital brake = 0.35
time to 50% = 0.20s
time to 75% = 0.38s
time to 100% = 0.65s
```

That is too slow for the desired GT-era digital brake feel. The next narrow production candidate should change only Controller Mode A digital brake shaping first, because Joe is driving in Mode A and the simulator is not receiving full brake request until more than half a second after button press.

Recommended next candidate:

- keep raw brake force unchanged
- keep brake-pressure regulator unchanged
- keep brake-steer priority unchanged for this isolated test
- shorten digital brake full-pressure time from `0.65s` to a diagnostic range around `0.16-0.20s`
- raise or preserve the initial digital bite around `0.35-0.45`
- keep release quick and direct

Controller Mode A brake-shaping candidate applied:

- `DigitalBrakeInitialPressure`: `0.35 -> 0.40`
- `DigitalBrakeFullPressureSeconds`: `0.65 -> 0.18`
- unchanged: raw brake force, pressure regulator, brake-steer priority, tyres, steering, suspension, yaw recovery, cleanup assists

Validation after applying candidate:

- `--classic-brake-stack-separation-probe`
  - time to `25%`: `0.00s`
  - time to `50%`: `0.05s`
  - time to `75%`: `0.10s`
  - time to `100%`: `0.18s`
  - digital release to zero is immediate on button release unless an analogue trigger is still held
- Physical brake-stack rows are unchanged:
  - raw mechanical braking still threatens tyre budget
  - regulator-only still civilises the event
  - full production still applies the same brake-steer/regulator behaviour

Road-test target:

```text
digital brake button
-> immediate hard request
-> raw brake system threatens tyre budget
-> regulator manages it
-> brake + steer feels loaded rather than politely interpreted
```

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

## High Speed Ring Authored Track Session - 2026-09-07

This session moved High Speed Ring from a generated visual track toward an authored Blender-driven visual and physical surface workflow.

### Authored Asset Ownership

High Speed Ring now treats the Blender file as the visual source of truth:

```text
Assets\Tracks\HighSpeedRing\
  Editable\HighSpeedRing_Authoring.blend
  Runtime\HighSpeedRing_Authored.glb
  Textures\
  Documentation\HighSpeedRing_AuthoredManifest.json
  Documentation\HighSpeedRing_AuthoredManifest.md
```

Project rule established:

- Generated files are temporary by default.
- Authored or approved files are persistent.
- Procedural bootstrap exports should go under `Temp\TrackExport\HighSpeedRing\` unless explicitly promoted.
- The procedural track still owns gameplay data such as spline, checkpoints, lap logic, AI reference path, and start metadata where applicable.
- The authored GLB owns the visible road, kerbs, scenery, terrain, walls, signs, props, and any authored visual changes.

The build-integrated import path exports the current authored Blender scene and only replaces `Runtime\HighSpeedRing_Authored.glb` when the generated GLB bytes differ:

```powershell
dotnet build .\RType.csproj
```

To disable the build-time High Speed Ring authored import:

```powershell
dotnet build .\RType.csproj /p:ImportHighSpeedRingAuthoredTrackOnBuild=false
```

Last recorded authored import result:

- Build passed.
- Objects: `28`, visible exported: `26`, hidden omitted: `2`.
- Meshes: `19`.
- Vertices: `18732`.
- Triangles: `18700`.
- Materials: `13`.
- Textures: `7`.
- Missing textures: `0`.
- Duplicate authored IDs: `0`.
- Validation passed: `True`.
- GLB changed: `True`.

### Authored Runtime Import

Runtime High Speed Ring visuals now load from:

```text
Assets\Tracks\HighSpeedRing\Runtime\HighSpeedRing_Authored.glb
```

The runtime authored GLB path preserves Blender object/node names for diagnostics. Hidden Blender objects are omitted by the export, so hidden bootstrap objects such as old block walls should not appear in-game after a fresh import.

Material and texture handling notes:

- Visible Blender meshes are baked/exported, including modifiers and converted curves.
- Texture sampling for the authored track is nearest/point sampling for the intended pixel-art look.
- Transparency is detected from glTF alpha mode, base colour alpha, and texture alpha pixels.
- Known transparent authored meshes include fence, barrier, grandstand, and start-finish checker objects.
- Some Blender materials still trigger the exporter warning about multiple image texture nodes feeding one glTF texture. This is not treated as fatal, but those material graphs may need cleanup if a specific material imports differently from Blender.

Coordinate handling:

- Blender authoring is Z-up in the Blender scene.
- Blender exports glTF as Y-up.
- The runtime imports the authored glTF into GranTurismo world coordinates as X right, Y up, Z forward, in metres.
- Start pole markers are treated as front-centre car markers. The spawned vehicle centre is placed half a body length behind the selected pole marker.

### Driveable Surface Rule

The explicit authoring rule is:

```text
Any authored GLB mesh/node ending exactly with _Driveable is physical tyre ground.
```

Examples:

- `Track_Asphalt_Driveable`
- `Kerb_Left_Driveable`
- `Kerb_Right_Driveable`
- `Terrain_Inner_Driveable`

Do not infer driveability from material, proximity, neighbouring meshes, or visual category. If an authored object should not support tyres, remove the `_Driveable` suffix in Blender. If terrain should be physical off-road, keep the suffix and make sure its coverage overlaps adjacent driveable surfaces enough to avoid tiny uncovered slivers.

The fall-through investigation ended as an authored coverage issue, not a runtime contact bug:

- First miss area: approximately `X=1028`, `Z=212`.
- Wheel: `FL`.
- Time: `4.417s`.
- Speed: `5.12 m/s`.
- Previous source: `Terrain_Inner_Driveable`, triangle `2151`.
- Current candidate triangles: `0`.
- Classification: probe left `Track_Asphalt_Driveable`, was supported by `Terrain_Inner_Driveable`, then moved about `0.022m` beyond the terrain edge.

Runtime contact behaviour should remain unchanged unless a new runtime-side failure is proven:

- no procedural ground fallback,
- no proximity-based driveable inference,
- no wider blind snap search,
- keep anti-tunnel thresholds,
- keep suspension ray logic,
- keep spatial grid/contact filtering.

The coverage debug output remains useful:

```text
Temp\TrackExport\HighSpeedRing\DriveableCoverage_FirstMiss.svg
```

### Physical Chassis Progress

Phase 1 established authored mesh contact:

- `_Driveable` triangles are collected from authored GLB nodes after world transforms.
- `TryGetSurfaceContact(...)` is the primary contact API.
- Contact returns hit point, geometric normal, source mesh/node, triangle identity, and diagnostics.
- A uniform X/Z spatial grid avoids brute-force triangle checks.
- Four independent wheel contacts feed the active `ClassicFourWheelVehicleSimulator`.

Phase 2 established contact-plane tyre force directions:

- Each wheel builds a contact frame from geometric normal `N`, projected wheel-forward tangent `T`, and lateral tangent `B`.
- Existing tyre equations are preserved.
- Tyre forces are reconstructed into world space through the contact frame.
- Missing authored contact gives zero tyre force in physical modes.

Phase 3A added shadow suspension diagnostics:

- Wheel centre is solved along the suspension axis against the contacted road plane while respecting tyre radius.
- Dynamic suspension travel is zero at the loaded/static ride-height reference.
- Static preload equals configured static corner load.
- `MaxCompressionMeters` and `MaxDroopMeters` are treated as travel around loaded static ride height.
- Spring rates are currently treated as effective wheel rates with motion ratio `1.0`.

Phase 3B added physical chassis Y, pitch, and roll authority:

- A single `Vector3` chassis velocity is authoritative in authored physical mode.
- World gravity is applied once as `(0, -9.81, 0)`.
- Suspension spring, damper, bump-stop, and ARB support forces move the chassis vertically and drive pitch/roll.
- Hardpoints are full chassis-local offsets transformed by the physical chassis orientation.
- Roll restoring convention and damping were fixed and validated.
- Pitch symmetry is validated at `+/-3deg`; `+/-5deg` exceeds suspension reach and is not a valid symmetric pitch gate.

Phase 3C-A added physical load diagnostics:

- Tuned normal load, suspension support magnitude, road-normal projected physical load, filtered physical load, and hybrid candidate are reported side by side.
- Physical suspension responds correctly to crests, dips, bumps, drops, contact loss, and landing.

Phase 3C-B added physical ARB support:

```text
axleRollAngle = atan2(leftTravel - rightTravel, trackWidth)
arbTorqueNm = arbRateNmPerRad * axleRollAngle
arbWheelForceN = arbTorqueNm / trackWidth
leftArbContributionN = +arbWheelForceN
rightArbContributionN = -arbWheelForceN
```

ARB force is applied as equal-and-opposite support force contributions at the suspension hardpoints:

- `FL_ARB + FR_ARB = 0`
- `RL_ARB + RR_ARB = 0`

Tyre grip still does not use physical load by default.

Phase 3C-C added controlled tyre-load authority modes:

- `Tuned`: existing load path, default.
- `PhysicalRaw`: tyre load is `max(0, dot(unclamped support vector including ARB, road normal))`.
- `PhysicalFiltered`: same physical load through a first-order filter with default `tau = 0.05s`.
- `Hybrid`: diagnostic only; current data suggests it double-counts tuned and physical transfer.

Physical modes do not normalize total load back to vehicle weight. Contact loss gives `0 N` tyre load and `0` tyre force, with no tuned fallback.

Runtime comparison commands:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --tyre-load-authority Tuned
dotnet .\bin\Debug\net8.0-windows\RType.dll --tyre-load-authority PhysicalRaw
dotnet .\bin\Debug\net8.0-windows\RType.dll --tyre-load-authority PhysicalFiltered --physical-load-filter-recontact SeedFromRaw --physical-load-filter-tau 0.05
dotnet .\bin\Debug\net8.0-windows\RType.dll --tyre-load-authority PhysicalFiltered --physical-load-filter-recontact ResumeFromZero --physical-load-filter-tau 0.05
```

Hybrid is intentionally not exposed as a normal runtime option.

### Current Probe Evidence

Most recent tyre-load authority comparison:

- Static flat all modes total: `10398.6 N`.
- PhysicalRaw acceleration stable, final speed: `8.453 m/s`.
- PhysicalRaw braking stable, final speed: `7.988 m/s` versus Tuned `8.018 m/s`.
- PhysicalRaw cornering symmetric, yaw approximately `-/+0.237 rad/s`, roll approximately `+/-0.982deg`.
- PhysicalFiltered cornering symmetric, yaw approximately `-/+0.274 rad/s`, roll approximately `+/-1.268deg`.
- Crest unload visible, minimum projected load: `6790.5 N`.
- Dip load increase visible, peak projected load: `14239 N`.
- Landing raw peak input: `52146.1 N`.
- Filtered landing peak previously measured around `38446 N` for resume-from-zero and `40953.9 N` for seed-from-raw.

User live drive feedback after the physical chassis/load work was positive. No tyre model, grip curve, steering, yaw, spring, damper, ARB, or CG tuning should be changed until controlled runtime comparison is complete.

### Current Decisions

- Keep `Tuned` as the default tyre-load authority.
- Compare `Tuned`, `PhysicalRaw`, and `PhysicalFiltered` in real High Speed Ring driving before choosing a preferred mode.
- Do not make `Hybrid` a runtime candidate unless new data gives it a clear role.
- Do not proceed to a permanent physical tyre-load switch yet.
- Do not reopen the contact algorithm unless a new runtime-side failure is proven.
- Fix authored `_Driveable` coverage in Blender when holes or intentional terrain edges are found.
