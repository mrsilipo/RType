# RType Data Architecture Audit

Last updated: 2026-08-25

## Current Runtime Truth

Probe defaults, the car picker, and the active race path now start from:

- `Data/PurchaseCars/2000_Ek9_Stock.json`

The global simulation/handling configuration currently loads from:

- `Data/Simulation/arcade_physics.json`

The stock EK9 purchase-car definition is consumed by the car picker and the RType engine/audio resolver:

- `Data/PurchaseCars/2000_Ek9_Stock.json`

`VehicleBuildDefinitionLoader` converts the selected purchase-car build into `VehicleSimulationParameters` from modular catalogs and tunes. The active stock EK9 path no longer opens `Data/Vehicles/ek9_reference_2000.json` to construct runtime physics, drivetrain, limiter, mass, tyre, brake, suspension, torque, engine-brake, or race sample audio parameters.

Live gameplay engine sound now uses the RType-owned engine path only. The old `EngineSimulatorSound` fallback is disconnected from `VehicleAudioSystem`.

Live gameplay vehicle physics now stays on the current torque-curve power unit. The old `EngineSimPowerUnit` selector is disconnected from `EnginePowerUnitFactory` and kept only for probes/reference while the RType-owned engine simulator is rebuilt.

Assembled purchase-car runtime parameters now force the old EngineSim physics flags off. This prevents legacy reference files from silently re-enabling the old procedural EngineSim path while the new catalog/build assembly model is the source of truth.

The current build validator can verify both sides of this transitional state:

- RType engine/audio resolves from purchase-car builds plus engine catalogs/tunes.
- Chassis, drivetrain, suspension, brakes, wheels, tyres, and vehicle mass now resolve through the build bridge for the car picker.
- Brake system, tyre model, aero package, steering setup, and handling setup now resolve through part/tune data.
- The old `Data/Vehicles/ek9_reference_2000.json` path is only a compatibility alias. The old monolithic reference file now lives at `Data/Legacy/Vehicles/ek9_reference_2000.json` for source-history/reference use. Active assembled purchase-car runtime no longer opens it to construct `VehicleSimulationParameters`; drivetrain limits, limiter behavior, race sample audio, mass, inertia, torque curves, brakes, tyres, and suspension geometry now come from the modular catalogs.
- Relative and absolute requests for the old EK9 vehicle reference path are redirected by `VehicleRuntimeLoader` to the assembled purchase-car record.
- `dotnet run -- --compare-vehicle-build` compares catalog-resolved build fields against the current vehicle runtime file.

The older setup file has been archived and is not consumed by runtime:

- `Data/Legacy/Setups/ek9_factory.json`

## Naming Problem

`Data/gt1_engine.json` was misleading. It was not an engine definition. It controlled the global vehicle simulation layer:

- fixed physics tick rate
- maximum frame time and tick clamp
- stability assist
- throttle assist
- brake assist and ABS behavior
- steering assist
- RPM response helper values
- vehicle safety limits

Completed rename:

- from `Data/gt1_engine.json`
- to `Data/Simulation/arcade_physics.json`

Optional future variants:

- `Data/Simulation/simulation_index.json`
- `Data/Simulation/arcade_physics.json`
- `Data/Simulation/simulation_physics.json`
- `Data/Simulation/debug_physics.json`

## Proposed Future Layout

```text
Data/
  Controls/
    racing_xbox360_default.json

  Simulation/
    arcade_physics.json

  Surfaces/
    default_surfaces.json

  Tracks/
    lakeside_park.json

  Vehicles/
    ek9_chassis.json

  PurchaseCars/
    2000_Ek9_Stock.json

  VehicleBuilds/
    legacy path redirects only

  Garage/
    OwnedVehicles/
      vehicle_0001.json

  Parts/
    part_catalog_index.json
    Engines/
      engine_blocks.json
      engine_heads.json
      engines.json
      block_upgrades.json
      head_upgrades.json
      cam_sets.json
      displacement_kits.json
      port_polishing.json
      throttle_bodies.json
      intakes.json
      intake_lengths.json
      valve_springs.json
      headers.json
      exhausts.json
      flywheels.json
      clutches.json
    Chassis/
      body_shells.json
    Drivetrain/
      gearboxes.json
      final_drives.json
      differentials.json
    Suspension/
      suspension_sets.json
      alignments.json
    Brakes/
      brakes.json
    Tyres/
      tyres.json
    Wheels/
      wheels.json

  EngineRuntime/
    rtype_engine_assemblies/
    rtype_resolved_profiles/

  Tunes/
    Engines/
      engine_tunes.json

  Legacy/
    EngineSimProfiles/
    Setups/
    Upgrades/
```

## Proposed Ownership Boundaries

### Vehicle Chassis Data

Future file:

- `Data/Vehicles/ek9_chassis.json`

Owns:

- body dimensions
- wheelbase and track width
- base curb shell mass
- CG reference
- yaw inertia calibration scale
- suspension pickup/geometry defaults
- brake hard points
- drivetrain layout
- render/model identity later

Does not own:

- installed engine build
- user-owned upgrade state
- player purchase/garage state

### Purchase Car / Vehicle Build Data

Current file:

- `Data/PurchaseCars/2000_Ek9_Stock.json`

Owns:

- selected chassis
- selected engine
- selected block/head
- installed stock parts
- selected drivetrain, suspension, brakes, wheels, tyres
- stock purchase-car state until player profiles exist

This should become the runtime entry point for selecting a car.

### Engine And Part Catalog Data

Current files:

- `Data/Parts/Engine/*.json`

Current path:

- `Data/Parts/Engine/*.json`

Owns:

- factory Honda engines
- blocks and heads
- engine construction upgrades
- cams, valvetrain, intake, exhaust, flywheel, clutch
- baseline drive torque curves
- baseline engine-brake torque curves
- part weight and durability
- compatibility tags
- block/head compatibility rules
- part requirements such as VTEC head requirements

The catalogs describe available parts. They should not describe a specific player-owned car.

Base engine catalog records now also declare `defaultInstalledParts`. These are not player upgrades; they define the stock support package that comes with a factory engine assembly:

- block/head upgrade baseline
- cam package appropriate to VTEC or non-VTEC heads
- engine-specific stock rotating assembly/displacement package
- stock port, throttle, intake, runner length, valve springs, header, exhaust, flywheel, clutch, and engine audio DSP

`EngineAssemblyResolver` applies those defaults first and then overlays `assembly.engine.installedParts` from a purchase car or owned vehicle. That makes future engine swaps cleaner: installing `engine_k20a` can start from a full stock K20A package, while a garage vehicle only stores the changed slots.

Current important default split:

- `displacement_stock_b16a`, `displacement_stock`, `displacement_stock_b18c`, `displacement_stock_b18a`, `displacement_stock_b18b`, `displacement_stock_d16y4`, `displacement_stock_d16y8`, `displacement_stock_k20a`, `displacement_stock_k24a3`
- `flywheel_stock`, `flywheel_stock_b18c`, `flywheel_stock_b18a_b18b`, `flywheel_stock_d16`, `flywheel_stock_k20a`, `flywheel_stock_k24a3`
- `valve_springs_stock`, `valve_springs_stock_b18c_k20a`, `valve_springs_stock_b18a_b18b_d16`, `valve_springs_stock_d16y8`, `valve_springs_stock_k24a3`
- `cam_set_stock` for VTEC heads and `cam_set_stock_non_vtec` for non-VTEC heads

### Vehicle Part Catalog Data

Current files:

- `Data/Parts/part_catalog_index.json`
- `Data/Parts/Chassis/body_shells.json`
- `Data/Parts/Drivetrain/gearboxes.json`
- `Data/Parts/Drivetrain/final_drives.json`
- `Data/Parts/Drivetrain/differentials.json`
- `Data/Parts/Suspension/suspension_sets.json`
- `Data/Parts/Suspension/alignments.json`
- `Data/Parts/Brakes/brakes.json`
- `Data/Parts/Brakes/brake_systems.json`
- `Data/Parts/Wheels/wheels.json`
- `Data/Parts/Tyres/tyres.json`
- `Data/Parts/Tyres/tyre_models.json`
- `Data/Parts/Aero/aero_packages.json`

Owns:

- non-engine stock parts and future upgrade parts
- gearbox ratios, final drive ratios, differential behavior
- suspension rates, damping, ride heights, anti-roll data, and alignment presets
- brake geometry, pad friction, and thermal capacity
- brake piston diameters, effective radius ratio, clamp force multiplier, and calculated hydraulic piston area
- brake line pressure, bias, ABS behavior, and handbrake force
- wheel size, offset, bolt pattern, and mass
- tyre size, loaded radius, peak grip, rolling resistance, slip targets, and wear/heat metadata
- detailed tyre stiffness, slip, relaxation, camber, scrub, and sliding-friction behavior
- aero drag/lift/frontal-area packages
- body shell dimensions, base mass, torsional rigidity, and durability

The current validator resolves these ids, and the assembled purchase-car runtime now consumes the resolved catalog values for drivetrain limits, limiter behavior, mass, yaw inertia, suspension geometry, brakes, tyres, torque curves, and engine-brake curves.

Body shell records can now also declare `data.engineBay` metadata:

- `orientation`, currently `transverse` for the EK9
- `allowedEngineFamilies`, currently B/D/K Honda families for future FF swap paths
- `requiresSwapKitForFamilies`, currently `honda_k_series` for EK9 K swaps
- `requiredSwapKitSlotsByFamily`, currently `engineMounts`, `wiringLoom`, `driveshafts`, and `shiftLinkage` for EK9 K swaps
- `maxDisplacementCcWithoutBodyModification`

