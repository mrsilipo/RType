# GT1 Internal Physics Audit

## Data Boundaries

Vehicle definitions in `Data/Vehicles/*.json` should stay specific to a car:

- identity, layout, mass, chassis dimensions, CG and inertia
- engine hardware, torque curve, rev limiter, VTEC/cam profile, engine braking curve
- clutch, gearbox, final drive and differential
- suspension geometry, springs, dampers and anti-roll bars
- brake hardware and per-car handbrake torque
- wheel and tyre dimensions/compound values
- per-car prototype tuning that describes the vehicle response

`Data/gt1_engine.json` should stay global to the simulation feel:

- fixed-step timing, frame catch-up and safety limits
- digital throttle/brake assist behavior
- steering assist response
- stability and counter-steer assistance
- generic RPM response helpers

Upgrade definitions in `Data/Upgrades/*.json` should describe upgrade effects only. They should not duplicate base vehicle facts unless they are explicit overrides.

Setup files in `Data/Setups/*.json` should be driver/tuning choices layered on top of the base vehicle. They should not become another source of truth for factory dimensions or engine hardware.

## Wall Contact Upgrade

The wall resolver now treats wall contact as a small contact manifold instead of resolving each hull point independently. The previous point-by-point response could make overlapping contact points fight each other and repeatedly re-project velocity into the barrier.

The new path:

- gathers all active hull contacts first
- computes a weighted contact normal and contact point
- applies one combined positional correction
- separates inward velocity from tangential wall-scrape velocity
- keeps scrape momentum unless the hit is genuinely direct
- adds a small separation speed so the car does not glue itself to the wall
- applies yaw response from the combined wall impulse
- adds a mild yaw-away correction when the nose is still pointed into the wall while sliding

Regression coverage now includes a stuck-wall scenario: car starts against the wall, angled into it, with throttle and steering still pushing into the barrier. The test requires forward scrape progress without crossing through the wall or spinning excessively.

## Engine Sim Launch And Clutch Pass

The EK9 full driveline path now lets the imported Engine Sim clutch capacity drive crank/clutch coupling instead of capping it down to the older generic game clutch value. Pre-rev launches bite harder, drop from limiter into the clutch band earlier, and feed controlled driven-wheel slip so the engine and car movement feel connected.

The remaining launch controller protection is now a narrow clutch-band guard with small slip-linked RPM chatter. It is still a game-side launch assist, not the exact native Engine Sim constraint solver, so this is the next place to keep reducing heuristics as the crank/clutch/wheel model improves.

## Next Cleanup Candidates

- Split vehicle JSON loading into smaller readers: identity/chassis, powertrain, suspension, tyres, audio and prototype tuning.
- Move vehicle-specific prototype tuning under clearer names where possible. `simulation.currentPrototype` currently mixes real car response values with game-feel calibration values.
- Promote wall/contact tuning into a named collision block in the schema if we keep expanding crash behavior.
- Add a lightweight physics probe for wall recovery similar to `LaunchProbe` and `HandlingProbe`, so tuning can print contact count, impact speed, tangent speed, yaw and progress.
- Keep `Data/gt1_engine.json` for global controller/assist behavior only; do not let car-specific rev limiter, cam profile, clutch or tyre facts drift into it.
- Continue replacing launch clutch protection with measured crank, clutch and tyre states from the Engine Sim driveline.
