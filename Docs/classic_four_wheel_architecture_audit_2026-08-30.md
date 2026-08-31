# Classic Four-Wheel Architecture Audit - 2026-08-30

## Current Conclusion

The EK9 handling issue is no longer behaving like a tyre-curve tuning problem. The six-variant tyre sweep barely moved the medium/hard rear/front slip gap, which points at architecture and force separation rather than another JSON value change.

This pass keeps the runtime simulator as a four-wheel model. No gameplay path has been replaced with a bicycle model.

## Foundation Changes

- `classicFourWheel` now resolves its active front/rear tyre facts from `VehicleSimulationParameters.FrontTyres` and `RearTyres` through a classic adapter.
- Global `Data/Simulation/classic_four_wheel_physics.json` remains responsible for solver, steering, resistance, yaw, low-speed, and assist defaults.
- `VehicleAxleGeometry` now exposes wheelbase, CG-to-front axle, CG-to-rear axle, and tracks from one shared calculation.
- `WheelKinematics` now owns local wheel velocity and slip-angle calculation as a pure helper.
- `--classic-kinematic-audit-probe` prints raw per-wheel kinematics before tyre force interpretation.

## EK9 Baseline After Per-Car Tyres

Resolved EK9 facts:

- mass: `1060 kg`
- wheelbase: `2.620 m`
- front weight: `62.0%`
- CG-to-front axle: `0.996 m`
- CG-to-rear axle: `1.624 m`
- rear/front distance ratio: `1.63`
- front tyre: `76000 N/rad`, `9.4 deg` peak, `1.36` peak friction, `0.32 m` relaxation length
- rear tyre: `70500 N/rad`, `8.2 deg` peak, `1.08` peak friction, `0.36 m` relaxation length

The geometry means some extra rear yaw-derived lateral velocity is expected on this front-heavy car. That does not mean the live cornering balance is validated. The important question is whether the final force/yaw/assist stack turns expected geometry into excessive trolley behavior.

## Probe Result

The new probe shows sane zero-yaw/zero-lateral behavior and expected rear/front distance asymmetry in pure yaw. That only validates the raw axle-distance math. It does not validate the complete cornering behavior.

The steady reference printed by the probe expects the front axle to work harder than the rear in the checked 150 km/h cases:

- medium reference: front/rear slip `6.81/4.50 deg`, gap `-2.31 deg`
- hard reference: front/rear slip `11.68/7.72 deg`, gap `-3.96 deg`

The live dynamic sim still shows the opposite trend:

- medium dynamic: front/rear slip about `3.31/5.42 deg`, gap `+2.12 deg`
- hard dynamic: front/rear slip about `3.06/6.85 deg`, gap `+3.79 deg`

So the corrected conclusion is: raw geometry is internally consistent, but live cornering balance is not yet physically validated.

The dynamic 150 km/h 25% throttle cases are not Joe-ready:

- medium: speed ends around `133 km/h`, body slip `6.6 deg`, body damping around `2918-4178 N`, slip gap around `+1.7 to +2.1 deg`
- hard: speed ends around `120 km/h`, body slip `9.2 deg`, body damping around `12726-14443 N`, slip gap around `+3.2 to +3.8 deg`

The per-car tyre wiring exposed the old cleanup-force problem harder. Treat this as useful diagnostic progress, not a final drive-test build.

## Architecture Direction

The handling stack should settle into these layers:

1. Input intent: raw keyboard, D-pad, stick, and wheel input becomes car-agnostic driver intent.
2. Vehicle geometry: explicit axle positions, tracks, CG height, mass, and yaw inertia from resolved vehicle data.
3. Wheel kinematics: per-wheel local velocity and signed raw slip angle. No tyre force logic.
4. Tyre model: slip, load, surface, relaxation, and tyre data become longitudinal/lateral tyre forces.
5. Drivetrain: FF/FR/AWD torque and engine braking route through data-driven differentials.
6. Load transfer: one consistent model feeds per-wheel normal load.
7. Assist layer: yaw recovery, rear follow, body slip damping, and retention are named, bounded, toggleable game-feel overlays.
8. Regression harness: probes run as a suite across every car definition.

## Next Implementation Work

Do not tune tyre curves next.

The per-assist matrix probe now exists as `--classic-assist-matrix-probe`.

Latest matrix result:

- `150 25% medium`: all assists on drops `17.3 km/h`; all cleanup off drops `13.7 km/h`
- `150 25% hard`: all assists on drops `29.8 km/h`; all cleanup off drops `16.2 km/h`
- `chain T1-T4`: all assists on drops `66.6 km/h`; all cleanup off drops `57.7 km/h`
- rear-follow is inactive in these cases
- speed retention is protective in hard cornering; disabling it increases hard-corner speed loss to `35.7 km/h`
- body-slip damping and lateral velocity damping trade load with each other, so tuning either one alone is misleading

The cleanup stack is creating high lateral acceleration and speed loss. With cleanup disabled, speed loss improves but body slip/rear slip grow, so the issue is not "delete the assists"; the issue is that assist forces are currently doing too much of the tyre/chassis job.

`--classic-base-force-probe` now explains the cleanup-off residual.

At 150 km/h, 25% throttle, cleanup off:

- straight: `8.9 km/h` drop, speed-axis force about `-873 N`
- small steer: `13.0 km/h` drop, speed-axis force about `-1284 N`
- medium steer: `17.6 km/h` drop, speed-axis force about `-1739 N`
- hard steer: `20.3 km/h` drop, speed-axis force about `-2006 N`

The residual is not coming from friction-circle throttle clamping in these cases:

- small/medium/hard all show `gripLoss=0 N`
- requested longitudinal force is preserved into wheel-frame longitudinal force
- steering projection is not the main loss either; it is currently a small forward contribution in these cases

The extra loss is mostly lateral tyre force doing negative work against lateral/body slip:

- medium lateral-power component: about `-1228 N`
- hard lateral-power component: about `-1961 N`

So the base model is not ignoring 25% throttle. It is generating enough body/rear slip that the tyres spend significant work scrubbing lateral motion away. That makes the next suspect the base slip/yaw/load/tyre-force relationship, not the friction circle and not rear-follow.

Next pass should inspect the base slip/yaw/load/tyre-force relationship before redesigning cleanup:

- keep the per-assist matrix as the pass/fail harness
- use the base-force probe as the pass/fail harness for cleanup-off residual speed loss
- check why medium steering reaches about `9.4 deg` body slip and rear grip usage around `0.80` with cleanup off
- audit base tyre force direction, yaw torque sign/magnitude, and load transfer before changing assist curves
- after the base relationship is understood, unify body-slip damping and lateral velocity damping into one bounded cleanup term
- preserve some high-slip/carryover recovery, because all-cleanup-off improves speed loss but increases body slip and rear slip
- only then prepare a Joe drive build

Digital steering hold-duration shaping should follow as a separate input-layer pass for keyboard/D-pad only.

## Natural Yaw Audit

`--classic-yaw-moment-probe` now traces:

- per-wheel lateral force
- per-wheel yaw moment
- front/rear/net yaw moment
- scaled yaw inertia
- explicit yaw damping acceleration
- calculated yaw acceleration
- measured yaw acceleration
- expected front lateral force from the active tyre curve

Latest result with cleanup off:

- configured yaw inertia: `914 kgm2`
- simulator scaled yaw inertia: `2240 kgm2`
- simple dimension/mass reference: about `1801 kgm2`
- scaled yaw inertia is about `1.24x` the simple reference and `2.45x` the configured vehicle value

The front tyres are not under-producing force:

- early medium turn-in front actual/expected lateral force is about `1.00`
- early hard turn-in front actual/expected lateral force is about `1.00`

The yaw equation is internally consistent:

- calculated yaw acceleration closely matches measured yaw acceleration
- wheel yaw moments sum to the measured yaw response
- no extra hidden yaw resistance appears beyond the explicit yaw damping term

The causal break is effective yaw resistance during turn-in:

- medium at `0.042s`: front moment about `-2889 Nm`, yaw acceleration about `-70 deg/s2`
- medium at `0.250s`: front moment has fallen to about `-538 Nm`, rear moment is already about `-711 Nm`
- hard at `0.042s`: front moment about `-6736 Nm`, yaw acceleration about `-168 deg/s2`
- hard at `0.250s`: front moment has fallen to about `-762 Nm`, rear moment is already about `-2735 Nm`
- hard at `0.500s`: explicit yaw damping is about `+65 deg/s2`, rear moment is about `-6877 Nm`, rear grip is saturated

So the first fault is not rear grip, friction-circle clamp, or front tyre force generation. The car initially asks the front tyres for force, the tyres produce it, and the yaw equation integrates it correctly. But the effective yaw system is too resistant/slow, so lateral velocity and body slip build faster than rotation. Then front slip collapses/reverses, the rear begins driving the yaw moment, rear grip saturates, and lateral scrub becomes the speed/RPM loss.

## Inertia Versus Yaw Damping Split

`--classic-yaw-moment-probe` now reports four counterfactual yaw accelerations:

- current inertia + current damping
- reference inertia + current damping
- current inertia + zero damping
- reference inertia + zero damping

Formula:

```text
scaledYawInertia = configuredYawInertia * classicFourWheel.yaw.inertiaScale
scaledYawInertia = 914 * 2.45 = 2240 kgm2
```

Simple inertia sanity reference is about `1801 kgm2`, so the current scaled inertia is about `1.24x` that reference.

Medium steer, cleanup off:

- `0.10s`: raw tyre moment `-2219 Nm`; damping removes `18%`; current/reference/zero/both yaw accel `-46/-60/-57/-71 deg/s2`
- `0.25s`: raw tyre moment `-1249 Nm`; damping removes `55%`; current/reference/zero/both yaw accel `-14/-22/-32/-40 deg/s2`
- `0.50s`: raw tyre moment `-2695 Nm`; damping removes `44%`; current/reference/zero/both yaw accel `-39/-56/-69/-86 deg/s2`

Hard steer, cleanup off:

- `0.10s`: raw tyre moment `-5748 Nm`; damping removes `16%`; current/reference/zero/both yaw accel `-124/-160/-147/-183 deg/s2`
- `0.25s`: raw tyre moment `-3496 Nm`; damping removes `48%`; current/reference/zero/both yaw accel `-47/-69/-89/-111 deg/s2`
- `0.50s`: raw tyre moment `-2054 Nm`; damping removes `124%`; current/reference/zero/both yaw accel `+13/0/-53/-65 deg/s2`

Classification: both materially responsible, with damping becoming dominant at the exact problem point. The `2.45` inertia scale slows initial yaw response, but explicit yaw damping removes roughly half the raw tyre yaw moment by `0.25s`, and at hard `0.50s` it fully reverses the raw yaw acceleration.

History trace:

- `Data/Simulation/classic_four_wheel_physics.json` was introduced in commit `060cdf3` already using `classicFourWheel.yaw.inertiaScale = 2.45`
- the older `classic_bicycle_physics.json` has `inertiaScale = 1.0`
- `Core/DrivabilityTuningOverlay.cs` describes the value as "Multiplier on vehicle yaw inertia" and says higher values make the car resist rotation more
- no deeper repo history explains the exact `2.45` value; based on available evidence, it is an empirical drivability/stability multiplier, not a resolved vehicle property

## Yaw Damping Zero Experiment

`--classic-yaw-damping-experiment-probe` compares current yaw damping against probe-only zero damping with:

- cleanup off
- inertia scale unchanged at `2.45`
- tyres unchanged
- steering unchanged
- drivetrain unchanged

Positive steer produces negative yaw in this simulator, so a larger negative yaw-rate magnitude means more rotation into the requested turn.

Medium steer:

- current damping speed drop: `17.6 km/h`
- zero damping speed drop: `20.5 km/h`
- yaw at `0.25s`: current `-9.5 deg/s`, zero `-11.8 deg/s`, reference `18.6 deg/s`
- body slip at `1.00s`: current `9.3 deg`, zero `12.0 deg`
- front/rear slip at `1.00s`: current `6.1/8.6 deg`, zero `8.8/11.1 deg`
- lateral tyre power at `1.00s`: current `-72 kW`, zero `-109 kW`
- equivalent drag at `1.00s`: current `-1782 N`, zero `-2702 N`

Hard steer:

- current damping speed drop: `20.3 km/h`
- zero damping speed drop: `32.3 km/h`
- yaw at `0.25s`: current `-23.1 deg/s`, zero `-28.3 deg/s`, reference `34.6 deg/s`
- body slip at `1.00s`: current `13.5 deg`, zero `23.6 deg`
- front/rear slip at `1.00s`: current `6.9/13.1 deg`, zero `17.4/22.4 deg`
- lateral tyre power at `1.00s`: current `-110 kW`, zero `-199 kW`
- equivalent drag at `1.00s`: current `-2757 N`, zero `-5061 N`

Classification: zero damping does not reveal a healthy natural response. It recovers some early yaw rate, but downstream body slip, tyre slip, scrub power, and speed loss all get worse. Existing yaw damping is over-aggressive in the yaw-moment audit, but it is also hiding a base-model instability once the car starts rotating. The next diagnostic should not simply delete yaw damping; it should isolate why the undamped base model over-rotates into excessive body slip.

## Body Dynamics Coupling Audit

`--classic-body-dynamics-probe` compares current yaw damping against probe-only zero damping with cleanup assists disabled. It checks the rigid-body body-frame relationship:

```text
bodyLateralAcceleration = bodyLateralTyreForce / mass - bodyForwardSpeed * yawRate
```

It also compares measured beta against an independent body-frame/travel-angle calculation, and inverts the front-wheel force transform to verify the tyre-to-body/world frame direction.

Result: body-frame dynamics are internally consistent.

- medium/current damping: max body-lateral acceleration error `0.00 m/s2`, max betaDot error `0.2 deg/s`
- medium/zero damping: max body-lateral acceleration error `0.01 m/s2`, max betaDot error `0.2 deg/s`
- hard/current damping: max body-lateral acceleration error `0.01 m/s2`, max betaDot error `0.2 deg/s`
- hard/zero damping: max body-lateral acceleration error `0.03 m/s2`, max betaDot error `0.4 deg/s`
- no material beta mismatch was found; simulator beta, body-frame atan beta, and travel-angle-minus-heading beta agree within the probe threshold
- front-wheel force transform rebuild error is effectively zero (`0.000-0.001 N`), so steering force is being rotated into body/world space consistently

Important checkpoints:

- medium/current at `1.00s`: beta `9.3 deg`, measured/predicted body-lateral acceleration `1.09/1.08 m/s2`
- medium/zero at `1.00s`: beta `12.0 deg`, measured/predicted body-lateral acceleration `0.92/0.92 m/s2`
- hard/current at `1.00s`: beta `13.5 deg`, measured/predicted body-lateral acceleration `-5.32/-5.32 m/s2`
- hard/zero at `1.00s`: beta `23.6 deg`, measured/predicted body-lateral acceleration `6.69/6.67 m/s2`

Classification: the runaway body slip is not caused by a missing, double-applied, or wrong-sign body-frame rotational coupling. It is also not caused by a materially wrong beta calculation or a front tyre force-frame transform error. The instability is physically consistent within the current base model and sits downstream of rigid-body coupling, most likely in how the tyre model behaves once slip builds and force reverses/saturates.

## Tyre Response Audit

`--classic-tyre-response-probe` samples the resolved EK9 tyre data used by `classicFourWheel`, then correlates that static response with the live front-wheel trajectory in medium and hard steer. Cleanup assists remain disabled and no production values are changed.

Resolved tyre facts:

- front: stiffness shape `8.94`, peak `9.4 deg`, falloff `20.0 deg`, max grip `1.36`, sliding grip `0.97`
- rear: stiffness shape `8.29`, peak `8.2 deg`, falloff `19.0 deg`, max grip `1.08`, sliding grip `0.96`
- the classic four-wheel lateral curve does not currently apply `TyreAxleParameters.LoadSensitivity`; normal load only scales max force through `Fz * maxGrip`

Static curve result:

- the tyre curve does not reverse force at positive slip
- force sign always follows signed slip angle
- front force rises smoothly to peak around `9.4-10 deg`
- front post-peak falloff is very mild: about `100%` usage at `10 deg`, `99.5%` at `12 deg`, `98.4%` at `15 deg`, and `97.0%` by `20+ deg`
- rear behaves similarly, with mild falloff from peak to `96.0%` sliding force

Dynamic front-wheel correlation:

- actual wheel-frame lateral force matches the static curve expectation at the same slip/load
- combined-grip clamping is not causing the observed front force collapse in these samples
- no front sign mismatch was found: signed force follows signed slip throughout
- medium steer front slip crosses through zero at about `0.383s`
- hard steer front slip crosses through zero at about `0.317-0.325s`

Medium steer checkpoints:

- `0.10s`: steer `3.4 deg`, front slip about `+2.8 deg`, front Fy about `+1015/+1165 N`
- `0.25s`: steer `3.4 deg`, front slip about `+1.3 deg`, front Fy only about `+267/+266 N`
- `0.50s`: steer `3.5 deg`, front slip about `-1.5 deg`, front Fy about `-379/-343 N`
- `1.00s`: steer `3.5 deg`, front slip about `-6.0/-6.1 deg`, front Fy about `-4580/-2297 N`

Hard steer checkpoints:

- `0.10s`: steer `6.4 deg`, front slip about `+5.0 deg`, front Fy about `+2311/+3275 N`
- `0.25s`: steer `6.4 deg`, front slip about `+1.5/+1.6 deg`, front Fy only about `+402/+357 N`
- `0.50s`: steer `6.5 deg`, front slip about `-4.6/-4.9 deg`, front Fy about `-3101/-1888 N`
- `1.00s`: steer `6.7 deg`, front slip about `-6.9/-7.0 deg`, front Fy about `-5227/-2413 N`

Classification: the instability is not being created by an aggressive post-peak tyre falloff or a tyre force sign bug. The tyre curve is still in the initial rise/linear region for the live front samples, and the force reversal is correct for the measured signed slip. The real event is that front slip collapses from useful positive slip to near zero by `0.25s`, then crosses negative by about `0.32-0.38s`. That points back to the vehicle state reaching a front-slip reversal condition early: body/travel angle has grown enough relative to the small speed-sensitive steering angle that the front tyres are no longer being asked to support the intended turn.

## Steering Path Audit

`--classic-steering-path-probe` traces raw input through speed cap, ramp, actual road-wheel angle, front slip, body slip, and front-axle local velocity angle. It also runs two probe-only counterfactuals:

- unrestricted speed cap + current steering ramp
- current speed cap + instant steering ramp

Production handling values are unchanged.

Current speed cap curve:

- `0 km/h = 26.0 deg`
- `60 km/h = 12.5 deg`
- `120 km/h = 11.5 deg`
- `200 km/h = 6.0 deg`
- `150 km/h cap = 9.76 deg`
- steer speed = `145 deg/s`

At both medium and hard steering, the ramp is not the active limiter by the first checkpoint:

- medium input `0.35` reaches about `3.42 deg` actual road-wheel angle by `0.10s`
- hard input `0.65` reaches about `6.37 deg` actual road-wheel angle by `0.10s`
- instant-ramp counterfactual does not improve the failure; zero crossing happens slightly earlier in hard steer

Medium steering:

- current: front-slip zero crossing at `0.383s`, speed drop `4.1 km/h`, beta at `1.00s` `9.3 deg`
- unrestricted cap: zero crossing at `0.325s`, speed drop `9.3 km/h`, beta at `1.00s` `22.7 deg`
- instant ramp: zero crossing at `0.367s`, speed drop `4.2 km/h`, beta at `1.00s` `9.3 deg`
- at current zero crossing, actual steer is `3.45 deg`; front local velocity angle is `3.53 deg`; steering needed to maintain `+1/+2 deg` front slip is `4.53/5.53 deg`

Hard steering:

- current: front-slip zero crossing at `0.317s`, speed drop `6.2 km/h`, beta at `1.00s` `13.5 deg`
- unrestricted cap: zero crossing delayed to `0.408s`, but speed drop worsens to `13.1 km/h`, beta at `1.00s` explodes to `44.2 deg`
- instant ramp: zero crossing at `0.300s`, speed drop `6.4 km/h`, beta at `1.00s` `13.3 deg`
- at current zero crossing, actual steer is `6.41 deg`; front local velocity angle is `6.45 deg`; steering needed to maintain `+1/+2 deg` front slip is `7.45/8.45 deg`

Classification: the current digital ramp is not the cause of the front-slip reversal in these constant-hold tests. The speed-sensitive cap is a real hard-case authority limiter, but bypassing it is not a clean fix: it delays front-slip reversal while producing much larger body slip and speed loss. The medium case is not cap-limited in the same way; it is limited by the requested raw input itself (`0.35 * cap`) being overtaken by front-axle velocity angle. The next design work should separate two things: a better digital input intent curve for Joe's tap/hold feel, and a base physics issue where more steering angle can amplify sideslip rather than settling the car.

## Steady-State Equilibrium Audit

`--classic-equilibrium-probe` runs medium and hard steer for `3.0s` with cleanup assists disabled and production values unchanged. It tracks beta, betaDot, yaw rate, yaw acceleration, front/rear lateral force, front/rear yaw moment, front/rear slip, and rear grip usage. It also compares against the same EK9 steady-state reference calculation used by the causal probes.

Reference inputs:

- wheelbase `2.620 m`
- CG to front axle `0.996 m`
- CG to rear axle `1.624 m`
- front weight `62.0%`
- front cornering stiffness `76000 N/rad`
- rear cornering stiffness `70500 N/rad`
- understeer index `0.002933`

Medium steer:

- actual steering settles around `3.4-3.9 deg`
- reference expects roughly `18.5-21.8 deg/s` yaw rate, `-3.6 to -3.7 deg` beta, front slip around `6.7-6.9 deg`, rear slip around `4.4-4.6 deg`
- sim crosses front slip through zero at `0.383s`, while rear grip is still only `0.06`
- rear saturates later at `0.833s`
- by `3.0s`, yaw acceleration and betaDot are near zero, but the state is unhealthy: beta `8.9 deg`, front slip `-5.3 deg`, rear slip `-8.2 deg`, rear grip `1.00`
- speed drop over the run is `17.6 km/h`

Hard steer:

- actual steering settles around `6.4-7.3 deg`
- reference expects roughly `34.4-41.1 deg/s` yaw rate, `-6.6 to -6.9 deg` beta, front slip around `12.4-12.9 deg`, rear slip around `8.2-8.5 deg`
- sim crosses front slip through zero at `0.317s`, while rear grip is `0.39`
- rear saturates at `0.442s`
- by `3.0s`, yaw acceleration is near zero, but the state is unhealthy: beta `12.5 deg`, front slip `-5.6 deg`, rear slip `-11.8 deg`, rear grip `0.98`
- speed drop over the run is `20.3 km/h`

Classification: the current EK9 classic-four-wheel setup does not converge to the healthy steady-state solution implied by the reference model. It converges toward a compromised saturated state: front slip collapses/reverses first, then the rear saturates, then yaw acceleration/betaDot settle only after the axle roles have effectively inverted. That makes the front axle stop leading the turn and leaves the rear carrying the corner, which matches Joe's "trolley rear / cannot turn / scrubs speed" feedback.

The key balance problem is not a local sign bug. It is the combination of yaw response, speed-sensitive steer angle, and front/rear lateral force balance failing to keep the car near the positive-front-slip reference trajectory long enough to settle. The next correction should target convergence toward the healthy equilibrium, not another cleanup force.

## Equilibrium Yaw Matrix