The resolver treats this as compatibility metadata, not as a hard blocker. Unsupported or physically questionable assemblies produce warnings or info messages so the game can later keep bad combinations out of shops, events, or install menus without crashing the runtime.

Current FF drivetrain swap catalog support includes the stock EK9/B-series drivetrain and a K20A transverse FF set:

- `stock_k20a_6_speed`
- `stock_k20a_final_drive`
- `stock_k20a_helical_lsd`

Current EK9 K-series chassis-side swap-kit support includes:

- `ek9_k_series_engine_mounts`
- `ek9_k_series_wiring_loom`
- `ek9_k_series_driveshafts`
- `ek9_k_series_shift_linkage`

Catalog weight is not yet final vehicle mass. The current stock vehicle-side catalog estimate excludes some mass buckets still owned by the vehicle file, such as fluids, fuel, cabin details, accessories, and player/driver mass.

Engine assembly mass now comes from the same `EngineAssemblyResolver` result that resolves torque, limiter, clutch, fuel, and audio data. This avoids a duplicate mass path where engines using `defaultInstalledParts` could be undercounted by the vehicle-side loader.

Yaw inertia now resolves from assembled mass components and shell dimensions, then applies the body shell's `yawInertiaCalibrationScale`. This keeps the stock EK9 at the calibrated `1450 kgm2` handling feel while allowing future body, engine, wheel, and drivetrain changes to alter yaw inertia through data.

Engine mass is no longer only an opaque engine aggregate. `ResolvedEngineAssembly.MassComponents` carries block/head/flywheel and installed part mass or delta entries. `VehicleMassResolver` expands those into chassis-space mass components around the engine bay. Stock accessories remain baseline-included, while upgrade catalogs use `weightDeltaKg`; negative deltas are valid when an upgraded part is lighter than the stock baseline.

The stock body shell now owns explicit body-shell mass-centre fields:

- `bodyMassCenterY`
- `bodyMassCenterLongitudinalMeters`

That prevents every modified build from being artificially forced back to the stock target CG by a hidden body-shell solve. Stock remains calibrated, but modified builds can now move resolved CG, front/rear distribution, and yaw inertia through part data.

Brake axle internals and tyre stiffness now resolve from the installed catalog parts. The build bridge no longer borrows brake piston area, brake effective radius, brake clamp multiplier, tyre cornering stiffness, or tyre longitudinal stiffness from `Data/Vehicles/ek9_reference_2000.json`.

The assembled engine path owns limiter behavior, clutch behavior, drive torque curves, and closed-throttle engine-brake curves. `VehicleBuildDefinitionLoader` maps those values from `ResolvedEngineAssembly` into `VehicleSimulationParameters` directly.

Race sample audio construction is now resolved from the selected engine audio profile without opening the old vehicle reference file. The selected profile path is catalog-owned through the `engineAudioDsp` part, and the profile owns sample loops, mix volumes, high-rpm/VTEC blend points, limiter shutter values, and source recording/generation metadata.

Engine compatibility validation is now layered:

- family tags still provide broad compatibility for engine parts and tunes
- block catalogs can declare allowed head families
- head catalogs can declare allowed block families and bore limits
- parts can declare requirements, currently including `vtecHead`
- invalid or unsupported mixes produce resolver warnings, not player-facing hard failures

### Engine Tune Data

Current file:

- `Data/Tunes/Engine/engine_tunes.json`
- `Data/Tunes/Chassis/steering_setups.json`
- `Data/Tunes/Chassis/handling_setups.json`

Owns:

- idle target
- throttle response map/gamma
- VTEC activation and transition behavior
- rev limiter RPM and cut rhythm
- ignition timing maps
- AFR/fuel target values
- combustion/fuel dose calibration

Does not own:

- block/head identity
- bore, stroke, rod length, or displacement hardware
- cam lift/duration hardware
- intake/header/exhaust geometry
- flywheel/clutch hardware

The resolver applies tune data after hardware parts. That lets the same B16B hardware run factory, street, club sport, race, or bad/safe custom tunes later.

Closed-throttle engine braking now resolves through the same engine assembly path as drive torque. The active build no longer borrows `engineBrakeTorqueCurveNm` from `Data/Vehicles/ek9_reference_2000.json`; each engine owns its baseline decel curve and parts modify it relative to that engine's own baseline displacement, compression, and rotational inertia.

Chassis tune data owns adjustable/runtime behavior that is not a purchased physical component:

- steering ratio/lock/assist/input response
- arcade handling helpers
- launch helper calibration
- wall collision response
- visual suspension/body motion tuning

### RType Engine Runtime Data

Current runtime source:

- `Data/PurchaseCars/2000_Ek9_Stock.json`
- `Data/Parts/Engine/*.json`

The transitional stock B16B assembly files were removed after the build/catalog resolver landed. A `VehicleBuild` plus catalogs now resolves into one runtime `RTypeEngineAssembly`.

### Player/Garage Data

Initial owned-vehicle fixture implemented:

- `Data/Garage/OwnedVehicles/vehicle_0001.json`

Modified owned-vehicle proof fixture implemented:

- `Data/Garage/OwnedVehicles/vehicle_0002_modified_ek9.json`

Engine-swap owned-vehicle proof fixture implemented:

- `Data/Garage/OwnedVehicles/vehicle_0003_k20a_swap_ek9.json`

The modified fixture keeps the stock purchase-car template link but changes installed part IDs. It is not a new purchase car. It proves the future career-mode pattern where buying a stock car creates an owned vehicle record, and subsequent modifications change the owned record only.

The K20A swap fixture proves the separate requirement that an owned EK9 can install a different engine family without cloning every stock engine part into the vehicle file. It selects `engine_k20a`, leaves `assembly.engine.installedParts` empty, and relies on the K20A engine record's `defaultInstalledParts` to resolve a complete stock K20A assembly. It then selects matching K-series FF drivetrain parts and the required chassis-side EK9 K-series swap-kit parts. Missing required swap-kit slots now produce validation warnings; the current K20A fixture has all required slots and resolves without warnings.

Supported Frankenstein engine paths now live in `Data/Parts/Engine/engine_combinations.json`. These are not purchase cars and not garage-owned vehicles; they are authored block/head recipes used by `EngineAssemblyResolver` to separate known supported hybrids from random catalog mixing.

Current authored hybrid recipes:

- `combo_k24a3_block_k20a_head`: K24A3 block with K20A head.
- `combo_b18b_block_b16b_head_lsvtec`: B18B block with B16B Type R head.

The resolver auto-matches a recipe by block/head pair, exposes the selected combination on `ResolvedEngineAssembly`, and emits `supported_engine_combination` for known hybrids. Non-factory pairs with no authored recipe emit `unapproved_engine_combination`, so future garage filtering can keep players on known mod paths.

Engine power synthesis is now separated from JSON assembly parsing. `Data/EnginePowerComposer.cs` owns:

- E85/high-compression fuel multiplier blending.
- Drive torque curve scaling from displacement, compression, cam flow, intake flow, exhaust flow, VTEC blend, and fuel multiplier.
- Engine-brake torque curve scaling from displacement, compression, and rotational inertia.

`EngineAssemblyResolver` still determines the assembled parts and validates the build, then passes an explicit composition input into the composer. This keeps the current runtime values unchanged while making the same formulas reusable for future garage preview, dyno, tuning, and audio sample-generation tooling.

Verification command:

- `dotnet bin\Verification\RType.dll --engine-power-composer-probe`

Current modified proof changes:

- body shell: `stock_ek9_body_shell` -> `club_sport_seam_welded_ek9_body_shell`
- fuel: `fuel_98ron` -> `fuel_e85`
- tune: `tune_b16b_factory` -> `tune_b16b_club_sport_e85`
- engine hardware: billet block/head upgrades, club-sport cams, high-compression displacement kit, porting, throttle body, intake, short runner, valve springs, header, exhaust, flywheel, and clutch

Verification command:

- `dotnet bin\Verification\RType.dll --vehicle-modification-comparison-probe`
- `dotnet bin\Verification\RType.dll --vehicle-engine-swap-probe`

Current verified resolver deltas:

- displacement: `+120cc`
- compression: `+1.6`
- peak torque: `+31.0Nm`
- peak engine braking: `+36.1Nm`
- engine inertia: `-0.048kgm2`
- total mass: `-7.8kg`
- front distribution: `-0.03pp`
- CG height: `-0.012m`
- yaw inertia: `-10kgm2`
- clutch capacity: `+190Nm`
- fuel multiplier: `+0.021`

Current verified K20A swap deltas:

- engine: `engine_b16b` -> `engine_k20a`
- engine family: `honda_b_series` -> `honda_k_series`
- transmission: `stock_s4c_5_speed` -> `stock_k20a_6_speed`
- final drive: `stock_ek9_final_drive` -> `stock_k20a_final_drive`
- differential: `stock_ek9_helical_lsd` -> `stock_k20a_helical_lsd`
- swap kits: none -> `ek9_k_series_engine_mounts`, `ek9_k_series_wiring_loom`, `ek9_k_series_driveshafts`, `ek9_k_series_shift_linkage`
- displacement: `1595cc` -> `1998cc`
- peak torque: `159.8Nm` -> `206.0Nm`
- limiter: `8400rpm` -> `8600rpm`
- swap-kit mass: `23.9kg`
- total mass: `1060.0kg` -> `1115.4kg`

Future ownership:

- player profile
- cash/progression
- owned cars
- installed upgrades
- purchased but uninstalled parts
- car mileage/wear/durability
- saved setups

Owned vehicle records use the same `assembly` shape as purchase cars, but declare `role: owned_vehicle`, record their source purchase-car template, and carry ownership fields such as `ownerProfileId` and `garageSlot`.

