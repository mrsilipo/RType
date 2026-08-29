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

`Data/Simulation/arcade_physics.json` should stay global to the simulation feel:

- fixed-step timing, frame catch-up and safety limits
- digital throttle/brake assist behavior
- steering assist response
- stability and counter-steer assistance
- generic RPM response helpers

Legacy upgrade definitions in `Data/Legacy/Upgrades/*.json` should describe upgrade effects only. They should not duplicate base vehicle facts unless they are explicit overrides.

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

## 2026-08-29 Classic Four-Wheel EK9 Handling Checkpoint

The active race handling path is now `classicFourWheel` through `Vehicle/ClassicFourWheelVehicleSimulator.cs` and `Data/Simulation/classic_four_wheel_physics.json`.

Tonight's main handling issue was not a simple tuning problem. The probe was using `State.LateralAcceleration` as the sign-convention truth, but that value had been a rotating-frame lateral-speed derivative. It now publishes the physical local lateral acceleration, and the sign probe checks `PhysicalLoadTransferLateralAcceleration`. That unblocked honest rear-yaw testing and exposed the actual balance problem.

The successful checkpoint keeps rear yaw honest (`RearYawMomentScale = 1.0`) and uses tyre/yaw/body-slip tuning rather than a rear yaw discount. The drive feedback changed from "trolley / centre-rotated / will not turn" to "amazing handling, predictable, almost too easy." Treat this as a solved core-steering checkpoint unless new telemetry contradicts it.

Latest successful real run:

- `Telemetry/RaceRuns/20260829_231240_Honda_Civic_Type_R_EK9_Showroom_Stock_High_Speed_Ring_normal_manual.csv`
- duration: `129.7s`
- wall contacts: `0`
- crash severity: `0`
- samples: `7755 ROAD`, `27 CURB`
- road steering samples above 60 km/h: body slip avg/max `4.99/8.69deg`, front slip avg/max `2.73/12.71deg`, rear slip avg/max `3.98/6.73deg`
- high-speed hard no-brake road steering above 100 km/h: body slip avg/max `5.41/8.69deg`, front slip avg/max `2.98/12.71deg`, rear slip avg/max `4.28/6.73deg`
- no-brake high-speed cornering still carries a noticeable cleanup/scrub speed cost; this is now a refinement target, not a core turning failure

Current validation commands for this path:

```powershell
dotnet build RType.csproj -c Release --no-restore
dotnet run --project RType.csproj -c Release --no-build -- --classic-four-wheel-probe
dotnet run --project RType.csproj -c Release --no-build -- --classic-deceleration-probe
```

Next work should move from "make the car turn" to "make the limit believable":

- add smoother controller/rack transition behavior so small corrections feel less clicky without reducing steering authority
- make high-speed overcommit produce progressive understeer and risk instead of perfectly managed rotation
- model FF lift-off/trail-brake risk deliberately after preserving the current stable baseline
- keep logging front/rear slip gap, body slip, cleanup damping, and cleanup speed-retention force during real laps
- do not broadly retune tyre balance, rear yaw, or steering-angle curve until the GT1/GT2 comparison drive identifies a specific missing behavior

## Next Cleanup Candidates

- Split vehicle JSON loading into smaller readers: identity/chassis, powertrain, suspension, tyres, audio and prototype tuning.
- Move vehicle-specific prototype tuning under clearer names where possible. `simulation.currentPrototype` currently mixes real car response values with game-feel calibration values.
- Promote wall/contact tuning into a named collision block in the schema if we keep expanding crash behavior.
- Add a lightweight physics probe for wall recovery similar to `LaunchProbe` and `HandlingProbe`, so tuning can print contact count, impact speed, tangent speed, yaw and progress.
- Keep `Data/Simulation/arcade_physics.json` for global controller/assist behavior only; do not let car-specific rev limiter, cam profile, clutch or tyre facts drift into it.
- Continue replacing launch clutch protection with measured crank, clutch and tyre states from the Engine Sim driveline.
