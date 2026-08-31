# EK9 + AD09 Handling Reference Targets

This document defines practical simulation targets for the stock EK9 reference car on Yokohama ADVAN NEOVA AD09-class tyres.

It is not a CAD or tyre-lab replacement. Its purpose is to give the game a defensible handling target so probes and road tests stop chasing vague feel words.

## Source Vehicle

Reference vehicle:

- Honda Civic Type R EK9 / late 1990s JDM hatch
- Front-wheel drive
- Helical front LSD
- Approximate mass: 1040-1060 kg
- Wheelbase: 2.620 m
- Front track: 1.480 m
- Rear track: 1.480 m
- Height: 1.360 m
- Ground clearance: 0.135 m
- Static weight distribution target: about 62% front
- Stock tyre reference: 195/55R15

The repo currently matches the important geometry closely.

## Tyre Reference

Reference tyre class:

- Yokohama ADVAN NEOVA AD09
- Extreme-performance street / track-day tyre
- Road legal
- High dry grip
- Sharp steering response
- Track-capable consistency

Public sources do not provide a single reliable lateral friction coefficient for AD09 across loads, camber, surface, temperature, and pressure. Use the ranges below as simulation targets, not measured lab constants.

The same tyre size and compound should use the same underlying tyre law front and rear. Avoid solving chassis balance by making the same AD09 fundamentally weaker on the rear axle. Front/rear differences should emerge from:

- static and dynamic load
- camber/alignment
- roll stiffness distribution
- drive and braking force
- load sensitivity
- suspension state
- surface contact

If the model needs different front/rear peak friction for the same tyre, treat that as a temporary game-balance override and document it explicitly.

Suggested dry warm asphalt targets:

- Conservative real road: 0.95-1.05g sustained
- Strong street/track tyre target: 1.05-1.15g sustained
- Short transient peak: 1.15-1.25g
- GT-style fun target without aero: 1.20-1.35g sustained
- GT-style fun target with aero/assist grip: 1.35-1.55g at high speed

A stock EK9 on AD09 should feel sharp and capable, but it should still be a front-heavy FF car. It should not corner like a slick-shod aero race car unless we explicitly choose that as the game target.

Suggested slip target bands:

- Normal useful slip: 2-5 deg
- Hard cornering: 4-7 deg
- Near peak: 6-8 deg
- Clearly overdriven: 8-12 deg
- Sustained 12+ deg: genuine slide/understeer state, not more useful turning

The tyre should have a broad, progressive limit. Do not make peak grip a sharp cliff. Past peak slip, force should flatten and fall gradually so the player can read the car and manage the limit.

## Corner-Speed Budget

Required lateral acceleration:

```text
a_lat = v^2 / radius
g = a_lat / 9.81
```

Speed possible at a given lateral-g:

```text
speed = sqrt(g * 9.81 * radius)
```

Approximate speed in km/h by radius and grip target:

```text
        40m   60m   80m  100m  120m  150m  180m  200m
0.80g    64    78    90   101   110   124   135   143
1.00g    71    87   101   113   124   138   151   159
1.15g    76    94   108   121   132   148   162   171
1.25g    80    98   113   126   138   154   169   178
1.45g    86   105   121   136   149   166   182   192
```

Required lateral-g by speed and radius:

```text
        40m   60m   80m  100m  120m  150m  180m  200m
 60     0.71  0.47  0.35  0.28  0.24  0.19  0.16  0.14
 80     1.26  0.84  0.63  0.50  0.42  0.34  0.28  0.25
100     1.97  1.31  0.98  0.79  0.66  0.52  0.44  0.39
120     2.83  1.89  1.42  1.13  0.94  0.76  0.63  0.57
150     4.42  2.95  2.21  1.77  1.47  1.18  0.98  0.88
180     6.37  4.25  3.19  2.55  2.12  1.70  1.42  1.27
200     7.87  5.24  3.93  3.15  2.62  2.10  1.75  1.57
```

Interpretation:

- 100 km/h through a 100 m radius corner is only about 0.79g. The EK9 should do this comfortably.
- 120 km/h through a 100 m radius corner is about 1.13g. This is near strong AD09 territory.
- 150 km/h through a 150 m radius corner is about 1.18g. This should be possible only near the limit.
- 150 km/h through a 120 m radius corner is about 1.47g. This is beyond a realistic stock EK9 unless the game adds extra grip/downforce.
- 180 km/h through a 150 m radius corner is about 1.70g. That is race-car/downforce/game-assist territory.

## Expected FF Behaviour

Normal steady throttle:

- Front axle leads the turn.
- Rear follows and stabilizes.
- Mild understeer appears as front slip rises.
- Rear should not dominate ordinary steady cornering.

Aggressive turn-in:

- Front slip should build quickly.
- Chassis should take a set.
- Outside front load rises.
- Inside rear unloads.
- Rear can become lighter, but should not instantly spin.

Lift or trail brake into corner:

- Front load increases.
- Rear load decreases.
- Inside rear may become very light.
- Rear becomes more willing to rotate.
- Driver should be able to catch rotation with steering/throttle/brake modulation.

Too much steering:

- Front slip exceeds useful range.
- Front grip saturates or falls progressively.
- Car pushes wide.
- It should feel like front understeer, not rear-caster steering.

Too much brake + steering:

- Front tyres share grip between braking and cornering.
- Turning authority is reduced but not gone.
- Releasing brake should progressively restore front lateral authority.
- Rear can rotate if unloaded, but it should be controllable.

## Telemetry Target Bands

Use turn-normalized values when judging slip direction.

Comfortable corner:

- Lateral-g: 0.4-0.8g
- Front slip: 1-4 deg
- Rear slip: 0.5-3 deg
- Front grip usage: 0.20-0.65
- Rear grip usage: 0.10-0.55
- Beta: small and settling

Fast committed corner:

- Lateral-g: 0.8-1.15g
- Front slip: 4-8 deg
- Rear slip: 2-6 deg
- Front grip usage: 0.60-0.95
- Rear grip usage: 0.35-0.85
- Beta: present but not running away

Near-limit corner:

- Lateral-g: 1.10-1.30g for realistic AD09 target
- Front slip: near peak, roughly 7-10 deg
- Rear slip: lower than front in stable understeer, roughly 4-8 deg
- Front grip usage: 0.90-1.00
- Rear grip usage: should usually remain below front unless lift/trail brake provokes rotation
- Beta: should settle, not climb continuously

Overdriven front:

- Front slip: above peak, 10+ deg
- Front grip usage: near 1.0
- Lateral-g: stops increasing or falls
- Yaw rate: stops increasing meaningfully
- Feel: understeer/push, not sudden rear steering

Provoked FF rotation:

- Triggered by lift, trail brake, or fast weight transfer, not by ordinary steady throttle alone.
- Rear normal load drops, especially inside rear.
- Rear grip usage rises relative to available load.
- Rear yaw contribution increases.
- Countersteer produces a restoring front yaw moment.

Power-on corner exit:

- Front tyres spend part of their friction budget on drive force.
- Front slip rises.
- Yaw gain falls slightly.
- The car begins to run wider.
- The helical LSD should still let it pull out of the corner effectively.
- The rear should not become the primary steering axle.

Lift-off mid-corner:

- The car should tighten its line.
- Rear load should reduce.
- Rear slip can rise above the steady-throttle value.
- Beta can transiently rise into the 3-6 deg region.
- Corrective steering and/or throttle should restore balance.

This lift-off response is a core EK9 behaviour target. If lifting throttle mid-corner leaves the car on exactly the same arc, the chassis is too locked down.

Trail braking:

- Front tyres should not keep full lateral capacity while braking.
- Front load should increase and rear load should decrease.
- Turning authority should reduce but not disappear.
- Brake release should progressively restore front lateral authority.
- Rear rotation should emerge from rear unloading, not from a scripted yaw force.

Braking target on warm AD09-class tyres:

- Sustained hard braking: 0.9-1.1g
- Short favourable peak: 1.15-1.2g
- Sustained 1.3g+ should be treated as suspicious for a stock no-downforce EK9.

## Current Simulation Comparison

From `--classic-turn-radius-budget-probe`:

- Cleanup-off physics reaches about 1.20-1.25g in many high-speed cases.
- More steering angle above that creates more slip/beta/speed loss rather than much tighter path.
- With all assists on, yaw recovery strongly suppresses mid/high-speed rotation.
- With yaw recovery disabled, high-speed lateral response rises sharply, sometimes beyond a realistic stock AD09 target.
- Classic four-wheel currently uses aero drag, but does not appear to apply front/rear aero load into wheel normal loads.

This means the current high-speed "won't turn" feel likely comes from a combination of:

- speed/radius demand exceeding realistic stock-tyre capacity,
- yaw recovery suppressing rotation too aggressively,
- lateral cleanup adding/removing path authority outside tyre physics,
- missing aero-load path if we want GT-style high-speed grip.

## Calibration Decision

Before tuning, choose one target:

### Realistic Stock EK9 + AD09

- Sustained lateral-g target: 1.05-1.15g
- Transient peak: 1.20-1.25g
- High-speed corners above this must require braking/lift.
- No large downforce.
- Yaw recovery should be mild and should not stop the car from rotating at 0.50-0.75 input.

### GT-Style Stock EK9 Feel

- Sustained lateral-g target: 1.20-1.35g
- Transient peak: 1.35-1.45g
- Small hidden game grip or mild aero-like load is acceptable.
- Car remains FF and understeer-biased, but is raceable at higher speeds.

### Tuned / Semi-Race EK9

- Sustained lateral-g target: 1.35-1.55g
- Requires explicit tyre/aero/suspension upgrade data.
- Should not be labelled stock-road-car physics.

## Next Probe Criteria

For each candidate, test:

- 100, 120, 150, 180, 200 km/h
- steering commands 0.25, 0.50, 0.75, 1.00
- steady throttle, lift, trail brake
- radii around 80, 100, 120, 150, 180, 200 m if track data is available

Report:

- actual corner radius
- required g for that radius/speed
- actual lateral-g
- front/rear slip
- front/rear grip usage
- front/rear yaw moment
- beta and betaDot
- yaw rate and yaw acceleration
- speed loss
- assist contributions

The target is not maximum grip. The target is a believable FF sequence:

```text
small input -> clean arc
medium input -> useful front-led cornering
large input -> near-limit front-led cornering
too much input -> progressive understeer
lift/brake -> conditional rear rotation
countersteer -> controllable recovery
```

## Permanent Validation Manoeuvres

Use these as the first executable validation suite:

### 0.5g Steady Sweeper

Purpose: confirm ordinary cornering is clean and front-led.

Expected:

- lateral-g around 0.45-0.60
- front slip 1-4 deg
- rear slip 0.5-3 deg
- beta under about 1.5 deg
- no rear rotation event
- assist contribution should be small

### 0.9-1.0g Steady Sweeper

Purpose: confirm fast EK9 state.

Expected:

- lateral-g around 0.85-1.05
- front slip 4-7 deg
- rear slip 3-5 deg
- front slip generally greater than rear
- beta roughly 2-4 deg
- yaw and beta settling

### Power-On Corner Exit

Purpose: confirm FF power understeer and LSD usefulness.

Expected:

- throttle increases front longitudinal usage
- front slip rises
- yaw gain falls slightly
- radius opens progressively
- car still exits effectively
- no sudden rear steering

### Lift-Off Mid-Corner

Purpose: confirm adjustable EK9 attitude.

Expected:

- throttle release increases front load and reduces rear load
- yaw rate increases above steady-state briefly
- rear slip rises
- beta rises into a controllable range
- corrective steering/throttle recovers the car

### Trail-Brake Entry

Purpose: confirm braking, front load, rear unload and rotation interact.

Expected:

- front longitudinal usage rises
- front lateral capacity is reduced but meaningful
- rear axle unloads
- inside rear becomes light
- brake release returns front lateral authority progressively
- rear rotation is possible but catchable

### Left-Right / Countersteer Transient

Purpose: confirm tyre and suspension state have memory.

Expected:

- first direction builds tyre/suspension state
- reversal must unwind that state
- countersteer produces restoring front yaw moment
- response is fast enough for racing but not an instant vector flip