The current resolver exposes that ownership/template metadata without yet implementing profile persistence, purchase transactions, mileage, wear, or saved setup management.

`GarageVehicleFactory` now owns the first purchase-to-owned creation path.

```text
purchase car template
  -> clone assembly
  -> set owned vehicle id/display name
  -> set role owned_vehicle
  -> set template.sourcePurchaseCar + template.purchaseCarId
  -> set ownership.playerOwned/profile/garageSlot
  -> save under a caller-selected garage directory
```

Important behavior:

- The purchase-car template is not modified.
- The owned vehicle starts with the stock assembly exactly as purchased.
- Future modifications should alter the owned vehicle record only.
- The factory currently does not implement economy, profile persistence, mileage, wear, or inventory ownership.

Verification:

- `dotnet bin\Verification\RType.dll --garage-vehicle-factory-probe`

`GarageModInstaller` now owns the first owned-vehicle install path.

```text
owned vehicle record
  -> build VehicleModPathReport
  -> refuse non-owned/purchase templates
  -> locate selected slot/id in engine or vehicle option report
  -> reject blocked options by default
  -> mutate only the owned vehicle assembly JSON
  -> resolve the edited vehicle back into runtime parameters
```

Important behavior:

- Purchase-car templates remain immutable.
- The installer depends on the resolver reports for Ready/Advisory/Blocked status instead of duplicating compatibility rules.
- `Data/GarageModSlotMap.cs` is the shared slot map used by the engine preview resolver, vehicle preview resolver, and garage installer. This keeps catalog slot targeting and install writes aligned.
- Engine part, tune, fuel, and authored engine-combination installs are supported.
- Vehicle drivetrain, suspension, brake, wheel, aero, tyre, and tyre-package installs are supported.
- Tyre packages write the compound and tyre-model fields together so the player-facing install path does not leave incompatible tyre data behind.
- The install result returns a `GarageModInstallReceipt` carrying owner, garage slot, source purchase car, installed option, before/after engine/fuel/tune, before/after peak torque, and before/after mass/weight distribution for future profile/economy history.

Verification:

- `dotnet bin\Verification\RType.dll --garage-mod-installer-probe`

`GarageProfileLoader` and `GarageInventoryModPathResolver` now own the first player-profile inventory view.

Current fixture:

- `Data/Garage/Profiles/dev_profile.json`

```text
garage profile
  -> owned vehicle references
  -> owned part ids
  -> purchasable part ids
  -> locked part ids
  -> selected owned vehicle
  -> VehicleModPathReport
  -> inventory-aware option availability
```

Important behavior:

- `VehicleModPathResolver` still answers whether the assembled car can accept a candidate part.
- `GarageInventoryModPathResolver` answers whether the player owns, can buy, has locked, or cannot currently install that candidate.
- Availability buckets are `Installed`, `OwnedReady`, `Purchasable`, `Locked`, `NotOwned`, and `BlockedByBuild`.
- `BlockedByBuild` takes priority over inventory. A player owning a part does not make an unsafe build installable.
- `GarageModInstaller.ApplyProfileOwnedOption` refuses purchasable-but-unowned parts. Purchases must move the part into profile inventory before install.

Verification:

- `dotnet bin\Verification\RType.dll --garage-inventory-probe`

Vehicle purchases now use the same shop service.

Current vehicle price fixture:

- `Data/Garage/vehicle_prices.json`

```text
profile JSON
  -> purchase-car template
  -> vehicle price lookup
  -> credit check
  -> GarageVehicleFactory creates owned vehicle JSON
  -> append profile.ownedVehicles
  -> append transactionHistory
```

Important behavior:

- Only `role: purchase_car_stock` JSON can be purchased.
- The purchase-car template is not modified.
- The generated owned vehicle records source purchase-car metadata, owner profile id, and allocated garage slot.
- Vehicle ids are allocated as `vehicle_####`. Existing ids with suffixes, such as `vehicle_0002_modified_ek9`, still reserve their numeric prefix.
- Vehicle purchases mutate profile JSON and create a new owned vehicle JSON file.
- Vehicle purchases do not mutate any existing owned vehicle.

Verification:

- `dotnet bin\Verification\RType.dll --garage-vehicle-purchase-probe`

Saved setup overlays now sit beside permanent installed hardware.

Current fixture:

- `Data/Garage/SavedSetups/vehicle_0001_track_day_setup.json`

```text
profile saved setup reference
  -> owned vehicle record
  -> saved setup overrides
  -> temporary cloned vehicle assembly
  -> VehicleAssemblyResolver
  -> runtime parameters for race/session use
```

Important behavior:

- Saved setup overlays do not mutate purchase-car templates.
- Saved setup overlays do not mutate owned vehicle hardware records.
- Current setup overlays may select engine tune, selected fuel, alignment, steering setup, and handling setup.
- Permanent parts such as engine internals, suspension kits, gearboxes, differentials, brakes, wheels, and tyres still go through the garage install path.
- Chassis tune inheritance is now active for steering and handling catalogs so street/club/pro setup records can override only their changed fields while inheriting complete stock runtime requirements.

Verification:

- `dotnet bin\Verification\RType.dll --garage-saved-setup-probe`

`GarageShopService` now owns the first part purchase transaction path.

Current price fixture:

- `Data/Garage/part_prices.json`

```text
profile JSON
  -> purchasable part id
  -> price lookup
  -> credit check
  -> append ownedPartIds
  -> append transactionHistory
  -> profile-aware install can proceed without override
```

Important behavior:

- Engineering catalogs do not yet own prices. The temporary shop price file keeps economy values separate from physical part definitions.
- Already-owned parts cannot be purchased twice.
- Locked parts cannot be purchased.
- Purchasable parts require a defined price before a transaction can occur.
- Purchases mutate only the profile JSON, not vehicle assembly JSON.
- Installs still mutate only owned vehicle JSON, not profile JSON.

Verification:

- `dotnet bin\Verification\RType.dll --garage-inventory-probe`

## Legacy / Transitional Systems

### `Data/Legacy/Setups/ek9_factory.json`

Status: legacy/reference.

Problem:

- Uses old `stage_0` upgrade naming.
- Does not reference the new detailed engine part catalogs except through the newly added build pointer.
- Not consumed by runtime.

Recommendation:

- Keep as read-only historical reference until the player profile/setup layer is designed.
- Do not use it as runtime data.
- Future saved setups should be layered against owned vehicle records, not purchase-car templates.

### `Data/Legacy/Upgrades/*.json`

Status: legacy gameplay upgrade model.

Problem:

- Uses broad stage-based upgrade thinking.
- Does not match the new component assembly model.
- Engine upgrade concepts overlap with new part catalogs.

Recommendation:

- Moved to `Data/Legacy/Upgrades/`.
- Rebuild future upgrade UI from `Data/Parts/**` instead.

### `Data/Legacy/EngineProfiles/*.json`

Status: legacy Engine Sim approximation profiles.

Problem:

- Used by old `EngineSimulator*` tooling/probes.
- Parallel to the new RType Sim Engine profile system.
- No longer selected by live vehicle audio or live vehicle physics.

Recommendation:

- Keep read-only for reference/probe comparison until then.

### `Audio/EngineSimulatorSound.cs` And `Vehicle/EngineSimPowerUnit.cs`

Status: legacy runtime approximation, disconnected from gameplay.

Problem:

- These were pre-reset approximation paths from before the current RType-owned port direction.
- They can hide problems by acting as an unplanned fallback beside the new RType engine runtime.

Recommendation:

- Keep compileable for comparison probes only.
- Do not reconnect to live gameplay.
- Delete or archive after equivalent RType-owned comparison tools replace the old probes.

### Old `Data/RTypeEngineProfiles/BaseEngines` And `Parts`

Status: legacy/transitional RType engine assembly scaffold.

Problem:

- Duplicated data now represented by `PartCatalogs`.
- Hand-built the B16B stock runtime assembly outside the new vehicle build system.

Replacement:

- `Data/PurchaseCars/2000_Ek9_Stock.json`
- engine/part catalogs
- build/catalog resolver producing runtime profile output

Current cleanup:

- Active part catalogs now live under `Data/Parts/Engine`.
- Engine tune catalogs now live under `Data/Tunes/Engine`.
- Old procedural RType/ATG reference profiles now live under `Data/Legacy/EngineProfiles`.
- `Data/RTypeEngineProfiles` should remain unused.

### `Data/Legacy/Vehicles/ek9_reference_2000.json`

Status: legacy/reference file. The old `Data/Vehicles/ek9_reference_2000.json` path is retained only as a compatibility alias that redirects to the stock purchase car. Active purchase-car runtime no longer opens the legacy file to construct `VehicleSimulationParameters`.

Problem:

- Contains old chassis, physics, engine, audio, tyres, setup-like values, and prototype behavior.
- Should not be used as an active assembled vehicle source.

Recommendation:

- Keep only for old debug/reference comparison until those tools are retired.
- Do not rename it into a new active chassis file.
- Store real chassis/body-shell data in `Data/Parts/Chassis/body_shells.json`.
- Store stock/off-the-shelf cars in `Data/PurchaseCars`.
- Store future player-owned cars under a garage/owned-vehicle path populated from purchase-car templates.

## Recommended Migration Order