`--classic-equilibrium-matrix-probe` tests whether the two known base-yaw stabilisers can independently restore the healthy equilibrium:

- `classicFourWheel.yaw.inertiaScale`
- explicit yaw damping

Cleanup assists remain disabled and production values are not changed.

Reference context:

- current inertia scale `2.45`
- current damping `1.85`
- configured yaw inertia `914 kgm2`
- simple reference yaw inertia scale about `1.97`

Inertia-only sweep, damping unchanged:

- `2.45` current remains the best qualitative row
- reducing to `2.00` or the `1.97` reference region makes front-slip zero crossing earlier
- reducing to `1.00` is worse again, especially in hard steer
- no inertia-only row creates a healthy equilibrium

Damping-only sweep, inertia unchanged:

- reducing damping does not fix the equilibrium
- lower damping generally increases beta, speed loss, and the severity of negative front slip
- zero damping is the worst hard-steer case: max beta `26.5 deg`, speed loss `32.3 km/h`, and no healthy equilibrium
- no damping-only row creates a healthy equilibrium

Small combined checks:

- `1.97` inertia scale + `50%` damping does not fix the issue
- `2.00` inertia scale + `50%` damping does not fix the issue
- both combined rows still cross front slip negative early and saturate the rear

Best row remains the current production yaw settings:

- medium current: front slip zero at `0.38s`, rear saturation at `0.83s`, speed loss `17.6 km/h`
- hard current: front slip zero at `0.32s`, rear saturation at `0.44s`, speed loss `20.3 km/h`

Classification: yaw resistance is not the root cause of the inverted equilibrium. Reducing historical yaw resistance exposes the instability more strongly instead of restoring the reference state. The next physics pass should move to front/rear lateral balance and resolved EK9 tyre/axle parameter relationships, not further damping/inertia tuning.

## Front/Rear Lateral Balance Audit

`--classic-lateral-balance-probe` audits the resolved EK9 tyre and axle values that actually reach `classicFourWheel`, then compares the reference front/rear balance against the four-wheel simulation. Cleanup assists remain disabled and production values are not changed.

Resolved geometry:

- wheelbase `2.620 m`
- CG to front axle `0.996 m`
- CG to rear axle `1.624 m`
- front weight `62.0%`

Resolved front axle:

- static axle load `6447 N`, per-wheel `3223 N`
- physical tyre stiffness `76000 N/rad`
- load sensitivity `0.120`
- peak friction `1.36`
- peak slip `9.4 deg`
- slide slip `20.0 deg`
- classic adapter stiffness shape `8.94`
- classic axle capability at static load `8768 N`

Resolved rear axle:

- static axle load `3952 N`, per-wheel `1976 N`
- physical tyre stiffness `70500 N/rad`
- load sensitivity `0.120`
- peak friction `1.08`
- peak slip `8.2 deg`
- slide slip `19.0 deg`
- classic adapter stiffness shape `8.29`
- classic axle capability at static load `4268 N`

No obvious front double-penalty exists in the resolved data:

- front has more load, higher stiffness, higher peak grip, higher peak slip, and similar load sensitivity
- classic four-wheel currently does not apply tyre `LoadSensitivity` in the lateral curve, so there is no hidden front load-sensitivity penalty in this path
- front static lateral capacity is about `2.05x` rear static capacity

Reference balance from the resolved physical values:

- understeer index `0.002933`
- tendency: understeer-biased
- medium at `3.45 deg` road-wheel angle: yaw `18.6 deg/s`, beta `-3.71 deg`, front/rear slip `6.72/4.44 deg`
- hard at `6.41 deg` road-wheel angle: yaw `34.6 deg/s`, beta `-6.89 deg`, front/rear slip `12.48/8.24 deg`

Four-wheel dynamic result:

- medium at `0.25s`: actual front/rear slip `1.28/-1.57 deg`, while reference expects `6.67/4.41 deg`
- medium front-slip zero crossing happens at `0.383s`, before rear saturation
- medium rear saturates later at `0.833s`
- hard at `0.25s`: actual front/rear slip `1.54/-3.43 deg`, while reference expects `12.40/8.19 deg`
- hard front-slip zero crossing happens at `0.317s`, before rear saturation
- hard rear saturates later at `0.442s`

Classification: the resolved EK9 tyre/axle data does not itself predict the bad balance. The reference model predicts a healthy understeer-biased positive-front-slip/positive-rear-slip state. The four-wheel simulation diverges from that before rear saturation: rear slip is already negative at `0.25s`, front slip then collapses to zero, and only afterward does the rear saturate. The mismatch is therefore not caused by front grip/stiffness/load being too weak in the resolved parameter data. The next suspect is the four-wheel slip-angle sign/reference convention or per-axle kinematic interpretation, especially why the rear slip is negative while the reference expects positive rear slip under the same steering/speed conditions.

## Slip Kinematics Audit

`--classic-slip-kinematics-probe` rebuilds each wheel's local velocity from first principles and compares it against the simulator's published wheel kinematics.

Simulator convention:

- body axes: `+forward` is the car nose, `+right` is the car right side
- positive yaw rotates heading toward `+right` in code
- in this positive-steer test, yaw rate becomes negative
- four-wheel slip formula is `slip = wheelSteerAngle - atan2(localLateralVelocity, effectiveLocalForwardVelocity)`
- independent reconstruction uses `Vwheel_body = Vcg_body + omega x r`, implemented as `uWheel = u + r * xRight`, `vWheel = v - r * zForward`

Result: the four-wheel per-wheel slip calculation rebuilds correctly.

Medium at `0.25s`:

- CG velocity `u/v = 41.46 / 1.47 m/s`
- yaw rate `-9.5 deg/s`
- rear yaw lateral contribution about `-0.27 m/s`
- reconstructed rear local lateral velocity about `1.20 m/s`
- simulator rear local lateral velocity about `1.14 m/s`
- reconstructed rear slip about `-1.65 deg`
- simulator rear slip about `-1.57/-1.58 deg`
- slip delta only about `0.075 deg`

Hard at `0.25s`:

- CG velocity `u/v = 41.30 / 3.26 m/s`
- yaw rate `-23.1 deg/s`
- rear yaw lateral contribution about `-0.65 m/s`
- reconstructed rear local lateral velocity about `2.61 m/s`
- simulator rear local lateral velocity about `2.49 m/s`
- reconstructed rear slip about `-3.58/-3.63 deg`
- simulator rear slip about `-3.41/-3.46 deg`
- slip delta about `0.17 deg`

Classification: there is no local per-wheel kinematic sign bug in `WheelKinematics`. The rear slip is negative because the actual simulated vehicle state already has positive body lateral velocity that is larger than the yaw-rate contribution can cancel at the rear axle. With rear steer `0 deg`, that produces a positive rear local travel angle and therefore negative rear slip under the simulator's convention. The steady-state reference expects a different state: more yaw alignment and opposite-sign beta such that rear slip remains positive. The next issue is therefore not wheel slip reconstruction, but why the transient force balance allows body lateral velocity to build in the wrong direction before yaw/reference equilibrium is reached.

## Early Transient Force-Balance Audit

`--classic-transient-force-balance-probe` focuses on the first `0.40s`, before the bad state fully develops. It compares actual lateral force/impulse against the lateral acceleration implied by the reference turn and the simple kinematic steering radius. Cleanup assists remain disabled and production values are not changed.

Medium steer:

- actual road-wheel angle about `3.42-3.45 deg`
- reference yaw target about `18.5-18.7 deg/s`
- reference lateral acceleration demand about `13.4-13.5 m/s2`
- simple kinematic steering-radius demand about `39.4-39.5 m/s2`
- actual lateral acceleration falls from `2.64 m/s2` at `0.05s` to `0.10 m/s2` at `0.25s`, then goes negative by `0.30s`
- front force rises immediately, peaks at `0.025s`, begins falling by `0.075s`, and front slip crosses zero at `0.383s`
- actual/reference total lateral impulse through `0.40s`: `505 / 5594 Ns` (`0.09x`)
- actual/reference front impulse through `0.40s`: `449 / 3468 Ns`
- actual/reference rear impulse through `0.40s`: `160 / 2126 Ns`