1. Done: rename `Data/gt1_engine.json` to `Data/Simulation/arcade_physics.json`.
2. Done: update `GameLaunchOptions.DefaultSimulationEngineDefinitionPath`.
3. Add compatibility fallback for the old path only if needed for command-line scripts.
4. Done: add `VehicleBuildDefinitionLoader`.
5. Done for RType engine/audio: add build/catalog resolver for block/head/part/tune IDs.
6. Done: add `dotnet run -- --validate-rtype-builds`.
7. Done for validation: validator now reports chassis, gearbox, final drive, tyre, brake, suspension, mass, CG, and driven-wheel data from the active purchase-car build and catalogs.
8. Done: add catalogs for drivetrain, suspension, brakes, tyres, wheels, and chassis/body shells.
9. Done for validation: build validator resolves those non-engine catalog ids.
10. Done for the bridge: add `VehicleBuildDefinitionLoader` that resolves chassis, drivetrain, suspension, brakes, wheels, tyres, mass buckets, and RType engine assembly from build/catalog data.
11. Done for parity: add `dotnet run -- --compare-vehicle-build`.
12. Done: add conversion from resolved build data into `VehicleSimulationParameters`.
13. Done: make `RacingGame` select `Data/PurchaseCars/2000_Ek9_Stock.json`, not the chassis JSON directly.
14. Done: migrate brake system, tyre model, aero package, steering setup, and handling setup into parts/tunes.
15. Done for live runtime selection: disconnect old `EngineSimulatorSound` and `EngineSimPowerUnit` gameplay hooks.
16. Done for legacy profile data: move old `Data/EngineProfiles` files under `Data/Legacy/EngineProfiles`.
17. Migrate remaining legacy-only runtime fields into catalogs/tunes or explicit build/runtime buckets.
18. Do not rename `ek9_reference_2000.json` into a new active chassis file. Keep chassis identity and hard-point data in `Data/Parts/Chassis/body_shells.json`; keep stock/off-the-shelf cars in `Data/PurchaseCars`.
19. Done: move `Data/RTypeEngineProfiles/PartCatalogs` to `Data/Parts/Engine`.
20. Done for upgrades: move old `Data/Upgrades` under `Data/Legacy/Upgrades`.
21. Done: move old `Data/Setups/ek9_factory.json` under `Data/Legacy/Setups`.
22. Delete legacy files only after probes/build/runtime no longer reference them.

23. Done for garage filtering: add `EngineModPathResolver` to generate installable engine option reports from the active purchase/owned build instead of hardcoded upgrade menus.
24. Done for validation: add `--engine-mod-path-probe`, proving warning-level resolver messages block unsafe options while info-level messages stay visible as player guidance.

## Target Runtime Flow

```text
selected vehicle build
  -> chassis definition
  -> engine block/head/parts catalogs
  -> selected engine tune
  -> drivetrain/suspension/brake/tyre catalogs
  -> resolved vehicle simulation parameters
  -> resolved RType engine assembly
  -> game runtime
```

This keeps showroom cars, user-owned cars, bought parts, installed parts, and resolved physics/audio data separate.

## Garage Mod-Path Filtering

The current first pass treats the engine garage as a simulation-backed query, not a static list.

```text
vehicle build JSON
  -> assembly.engine
  -> current resolved engine
  -> candidate part/tune/fuel/combination change
  -> EngineAssemblyResolver
  -> selectable option + warnings/info + preview metrics
```

Important behavior:

- Warning-level validation messages mean the option is not selectable in the normal garage flow.
- Info-level validation messages are advisory and should be shown to the player, not hidden.
- The report exposes separate `Ready`, `Advisory`, `Blocked`, and `Installed` option buckets, plus per-slot groups. Future UI should use these groups instead of rebuilding warning logic locally.
- Each option carries tier/category metadata when the catalog has it, plus preview displacement, compression, limiter, and peak torque after the candidate is resolved.
- High-compression parts on 98 RON/factory tune are blocked because they produce octane warnings.
- E85 on a stock engine is selectable but reports that a retune is recommended.
- Authored Frankenstein engine combinations are surfaced by recipe ID, but still run through the same resolver path as normal installed parts.

The first whole-vehicle wrapper is `VehicleModPathResolver`.

It currently exposes:

- the current `ResolvedVehicleAssembly`
- current vehicle and engine validation warnings/info
- the grouped `EngineModPathReport`

This is deliberately the entry point future garage menus should call. Non-engine option groups should be added beside the existing `Engine` report so garage screens can ask one service for a complete car modification picture.

First non-engine groups now implemented:

- gearbox
- final drive
- differential
- front/rear suspension
- alignment
- front/rear brakes
- brake system
- front/rear wheels
- front/rear tyres
- aero package

These candidates are evaluated by resolving a full cloned vehicle build. The report subtracts validation codes already present on the current build, so a candidate is only advisory or blocked when the new option introduces a new message.

Known intentional gap:

- Tyre compound changes are blocked as single-slot changes when the existing tyre model remains tied to the old compound. This is now diagnostic behavior only.
- Player-facing tyre upgrades should use `Data/Parts/Tyres/tyre_packages.json`, which changes front/rear compounds and front/rear tyre models together.
- K-series drivetrain parts appear blocked on a B-series EK9 because they do not match the current engine family. They become valid in the K20A swap owned-vehicle fixture.

Current tyre packages:

- `tyre_package_sports_hard_ek9`: stock EK9 sports-hard compound with front/rear EK9-specific tyre models.
- `tyre_package_sports_medium_balanced`: sports-medium compound with shared balanced tyre model on both axles.
- `tyre_package_semi_slick_aggressive`: semi-slick compound with shared aggressive tyre model on both axles.

Current verification:

- `dotnet bin\Verification\RType.dll --vehicle-mod-path-probe`

Current probe result:

- stock EK9: warning-clean purchase car, `62` ready engine options, `8` advisory options, `7` blocked options, `16` installed/current options
- modified EK9: warning-clean owned vehicle, `63` ready engine options, `8` advisory options, `6` blocked options, `16` installed/current options

## Saved Setup Editing Checkpoint

Saved setup editing is now separated from permanent installed hardware.

Runtime ownership split:

```text
purchase car template
  -> owned vehicle assembly JSON
  -> saved setup overlay JSON
  -> temporary resolved overlay
  -> VehicleAssemblyResolver
```

Permanent part installs are still owned-vehicle mutations through `GarageModInstaller`. Saved setup edits are limited to setup/tune selections through `GarageSavedSetupEditor`.

Editable saved setup fields:

- `overrides.engine.tuneId`
- `overrides.engine.fuelSelected`
- `overrides.suspension.alignment`
- `overrides.tuning.steering`
- `overrides.tuning.handling`

Validation behavior:

- `GarageSavedSetupEditor.UpdateSetup` loads the profile and confirms the selected vehicle is owned by that profile.
- It confirms the setup reference belongs to that owned vehicle and that the setup file declares the same owner profile and vehicle id.
- It clones the setup JSON and applies only whitelisted override fields.
- It writes the candidate to a temp file and calls `GarageSavedSetupResolver.ResolveWithSetupFile`.
- The resolver clones the owned vehicle to a temporary overlay, applies the candidate setup, then resolves the full vehicle through `VehicleAssemblyResolver`.
- Warning-level vehicle or engine validation messages reject the edit before the real setup file is written.
- Successful edits write only the setup file. The owned vehicle assembly file remains unchanged.

Current probe:

- `--garage-saved-setup-editor-probe` edits alignment, steering, and handling in a temporary setup, verifies the resolved overlay uses those new ids, verifies the owned vehicle file hash is unchanged, rejects an invalid setup edit, and proves the failed edit did not partially write the setup file.

Validation run for this checkpoint:

- `dotnet build RType.csproj --no-restore -o bin\VerificationSetupEdit`
- `dotnet bin\VerificationSetupEdit\RType.dll --garage-saved-setup-editor-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --garage-saved-setup-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --garage-inventory-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --vehicle-catalog-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --garage-mod-installer-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --garage-vehicle-purchase-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --vehicle-mod-path-probe`
- `dotnet bin\VerificationSetupEdit\RType.dll --physics-smoke-test`

## Active Setup Runtime Selection

Saved setups now have a runtime selection path instead of being probe-only overlays.

Selection flow:

```text
garage profile
  -> selected owned vehicle
  -> active saved setup, explicit setup, or no setup
  -> temporary overlay build when setup is present
  -> VehicleBuildDefinitionLoader.LoadSimulationParameters
  -> SimpleVehicleSimulator / Engine Room / probes
```

New services:

- `Data/GarageSavedSetupActivationService.cs`
- `Data/GarageRuntimeVehicleResolver.cs`

Activation behavior:

- `SetActiveSetup` resolves the target saved setup against the selected profile-owned vehicle before mutating profile data.
- If validation passes, it marks that setup active and clears other active setup flags for the same vehicle.
- A missing setup is rejected before writing the profile.
- `ClearActiveSetup` removes active setup selection for one owned vehicle so runtime loading uses the owned vehicle assembly directly.

Runtime behavior:

- `VehicleRuntimeLoader` keeps direct build loading as the default.
- Supplying a garage profile opts into profile-aware loading.
- Supported launch flags are:
  - `--garage-profile <path>`
  - `--garage-vehicle <vehicle id or path>`
  - `--garage-setup active|none|<setup id or path>`
- If no garage vehicle is supplied, the first owned vehicle by garage slot is selected.
- If no setup is supplied, `active` is assumed.
- If no active setup exists for the vehicle, the owned vehicle assembly is loaded without overlay.

Runtime integration:

- `RacingGame` uses the garage runtime resolver in `BeginPreRace` when a garage profile is provided.
- `RTypeEngineRoomScreen` now loads through `VehicleRuntimeLoader` with launch options, so the engine room can test an owned vehicle plus active setup.
- Runtime probes that already accept `GameLaunchOptions` now use the profile-aware overload: physics smoke, launch, shift, handling, and audio diagnostics.

Current probe:

- `--garage-active-setup-probe` switches between two setup records on a temporary owned EK9, verifies active setup resolution, verifies clearing active setup returns to stock owned assembly, rejects missing setup activation without partial profile writes, and verifies the owned vehicle JSON hash is unchanged.

Validation run:

- `dotnet build RType.csproj --no-restore -o bin\VerificationActiveSetup`
- `dotnet bin\VerificationActiveSetup\RType.dll --garage-active-setup-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --garage-saved-setup-editor-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --garage-saved-setup-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-vehicle vehicle_0001 --garage-setup active`
- `dotnet bin\VerificationActiveSetup\RType.dll --garage-inventory-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --garage-mod-installer-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --garage-vehicle-purchase-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --vehicle-catalog-probe`
- `dotnet bin\VerificationActiveSetup\RType.dll --launch-probe --garage-profile Data/Garage/Profiles/dev_profile.json --garage-vehicle vehicle_0001 --garage-setup active`
- `dotnet bin\VerificationActiveSetup\RType.dll --shift-probe --garage-profile Data/Garage/Profiles/dev_profile.json --garage-vehicle vehicle_0001 --garage-setup active`
- `dotnet bin\VerificationActiveSetup\RType.dll --handling-probe --garage-profile Data/Garage/Profiles/dev_profile.json --garage-vehicle vehicle_0001 --garage-setup active`
- `dotnet bin\VerificationActiveSetup\RType.dll --audio-diagnostics-smoke --garage-profile Data/Garage/Profiles/dev_profile.json --garage-vehicle vehicle_0001 --garage-setup active`

## Active Vehicle Runtime Selection

Garage profiles now distinguish the selected car from the list of owned cars.

Data shape:

```json
{
  "activeVehicleId": "vehicle_0001",
  "ownedVehicles": []
}
```

Selection behavior:

- `GarageProfileLoader` loads `activeVehicleId`.
- `GarageRuntimeVehicleResolver` uses `activeVehicleId` when no explicit garage vehicle is supplied.
- If `activeVehicleId` is empty, the resolver falls back to the first owned vehicle by `garageSlot`.
- If `activeVehicleId` names a vehicle the profile does not own, runtime selection throws instead of silently selecting the wrong car.

Write behavior:

- `Data/GarageActiveVehicleService.cs` owns profile active-vehicle writes.
- `SetActiveVehicle` confirms the vehicle belongs to the profile, resolves the vehicle assembly, rejects warning-producing vehicles, then writes `activeVehicleId`.
- `ClearActiveVehicle` removes the field and returns runtime loading to first-slot fallback.
- Active vehicle selection never mutates owned vehicle assembly JSON.

Current probe:

- `--garage-active-vehicle-probe` verifies default active vehicle selection, switching to a second owned vehicle, rejecting a missing vehicle without partial profile writes, clearing back to first-slot fallback, and preserving owned vehicle file hashes.

Validation run:

- `dotnet build RType.csproj --no-restore -o bin\VerificationActiveVehicle`
- `dotnet bin\VerificationActiveVehicle\RType.dll --garage-active-vehicle-probe`
- `dotnet bin\VerificationActiveVehicle\RType.dll --garage-active-setup-probe`
- `dotnet bin\VerificationActiveVehicle\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationActiveVehicle\RType.dll --vehicle-catalog-probe`
- `dotnet bin\VerificationActiveVehicle\RType.dll --garage-saved-setup-editor-probe`
- `dotnet bin\VerificationActiveVehicle\RType.dll --garage-inventory-probe`
- `dotnet bin\VerificationActiveVehicle\RType.dll --garage-mod-installer-probe`
- `dotnet bin\VerificationActiveVehicle\RType.dll --garage-vehicle-purchase-probe`

## Vehicle Purchase Active-State Integration

Vehicle purchase now participates in active vehicle state.

Behavior:

- If a profile has no `activeVehicleId`, `GarageShopService.PurchaseVehicle` assigns the newly purchased vehicle as active.
- If a profile already has `activeVehicleId`, later purchases preserve that selection.
- Purchase transaction history records `becameActiveVehicle` for each vehicle purchase.
- `GarageVehiclePurchaseResult` exposes `BecameActiveVehicle` so future menu code can show whether a purchase became the selected garage/race car.

Reasoning:

- First-car purchase should produce a complete runnable garage profile without requiring a second selection write.
- Later purchases should not steal the player's current active car. The player or garage UI should explicitly switch vehicles through `GarageActiveVehicleService`.

Current probe:

- `--garage-vehicle-purchase-probe` verifies first purchase sets `activeVehicleId`, second purchase preserves it, transaction history records both active-state outcomes, and the purchase-car template hash stays unchanged.

Validation run:

- `dotnet build RType.csproj --no-restore -o bin\VerificationPurchaseActive`
- `dotnet bin\VerificationPurchaseActive\RType.dll --garage-vehicle-purchase-probe`
- `dotnet bin\VerificationPurchaseActive\RType.dll --garage-active-vehicle-probe`
- `dotnet bin\VerificationPurchaseActive\RType.dll --garage-active-setup-probe`
- `dotnet bin\VerificationPurchaseActive\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationPurchaseActive\RType.dll --garage-inventory-probe`
- `dotnet bin\VerificationPurchaseActive\RType.dll --garage-mod-installer-probe`
- `dotnet bin\VerificationPurchaseActive\RType.dll --garage-saved-setup-editor-probe`
- `dotnet bin\VerificationPurchaseActive\RType.dll --vehicle-catalog-probe`

## Saved Setup Creation

Saved setups can now be created from owned vehicle state.

Creation flow:

```text
profile + owned vehicle
  -> validate owned vehicle assembly
  -> snapshot tune-like selected ids
  -> temp saved setup candidate
  -> ResolveWithSetupFile validation
  -> write setup file
  -> append profile.savedSetups reference
  -> optionally mark active
```

Snapshot fields:

- `assembly.engine.tuneId`
- `assembly.engine.fuel.selected`
- `assembly.suspension.alignment`
- `assembly.tuning.steering`
- `assembly.tuning.handling`

Important boundary:

- Saved setup creation deliberately snapshots selected setup ids from the owned vehicle assembly JSON, not expanded inherited catalog values. That keeps setup files small and preserves the player's selected tune/setup identity.
- Permanent hardware remains in the owned vehicle assembly. A saved setup does not snapshot engine parts, gearbox, tyres, wheels, suspension kits, brakes, or aero package.

Service:

- `Data/GarageSavedSetupCreationService.cs`
- Main entry: `CreateFromOwnedVehicle(profilePath, ownedVehicleIdOrPath, setupOutputDirectory, displayName, makeActive)`
- Setup ids allocate as `{vehicleId}_setup_###`.
- If `makeActive` is true, existing setup refs for that same vehicle are marked inactive before appending the new active setup.
- Owned vehicle JSON is never mutated.

Current probe:

- `--garage-saved-setup-creation-probe` verifies setup id allocation, setup file creation, profile registration, active flag behavior, active runtime resolution, missing vehicle rejection, and owned vehicle hash preservation.

Validation run:

- `dotnet build RType.csproj --no-restore -o bin\VerificationSetupCreate`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-saved-setup-creation-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-saved-setup-editor-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-active-setup-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-active-vehicle-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-vehicle-purchase-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-inventory-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --garage-mod-installer-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --vehicle-catalog-probe`
- `dotnet bin\VerificationSetupCreate\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationSetupCreate\RType.dll --launch-probe --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationSetupCreate\RType.dll --audio-diagnostics-smoke --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`

## Garage Profile Integrity Validation

The garage profile now has a whole-save diagnostic pass.

Service:

- `Data/GarageProfileIntegrityValidator.cs`

Validation scope:

- profile has owned vehicles
- `activeVehicleId` is set or explicitly reports first-slot fallback info
- `activeVehicleId` names an owned vehicle
- owned vehicle ids are unique
- garage slots are unique and positive
- owned vehicle paths exist and resolve
- owned vehicle file role is `owned_vehicle`
- owned vehicle file id matches the profile reference
- owned vehicle owner profile id matches the profile
- resolved owned vehicle and engine assemblies have no warning-level validation messages
- saved setup ids are unique per vehicle
- each vehicle has no more than one active setup
- saved setup targets an owned vehicle
- saved setup file id, owner profile id, and vehicle id match the profile reference
- saved setup overlay resolves without warning-level validation
- inventory does not mark the same part as both owned and locked

The validator does not write files. It returns `GarageProfileIntegrityReport` with `Info`, `Warnings`, and `IsClean`.

Current probe:

- `--garage-profile-integrity-probe` checks one clean profile and one deliberately broken profile. The broken profile covers bad active vehicle id, duplicate owned vehicle ids, duplicate garage slots, wrong owned-vehicle owner, duplicate saved setup ids, multiple active setups for one vehicle, setup owner mismatch, setup resolve failure, setup vehicle not owned, setup id mismatch, and owned+locked inventory conflict.

Validation run:

- `dotnet build RType.csproj --no-restore -o bin\VerificationGarageIntegrity`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-profile-integrity-probe`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-saved-setup-creation-probe`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-active-vehicle-probe`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-vehicle-purchase-probe`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --vehicle-catalog-probe`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --launch-probe --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationGarageIntegrity\RType.dll --audio-diagnostics-smoke --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`

## Garage Inventory Catalog And Price Integrity

Garage profile integrity validation now checks whether inventory ids point at real data.

Known catalog id sources:

- `Data/Parts/part_catalog_index.json`
- `Data/Parts/Engine/part_catalog_index.json`
- `Data/Tunes/Engine/engine_tunes.json`
- `Data/Tunes/Engine/fuels.json`

Shop price source:

- `Data/Garage/part_prices.json`

Validation scope:

- owned inventory ids must exist in the known part/tune/fuel catalog universe
- locked inventory ids must exist in the known part/tune/fuel catalog universe
- concrete purchasable ids must exist in the known part/tune/fuel catalog universe
- concrete purchasable ids must have a price entry before they are exposed as buyable
- the purchasable wildcard `*` remains valid and intentionally skips catalog/price checks because it means broad catalog visibility, not a concrete part id

Current warning codes:

- `inventory_owned_part_missing_catalog`
- `inventory_locked_part_missing_catalog`
- `inventory_purchasable_part_missing_catalog`
- `inventory_purchasable_part_missing_price`

Current probe:

- `--garage-profile-integrity-probe` now checks missing owned, locked, and purchasable catalog ids, plus a concrete purchasable fuel id that exists in the tune data but has no garage price.

Validation run:

- `dotnet build RType.csproj --no-restore -o bin\VerificationGarageCatalogIntegrity`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-profile-integrity-probe`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-saved-setup-creation-probe`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-vehicle-purchase-probe`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-inventory-probe`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --vehicle-catalog-probe`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --audio-diagnostics-smoke --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
- `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-active-vehicle-probe`

## Garage Purchase Catalog Gate

Catalog identity is now enforced at purchase time, not only during profile audits.

Shared service:

- `Data/GarageCatalogIdentityIndex.cs`

Runtime purchase rule:

- `GarageShopService.PurchasePart` loads the shared known-id universe.
- If the catalog identity index has load warnings, the purchase is rejected instead of allowing a potentially stale shop transaction.
- If the requested part id is not in the known part/tune/fuel universe, the purchase is rejected before price lookup.
- Price rows remain economy data only; they cannot create valid parts by themselves.

Current probe:

- `--garage-inventory-probe` writes a temporary price catalog containing `missing_shop_part`, marks that id as purchasable in a temporary profile, and verifies `PurchasePart` still rejects it because no real catalog entry exists.

## Vehicle Purchase Price Integrity

Vehicle price rows are now bound to purchase-car template identity, not only loose ids.

Runtime purchase rule:

- `GarageShopService.PurchaseVehicle` opens the requested source JSON and requires `role: purchase_car_stock`.
- It resolves the purchase-car assembly through `VehicleAssemblyResolver` before cloning it into an owned vehicle.
- Warning-level vehicle or engine resolver messages reject the purchase-car template.
- `Data/Garage/vehicle_prices.json` must contain a row where both `purchaseCarId` and normalized `path` match the purchase-car template.
- A price row with the right id but wrong path is rejected.
- A price row with the right path but wrong id is rejected.
- Purchase-car JSON remains the assembled showroom source of truth; vehicle price rows remain economy metadata only.

Current probe:

- `--garage-vehicle-purchase-probe` buys two EK9 stock purchase cars normally, verifies first purchase active-vehicle initialization, verifies later purchase does not steal active state, rejects owned-vehicle templates, then verifies both vehicle price mismatch directions are rejected.

## Engine Audio Identity And Sample Generation Readiness

Race sample audio now exposes structured build identity instead of only loop file paths.

Runtime source:

- `VehicleRaceSampleAudioBuilder.Build`
- `ResolvedEngineAssembly`
- `VehicleAudioParameters`

Resolved audio identity fields:

- engine audio DSP id/display name
- engine audio profile id/path
- source recording path
- generation method
- generated sample-set path
- sample generation key
- engine id/code/family
- authored engine combination id when present
- block/head ids
- valvetrain
- tune id
- fuel id
- displacement and compression
- VTEC state and activation rpm

Sample generation key shape:

```text
engineId__factory-or-combinationId__blockId__headId__tuneId__fuelId__blockUpgrade__headUpgrade__displacement__ports__throttleBody__cams__intake__runnerLength__valveSprings__headers__exhaust__flywheel__clutch__engineAudioDsp
```

Reasoning:

- Future generated samples need to be tied to the exact assembled engine, not just the purchase car or chassis.
- A B16B with stock cams, 98 RON, stock exhaust and a B16B with club cams, E85, short runners, and club exhaust should resolve to different sample-generation keys.
- Frankenstein engines can use the authored combination id in the same key, keeping K24/K20 or B18/B16 hybrid sample caches separate from factory engines.
- Current runtime playback stays unchanged; this is metadata for tooling, probes, future cache lookup, and generated sample management.

Current probe:

- `--audio-probe` prints the resolved generation key and source engine identity.
- `--vehicle-catalog-probe` verifies the audio identity matches the resolved engine assembly and that current vehicles expose a generation method and generated sample-set path.

## Engine Audio Fallback Contract

The current EK9/B16B sample set is allowed to act as a temporary development fallback, but this is now explicit data rather than an invisible side effect.

Catalog fields:

- `modifies.audio.profileEngineId`
- `modifies.audio.profileEngineFamily`
- `modifies.audio.fallbackAllowed`

Runtime behavior:

- If the audio profile source engine id does not match the resolved engine id and `fallbackAllowed` is true, `VehicleAssemblyResolver` emits `engine_audio_profile_fallback` as an info message.
- If the source family does not match and `fallbackAllowed` is true, the resolver emits `engine_audio_profile_family_fallback` as an info message.
- If either mismatch occurs without `fallbackAllowed`, the resolver emits warning-level mismatch messages.

Reasoning:

- This keeps the current race audio stable while making missing engine-specific sample coverage visible.
- The game can still drive a K20A, D16Y8, or other future engine using temporary fallback samples.
- Future sample generation can search for fallback info messages or compare `EngineAudioEngineId` against `EngineAudioProfileEngineId` to find builds that need dedicated generated or recorded loops.

Current probe:

- `--vehicle-catalog-probe` reports audio fallback count in its PASS summary and verifies mismatched profile engines are only accepted when fallback is explicitly allowed.

## Engine Audio Profile Catalog

Engine audio sample coverage is now indexed separately from runtime DSP part selection.

Catalog:

- `Data/Audio/engine_audio_profile_catalog.json`

Catalog ownership:

- audio profile id/path
- source engine id/family/code
- coverage level, currently `exact`
- generation method
- generated sample-set path
- source recording provenance path
- whether the source recording is required for validation
- fallback families
- required sample roles

Validation:

- `--engine-audio-profile-catalog-probe`

The probe validates:

- catalog has at least one profile
- profile JSON exists and id matches catalog entry
- profile source engine/family matches catalog entry
- generation method matches
- generated sample-set directory exists
- required sample roles are present
- each sample path exists and loads as WAV PCM
- each sample has positive RPM metadata

Current known limitation:

- The EK9/B16B profile references the original MP3 source recording as provenance, but that MP3 is not currently present in the tree. The catalog marks it `sourceRecordingRequired: false`, so the profile remains valid because the runtime WAV loops exist and pass validation.

## Engine Audio DSP To Profile Cross-Check

The profile catalog is now linked back to the DSP parts that select profiles at runtime.

Validation:

- `--engine-audio-profile-catalog-probe`

The probe now also loads:

- `Data/Parts/Engine/engine_audio_dsp.json`

For every DSP part with `modifies.audio.engineAudioProfilePath`, it verifies:

- the profile path is registered in `Data/Audio/engine_audio_profile_catalog.json`
- `profileEngineId` matches the catalog source engine id
- `profileEngineFamily` matches the catalog source family
- `generationMethod` matches the catalog method
- `generatedSampleSetPath` matches the catalog generated sample-set path
- if `fallbackAllowed` is true, every DSP compatibility family is listed in the profile catalog fallback family list

Reasoning:

- DSP parts remain the runtime selection mechanism.
- The profile catalog remains the sample coverage and generation-readiness index.
- This cross-check prevents a DSP from silently pointing at unregistered, stale, or incorrectly described audio sample data.

## Engine Audio Coverage Matrix

The project now has a separate readiness report for engine sample coverage.

Validation:

- `--engine-audio-coverage-probe`

The probe checks:

- every factory engine in `Data/Parts/Engine/engines.json`
- every authored hybrid in `Data/Parts/Engine/engine_combinations.json`
- every assembled purchase and owned vehicle under `Data/PurchaseCars` and `Data/Garage/OwnedVehicles`

Coverage categories:

- `exact profile`: the profile catalog source engine id matches the engine or combination being evaluated.
- `fallback`: no exact profile exists, but a registered profile explicitly allows the engine family as a temporary fallback.
- `missing exact/fallback profile`: there is no registered exact profile and no approved family fallback.

Runtime vehicle rules are stricter than catalog-readiness rules:

- A vehicle's resolved `EngineAudioProfilePath` must be registered in `Data/Audio/engine_audio_profile_catalog.json`.
- A vehicle must expose a non-empty engine-audio sample generation key.
- If a vehicle uses a profile from another engine, the fallback must be explicitly allowed by the profile catalog.

Reasoning:

- Exact profile gaps are useful backlog information, not an immediate runtime failure.
- Missing or unauthorized vehicle audio profiles are failures because the game would not know which sample recipe is authoritative.
- The sample generation key is the future cache identity for generated samples built from engine id, authored combination id, block, head, tune, fuel, block/head upgrades, displacement, port work, throttle body, cams, intake, runner length, valve springs, headers, exhaust, flywheel, clutch, and engine-audio DSP selection.

## Engine Audio Generation Targets

`Data/Audio/engine_audio_generation_targets.json` is now the owned backlog for future generated or recorded sample sets.

Each target stores:

- target id, type, priority, and generation status
- desired future audio profile id/path
- target sample-set folder
- required sample roles
- an engine assembly request consumed by `EngineAssemblyResolver`

Validation:

- `--engine-audio-generation-target-probe`

The probe verifies:

- every factory engine has a generation target
- every authored engine combination has a generation target
- each target resolves cleanly through the engine assembly path
- VTEC engines request a VTEC sample role
- non-VTEC engines do not request a VTEC sample role
- covered-exact targets point at a registered profile
- every target produces a unique runtime-style sample generation key

This gives future sample generation a concrete queue while preserving the current temporary fallback behavior for gameplay.

## Engine Audio Tracked Gap Contract

The coverage matrix and generation target catalog are now linked.

`--engine-audio-coverage-probe` loads `Data/Audio/engine_audio_generation_targets.json` and treats fallback coverage as acceptable only when the exact future target is tracked.

Rules:

- A factory engine without exact profile coverage must have a matching `factory_engine` target.
- An authored combination without exact profile coverage must have a matching `authored_combination` target.
- An assembled vehicle using fallback audio must point at an engine or combination that is present in the generation target catalog.

This means fallback audio is still allowed for development, but it cannot become invisible technical debt.

## Engine Audio Target Status Consistency

Generation target status is now checked against the registered profile catalog.

Rules:

- `covered_exact` means the desired profile id must exist in `Data/Audio/engine_audio_profile_catalog.json`.
- `needs_generation` means the desired profile id must not exist yet.
- Desired profile ids and target profile paths must be unique across generation targets.
- Every registered profile must be owned by a `covered_exact` generation target.

This prevents three common drift cases:

- a profile is added but its generation target still says `needs_generation`
- a profile exists without a matching generation target
- two targets point at the same future profile identity

## Engine Audio Exactness By Generated Key

Audio profile exactness is now evaluated at two levels:

- Catalog/base coverage can still say `engine_b16b` has an exact profile.
- Assembled vehicle coverage must match the full generated sample key.

This matters because a stock B16B and a modified B16B are not the same audio target. The generated key includes tune, fuel, cams, intake, runner length, headers, and exhaust, so a B16B club/E85 build should not be treated as exact just because the source profile is also B16B.

`Data/Audio/engine_audio_generation_targets.json` now supports `engine_build` targets for this case. They are resolved through `EngineAssemblyResolver` and tracked by generated key, but they do not count as base factory-engine or authored-combination coverage.

Current explicit modified-build target:

- `audio_target_engine_b16b_club_e85`

This target keeps the modified EK9 playable on the temporary B16B reference sample while making the missing modified-engine sample set visible as owned backlog.

## Engine Audio Declared Generation Keys

Generation targets now declare the exact sample key they are expected to produce.

Field:

- `expectedGenerationKey`

Runtime relationship:

- `--engine-audio-generation-target-probe` computes the key from the target's resolved engine assembly and fails if it differs from the declared value.
- `--engine-audio-coverage-probe` uses the declared keys when deciding whether assembled vehicle audio fallbacks are tracked.

Reasoning:

- External sample-generation tools need a stable key they can read directly from JSON.
- The game still owns the canonical key formula through `VehicleRaceSampleAudioBuilder.Build`.
- The probe is the guardrail that keeps the declared JSON queue and runtime formula aligned.

## Engine Audio Expanded Generation Key

The declared generation key now represents the full assembled engine audio recipe, not only the engine core.

Included identity fields:

- engine id and factory/combination id
- block and head ids
- tune and fuel ids
- block/head upgrades
- displacement kit
- port polishing
- throttle body
- cam set
- intake and intake runner length
- valve springs
- headers and exhaust
- flywheel and clutch
- engine-audio DSP part

Reasoning:

- Engine audio and rev behavior can change from valvetrain, rotating assembly, intake/exhaust, compression/displacement, fuel/tune, flywheel, clutch, and DSP choice.
- Two builds using the same block/head/tune can still need different generated samples if their airflow, valvetrain, rotating mass, or audio DSP selection differs.
- The expanded key makes sample-generation backlog entries specific enough for future generated recordings, Frankenstein engine builds, and upgrade paths.

## Active Part Catalog Integrity Gate

The active part/tune catalog layer now has a dedicated low-level probe:

- `--part-catalog-integrity-probe`

Validated indexes:

- `Data/Parts/part_catalog_index.json`
- `Data/Parts/Engine/part_catalog_index.json`
- `Data/Tunes/Chassis/chassis_tune_index.json`
- direct engine tune/fuel catalogs under `Data/Tunes/Engine`

The probe verifies:

- indexed catalog files exist
- slot names are unique within each index
- catalog root slots match index slots when declared
- active indexed paths do not point into `Data/Legacy`
- active indexed paths do not point into old `Data/RTypeEngineProfiles`
- all active catalog ids are globally unique across vehicle, engine, tune, and fuel catalogs
- every `inherits` link resolves to an active catalog id
- retired roots `Data/RTypeEngineProfiles`, `Data/Setups`, and `Data/Tyres` do not contain live files in either the source root or runtime output root
- every installable engine catalog slot has a matching garage installed-slot mapping
- mapped engine installed-slot names are unique
- all required engine installed slots are covered by the slot map
- every installable vehicle catalog slot has a matching garage target-slot mapping
- vehicle target slots map to known assembly paths or the special paired `tyrePackage` installer
- required vehicle install slots are covered by the vehicle slot map
- `tyrePackage` records reference existing front/rear tyre compounds and front/rear tyre models from the correct catalog slot types

Packaging:

- `RType.csproj` excludes `Data/Legacy/**` from content copying. The old retired source roots are expected to stay empty or absent; any preserved reference data belongs under `Data/Legacy`.
- Empty directory shells can still exist after old builds or tooling, but they are not active data. The probe fails on files, not empty folders.

Current result:

- 36 active catalogs
- 192 unique active ids
- 10 inheritance links

Reasoning:

- Higher-level assembly probes prove selected vehicles resolve.
- This probe proves the active catalog surface itself is clean enough for future garage, setup, part swapping, and sample generation work.
- It makes legacy leaks and duplicate ids fail early before they become confusing runtime vehicle behavior.
- It also makes paired tyre-package installs explicit: individual tyre compound swaps can still appear as blocked options when they would leave model/compound compatibility mismatches, while tyre packages are the preferred player-facing path for changing all tyre-related fields together.

## Launch Vehicle Path Semantics

Runtime launch options now use `VehiclePath`, not `VehicleDefinitionPath`.

Current runtime default:

- `Data/PurchaseCars/2000_Ek9_Stock.json`

The user-facing CLI remains:

- `--vehicle <path>`

Meaning:

- The argument is an assembled purchase-car or owned-vehicle path.
- It is not intended to point at the retired monolithic `Data/Vehicles/ek9_reference_2000.json` schema.

Compatibility:

- The old EK9 reference path is still accepted by `--vehicle-assembly-probe` and redirected to `Data/PurchaseCars/2000_Ek9_Stock.json`.
- This is a migration convenience only.

Intentional legacy metadata:

- `vehicleDefinitionPath` inside the assembly/resolver layer still means old monolithic vehicle metadata.
- `ResolvedVehicleAssembly.VehicleDefinitionPath` remains as a legacy detection field.
- `--vehicle-catalog-probe` rejects active purchase/owned vehicles that still declare this field.

Reasoning:

- A purchase car is an assembled stock build.
- An owned vehicle is a mutable garage build seeded from a purchase car.
- Launch-time selection should therefore name the thing the game actually resolves: an assembled vehicle path.

## Runtime Assembly-Only Loader

`VehicleRuntimeLoader` now enforces the assembled vehicle schema for active runtime loading.

Current rule:

- known old EK9 reference path -> redirect to `Data/PurchaseCars/2000_Ek9_Stock.json`
- resolved file has `assembly` block -> load through `VehicleBuildDefinitionLoader`
- resolved file lacks `assembly` block -> fail with a clear `InvalidDataException`

Reasoning:

- Runtime gameplay should not silently consume monolithic vehicle JSON.
- A silent fallback hides missing catalog fields and makes it unclear whether the car is running from the new purchase/owned assembly model or the old reference file.
- Diagnostics can still use explicit legacy/reference loaders while migration work continues, but active runtime cannot bypass the assembly model.

Additional guardrails:

- `Data/Vehicles` is now a retired root and must not contain live files.
- `Data/Legacy` is excluded from runtime content copying.
- `--part-catalog-integrity-probe` validates both conditions.

## Legacy Vehicle Path Migration Boundary

Legacy vehicle path aliases are centralized in:

- `Data/VehiclePathMigration.cs`

Current aliases:

- `Data/VehicleBuilds/ek9_showroom_stock.json` -> `Data/PurchaseCars/2000_Ek9_Stock.json`
- `Data/Vehicles/ek9_reference_2000.json` -> `Data/PurchaseCars/2000_Ek9_Stock.json`

Consumers:

- `VehicleBuildDefinitionLoader`
- `VehicleAssemblyResolver`
- `VehicleRuntimeLoader`
- `VehicleAssemblyProbe`
- racing menu selection compatibility

Resolved identity:

- `ResolvedVehicleAssembly.BuildPath` reports the canonical resolved data path, not the legacy alias supplied by the caller.

Reasoning:

- Old commands and habits can keep working during migration.
- The compatibility debt is now isolated in one file.
- Active systems and probes no longer scatter retired path strings across loaders.

## Owned Vehicle Provenance Validation

Owned vehicles are mutable garage records, but they must keep a clean link back to their immutable purchase-car template.

Required owned-vehicle fields:

- `template.sourcePurchaseCar`
- `template.purchaseCarId`

Resolver validation:

- missing source template path is a warning
- missing purchase-car id is a warning
- source template path must resolve
- source template role must be `purchase_car_stock`
- source template must not be marked `ownership.playerOwned`
- source template must contain an `assembly` block
- source template `id` must match `template.purchaseCarId`