Hard steer:

- actual road-wheel angle about `6.35-6.43 deg`
- reference yaw target about `34.3-34.8 deg/s`
- reference lateral acceleration demand about `24.9-25.1 m/s2`
- simple kinematic steering-radius demand about `73.3-73.6 m/s2`
- actual lateral acceleration falls from `6.56 m/s2` at `0.05s` to `-0.86 m/s2` at `0.25s`
- front force rises immediately, peaks at `0.050s`, begins falling by `0.058s`, and front slip crosses zero at `0.317s`
- actual/reference total lateral impulse through `0.40s`: `1319 / 10132 Ns` (`0.13x`)
- actual/reference front impulse through `0.40s`: `1004 / 6282 Ns`
- actual/reference rear impulse through `0.40s`: `548 / 3850 Ns`

Classification: the previous steady-state reference should not be treated as an achievable target at these steering/speed points. It implies about `1.37g` for medium and `2.56g` for hard before considering transient limits, while the EK9's available tyre capacity is far lower. The four-wheel sim is not failing to reach an easy healthy equilibrium; it is being asked for a turn demand that exceeds the car's grip envelope, so the front force is short-lived, the rear quickly contributes opposite lateral force, and the velocity vector cannot bend into the requested path before slip signs invert.

This reframes the next correction. The issue is not that the solver should magically reach the earlier reference state. The input/steering/tyre system needs to prevent excessive high-speed steering demand from instantly asking for impossible lateral acceleration, while still preserving Joe's desired tap/hold steering feel and progressive tyre communication. The next pass should define a physically achievable steering/yaw target envelope from grip capacity, then map digital input duration into that envelope.

## Steering Envelope Audit

`--classic-steering-envelope-probe` derives a physical speed/road-wheel-angle sanity envelope from lateral acceleration targets, then compares it to the current classic steering cap. Production values are not changed.

Formula used:

```text
R = v^2 / a_lat
delta = atan(wheelbase / R)
delta = atan(wheelbase * a_lat / v^2)
```

Resolved EK9 context:

- wheelbase `2.620 m`
- mass `1060 kg`
- estimated tyre-capacity peak lateral acceleration from resolved axle grip: about `1.25g`
- this estimate is a raw capacity sanity check, not a final sustained-cornering claim

Current cap curve:

- `0 km/h = 26.0 deg`
- `60 km/h = 12.5 deg`
- `120 km/h = 11.5 deg`
- `200 km/h = 6.0 deg`

Physical road-wheel angles for useful lateral acceleration targets:

- at `60 km/h`: `0.5g = 2.65 deg`, `0.7g = 3.71 deg`, `0.9g = 4.76 deg`, `1.0g = 5.29 deg`
- at `120 km/h`: `0.5g = 0.66 deg`, `0.7g = 0.93 deg`, `0.9g = 1.19 deg`, `1.0g = 1.33 deg`
- at `150 km/h`: `0.5g = 0.42 deg`, `0.7g = 0.59 deg`, `0.9g = 0.76 deg`, `1.0g = 0.85 deg`
- at `200 km/h`: `0.5g = 0.24 deg`, `0.7g = 0.33 deg`, `0.9g = 0.43 deg`, `1.0g = 0.48 deg`

Current 150 km/h demand:

- current cap `9.76 deg` implies about `15.2 m` radius and `11.62g` pure geometric lateral demand
- medium request `3.45 deg` implies about `43.5 m` radius and `4.07g` pure geometric lateral demand
- hard request `6.41 deg` implies about `23.3 m` radius and `7.59g` pure geometric lateral demand

These pure-geometry numbers are higher than the earlier bicycle-reference demand because the bicycle reference includes tyre slip and understeer. Both views point the same way: the current high-speed steering curve is commanding far more curvature than the tyres can physically support.

Proposed architecture:

- layer 1, physical authority: speed-dependent road-wheel envelope derived from lateral-g capacity, with a normal zone and a limited overdrive zone
- layer 2, digital intent: tap/hold curve maps key-hold duration into that envelope
- quick tap: small correction
- sustained hold: progressively more steering inside normal authority
- longer hold: deliberate overdrive, still bounded enough that the tyre model can communicate understeer rather than immediately inverting the slip state
- release/countersteer: separate return/countersteer rates so recovery can stay responsive without increasing high-speed steady steering demand

Initial diagnostic envelope from the probe:

- at `150 km/h`, normal digital hold around `0.64 deg`
- deliberate overdrive around `0.81 deg`

Classification: the current high-speed steering caps are conceptually too large when interpreted as actual road-wheel angles. This is the first clear design-level mismatch: steering was defined visually/control-wise rather than from curvature/lateral acceleration. The next implementation should add a physical steering authority layer and then put GT2-style digital tap/hold shaping inside that envelope.

## Two-Layer Steering Implementation

Implemented in `ClassicFourWheelVehicleSimulator`:

- layer 1: normalized player steering command (`-1..+1`)
- layer 2: physical road-wheel angle envelope
- the old speed-angle table is preserved as the legacy control-intent reference, not treated directly as high-speed road-wheel angle
- physical high-speed envelope blends in from `70 km/h` and is fully active by `130 km/h`
- normal zone uses `0.75g` curvature target plus `20%` of resolved front peak slip
- overdrive zone uses `0.95g` curvature target plus `45%` of resolved front peak slip
- full input still reaches an overdrive zone, so the tyre model can still produce understeer rather than steering being used as traction control

New steering config fields:

- `physicalEnvelopeBlendStartKmh`
- `physicalEnvelopeFullKmh`
- `normalLateralAccelerationG`
- `overdriveLateralAccelerationG`
- `normalCommand`
- `minimumHighSpeedAngleDegrees`
- `normalPeakSlipFraction`
- `overdrivePeakSlipFraction`

New steering telemetry:

- `SteeringNormalizedCommand`
- `SteeringLegacyControlMaxAngleDegrees`
- `SteeringPhysicalNormalAngleDegrees`
- `SteeringPhysicalOverdriveAngleDegrees`

Validation from `--classic-steering-architecture-probe` with cleanup off:

- at `150 km/h`, command `0.25` gives about `0.85 deg` road angle, `0.95g` geometric demand, rear grip `0.02`, speed loss `4.24 km/h`
- at `150 km/h`, command `0.50` gives about `1.71 deg`, `1.88g` geometric demand, rear grip `0.47`, speed loss `4.99 km/h`
- at `150 km/h`, command `0.75` gives about `2.57 deg`, `2.78g` geometric demand, rear grip `0.91`, speed loss `6.33 km/h`
- at `150 km/h`, command `1.00` gives about `5.14 deg`, `5.39g` geometric demand, rear grip `1.00`, speed loss `8.71 km/h`

Regression probe result:

- `--classic-four-wheel-probe` passes
- cornering speed loss at `150 km/h`, `25%` throttle:
  - straight drop `9.2 km/h`
  - small steer extra `+0.3 km/h`
  - medium steer extra `+1.5 km/h`
  - near-limit steer extra `+4.6 km/h`

Interpretation: the two-layer architecture removes the catastrophic impossible-demand behavior and sharply reduces cornering speed/RPM loss. It is not the final handling feel. Sustained high-speed cases still tend toward negative front slip over longer windows, which now looks like a follow-up base balance/input-envelope tuning problem rather than a broken force equation.

## Steering Envelope Tuning Pass

After the first two-layer implementation, `--classic-steering-envelope-matrix-probe` showed that the slip allowance was still too large and that the legacy steering curve was still leaking into the `100 km/h` range because the physical envelope did not fully take over until `130 km/h`.

The chosen production steering-only values are:

- `physicalEnvelopeBlendStartKmh = 50`
- `physicalEnvelopeFullKmh = 95`
- `normalLateralAccelerationG = 0.65`
- `overdriveLateralAccelerationG = 0.88`
- `normalPeakSlipFraction = 0.00`
- `overdrivePeakSlipFraction = 0.10`