Reasoning:

- Career mode will seed owned vehicles from purchase cars, then mutate the owned vehicle only.
- Purchase-car templates must remain immutable catalog entries.
- If an owned vehicle loses provenance, the game can no longer reliably answer what was bought, what was modified, and what should be restored or compared.

## Assembly-Driven Race Sample Audio Builder

Active race sample audio parameter construction now lives in:

- `Data/VehicleRaceSampleAudioBuilder.cs`

This builder consumes:

- `ResolvedEngineAssembly`
- `ResolvedDrivetrainBuild`
- active engine audio profile data

It produces:

- `VehicleAudioParameters`
- the full expanded `EngineAudioSampleGenerationKey`
- gear/final-drive audio metadata
- explicit fallback/source profile metadata

Current dependency boundary:

- `VehicleBuildDefinitionLoader` calls `VehicleRaceSampleAudioBuilder.Build`.
- Audio coverage and generation target probes call `VehicleRaceSampleAudioBuilder.Build`.
- Active source contains no direct `VehicleDefinitionLoader.*` calls.

Reasoning:

- Race audio is part of the assembled vehicle runtime model.
- It should not be owned by the old monolithic vehicle definition loader.
- Keeping the generation key beside the assembly-driven audio builder makes future generated sample sets follow the actual resolved engine build, including block/head/tune/fuel/upgrades/rotating assembly/audio DSP.

## Engine Installed Part Slot Validation

Engine assembly validation now verifies that an installed part belongs to the slot it is populating.

The resolver path is:

- `Data/Parts/Engine/part_catalog_index.json` declares engine catalog slots.
- `EngineAssemblyResolver.CatalogLookup` records each item ID with its catalog slot.
- `GarageModSlotMap.EngineCatalogSlotToInstalledSlot` maps catalog slots to vehicle/engine installed slots.
- `EngineAssemblyResolver` validates every `assembly.engine.installedParts` entry against that mapping before compatibility checks continue.

Protected failure mode:

- A part ID that exists in the catalog but belongs to a different slot now produces `engine_part_slot_mismatch`.
- A new installed slot that has no slot-map entry produces `unknown_engine_installed_slot`.

Reasoning:

- The data system is moving toward player-owned vehicles where every engine component can be swapped independently.
- Compatibility checks such as family, requirements, VTEC, tune, and fuel only make sense after the resolver proves the part is installed in the correct mechanical slot.
- This keeps invalid garage builds visible without hard-failing the runtime, matching the current policy that bad data should warn and be caught by probes/catalog validation.

## Vehicle Installed Part Slot Validation

Vehicle assembly validation now applies the same catalog-slot ownership rule to non-engine vehicle parts.

The guarded build fields are:

- `assembly.chassis.bodyShell` -> `bodyShell`
- `assembly.drivetrain.gearbox` -> `gearbox`
- `assembly.drivetrain.finalDrive` -> `finalDrive`
- `assembly.drivetrain.differential` -> `differential`
- `assembly.suspension.front` and `assembly.suspension.rear` -> `suspension`
- `assembly.suspension.alignment` -> `alignment`
- `assembly.brakes.front` and `assembly.brakes.rear` -> `brakes`
- `assembly.brakes.system` -> `brakeSystem`
- `assembly.wheels.front` and `assembly.wheels.rear` -> `wheels`
- `assembly.tyres.frontCompound` and `assembly.tyres.rearCompound` -> `tyres`
- `assembly.tyres.frontModel` and `assembly.tyres.rearModel` -> `tyreModel`
- `assembly.aero.package` -> `aeroPackage`
- `assembly.swapKits.*` -> `swapKit`

Runtime diagnostics:

- `VehicleAssemblyResolver` emits `vehicle_part_slot_mismatch` for normal assembly diagnostics when a known catalog ID is in the wrong vehicle field.

Probe guard:

- `PartCatalogIntegrityProbe` scans active purchase-car and owned-vehicle JSON directly.
- This catches slot mistakes even when a bad ID would otherwise break `VehicleBuildDefinitionLoader` before full runtime validation can complete.

Reasoning:

- The modular garage model depends on each installed part being structurally correct before compatibility tags are interpreted.
- A shape-compatible coincidence should not allow a part from the wrong catalog slot to affect mass, physics, or upgrade listings.
- Vehicle-side and engine-side slot semantics are now symmetric: a known ID is not enough; it must be installed in the correct mechanical category.

## Engine Power Composition Trace

Resolved engine assemblies now expose how their final torque and engine-brake curves were composed.

Runtime field:

- `ResolvedEngineAssembly.PowerComposition`

Trace contents:

- baseline displacement and resolved displacement
- base compression and resolved compression
- displacement scale
- compression scale
- low-cam and high-cam scale
- intake and exhaust scale
- low-flow and high-flow scale
- effective fuel multiplier
- VTEC enabled/activation/transition metadata
- baseline and resolved peak drive torque
- baseline and resolved peak engine-brake torque
- engine-brake displacement/compression/inertia scales
- total engine-brake scale

Reasoning:

- The engine data model is built around hand-authored real-ish baseline torque curves per engine.
- Parts, tune, and fuel then modify those baselines.
- Without a trace, probes can only inspect the final curve and cannot prove which factors drove the result.
- With a trace, stock builds should read as x1.000 against their own baseline, while modified builds expose the exact scaling chain.

Current verified examples:

- Stock EK9/B16B: baseline peak and resolved peak match with x1.000 displacement/compression/flow/fuel scales.
- Modified EK9/B16B: same B16B baseline with visible displacement, compression, fuel, and flow gains.
- K20A EK9 swap: K20A baseline peak remains the source; it does not inherit the B16B torque baseline.

Probe guard:

- `--engine-power-composer-probe` validates trace math on a synthetic modified VTEC fixture.
- `--vehicle-catalog-probe` validates active purchase/owned vehicles resolve a non-empty baseline peak trace and that trace peaks match the final resolved curves.

## Vehicle Mass Resolution Trace

Resolved vehicle assemblies now expose how final mass, CG, front/rear distribution, and yaw inertia were calculated.

Runtime field:

- `ResolvedMassProperties.Trace`

Trace contents:

- body-shell mass
- bolt-on/component mass
- catalog mass before calibration residual
- calibration residual mass
- final total mass
- component count
- vertical and longitudinal mass moments
- resolved CG height
- resolved longitudinal CG
- resolved front weight distribution
- raw yaw inertia
- yaw inertia calibration scale
- calibrated yaw inertia
- final clamped yaw inertia

Reasoning:

- Vehicle mass is no longer a single hardcoded car number.
- The resolver composes mass from the body shell, engine mass components, drivetrain, swap kits, suspension, brakes, wheels, tyres, and calibration residual.
- Future owned vehicles and garage installs need to explain how part changes affect total mass, front/rear balance, CG height, and yaw response.
- Without a trace, probes can only compare final values; with a trace, the mass pipeline is inspectable in the same way as engine power composition.

Probe guard:

- `--vehicle-catalog-probe` validates every active purchase/owned vehicle has a populated mass trace.
- The trace component count, total mass, and final yaw inertia must match the resolved runtime values.
- Empty catalog mass or raw yaw inertia is a failure.

Current verified examples:

- Stock EK9: 1060.0kg, 62.0% front, 0.480m CG height, 1450kgm2 yaw inertia.
- Modified EK9: 1052.2kg, 62.0% front, 0.468m CG height, 1440kgm2 yaw inertia.
- K20A EK9 swap: 1115.4kg, 62.3% front, 0.474m CG height, 1477kgm2 yaw inertia.

## Runtime Data Isolation Probe

Active runtime/data isolation now has a dedicated probe:

- `--runtime-data-isolation-probe`

Validated boundaries:

- active source cannot call `VehicleDefinitionLoader.*` directly outside `Data/VehicleDefinitionLoader.cs`
- retired active roots must contain no live files:
  - `Data/RTypeEngineProfiles`
  - `Data/Setups`
  - `Data/Tyres`
  - `Data/Vehicles`
- runtime output must not contain packaged `Data/Legacy` files
- old stock EK9 aliases must resolve to `Data/PurchaseCars/2000_Ek9_Stock.json`

Reasoning:

- The old monolithic vehicle loader is now reference/compatibility code, not an active gameplay fallback.
- Legacy files can remain in source history under `Data/Legacy`, but they must not be copied to runtime output.
- The stock purchase car and owned garage vehicles are the active assembled vehicle schema.

## 10-Phase Vehicle Assembly Completion Boundary

The original 10-phase vehicle assembly/data architecture plan is complete for the current scope.

Included:

- `Data/Parts/Engine` owns active engine parts.
- `Data/Tunes/Engine` owns active engine tunes and fuels.
- `Data/PurchaseCars/2000_Ek9_Stock.json` is the stock purchase-car source.
- `Data/Garage/OwnedVehicles/*.json` represents mutable owned vehicles seeded from purchase cars.
- Vehicle and engine assembly resolve through catalog/tune data rather than the retired monolithic vehicle JSON.
- Engine power starts from hand-authored baseline drive and engine-brake torque curves, then hardware/tune/fuel modifies them.
- FF drivetrain paths are implemented and tested first, with explicit compatibility tokens for future FR/AWD work.
- Chassis hard points and suspension kit behavior are split.
- Audio profile data and generation-target data provide a stable future sample contract.
- Legacy runtime-looking data roots are demoted and guarded.

Excluded:

- No sample exporter is included.
- Future Andre/Sim-Engine work means manually/offline authoring engine definitions and generated loop sets, then registering those profiles in the existing data contract.