The important architecture decision is that normal steering now follows the physical lateral-g envelope without extra slip allowance. A small front-slip allowance exists only in the full overdrive region.

Final `--classic-steering-architecture-probe` results with cleanup off:

- `100 km/h`: rear grip stays below saturation across `25/50/75/100%` commands (`max 0.45`), with speed loss `2.01-2.54 km/h`
- `150 km/h`: rear grip stays below saturation across all commands (`max 0.18`), with speed loss `4.20-4.36 km/h`
- `200 km/h`: rear grip stays below saturation across all commands (`max 0.07`), with speed loss `6.94-7.00 km/h`

At `150 km/h`:

- `25%` command: road angle `0.19 deg`, implied `0.22g`, front/rear slip `+0.06/-0.12 deg`, beta `0.13 deg`
- `50%` command: road angle `0.39 deg`, implied `0.43g`, front/rear slip `-0.06/-0.42 deg`, beta `0.44 deg`
- `75%` command: road angle `0.58 deg`, implied `0.65g`, front/rear slip `-0.44/-0.92 deg`, beta `0.99 deg`
- `100%` command: road angle `1.73 deg`, implied `1.92g`, front/rear slip `-2.75/-3.81 deg`, beta `4.24 deg`

Compared with the first two-layer implementation, the full-command case at `150 km/h` dropped from about `5.14 deg / 5.39g / rearGrip 1.00 / loss 8.71 km/h` to `1.73 deg / 1.92g / rearGrip 0.18 / loss 4.36 km/h`.

Regression checks:

- `dotnet build RType.csproj -c Release --no-restore`: passed
- `--classic-four-wheel-probe`: passed
- `--cornering-speed-loss-probe`, `150 km/h`, `25%` throttle:
  - straight drop `9.2 km/h`
  - small steer extra `+0.0 km/h`
  - medium steer extra `+0.0 km/h`
  - near-limit steer extra `+0.1 km/h`

Remaining caveat: sustained front slip can still go slightly negative in longer high-speed holds, but the previous inverted state is no longer dragging the rear axle into saturation. This should be judged in the next drive test as control feel and cornering arc, not just by the sign of one steady diagnostic number.

## Digital Tap/Hold Steering

Digital steering now shapes normalized command, not physical road-wheel angle.

New steering config fields:

- `digitalInitialCommandRatePerSecond`
- `digitalSustainedCommandRatePerSecond`
- `digitalRiseAccelerationSeconds`
- `digitalReleaseCommandRatePerSecond`
- `digitalCounterSteerRateMultiplier`

Current values:

- initial digital rise `1.4 command/s`
- sustained digital rise `3.2 command/s`
- rise acceleration window `0.85s`
- release `2.6 command/s`, plus existing graceful return behavior
- countersteer multiplier `1.7`

`--classic-digital-steering-feel-probe` confirms the intended shape:

- tap: about `0.12` normalized command at `0.08s`, then returns to center
- short hold: about `0.58` normalized command by `0.35s`
- long hold: reaches full command by about `0.70s`
- release: recenters quickly after the input is released
- countersteer: reverses direction faster than ordinary rise

This gives the requested GT-style control separation: tap/hold timing changes player intent, while the physical road-wheel angle remains bounded by the vehicle-speed envelope.

## Aggressive Steering Recalibration

Joe's road test of the conservative steering envelope failed. The car no longer scrubbed speed, but it also no longer turned enough to be useful at `100-150 km/h`. That means the previous steering probe was rewarding the wrong outcome: clean low-scrub numbers from a car that was effectively being prevented from cornering.

This pass keeps the two-layer architecture and still does not change tyres, yaw damping, yaw inertia, rear grip, or cleanup assists.

Changes made:

- digital steering buildup is much faster
- digital release is faster
- countersteer rate is higher than same-direction buildup
- physical steering authority is increased from the conservative envelope
- a bounded transient steering boost is added during steering initiation/countersteer
- the transient boost decays; it is not a permanent added steering angle
- steering matrix scoring now penalizes dead steering and poor lateral/yaw response

Active `Fast` candidate values:

- `physicalEnvelopeBlendStartKmh = 45`
- `physicalEnvelopeFullKmh = 95`
- `normalLateralAccelerationG = 0.95`
- `overdriveLateralAccelerationG = 1.20`
- `normalPeakSlipFraction = 0.04`
- `overdrivePeakSlipFraction = 0.18`
- `transientPeakSlipFraction = 0.18`
- `transientBoostSeconds = 0.34`
- `digitalInitialCommandRatePerSecond = 3.4`
- `digitalSustainedCommandRatePerSecond = 4.8`
- `digitalRiseAccelerationSeconds = 0.22`
- `digitalReleaseCommandRatePerSecond = 6.0`
- `digitalCounterSteerRateMultiplier = 2.2`

Candidate comparison from `--classic-steering-envelope-matrix-probe`:

- `too-soft`: still dead; max actual lateral response only `0.72g`, six dead-steering cases
- `fast`: best road-test compromise; max actual lateral response `0.99g`, no rear-saturation cases in the matrix
- `very-fast`: stronger response, but rear saturation appears in three cases
- `aggressive`: strongest response, but rear saturation also appears in three cases and speed loss rises

Active `Fast` result from `--classic-steering-architecture-probe`:

- `100 km/h`, `75%`: angle `2.31 deg`, implied `1.13g`, rear grip `0.41`, speed loss `3.31 km/h`
- `100 km/h`, `100%`: angle `4.20 deg`, implied `2.01g`, rear grip `0.98`, speed loss `4.56 km/h`
- `150 km/h`, `75%`: angle `1.24 deg`, implied `1.36g`, rear grip `0.25`, speed loss `4.90 km/h`
- `150 km/h`, `100%`: angle `2.80 deg`, implied `3.03g`, rear grip `0.96`, speed loss `6.31 km/h`
- `200 km/h`, `75%`: angle `0.86 deg`, implied `1.68g`, rear grip `0.16`, speed loss `7.41 km/h`
- `200 km/h`, `100%`: angle `2.32 deg`, implied `4.44g`, rear grip `0.95`, speed loss `8.83 km/h`

Digital-feel result for the active `Fast` timing:

- `0.08s`: normalized command about `0.30`
- `0.20s`: normalized command about `0.81`
- `0.35s`: normalized command reaches `1.00`
- countersteer crosses strongly by `0.70s`
- release returns to center promptly

Cornering speed-loss regression at `150 km/h`, `25%` throttle:

- straight drop `9.2 km/h`
- small steer extra `+0.1 km/h`
- medium steer extra `+0.3 km/h`
- near-limit steer extra `+1.2 km/h`

`--classic-four-wheel-probe` passes.

Important caveat: this is intentionally not the cleanest probe candidate. The cleanest candidate was the one Joe rejected as undriveable. The active `Fast` setup is chosen because it restores useful steering authority while avoiding the worst old failure in the matrix. Full high-speed hold is close to the rear-saturation boundary, so Joe's next road test should focus on whether full hold now feels like usable understeer or whether the trolley-rear state returns.

## Post-Road-Test Steering Escalation

Joe's follow-up test showed that the `Fast` candidate was still too conservative above `100 km/h`:

- `0-90 km/h` felt pretty good
- `100-120 km/h` still felt tight
- `120+ km/h` was too heavy and hard to steer
- quick tap started to work, but not enough past `100 km/h`
- short hold still did not create enough turn-in

The active build has therefore been moved to the sharper `Aggressive` steering candidate. This is intentional: the next useful road test needs to find the upper side of the steering-feel range rather than repeating another conservative pass.

Active `Aggressive` values:

- `physicalEnvelopeBlendStartKmh = 40`
- `physicalEnvelopeFullKmh = 95`
- `normalLateralAccelerationG = 1.15`
- `overdriveLateralAccelerationG = 1.40`
- `normalPeakSlipFraction = 0.08`
- `overdrivePeakSlipFraction = 0.30`
- `transientPeakSlipFraction = 0.32`
- `transientBoostSeconds = 0.42`
- `digitalInitialCommandRatePerSecond = 5.2`
- `digitalSustainedCommandRatePerSecond = 7.8`
- `digitalRiseAccelerationSeconds = 0.14`
- `digitalReleaseCommandRatePerSecond = 8.0`
- `digitalCounterSteerRateMultiplier = 3.0`

Validation on the active `Aggressive` setup:

- build passes
- `--classic-four-wheel-probe` passes
- `--classic-steering-architecture-probe` shows much higher authority:
  - `100 km/h`, `75%`: angle `3.16 deg`, rear grip `0.72`, speed loss `4.63 km/h`
  - `150 km/h`, `75%`: angle `1.81 deg`, rear grip `0.57`, speed loss `6.34 km/h`
  - `200 km/h`, `75%`: angle `1.35 deg`, rear grip `0.47`, speed loss `8.87 km/h`
  - full input at `100/150/200 km/h` reaches rear grip `1.00/1.00/1.00`, so full high-speed hold is deliberately near or at the limit
- `--cornering-speed-loss-probe`, `150 km/h`, `25%` throttle:
  - small steer extra `+0.1 km/h`
  - medium steer extra `+0.9 km/h`
  - near-limit steer extra `+3.0 km/h`

Interpretation: this setup is not mathematically cleaner than `Fast`; it is intentionally more road-testable. If Joe says this finally turns but full hold brings back rear weirdness, the next move is to split the difference between `Fast` and `Aggressive`, not return to the conservative envelope.

## Airborne/Flying Bug Finding

The newest Lakeside telemetry file checked:

- `Telemetry/RaceRuns/20260830_133716_Honda_Civic_Type_R_EK9_Showroom_Stock_Lakeside_Park_normal_manual.csv`

Findings:

- wall contacts: `0`
- last impact speed: `0`
- crash severity: `0`
- body pitch stayed within about `-0.73..+0.32 deg`
- body roll stayed clamped within about `-1.45..+1.45 deg`
- telemetry does not log `Position.Y`

Code audit finding: `ClassicFourWheelVehicleSimulator` was not following track elevation. It moved `X/Z` only and kept the start height, while `SimpleVehicleSimulator` already samples per-wheel terrain elevation and updates body/support height. On an elevated track such as Lakeside, that can make the car visually detach from the road without any collision event.

Fix added for classic four-wheel presentation:

- sample terrain elevation at all four wheel positions
- update wheel contact center height
- update support heights
- update ground pitch/roll from the track
- update visual body height to the support plane

This is a terrain/presentation fix, not a tyre/yaw/cleanup handling change.

## High-Speed Brake-Turn Authority

Joe's next road test confirmed the `Aggressive` steering setup is now in the right range:

- turning is much better overall
- the car now turns enough and is not too sharp
- no repeat of the flying bug
- new complaint: after hard braking at high speed, steering authority can disappear

Diagnostic added:

- `--classic-brake-turn-authority-probe`

The probe compares four `150 km/h` cases after a `0.50s` setup phase:

- coast then steer
- full brake held while steering
- full brake then release before steering
- full brake then `25%` trail brake while steering

Baseline finding before the fix: with full brake still held, the rear axle sat at `1.00` grip usage immediately during turn-in. The front steering command was present, but the braking state was consuming/stressing the grip budget enough that the car could not build a healthy brake-turn state.

Fix added:

- keep the two-layer steering architecture unchanged
- leave tyres, yaw inertia, yaw damping, rear grip, and cleanup assists unchanged
- add classic-four-wheel brake/steer grip-budget tuning fields
- reduce service brake pressure during simultaneous brake and steering
- reduce rear service brake more strongly than front service brake
- preserve lateral force when braking and steering compete for the same grip budget
- keep straight-line braking untouched because the gates require steering input

Active brake/steer values:

- `brakingSteeringLateralPriority = 0.65`
- `brakingSteeringPrioritySteerStart = 0.20`
- `brakingSteeringPrioritySteerEnd = 0.85`
- `brakingSteeringPriorityBrakeStart = 0.10`
- `brakingSteeringPriorityBrakeEnd = 0.75`
- `brakingSteeringFrontBrakeMultiplier = 0.72`
- `brakingSteeringRearBrakeMultiplier = 0.18`

Validation after the fix:

- build passes
- `--classic-four-wheel-probe` passes
- `--classic-steering-architecture-probe` unchanged for no-brake steering
- `--cornering-speed-loss-probe` remains healthy:
  - `150 km/h`, `25%` throttle, medium steer extra `+0.9 km/h`
  - near-limit steer extra `+3.0 km/h`

Brake-turn probe result after the fix:

- coast-turn is unchanged
- brake-release is unchanged
- full-brake turn-in rear grip at `0.25s` improves from locked at `1.00` to `0.05`
- full-brake turn-in speed loss over `1.00s` improves from `46.6 km/h` to `36.0 km/h`
- full-brake turn-in still brakes hard, but the rear no longer immediately consumes the whole grip budget at turn-in

Interpretation: the issue was not the approved steering calibration becoming slow again. It was missing brake proportioning for the classic four-wheel model. Under digital hard braking, especially in an FF car with heavy front load transfer, the rear service brake needed to give up pressure when the player also asks the car to rotate.

## Trail-Brake Dynamics Audit

Joe's follow-up road test:

- brake-turning is dramatically improved
- heavy braking while turning still feels like the front wheels are fighting straight ahead
- the car does not yet feel like the driver can balance brake pressure, steering correction, rear rotation, and countersteer naturally
- the current brake-steer modulation should remain provisional

Diagnostic added:

- `--classic-trail-brake-dynamics-probe`

The probe runs `150 km/h` cases with brake-steer modulation on and off:

- straight hard braking
- hard brake plus fixed steering
- hard brake, then progressive brake release while steering
- the same progressive release, then countersteer

Reference load transfer for the EK9:

- static load F/R: `6447 / 3952 N`
- expected `1.0g` braking load F/R: `8351 / 2047 N`
- measured full-brake load is around `9040 / 1360 N`, matching roughly `1.35g` braking

Key findings:

- Longitudinal load transfer exists and is strong.
- Front/rear grip capacity follows the load transfer: full braking raises front capacity to about `12.3 kN` and drops rear capacity to about `1.5 kN`.
- With brake-steer modulation off, rear longitudinal grip usage stays pinned around `0.99-1.00` during hard-brake turn-in. That is rear longitudinal saturation, not controlled rear lateral breakaway.
- With brake-steer modulation on, rear longitudinal grip usage during brake-turn drops to about `0.19-0.20`, so the rear tyres are no longer locked by service brake pressure.
- Modulation-on trail braking creates yaw/rotation, but the yaw balance is still rear-dominant. In progressive release, rear yaw moment grows to about `-3.5 kNm` by `1.5s`, while front yaw moment is only about `+1.7 kNm`.
- Countersteer works mechanically. In the countersteer case, opposite input produces a strong restoring front yaw moment of about `+7.8 kNm` at `0.75s`, flips yaw rate from about `-14 deg/s` to `+10 deg/s`, then reduces beta and rear slip.

Classification:

- Load transfer is correct, and axle capacities respond to it.
- Without brake-steer modulation, rear longitudinal saturation dominates too early.
- With brake-steer modulation, the system becomes controllable enough to rotate and countersteer, but the front/rear yaw balance under trail braking is not yet physically convincing.

Interpretation: the provisional brake-steer modulation is not merely polish. It is currently preventing a rear longitudinal saturation failure. However, once that failure is removed, the model still lacks a clean natural trail-brake balance where front load, rear unload, steering angle, rear slip, and countersteer settle into a believable controllable state.

## Classic Brake Pressure Regulator

Brake path audit result:

- classic four-wheel previously applied brake input as immediate service brake force
- force was split by static brake bias
- each tyre then clamped the requested force to its available grip
- there was no wheel-level brake pressure state in classic four-wheel
- classic four-wheel does not yet simulate true independent wheel angular lockup, so a full wheel-speed ABS model would be misleading at this stage

Architecture added:

- per-wheel brake pressure ratio state
- per-wheel regulator active telemetry
- service brake force is now separated from engine/drive force before the wheel solve
- only service brake force is pressure-regulated
- the regulator uses combined grip utilisation as its explicit proxy
- pressure releases quickly when requested braking would exceed the target wheel grip usage
- pressure reapplies progressively when grip margin returns

New data fields under `classicFourWheel.gripBudget`:

- `brakePressureFrontTargetGripUsage = 0.94`
- `brakePressureRearTargetGripUsage = 0.82`
- `brakePressureApplyRatePerSecond = 14`
- `brakePressureReleaseRatePerSecond = 38`
- `brakePressureMinimumRatio = 0.10`
- `brakePressureMinimumSpeedMetersPerSecond = 2.0`

Important distinction: this is not a true wheel-speed ABS yet. It is a stateful brake-pressure regulator based on grip utilisation. That is still a structural improvement over instant raw brake force because pressure now evolves over time and responds to the tyre/load state.

Validation:

- build passes
- `--classic-four-wheel-probe` passes
- `--cornering-speed-loss-probe` remains unchanged in no-brake cornering
- `--classic-steering-architecture-probe` remains unchanged in no-brake steering

Trail-brake result with brake-steer modulation disabled:

- straight full braking no longer pins the rear at `1.00` continuously
- rear brake pressure settles around `0.55-0.62` early in straight hard braking
- hard brake plus steer at `0.25s`: rear grip usage is about `0.31` instead of `1.00`
- progressive brake release at `0.50s`: rear grip usage is about `0.34` instead of `0.86-1.00`
- countersteer still produces a strong restoring front yaw moment and reduces beta/rear slip

Classification after regulator:

- missing pressure dynamics were a real architectural gap
- over-aggressive rear brake demand was caused by static bias plus heavy load transfer plus instant force application
- the new pressure regulator prevents the rear longitudinal saturation failure without relying on the brake-steer special case
- the remaining trail-brake feel should be road-tested before changing yaw, tyres, or steering again

## Classic Transient Load Transfer

Joe's next road test exposed a different issue: braking no longer hard-locks the rear, but the car still feels too composed, with countersteer reversing the car too instantly and without enough chassis weight state.

Code audit result:

- classic four-wheel tyre loads were computed directly from previous-frame longitudinal/lateral acceleration
- that created a one-tick delayed algebraic load transfer, not a physical transfer state
- visual body pitch/roll was smoothed separately and did not drive tyre normal loads
- there was no pitch/roll transfer displacement, transfer velocity, natural frequency, or damping ratio in the classic four-wheel load path

Architecture added:

- longitudinal load-transfer state: target, actual, velocity
- front lateral load-transfer state: target, actual, velocity
- rear lateral load-transfer state: target, actual, velocity
- second-order response using natural frequency and damping ratio
- equilibrium transfer magnitude still comes from mass, acceleration, CG height, wheelbase, and track width
- tyre loads now use actual transfer state, not instantaneous target transfer
- visual body pitch/roll now follows the same transfer state used by the tyres

New data fields under `classicFourWheel.chassisLoadTransfer`:

- `enabled = true`
- `longitudinalNaturalFrequencyHz = 5.5`
- `longitudinalDampingRatio = 0.72`
- `lateralNaturalFrequencyHz = 4.2`
- `lateralDampingRatio = 0.70`

New probe:

- `--classic-transient-load-transfer-probe`

Probe comparison:

- `instant-mod-off` keeps target and actual transfer identical, with zero transfer velocity
- `stateful-mod-off` carries transfer velocity through brake release and countersteer
- at `0.05s` hard braking, longitudinal target/actual transfer is `2160 / 1211 N`, so the load does not teleport to the target
- by `0.20s`, it is `2530 / 2245 N`, so the stiff EK9 still takes a set quickly
- after turn-in, lateral transfer builds as state rather than matching target immediately
- after countersteer, lateral transfer must unwind and reverse instead of snapping directly to the opposite side

Validation:

- build passes
- `--classic-transient-load-transfer-probe` runs
- `--classic-four-wheel-probe` passes
- `--cornering-speed-loss-probe` remains healthy: medium 150km/h extra loss is about `0.8-0.9 km/h`, near-limit extra loss is about `2.5-3.0 km/h`

Interpretation: the architecture gap was real. Classic four-wheel now has a minimal chassis transfer state between acceleration and tyre normal load. This is not final suspension simulation, and it should be road-tested before retuning brake pressure, yaw damping, tyres, or assists.

## RPM / Gear-Capacity Probe

Joe reported that vehicle speed could continue rising while RPM was pinned at the limiter.

Finding:

- classic four-wheel RPM presentation was clamped at the limiter
- drive force could still be requested from a stale/lower physics RPM during limiter approach
- speed could therefore rise past the current gear's RPM capacity before the limiter state fully governed the drivetrain

Fix:

- when the car is moving in a forward gear, physics RPM now follows the wheel-implied road RPM
- drive torque uses the road-coupled RPM once the driveline is coupled
- forward velocity is capped by the current gear's limiter-derived speed
- lateral velocity is preserved when applying the per-gear forward speed cap

New probe:

- `--classic-rpm-speedo-probe`

Validation after fix:

- gear 1 limiter speed: `65.1 km/h`, after sample `64.6 km/h`, drive force `0 N`, classification `limited`
- gear 2 limiter speed: `99.8 km/h`, after sample `99.3 km/h`, drive force `0 N`, classification `limited`
- gear 3 limiter speed: `144.1 km/h`, after sample `143.8 km/h`, drive force `0 N`, classification `limited`
- gear 4 limiter speed: `189.8 km/h`, after sample `186.4 km/h`, drive force `0 N`, classification `limited`
- gear 5 did not reach limiter because the current global/safety top-speed region is below fifth-gear limiter capacity

## Tyre Load Sensitivity / Front Axle Audit

New probe:

- `--classic-tyre-load-front-axle-audit-probe`

Static tyre-load finding:

- front effective peak mu is constant at `1.360` from `0.5x` to `1.5x` static wheel load
- rear effective peak mu is constant at `1.080` from `0.5x` to `1.5x` static wheel load
- lateral load transfer therefore does not reduce total axle peak capacity at all in the current classic tyre law

Examples:

- front axle with `1600 N` lateral transfer: total peak remains `8768 N`, delta `0 N`
- front axle with `3200 N` lateral transfer: total peak remains `8768 N`, delta `0 N`
- rear axle with `1000 N` lateral transfer: total peak remains `4268 N`, delta `0 N`
- rear axle with `2000 N` lateral transfer: total peak remains `4268 N`, delta `0 N`

Dynamic brake-turn audit at `150 km/h`, full brake, full steer:

- `stateful-reg-on`: front lateral use average/max `0.10 / 0.61`, rear `0.41 / 0.68`, classification `front-asleep-rear-dominant`
- `instant-reg-on`: front lateral use average/max `0.10 / 0.60`, rear `0.41 / 0.68`, classification `front-asleep-rear-dominant`
- `static-reg-on`: front lateral use average/max `0.17 / 0.64`, rear `0.42 / 0.76`, classification `front-slip-reversal`
- `stateful-raw45`: front lateral use average/max `0.15 / 0.61`, rear `0.43 / 0.74`, classification `front-asleep-rear-dominant`

Interpretation:

- transfer timing is not the dominant cause of the brake-turn problem
- the front axle remains under-participating across stateful, instant, static, and limited raw-brake comparisons
- the current tyre load law cannot make lateral load transfer feel like a meaningful grip penalty because total axle capacity is unchanged when load moves side-to-side
- the next handling architecture pass should add physically plausible tyre load sensitivity and then rerun the same audit before touching brake-steer assist, yaw, or steering
