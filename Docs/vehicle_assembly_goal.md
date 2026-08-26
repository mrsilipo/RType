# Vehicle Assembly Goal

Last updated: 2026-08-25

The active goal is to make vehicle builds the source of truth for assembled cars.

## Agreed Direction

- Engine part catalogs live under `Data/Parts/Engine`.
- Engine tune catalogs live under `Data/Tunes/Engine`.
- A stock purchase car is represented as an assembled vehicle build, currently `Data/PurchaseCars/2000_Ek9_Stock.json`.
- Player-owned cars are separate owned-vehicle records populated from a purchased stock build, then modified during career progression. Current development fixtures are `Data/Garage/OwnedVehicles/vehicle_0001.json` for stock owned EK9 and `Data/Garage/OwnedVehicles/vehicle_0002_modified_ek9.json` for a modified owned EK9 proof.
- Engine torque starts from hand-authored real torque curves where possible, then parts and tunes modify those curves.
- Frankenstein engines are valid authored combinations when we deliberately support them, such as a K20A head on a K24A3 block or a B-series Type R head on another compatible block.
- Invalid builds should not be a player-facing hard-fail path. The catalog and mod path should prevent trap builds by offering known compatible combinations.
- FF Hondas are first, but the data should not make FR/AWD impossible later.
- Driven-wheel resolution now maps FF, FR, MR, RR, AWD, and 4WD explicitly; current content remains FF-first.
- Stock mass resolution preserves the calibrated purchase car while exposing component masses and positions for future part swaps.
- Yaw inertia is now resolved from component mass positions and shell dimensions, then calibrated by `yawInertiaCalibrationScale` on the body shell. Stock EK9 remains at the prior `1450 kgm2` feel while future mass/part swaps can move inertia through the assembled data path.
- Suspension geometry is split between chassis hard points and suspension kit adjustments.
- Chassis hard-points own the suspension architecture constants that identify the shell: suspension type, caster baseline, camber gain, toe gain, body-roll camber behavior, caster-camber behavior, and baseline travel limits.
- Suspension kits own replaceable hardware behavior: spring rate, damping, ride height, anti-roll bar rate, roll-centre height, and usable bump/droop travel.
- Engine samples may eventually be generated from Andre-style Sim Engine methodology for each engine or hybrid setup, then consumed by RType's sample engine.
- Engine audio sample sets should be generated per engine or Frankenstein engine setup when recordings/procedural generation are available; generic fallback samples are acceptable only as temporary development scaffolding.
- No sample exporter is part of this 10-phase implementation. Audio generation readiness means the data owns stable engine identities, desired future profile paths, sample roles, generation keys, and tracked gaps for manual/offline Andre-style sample preparation later.
- Fuel is engine setup/tune data, not a physical part. The first supported fuels are `fuel_98ron` and `fuel_e85`.
- `fuel_98ron` is the stock neutral baseline. `fuel_e85` is an upgrade fuel with higher safe compression and tune-dependent benefits for high compression, aggressive cams, forced induction, and adjustable cam gears.
- E85 is not treated as a simple universal power boost. Its base multiplier is neutral; extra benefit comes from compression/tune/part combinations that can exploit higher octane/ethanol content.
- Build validation should guide safe compatible paths with Info/Warning reports. Missing required catalog IDs can still fail during development, but player-facing mod paths should not create trap builds.
- The mod path should expose known compatible part choices. Invalid/risky combinations should be understood by data and validation rather than becoming random hard-fail traps for the player.
- Whole-car validation now checks chassis compatibility tags, engine-family/drivetrain-orientation tags, axle-specific installs, tyre/wheel diameter fitment, selected fuel allowance, suspension hard-point completeness, FF differential behavior, and mass resolver residuals.
- Engine slot completeness validation now checks each assembled build for required installed parts: block/head upgrades, cams, displacement/rotating assembly, port work, throttle body, intake, runner length, valve springs, headers, exhaust, flywheel, clutch, and engine audio DSP.
- The active EK9 purchase car resolves without compatibility warnings. The only current assembly info is the calibrated residual mass bucket for unmodelled stock mass.
- The stock EK9 purchase car no longer declares a `vehicleDefinitionPath`; it is an assembled purchase-car record, not a monolithic reference vehicle wrapper.
- The stock EK9 chassis metadata now uses `chassisId`, while loaders still accept the older `vehicleId` key for compatibility.
- `ResolvedVehicleAssembly` now exposes ownership/template metadata: source purchase car path/id, player-owned flag, owner profile id, and garage slot.
- Whole-car validation now distinguishes immutable purchase templates from owned vehicle records. Purchase templates should not be player-owned; owned vehicles should record their source purchase-car template.
- Vehicle-side catalogs now support `inherits` for part variants. A variant deep-merges over its base item, so upgraded body shells can keep stock wheelbase, tracks, dimensions, and suspension hard-points while overriding weight, rigidity, durability, and deltas.
- The stock EK9 shell owns `calibrationResidualMassKg`; mass resolution now preserves the stock 1060kg car while allowing inherited shell variants to change real total mass instead of being cancelled out by the residual bucket.
- Engine-side catalogs now also support `inherits` for future engine, block, head, tune, fuel, and bolt-on variants.
- Engine block/head catalogs now expose explicit compatibility rules. Blocks can list allowed head families, heads can list allowed block families and bore windows, and the resolver reports advisory warnings for unsupported Frankenstein combinations.
- Engine parts can declare requirements such as `vtecHead`; the resolver warns if a selected part conflicts with the installed head/valvetrain rather than silently producing a nonsensical build.
- Engine assembly diagnostics now prove stock B16B/98 RON resolves unchanged, under-supported high-compression builds emit advisory validation messages, and a supported high-compression E85 club-tune build resolves cleanly without warnings.
- All currently catalogued base engines now have hand-authored baseline torque curves: B16A, B16B, B18C, B18A, B18B, D16Y4, D16Y8, K20A, and K24A3.
- Factory baseline curves are neutral at stock displacement, stock compression, stock flow, and 98 RON. Part/tune/fuel modifiers now scale away from each engine's own baseline instead of using the EK9/B16B as a hidden reference.
- All currently catalogued base engines now also have baseline engine-brake torque curves. Runtime closed-throttle deceleration for assembled builds now comes from the resolved engine assembly instead of `Data/Vehicles/ek9_reference_2000.json`.
- Engine-brake scaling is relative to each engine's own baseline displacement, compression, and rotational inertia, so the stock authored curve remains unchanged while displacement, compression, and flywheel changes can alter decel behavior.
- Clutch coupling rate is now owned by the installed clutch part, alongside torque capacity and bite point. The stock clutch preserves the previous `13.0` coupling value, while upgraded clutches can alter shift/launch coupling without editing the vehicle reference file.
- Assembled runtime clutch capacity, bite point, and coupling rate now come directly from the resolved engine assembly. The bridge no longer falls back to the old EK9 reference clutch values.
- Engine tune data now resolves limiter behavior values into the assembled runtime: cut duration, restore duration, and cut torque multiplier. The bridge no longer borrows those limiter fields from `Data/Vehicles/ek9_reference_2000.json`.
- Drive torque curves and closed-throttle engine-brake curves now map directly from `ResolvedEngineAssembly` into runtime parameters. Missing curves should be fixed in catalog data instead of silently replaced by old EK9 reference curves.
- Brake axle physics now resolves from installed brake parts: disc diameter, effective radius ratio, clamp multiplier, pad friction, and total piston area derived from catalogued piston diameters. Stock EK9 front/rear piston area resolves to `22.90cm2` / `9.08cm2`.
- Tyre cornering stiffness and longitudinal stiffness are now explicit tyre model data, so runtime no longer borrows those stiffness values from `ek9_reference_2000.json`.
- Engine audio recipes are now resolved through the assembled engine path. The active EK9 stock build selects `engine_audio_stock`, which points to `Data/Audio/EngineAudioProfiles/ek9_b16b_5zigen_reference.json` and documents the source/generation method for future sample-set creation.
- Runtime vehicle loading now builds race sample audio from the resolved engine audio profile directly. The active purchase-car path no longer opens `Data/Vehicles/ek9_reference_2000.json` to construct `VehicleSimulationParameters`.
- Non-EK9 factory engine probe cases intentionally have no installed audio DSP recipe yet. They prove mechanical engine assembly works first; future engine-family sample recipes should be added as each engine gets recorded/generated loops.
- Assembled vehicle builds now explicitly disable the old procedural EngineSim physics toggles instead of inheriting them from `ek9_reference_2000.json`. Live gameplay stays on the current torque-curve race power unit and race sample audio path while archived Sim Engine files remain reference-only.

## Phase Plan Bookmark

1. Add resolved vehicle and engine assembly models.
2. Replace reference runtime loading with a real build assembly resolver.
3. Add engine assembly synthesis from block/head/parts/tune.
4. Add compatibility and mod-path validation.
5. Add mass, CG, and inertia propagation from installed parts.
6. Finalize drivetrain assembly and FF-first driven-wheel mapping.
7. Split chassis hard points from suspension kit adjustments.
8. Integrate data-driven engine audio recipes.
9. Migrate probes and diagnostics to the build assembly path.
10. Demote or remove legacy reference runtime dependencies.

## Current Phase Status

- Phase 1 through Phase 10 are complete for the current vehicle assembly/data architecture scope.
- Build, physics smoke, catalog inheritance, engine assembly, vehicle assembly, and audio probes are passing.
- The active purchase-car runtime no longer depends on `Data/Vehicles/ek9_reference_2000.json` to construct `VehicleSimulationParameters`. The old vehicle file now lives at `Data/Legacy/Vehicles/ek9_reference_2000.json` for reference only. Relative and absolute requests for the old EK9 reference path still redirect through `VehicleRuntimeLoader` to `Data/PurchaseCars/2000_Ek9_Stock.json`.
- The old `Data/VehicleBuilds/ek9_showroom_stock.json` path is retained only as a compatibility alias to `Data/PurchaseCars/2000_Ek9_Stock.json`.
- The old `Data/RTypeEngineProfiles/...` runtime-looking folder has been cleared. Part/tune catalogs moved to `Data/Parts/Engine` and `Data/Tunes/Engine`; old RType/ATG procedural reference profiles moved to `Data/Legacy/EngineProfiles`.
- The old `Data/Setups/ek9_factory.json` stage-based setup wrapper has moved to `Data/Legacy/Setups/ek9_factory.json`. Active vehicle assembly validation now uses purchase/owned vehicle build records directly.
- `--vehicle-catalog-probe` validates every JSON build in `Data/PurchaseCars` and `Data/Garage/OwnedVehicles`, ensuring purchase templates and owned vehicles resolve through the same catalog path without warnings.
- Base engine catalog records now declare `defaultInstalledParts`. The resolver applies those stock defaults first, then overlays the purchase/owned vehicle's explicit installed parts. This gives future engine swaps a complete factory starting package while keeping garage builds focused on deliberate modifications.
- Stock rotating assembly defaults are now engine-specific instead of one generic B16B displacement part. B16A, B16B, B18C, B18A, B18B, D16Y4, D16Y8, K20A, and K24A3 each have stock bore/stroke/compression/inertia entries.
- Stock flywheel and valve-spring defaults are split where needed so factory B18C/K20/K24/D-series engines preserve their own inertia and safe RPM ranges instead of inheriting B16B behavior.
- EK9 body shells now expose engine-bay compatibility metadata, including allowed transverse Honda engine families and required swap-kit slots for K-series swaps.
- K-series FF drivetrain support now exists through `stock_k20a_6_speed`, `stock_k20a_final_drive`, and `stock_k20a_helical_lsd`.
- EK9 K-series chassis-side swap-kit support now exists through `ek9_k_series_engine_mounts`, `ek9_k_series_wiring_loom`, `ek9_k_series_driveshafts`, and `ek9_k_series_shift_linkage`.
- `Data/Garage/OwnedVehicles/vehicle_0003_k20a_swap_ek9.json` is a development proof that an owned EK9 can resolve a K20A engine swap through the garage path with K-series drivetrain parts, engine default support parts, and required chassis-side swap-kit parts.
- `--vehicle-engine-swap-probe` verifies the K20A swap invariant: owned FF vehicle, K-series engine family, six-speed K drivetrain, K20A stock default rotating assembly/flywheel/valve springs, four required swap-kit parts, higher torque than stock B16B, heavier resolved mass, and no validation warnings.

## Phase 1 Checkpoint

- Active stock EK9 data now reads as a purchase car assembled from catalog IDs.
- Old reference vehicle metadata has been removed from the purchase-car JSON.
- Loader compatibility remains intact for existing old-path command-line arguments and probes.
- Required engine part slots are checked during whole-vehicle assembly validation.

## Phase 2 Checkpoint

- Engine compatibility has moved beyond simple family tags.
- Block/head compatibility is now data-driven through catalog rule objects.
- VTEC-specific part requirements are now validated against the installed head.
- `--engine-compatibility-probe` intentionally exercises unsupported combinations so compatibility warnings remain testable.

## Phase 3 Checkpoint

- Purchase cars and owned vehicles now have distinct runtime semantics.
- `Data/PurchaseCars/2000_Ek9_Stock.json` remains the immutable stock/off-the-shelf template.
- `Data/Garage/OwnedVehicles/vehicle_0001.json` is a development owned-vehicle fixture copied from the stock EK9 template.
- Owned vehicle records resolve through the same catalog assembly path as purchase cars, so future garage mods can alter parts without changing purchase templates.

## Phase 4 Checkpoint

- `Data/Garage/OwnedVehicles/vehicle_0002_modified_ek9.json` proves an owned vehicle can diverge from the stock purchase-car template without mutating `Data/PurchaseCars/2000_Ek9_Stock.json`.
- The modified fixture keeps its source purchase-car metadata, uses the same EK9 chassis, and swaps to the seam-welded club-sport shell plus E85/high-compression B16B parts and matching E85 club-sport tune.
- `--vehicle-modification-comparison-probe` compares stock purchase car output against the modified owned vehicle and asserts the expected data changes: displacement, compression, torque, engine braking, clutch capacity, fuel multiplier, body shell, mass, CG, and inertia.
- Current comparison output shows the modified EK9 resolving to `1715cc`, `12.4:1`, `190.8Nm` peak torque, `121.1Nm` peak engine braking, E85 multiplier `1.021`, `420Nm` clutch capacity, `1052.2kg` total mass, `0.468m` CG height, `-0.03pp` front-weight shift, and `1440kgm2` yaw inertia.
- The stock physics smoke suite still represents stock-car acceptance. Running it directly against the modified owned fixture currently exposes a tuning/test gap in the grass pull-away case rather than a data resolver failure.

## Phase 5 Checkpoint

- Active setup data no longer contains the old `stage_0` factory setup wrapper.
- `Data/Legacy/Setups/ek9_factory.json` preserves the old file as reference only.
- `--vehicle-catalog-probe` is now the build-agnostic assembly health check for all purchase and owned vehicle records.
- Current catalog probe result: `1` purchase car, `2` owned vehicles, `0` warnings, all resolving torque curves, engine-brake curves, mass, drivetrain, and engine audio profile data.

## Phase 6 Checkpoint

- Base engine definitions now own their stock/default installed engine support parts through `defaultInstalledParts`.
- `EngineAssemblyResolver` merges engine defaults with build overrides, so a purchase car or owned vehicle can override only changed slots.
- Bare factory engine probe cases now resolve with clutch behavior and an audio recipe instead of acting as incomplete block/head/tune stubs.
- Engine-specific stock displacement/flywheel/valve-spring data prevents defaults from flattening every future swap into B16B geometry or inertia.

## Phase 7 Checkpoint

- FF engine-swap assembly now has a first validated path.
- The EK9 shell declares B/D/K transverse engine-family support in `data.engineBay.allowedEngineFamilies`.
- K-series support declares required slots in `data.engineBay.requiredSwapKitSlotsByFamily`.
- The K20A swap garage fixture installs real catalogued swap-kit parts for mounts, wiring loom, driveshafts, and shift linkage.
- The K20A swap garage fixture resolves to `1998cc`, `206.0Nm` peak torque, `8600rpm` limiter, `1115.4kg` total mass, K-series six-speed gearbox/final drive/LSD, `23.9kg` swap-kit mass, and no warnings.
- Vehicle mass now uses `EngineAssemblyResolver.EstimatedAssemblyMassKg` instead of a duplicate explicit-part-only estimate, so engines that rely on `defaultInstalledParts` are not undercounted.
- `--vehicle-engine-swap-probe` and K-swap `--physics-smoke-test` both pass.

## Phase 8 Checkpoint

- Engine part requirements now support fuel, tune-tier, and supporting-part advisories in data.
- `displacement_pro_high_comp` requires high-octane support, recommends E85, and recommends a club-sport tune or higher.
- Club/pro cam sets now declare minimum tune-tier recommendations instead of relying only on VTEC-head checks.
- `tune_b16b_club_sport_e85` is the first explicit E85 calibration tune. It gives the club build a valid target tune without making E85 a universal power button.
- `--engine-compatibility-probe` proves under-supported high-compression 98 RON/factory-tune builds produce warnings/info, while high-compression E85 with the matching club tune and supporting valve springs resolves with zero validation messages.

## Phase 9 Checkpoint

- Authored Frankenstein engine combinations now have a first catalog at `Data/Parts/Engine/engine_combinations.json`.
- The first supported hybrid recipes are `combo_k24a3_block_k20a_head` and `combo_b18b_block_b16b_head_lsvtec`.
- `EngineAssemblyResolver` can now distinguish a deliberate supported block/head hybrid from an unapproved accidental mix.
- Supported combinations can carry their own VTEC enablement, limiter/redline modifiers, tune recommendations, and fuel/tune requirements.
- The LS/VTEC path explicitly enables VTEC from the authored combination, so a non-VTEC donor base engine can become a valid VTEC assembly only through known data.
- `--engine-compatibility-probe` now verifies supported K24/K20, supported B18B/B16B LS/VTEC, and unapproved B18A/B16B head-swap behavior.

## Phase 10 Checkpoint

- Engine power synthesis has been extracted from `EngineAssemblyResolver` into `Data/EnginePowerComposer.cs`.
- The resolver still owns JSON parsing, installed-part overlay order, and validation; the composer now owns reusable fuel multiplier, drive torque curve, and engine-brake curve math.
- The composer accepts explicit input records, so future garage previews, dyno screens, engine-room tools, and sample-generation workflows can reuse the same synthesis formulas without reparsing vehicle JSON.
- `--engine-power-composer-probe` verifies the direct formula path: E85 high-compression blend resolves to `1.021`, VTEC/high-flow torque scales upward across the rev range, and the modified-style engine-brake fixture resolves to `121.1Nm` peak braking torque.

## Phase 11 Checkpoint

- Engine assemblies now expose detailed mass components through `ResolvedEngineAssembly.MassComponents`.
- Vehicle mass resolution expands engine block, head, flywheel, block/head upgrades, intake, exhaust, clutch, and other engine-part deltas into chassis-space mass components instead of treating every engine as one opaque lump.
- Stock accessory mass remains baseline-included so the calibrated showroom EK9 stays at `1060.0kg`, `62.0%` front, `0.480m` CG height, and `1450kgm2` yaw inertia.
- Upgrade parts now carry `weightDeltaKg` where appropriate. Negative deltas are valid for lighter headers, exhausts, runner choices, and port work.
- The stock body shell now owns an explicit `bodyMassCenterY` and `bodyMassCenterLongitudinalMeters`. This keeps stock calibrated while allowing modified part mass to move resolved CG, front/rear distribution, and yaw inertia naturally.
- `--vehicle-assembly-probe` now prints engine mass component count/sum and the largest engine component contributions.
- The modified EK9 now resolves to `15` engine mass components, `71.2kg` engine mass, `1052.2kg` total mass, `0.468m` CG height, `-0.03pp` front-weight shift, and `1440kgm2` yaw inertia.

## Phase 12 Checkpoint

- Garage mod-path filtering now has a first resolver at `Data/EngineModPathResolver.cs`.
- The resolver starts from a purchase or owned vehicle build, reads `assembly.engine`, resolves the current engine, then evaluates candidate changes by cloning that engine node and passing each candidate back through `EngineAssemblyResolver`.
- A mod option is selectable when the resolved candidate produces no warning-level validation messages. Info-level messages remain visible as advice, such as retune recommendations, known Frankenstein combination notices, or non-factory head notes.
- Engine part catalogs, engine tunes, fuels, and authored engine combinations all feed the same report shape through `EngineModPathReport` and `EngineModOption`.
- `fuels.json` does not need to declare a root slot; the catalog browser can infer `fuel`, `engineTune`, `engineCombination`, and other catalog slots from the array type it loads.
- Mod reports now expose explicit `Ready`, `Advisory`, and `Blocked` buckets plus per-slot groups. `Ready` means selectable with no messages, `Advisory` means selectable with info-level guidance, and `Blocked` means one or more warning-level validation issues.
- Mod options also expose tier, category, installed/current state, preview displacement, compression, limiter RPM, and peak torque. This is the first garage-friendly shape for future install menus.
- Authored Frankenstein recipes now carry their own tiers so they can be sorted/unlocked like other modification paths without pretending they are ordinary bolt-on parts.
- `--engine-mod-path-probe` verifies the stock EK9 and modified EK9 paths. Stock 98 RON/factory tune blocks high-compression race displacement with warnings, shows E85 with a retune advisory, and exposes the LS/VTEC recipe as an authored advisory combination. The modified E85 club-sport build accepts the high-compression path without warnings and rejects downgrading back to 98 RON while high compression is installed.
- `Data/VehicleModPathResolver.cs` is now the first whole-vehicle garage entry point. It wraps the current resolved vehicle assembly, current validation health, and the engine mod-path report. Suspension, brakes, tyres, drivetrain, aero, and swap-kit groups should be added beside the engine report in future phases rather than queried through separate UI-specific services.
- `--vehicle-mod-path-probe` verifies that stock and modified EK9 builds are warning-clean, expose ready/advisory/blocked engine option buckets, mark installed engine options, and preserve purchase-car versus owned-vehicle semantics.
- The whole-vehicle mod report now includes first-pass non-engine option groups for gearbox, final drive, differential, front/rear suspension, alignment, front/rear brakes, brake system, front/rear wheels, front/rear tyres, and aero packages.
- Non-engine options are evaluated by cloning the full build, changing one assembly slot, resolving through `VehicleAssemblyResolver`, then subtracting validation codes already present on the current build. This prevents baseline advisory messages, such as calibrated residual mass, from polluting every candidate option.
- Single tyre compound swaps are currently blocked when they leave the existing tyre model incompatible. This is intentional until tyre package options can change compound and tyre model together.
- Gearbox upgrade records now contain complete runtime fields for reverse ratio, auto/manual shift timing, downshift over-rev tolerance, mechanical over-rev limit, over-rev braking, and shock duration.

## Phase 13 Checkpoint

- Garage tyre upgrades now have explicit package data at `Data/Parts/Tyres/tyre_packages.json`.
- Tyre packages change `frontCompound`, `rearCompound`, `frontModel`, and `rearModel` together. This keeps compound/model compatibility intact and gives the garage a player-facing tyre install path.
- The root vehicle part index now includes the `tyrePackage` catalog.
- `VehicleModPathResolver` evaluates tyre packages by cloning the full vehicle build, applying all four tyre fields, resolving through `VehicleAssemblyResolver`, and reporting the same `Ready / Advisory / Blocked / Installed` status as other vehicle options.
- Single compound-only tyre options remain in the report and stay blocked when they would leave the old tyre model attached. This is retained as useful diagnostic behavior, not as the intended player-facing path.
- Current package IDs are `tyre_package_sports_hard_ek9`, `tyre_package_sports_medium_balanced`, and `tyre_package_semi_slick_aggressive`.
- `--vehicle-mod-path-probe` verifies that stock and modified EK9 builds mark the sports-hard package installed while exposing sports-medium and semi-slick packages as ready options.

## Phase 14 Checkpoint

- Purchase-to-owned vehicle creation now has a first backend service at `Data/GarageVehicleFactory.cs`.
- `GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar` clones a purchase-car template into an owned vehicle record, changes only identity, template, ownership, display name, and notes, and preserves the complete assembly section for runtime resolution.
- `GarageVehicleFactory.SaveOwnedVehicle` writes the owned vehicle JSON to a caller-provided output directory without overwriting existing files unless explicitly allowed.
- `--garage-vehicle-factory-probe` creates a temporary owned EK9 from `Data/PurchaseCars/2000_Ek9_Stock.json`, resolves it through `VehicleAssemblyResolver`, verifies ownership/template metadata, verifies stock B16B assembly data copied correctly, checks for zero warning-level validation, and confirms the purchase template hash is unchanged.
- This formalizes the career-mode direction: purchase-car records remain immutable showroom/catalog templates; owned vehicle records are the mutable garage state that future mods, mileage, wear, and saved setups will attach to.

## Phase 15 Checkpoint

- Owned vehicle modification now has a first backend write service at `Data/GarageModInstaller.cs`.
- `VehicleModPathResolver` and `EngineModPathResolver` remain the preview/filtering path. `GarageModInstaller` reuses those reports before writing, so install rules come from the same resolver warnings and advisory messages that the garage UI will show.
- Garage option slot mapping is centralized in `Data/GarageModSlotMap.cs`. The engine resolver, vehicle resolver, and installer now use the same catalog-slot-to-assembly-slot definitions instead of maintaining separate local tables.
- Garage installs are restricted to `role: owned_vehicle` records with `ownership.playerOwned: true`. Purchase-car stock templates are refused by design.
- `Ready` and `Advisory` options can install by default. `Blocked` options are rejected unless an explicit developer override is added by the caller.
- Engine installs support normal installed-part slots, tune changes, selected fuel changes, and authored engine combinations. Engine combinations update `engineId`, `blockId`, `headId`, and `combinationId` from `Data/Parts/Engine/engine_combinations.json`.
- Vehicle installs support drivetrain, suspension, brakes, wheels, aero, and axle tyre slots. Tyre packages are handled as a multi-field write to `assembly.tyres.frontCompound`, `rearCompound`, `frontModel`, and `rearModel`.
- `GarageModInstallResult` now includes a receipt record carrying install time, vehicle/source identity, owner/garage slot, option slot/id, before/after engine/fuel/tune, before/after peak torque, and before/after mass/weight distribution. This is intentionally profile/economy-ready without implementing cash or inventory yet.
- `--garage-mod-installer-probe` creates a temporary owned EK9, proves blocked high-compression displacement cannot install on stock 98 RON/factory calibration, installs E85, installs a sports-medium tyre package, installs a club-sport LSD, then installs the high-compression displacement path after fuel support is present.
- The probe also attempts to install E85 directly into `Data/PurchaseCars/2000_Ek9_Stock.json` and verifies the purchase template hash remains unchanged.

## Phase 16 Checkpoint

- Garage profile and inventory ownership now have a first data fixture at `Data/Garage/Profiles/dev_profile.json`.
- `Data/GarageProfileLoader.cs` loads profile id/display name/credits, owned vehicle references, owned part ids, purchasable part ids, and locked part ids.
- `Data/GarageInventoryModPathResolver.cs` wraps `VehicleModPathResolver` with player-profile inventory state. It preserves the underlying build validation status, then adds player-facing availability buckets: `Installed`, `OwnedReady`, `Purchasable`, `Locked`, `NotOwned`, and `BlockedByBuild`.
- `GarageModInstaller.ApplyProfileOwnedOption` is the first profile-aware install entry point. It refuses already-installed options, resolver-blocked options, locked options, not-owned options, and purchasable-but-not-owned options.
- This keeps the design clean: catalog/resolver data answers "can this part physically fit and resolve?", while the profile/inventory layer answers "does this player own or have access to this part?"
- `--garage-inventory-probe` creates a temporary owned EK9 and temporary profile, verifies owned E85 can install, verifies a purchasable engine audio DSP is refused before purchase, verifies the same part can install after purchase, verifies a locked semi-slick tyre package is refused, and verifies high-compression displacement remains blocked by build validation until the supporting setup is present.

## Phase 17 Checkpoint

- A first shop transaction layer now exists at `Data/GarageShopService.cs`.
- Temporary economy prices live in `Data/Garage/part_prices.json`. This keeps engineering catalogs focused on physical part data while the garage/economy layer owns credit costs.
- `GarageShopService.PurchasePart` loads a profile, refuses already-owned parts, refuses locked parts, refuses non-purchasable parts, requires a defined shop price, checks available credits, subtracts the price, appends the part id to `inventory.ownedPartIds`, and records a `transactionHistory` entry.
- `GarageModInstaller.ApplyProfileOwnedOption` can now be used after a successful shop purchase because the profile inventory has been updated.
- `--garage-inventory-probe` now validates the full first loop: purchasable-but-unowned install is refused, the part is purchased for credits, the part becomes `OwnedReady`, then it installs through the normal profile-owned path with no override. Duplicate purchase and locked-part purchase attempts are also rejected.

## Phase 18 Checkpoint

- Vehicle purchase transactions now use the same shop service through `GarageShopService.PurchaseVehicle`.
- Temporary purchase-car prices live in `Data/Garage/vehicle_prices.json`. Purchase-car JSON remains the assembled showroom source of truth; vehicle price data stays in the garage/economy layer.
- Buying a vehicle loads the profile, verifies the source JSON is a `purchase_car_stock` template, checks the price and credits, allocates the next `vehicle_####` id and first free garage slot, creates the owned vehicle through `GarageVehicleFactory`, saves it under the caller-provided owned-vehicle directory, appends the vehicle reference to `profile.ownedVehicles`, subtracts credits, and writes a `vehicle_purchase` transaction history entry.
- The owned vehicle generated by purchase resolves through `VehicleAssemblyResolver` immediately, proving the purchase path creates a physics-ready assembled car rather than a loose profile entry.
- `--garage-vehicle-purchase-probe` buys two stock EK9 templates into a temporary profile, verifies `vehicle_0001` and `vehicle_0002` allocation, verifies garage slots 1 and 2, verifies credits move from `40000 -> 21500 -> 3000`, verifies two profile vehicle references and transaction entries, rejects an insufficient-credit third purchase, rejects using an owned vehicle as a purchase template, and confirms the stock purchase template hash is unchanged.

## Phase 19 Checkpoint

- Saved setup overlays now have a first data path separate from permanent hardware installs.
- `Data/Garage/SavedSetups/vehicle_0001_track_day_setup.json` is the first setup fixture. It targets `vehicle_0001` and overrides only tune-like selections: engine tune id, selected fuel, alignment id, steering setup id, and handling setup id.
- `Data/Garage/Profiles/dev_profile.json` now references saved setups through `savedSetups`, keeping setup ownership attached to the player profile instead of the purchase-car template.
- `GarageProfileLoader` now loads saved setup references.
- `Data/GarageSavedSetupResolver.cs` loads a saved setup, verifies it belongs to the selected profile and owned vehicle, clones the owned vehicle JSON to a temporary overlay file, applies the setup overrides, and resolves that overlay through `VehicleAssemblyResolver`. The owned vehicle JSON is not mutated.
- Chassis tune records now use catalog inheritance: street/club/pro steering setups inherit from `stock_ek9_steering_setup`, and street/club/pro handling setups inherit from `stock_ek9_arcade_handling_setup`. This lets setup records select partial override tunes without missing required runtime fields.
- `--garage-saved-setup-probe` verifies a stock owned EK9 keeps its stock assembly on disk while the setup overlay resolves with `street_sport_alignment`, `street_quick_steering_setup`, and `club_sport_arcade_handling_setup`.

## Phase 20 Checkpoint

- Saved setup editing now has a backend write service at `Data/GarageSavedSetupEditor.cs`.
- The editor writes only the saved setup JSON, not the owned vehicle JSON. Permanent hardware installs remain owned-vehicle assembly changes through `GarageModInstaller`; tune-like session/setup changes remain saved-setup overrides.
- Editable setup fields are deliberately whitelisted to `overrides.engine.tuneId`, `overrides.engine.fuelSelected`, `overrides.suspension.alignment`, `overrides.tuning.steering`, and `overrides.tuning.handling`.
- `GarageSavedSetupResolver.ResolveWithSetupFile` validates an arbitrary setup file against a real profile-owned vehicle. The editor uses this to write a temporary candidate setup, resolve the full vehicle overlay through `VehicleAssemblyResolver`, reject warning-producing changes, then commit only after validation succeeds.
- `--garage-saved-setup-editor-probe` verifies valid setup edits update alignment/steering/handling, verifies the resolved overlay sees those changes, verifies the owned vehicle file hash is unchanged, verifies invalid setup edits are rejected, and verifies failed edits do not partially write the setup file.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationSetupEdit`
  - `dotnet bin\VerificationSetupEdit\RType.dll --garage-saved-setup-editor-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --garage-saved-setup-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --garage-inventory-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --vehicle-mod-path-probe`
  - `dotnet bin\VerificationSetupEdit\RType.dll --physics-smoke-test`

## Phase 21 Checkpoint

- Active saved setup selection now has a profile write service at `Data/GarageSavedSetupActivationService.cs`.
- `SetActiveSetup` validates the target setup by resolving it against the selected profile-owned vehicle before writing to the profile. It then marks that setup active and clears other active setups for the same vehicle.
- `ClearActiveSetup` marks all saved setups for the selected vehicle inactive, returning runtime loading to the owned vehicle's permanent assembly state.
- `Data/GarageRuntimeVehicleResolver.cs` is the first profile-aware runtime selector. It loads a garage profile, selects an owned vehicle, applies the active saved setup by default, supports an explicit setup id/path, and supports `setup = none` to bypass setup overlays.
- `VehicleRuntimeLoader` now has a profile-aware overload. Runtime callers can stay on direct `--vehicle` JSON loading or opt into garage selection by passing profile/vehicle/setup arguments.
- `GameLaunchOptions` now accepts:
  - `--garage-profile <path>`
  - `--garage-vehicle <vehicle id or path>`
  - `--garage-setup active|none|<setup id or path>`
- `RacingGame` and `RTypeEngineRoomScreen` now use the profile-aware runtime path when a garage profile is supplied. Existing menu car selection and direct purchase-car loading remain unchanged when no profile is supplied.
- `--garage-active-setup-probe` creates a temporary owned EK9 with two saved setups, verifies the initially active setup resolves, switches the active setup, verifies runtime loading selects the new setup, rejects a missing setup without partially writing the profile, clears the active setup, verifies runtime loading returns to the owned vehicle's stock assembly, and verifies the owned vehicle file hash is unchanged.
- Current validation for this checkpoint:
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

## Phase 22 Checkpoint

- Garage profiles now support a first-class `activeVehicleId` field.
- `Data/Garage/Profiles/dev_profile.json` sets `activeVehicleId` to `vehicle_0001`, making the development garage runtime path explicit instead of relying on implicit first-slot selection.
- `GarageProfileLoader` now loads `activeVehicleId`.
- `GarageRuntimeVehicleResolver` now selects the profile's active vehicle by default when no `--garage-vehicle` argument is supplied. If no active vehicle is declared, it still falls back to the first garage slot.
- `Data/GarageActiveVehicleService.cs` is the profile write path for active vehicle selection. It validates the owned vehicle resolves cleanly before writing `activeVehicleId`, and it can clear the active vehicle selection to return to first-slot fallback.
- `--garage-active-vehicle-probe` creates a temporary profile with two owned EK9s, verifies default runtime selection uses `activeVehicleId`, switches active vehicle, rejects a missing vehicle without partially writing the profile, clears active vehicle selection, verifies first-slot fallback, and verifies owned vehicle JSON files are not mutated.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationActiveVehicle`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --garage-active-vehicle-probe`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --garage-active-setup-probe`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --garage-saved-setup-editor-probe`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --garage-inventory-probe`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationActiveVehicle\RType.dll --garage-vehicle-purchase-probe`

## Phase 23 Checkpoint

- Vehicle purchases now initialize garage active vehicle selection.
- `GarageShopService.PurchaseVehicle` sets `activeVehicleId` to the newly bought vehicle when the profile does not already have an active vehicle. This makes first-car purchase immediately runnable through the profile runtime path.
- Later vehicle purchases preserve the existing active vehicle selection. Buying another car adds it to the garage, but does not silently switch the current race/engine-room car.
- Vehicle purchase transaction history now records `becameActiveVehicle` so career/profile UI can explain when a purchase changed the active car state.
- `GarageVehiclePurchaseResult` exposes `BecameActiveVehicle`.
- `--garage-vehicle-purchase-probe` now verifies first purchase initializes `activeVehicleId`, second purchase preserves it, transaction history records both states, and the purchase-car template remains unchanged.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationPurchaseActive`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --garage-active-vehicle-probe`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --garage-active-setup-probe`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --garage-inventory-probe`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --garage-saved-setup-editor-probe`
  - `dotnet bin\VerificationPurchaseActive\RType.dll --vehicle-catalog-probe`

## Phase 24 Checkpoint

- Saved setup creation now has a backend service at `Data/GarageSavedSetupCreationService.cs`.
- `CreateFromOwnedVehicle` snapshots tune-like selections from an owned vehicle's current assembly into a new saved setup file: engine tune id, selected fuel id, alignment id, steering setup id, and handling setup id.
- The service validates the owned vehicle before creating a setup, writes a temporary setup candidate, resolves it through `GarageSavedSetupResolver.ResolveWithSetupFile`, rejects warning-producing candidates, then writes the setup file and registers it under `profile.savedSetups`.
- Setup ids are allocated per owned vehicle using `{vehicleId}_setup_###`.
- Setup creation can optionally make the new setup active. When it does, other saved setups for that same vehicle are marked inactive. It does not affect active setups for other vehicles.
- Creating a setup never mutates the owned vehicle assembly JSON.
- `--garage-saved-setup-creation-probe` creates a temporary owned EK9, snapshots a non-active setup, snapshots a second active setup, verifies profile registration and active flag behavior, verifies runtime loading selects the active setup, rejects a missing owned vehicle, and verifies the owned vehicle file hash is unchanged.
- Current validation for this checkpoint:
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

## Phase 25 Checkpoint

- Garage profile integrity auditing now has a non-mutating validator at `Data/GarageProfileIntegrityValidator.cs`.
- The validator checks the whole garage profile as a runnable save state: active vehicle id, owned vehicle references, duplicate vehicle ids, duplicate garage slots, owned vehicle ownership metadata, resolver warning health, saved setup references, active setup counts, setup owner/vehicle identity, setup resolver health, and owned/locked inventory conflicts.
- The validator reports `Info` and `Warning` messages and exposes `IsClean`, so future menus can block racing only on real profile health issues while still surfacing advisory context.
- `--garage-profile-integrity-probe` verifies a clean profile reports zero warnings and an intentionally broken profile catches bad active vehicle id, duplicate owned vehicle ids, duplicate garage slots, wrong owned-vehicle owner, duplicate setup ids, multiple active setups, setup owner mismatch, setup resolve failure, setup id mismatch, setup targeting a non-owned vehicle, and an owned+locked inventory conflict.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationGarageIntegrity`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-profile-integrity-probe`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-saved-setup-creation-probe`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-active-vehicle-probe`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --launch-probe --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
  - `dotnet bin\VerificationGarageIntegrity\RType.dll --audio-diagnostics-smoke --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`

## Phase 26 Checkpoint

- Garage profile integrity validation now checks inventory ids against the known catalog universe.
- The known-id universe is built from `Data/Parts/part_catalog_index.json`, `Data/Parts/Engine/part_catalog_index.json`, `Data/Tunes/Engine/engine_tunes.json`, and `Data/Tunes/Engine/fuels.json`.
- The validator now flags owned, locked, or concrete purchasable part ids that are not present in any known part/tune/fuel catalog.
- Concrete purchasable ids must also have a price in `Data/Garage/part_prices.json`. The wildcard purchasable marker `*` is intentionally exempt because it means "catalog-visible when priced/available", not a concrete buyable item.
- New warning codes include `inventory_owned_part_missing_catalog`, `inventory_locked_part_missing_catalog`, `inventory_purchasable_part_missing_catalog`, and `inventory_purchasable_part_missing_price`.
- `--garage-profile-integrity-probe` now covers missing owned/locked/purchasable catalog ids and a concrete purchasable fuel with no price entry.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationGarageCatalogIntegrity`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-profile-integrity-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-saved-setup-creation-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-inventory-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --physics-smoke-test --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --audio-diagnostics-smoke --garage-profile Data/Garage/Profiles/dev_profile.json --garage-setup active`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-active-vehicle-probe`

## Phase 27 Checkpoint

- Catalog identity loading is now shared through `Data/GarageCatalogIdentityIndex.cs` instead of being private to the profile validator.
- `GarageProfileIntegrityValidator` still reports catalog index and catalog load warnings, but now consumes the shared identity index.
- `GarageShopService.PurchasePart` now rejects stale purchasable ids before reading their price. A profile cannot buy a part just because it is listed as purchasable and has a stale price row; it must also exist in the real part/tune/fuel catalog universe.
- `--garage-inventory-probe` now creates a temporary stale price catalog for `missing_shop_part` and verifies the purchase path rejects it even though the profile marks it purchasable and the temporary price file gives it a price.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationGarageCatalogIntegrity`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-inventory-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-profile-integrity-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationGarageCatalogIntegrity\RType.dll --vehicle-catalog-probe`

## Phase 28 Checkpoint

- Vehicle purchase pricing is now stricter and tied to the purchase-car template identity.
- `GarageShopService.PurchaseVehicle` now resolves the source purchase-car assembly before purchase and rejects warning-producing purchase cars before cloning them into owned vehicles.
- Vehicle price rows must match both `purchaseCarId` and normalized purchase-car path. A matching id with the wrong path, or a matching path with the wrong id, no longer creates a valid purchase.
- `GarageVehiclePurchaseProbe` now covers both mismatch directions with temporary vehicle price catalogs.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationGarageVehiclePriceIntegrity`
  - `dotnet bin\VerificationGarageVehiclePriceIntegrity\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationGarageVehiclePriceIntegrity\RType.dll --garage-inventory-probe`
  - `dotnet bin\VerificationGarageVehiclePriceIntegrity\RType.dll --garage-profile-integrity-probe`
  - `dotnet bin\VerificationGarageVehiclePriceIntegrity\RType.dll --vehicle-catalog-probe`

## Phase 29 Checkpoint

- Resolved race audio parameters now carry explicit engine assembly identity for future sample-generation workflows.
- `VehicleAudioParameters` now exposes the selected engine audio DSP id/display name, generation method, generated sample-set path, sample generation key, engine id/code/family/combination id, block/head ids, valvetrain, tune id, fuel id, displacement, compression, VTEC state, and VTEC activation rpm.
- `VehicleRaceSampleAudioBuilder.Build` builds this metadata directly from `ResolvedEngineAssembly`, so the audio identity follows the same block/head/tune/fuel/part composition as vehicle physics.
- The sample generation key includes engine id, factory or authored combination id, block, head, tune, fuel, cams, intake, runner length, headers, and exhaust. This is intended as the stable bridge for future Andre/Engine-Sim-style offline sample generation and per-build sample cache lookup.
- `--audio-probe` now prints the generation key, DSP id, generation method, sample-set path, and source engine identity.
- `--vehicle-catalog-probe` now verifies resolved audio identity matches the resolved engine and that current vehicles expose a generation method and generated sample-set path.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioIdentity`
  - `dotnet bin\VerificationEngineAudioIdentity\RType.dll --audio-probe`
  - `dotnet bin\VerificationEngineAudioIdentity\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationEngineAudioIdentity\RType.dll --engine-assembly-probe`
  - `dotnet bin\VerificationEngineAudioIdentity\RType.dll --vehicle-modification-comparison-probe`

## Open Questions

- Should future E85 benefits become more granular by part type, such as separate compression, cam, forced-induction, and ignition-timing modifiers?
- Should supported Frankenstein combinations eventually become selectable pseudo-engine IDs in the garage UI, or should the UI always present them as block/head recipes?
- Should race sessions be allowed to override an owned vehicle's selected fuel?
- Should fuel be an unlockable/purchasable consumable, a setup option, or both?
- Should every engine audio DSP part require a concrete sample profile before the part is selectable, or can engines temporarily fall back to the EK9/B16B recipe while awaiting generated samples?
- Should engine-family sample profiles live under `Data/Audio/EngineAudioProfiles/{family}/...` or stay flat until we have more than one complete engine family?

## Phase 30 Checkpoint

- Engine audio fallback status is now explicit in data and resolver output.
- `engine_audio_stock` declares the profile source engine/family as `engine_b16b` / `honda_b_series` and marks `fallbackAllowed: true`.
- `ResolvedEngineAssembly` and `VehicleAudioParameters` now carry `EngineAudioProfileEngineId`, `EngineAudioProfileEngineFamily`, and `EngineAudioFallbackAllowed`.
- `VehicleAssemblyResolver` reports `engine_audio_profile_fallback` or `engine_audio_profile_family_fallback` as info when a build intentionally uses a mismatched source profile. The same condition becomes a warning if fallback is not allowed.
- This makes the current development compromise visible: non-B16B engines can still run using the EK9/B16B reference samples, but probes and docs can now identify which vehicles need their own generated or recorded sample sets later.
- `--vehicle-catalog-probe` now counts audio fallbacks in its PASS summary and verifies that any mismatched source profile has `fallbackAllowed`.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioFallback`
  - `dotnet bin\VerificationEngineAudioFallback\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationEngineAudioFallback\RType.dll --engine-assembly-probe`
  - `dotnet bin\VerificationEngineAudioFallback\RType.dll --audio-probe`
  - `dotnet bin\VerificationEngineAudioFallback\RType.dll --vehicle-engine-swap-probe`

## Phase 31 Checkpoint

- Engine audio sample profile coverage now has a catalog at `Data/Audio/engine_audio_profile_catalog.json`.
- The catalog records profile id/path, source engine id/family/code, coverage level, generation method, generated sample-set path, source recording provenance, fallback families, and required sample roles.
- `--engine-audio-profile-catalog-probe` validates profile JSON identity, profile source engine/family, generation method, generated sample-set folder, required sample roles, sample WAV paths, sample RPM metadata, and loadable WAV frame data.
- Source recordings can be optional provenance. The current EK9 MP3 source path is missing from the tree, so the probe reports it as `1 missing optional source recordings` while staying strict on the actual runtime WAV loop assets.
- Current catalog truth: `ek9_b16b_5zigen_reference` is the only exact profile; it covers `engine_b16b` and allows temporary fallback for Honda B, D, and K families.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioProfileCatalog`
  - `dotnet bin\VerificationEngineAudioProfileCatalog\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioProfileCatalog\RType.dll --audio-probe`
  - `dotnet bin\VerificationEngineAudioProfileCatalog\RType.dll --vehicle-catalog-probe`

## Phase 32 Checkpoint

- Engine audio DSP parts are now cross-checked against the audio profile coverage catalog.
- `--engine-audio-profile-catalog-probe` now loads `Data/Parts/Engine/engine_audio_dsp.json` and verifies any DSP part that declares `engineAudioProfilePath` points at a registered profile in `Data/Audio/engine_audio_profile_catalog.json`.
- The probe also verifies DSP source engine id, source family, generation method, and generated sample-set path match the registered profile.
- If a DSP marks `fallbackAllowed: true`, every family in its compatibility list must also be listed in the profile catalog's `fallbackAllowedForFamilies`.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioProfileCatalog`
  - `dotnet bin\VerificationEngineAudioProfileCatalog\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioProfileCatalog\RType.dll --audio-probe`
  - `dotnet bin\VerificationEngineAudioProfileCatalog\RType.dll --vehicle-catalog-probe`

## Phase 33 Checkpoint

- Engine audio readiness now has a coverage matrix probe at `--engine-audio-coverage-probe`.
- The coverage probe checks every factory engine in `Data/Parts/Engine/engines.json`, every authored hybrid in `Data/Parts/Engine/engine_combinations.json`, and every purchase/owned vehicle build under `Data/PurchaseCars` and `Data/Garage/OwnedVehicles`.
- Factory engines and authored combinations are reported as exact profile, family fallback, or missing exact/fallback profile. Missing exact profiles are counted as generation backlog, not a runtime failure, while the B16B exact EK9 profile proves the current reference path.
- Assembled vehicles are stricter: each resolved vehicle must point at a registered profile, must have an engine-audio sample generation key, and any profile mismatch must be explicitly allowed by the profile fallback family list.
- Current truth remains deliberate: `ek9_b16b_5zigen_reference` is exact for `engine_b16b`; other current B/D/K engines and supported hybrids can temporarily fall back to the EK9/B16B profile until their own generated/recorded sample sets exist.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioCoverage`
  - `dotnet bin\VerificationEngineAudioCoverage\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioCoverage\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioCoverage\RType.dll --vehicle-catalog-probe`

## Phase 34 Checkpoint

- Engine audio generation backlog is now data-owned at `Data/Audio/engine_audio_generation_targets.json`.
- The target catalog defines the exact engine assembly requests that need sample generation. Each target includes target type, priority, status, desired profile id/path, target sample-set path, required sample roles, and an `engine` request resolved through `EngineAssemblyResolver`.
- Current targets cover all nine factory engines and both authored Frankenstein combinations.
- `audio_target_engine_b16b_factory` is marked `covered_exact` and points to the existing EK9/B16B profile.
- All other factory engines and authored hybrids are marked `needs_generation`, giving us explicit future work instead of relying on a console-only fallback count.
- `--engine-audio-generation-target-probe` validates target ids, target types, status values, required roles, clean engine resolution, VTEC/non-VTEC sample-role consistency, unique generated sample keys, and full coverage of current factory engines/authored combinations.
- The probe generates sample keys through `VehicleRaceSampleAudioBuilder.Build`, so generation targets use the same key shape as race runtime audio.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioGenerationTargets`
  - `dotnet bin\VerificationEngineAudioGenerationTargets\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineAudioGenerationTargets\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioGenerationTargets\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioGenerationTargets\RType.dll --vehicle-catalog-probe`

## Phase 35 Checkpoint

- The coverage matrix now cross-checks audio profile gaps against `Data/Audio/engine_audio_generation_targets.json`.
- `--engine-audio-coverage-probe` no longer reports fallback coverage as passive backlog only. Every non-exact factory engine, authored combination, or assembled vehicle fallback must have a matching generation target.
- Factory engine fallback/missing coverage is tracked by a `factory_engine` target whose engine request resolves that engine id.
- Authored hybrid fallback/missing coverage is tracked by an `authored_combination` target whose engine request resolves that combination id.
- Assembled vehicle fallbacks are also checked against the same target sets, so a runtime vehicle can temporarily use a fallback sample profile only if its exact future sample target is explicitly owned in data.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioTrackedGaps`
  - `dotnet bin\VerificationEngineAudioTrackedGaps\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioTrackedGaps\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineAudioTrackedGaps\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioTrackedGaps\RType.dll --vehicle-catalog-probe`

## Phase 36 Checkpoint

- Engine audio generation targets now validate profile/status consistency.
- `--engine-audio-generation-target-probe` now rejects duplicate desired profile ids and duplicate target profile paths.
- Targets marked `needs_generation` must not already have their desired profile registered. If a profile exists, the target should be promoted to `covered_exact` with matching metadata.
- Every registered audio profile must be owned by a `covered_exact` generation target. This keeps `Data/Audio/engine_audio_profile_catalog.json` and `Data/Audio/engine_audio_generation_targets.json` moving together.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioTargetStatus`
  - `dotnet bin\VerificationEngineAudioTargetStatus\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineAudioTargetStatus\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioTargetStatus\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioTargetStatus\RType.dll --vehicle-catalog-probe`

## Phase 37 Checkpoint

- Vehicle audio coverage now uses the full sample generation key for exactness, not just the source engine id.
- This fixes an important false positive: a modified B16B with E85, club cams, high-compression displacement, intake, runner, header, and exhaust is no longer treated as exact just because the current reference profile source is also `engine_b16b`.
- `Data/Audio/engine_audio_generation_targets.json` now includes `audio_target_engine_b16b_club_e85` as an `engine_build` target for the modified B16B club/E85 sample set.
- `engine_build` targets are resolved through `EngineAssemblyResolver` like all other targets, but they do not satisfy factory-engine or authored-combination catalog coverage. They represent tuned/modified engine setups that need their own generated sample set.
- `--engine-audio-coverage-probe` now resolves generation target keys and considers an assembled vehicle exact only when its runtime sample generation key is present in a `covered_exact` target.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioKeyCoverage`
  - `dotnet bin\VerificationEngineAudioKeyCoverage\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineAudioKeyCoverage\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioKeyCoverage\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioKeyCoverage\RType.dll --vehicle-catalog-probe`

## Phase 38 Checkpoint

- Engine audio generation targets now declare `expectedGenerationKey` directly in `Data/Audio/engine_audio_generation_targets.json`.
- `--engine-audio-generation-target-probe` resolves each target through `EngineAssemblyResolver` and `VehicleRaceSampleAudioBuilder.Build`, then verifies the resolver-computed key exactly matches the declared `expectedGenerationKey`.
- `--engine-audio-coverage-probe` now reads the declared generation keys from target data when checking whether vehicle fallback/backlog audio is tracked.
- This gives future sample-generation tooling a concrete key to consume from JSON while still protecting against drift through the resolver-backed probe.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioDeclaredKeys`
  - `dotnet bin\VerificationEngineAudioDeclaredKeys\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineAudioDeclaredKeys\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioDeclaredKeys\RType.dll --engine-audio-profile-catalog-probe`
  - `dotnet bin\VerificationEngineAudioDeclaredKeys\RType.dll --vehicle-catalog-probe`

## Phase 39 Checkpoint

- Engine audio sample generation keys now include the full engine audio recipe, not only engine id, block/head, tune/fuel, cams, intake, runner length, headers, and exhaust.
- The key now also includes block upgrade, head upgrade, displacement kit, port polishing, throttle body, valve springs, flywheel, clutch, and engine-audio DSP selection.
- `Data/Audio/engine_audio_generation_targets.json` now declares expanded `expectedGenerationKey` values for every tracked factory engine, authored combination, and modified B16B club/E85 build.
- This makes generated sample identity specific enough for future engine builds where rotating mass, valvetrain, airflow, fuel/tune, and DSP choice alter the sound or rev character.
- Current validation for this checkpoint:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineAudioExpandedKeys`
  - `dotnet bin\VerificationEngineAudioExpandedKeys\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineAudioExpandedKeys\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineAudioExpandedKeys\RType.dll --audio-probe`
  - `dotnet bin\VerificationEngineAudioExpandedKeys\RType.dll --vehicle-catalog-probe`

## Phase 40 Checkpoint

- Added `--part-catalog-integrity-probe` as a low-level active catalog guardrail.
- The probe validates all active part/tune catalog indexes:
  - `Data/Parts/part_catalog_index.json`
  - `Data/Parts/Engine/part_catalog_index.json`
  - `Data/Tunes/Chassis/chassis_tune_index.json`
  - direct engine tune/fuel catalogs under `Data/Tunes/Engine`
- It verifies indexed files exist, index slots are unique, catalog root slots agree with index slots when declared, active paths do not point into `Data/Legacy` or old `Data/RTypeEngineProfiles`, all active ids are globally unique, and every `inherits` link resolves to an active catalog id.
- `RType.csproj` now excludes `Data/Legacy/**` from content copying. The probe checks both source-root and runtime-output locations and fails if retired active-looking roots contain live files. Empty directory shells are tolerated because they do not carry active data.
- The probe now also validates the engine catalog slot map used by garage installs. Installable slots from `Data/Parts/Engine/part_catalog_index.json` must be present in `GarageModSlotMap.EngineCatalogSlotToInstalledSlot`, mapped installed-slot names must be unique, and every required installed engine slot must be covered.
- Vehicle-side catalog slot mapping is now validated too. Installable slots from `Data/Parts/part_catalog_index.json` must be present in `GarageModSlotMap.VehicleCatalogSlotTargets`, target slots must map to known assembly paths or the special `tyrePackage` paired installer, and required vehicle install slots must be covered.
- `tyrePackage` records are validated as paired installs: each package must reference existing front/rear tyre compounds and front/rear tyre models, and those references must point at the correct catalog slot types.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationPartCatalogIntegrity`
  - `dotnet bin\VerificationPartCatalogIntegrity\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationPartCatalogIntegrity\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationPartCatalogIntegrity\RType.dll --engine-assembly-probe`
  - `dotnet bin\VerificationPartCatalogIntegrity\RType.dll --engine-mod-path-probe`
  - `dotnet bin\VerificationPartCatalogIntegrity\RType.dll --vehicle-mod-path-probe`
  - `dotnet bin\VerificationPartCatalogIntegrity\RType.dll --engine-audio-profile-catalog-probe`

## Phase 41 Checkpoint

- Launch-time vehicle selection now uses `GameLaunchOptions.VehiclePath` and `GameLaunchOptions.DefaultVehiclePath`.
- The default launch vehicle is the assembled stock purchase car:
  - `Data/PurchaseCars/2000_Ek9_Stock.json`
- The CLI flag remains `--vehicle`, but it now semantically points at an assembled purchase/owned vehicle path rather than an old monolithic vehicle definition.
- `Data/Vehicles/ek9_reference_2000.json` is still accepted as a compatibility input and is redirected by `--vehicle-assembly-probe` to the stock purchase car. This keeps old test habits from breaking while the runtime data model moves forward.
- `ResolvedVehicleAssembly.VehicleDefinitionPath` and loader-side `vehicleDefinitionPath` metadata remain intentionally named for now because they describe legacy JSON metadata and are used by probes to reject active purchase/owned vehicles that still declare monolithic vehicle-definition references.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationLaunchVehiclePath`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --vehicle-assembly-probe`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --vehicle-assembly-probe --vehicle Data/Vehicles/ek9_reference_2000.json`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --engine-assembly-probe`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --audio-diagnostics-smoke`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --physics-smoke-test --auto-exit-ms 1`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationLaunchVehiclePath\RType.dll --vehicle-mod-path-probe`

## Phase 42 Checkpoint

- `VehicleRuntimeLoader` is now assembly-only for active runtime loading.
- It still redirects the known old EK9 reference path to `Data/PurchaseCars/2000_Ek9_Stock.json`, but after path resolution it requires the loaded JSON to contain an `assembly` block.
- If an arbitrary old monolithic vehicle JSON reaches `VehicleRuntimeLoader`, it now fails with a clear error instead of silently falling back to `VehicleDefinitionLoader`.
- Active gameplay/runtime callers now route through `VehicleRuntimeLoader` or through garage runtime resolution. The old `VehicleDefinitionLoader.LoadSimulationParameters` path is no longer used directly by active runtime code.
- Legacy definition parsing remains available for explicit diagnostic/reference helpers such as race sample audio parameter construction while the final migration is still underway.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationRuntimeAssemblyOnly`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --vehicle-assembly-probe`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --vehicle-assembly-probe --vehicle Data/Vehicles/ek9_reference_2000.json`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --physics-smoke-test --auto-exit-ms 1`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --audio-diagnostics-smoke`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --garage-active-vehicle-probe`
  - `dotnet bin\VerificationRuntimeAssemblyOnly\RType.dll --garage-saved-setup-probe`

## Phase 43 Checkpoint

- Resolved-engine race sample audio construction moved out of the legacy `VehicleDefinitionLoader` class.
- Added `VehicleRaceSampleAudioBuilder` as the active assembly-driven builder for `VehicleAudioParameters`.
- `VehicleBuildDefinitionLoader`, `--vehicle-catalog-probe`, `--engine-audio-coverage-probe`, and `--engine-audio-generation-target-probe` now call `VehicleRaceSampleAudioBuilder.Build`.
- The sample generation key builder now lives beside the race sample audio builder as `VehicleRaceSampleAudioBuilder.BuildSampleGenerationKey`.
- The builder preserves existing engine audio profile schema support, including numeric values stored directly or inside `{ "value": ... }` objects.
- Current source audit result:
  - no active code references to `VehicleDefinitionLoader.LoadSimulationParameters`
  - no active code references to `VehicleDefinitionLoader.LoadRaceSampleAudioParameters`
  - no active code references to `VehicleDefinitionLoader.*`
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationAudioBuilderSplit2`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --audio-probe`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --vehicle-assembly-probe`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --physics-smoke-test --auto-exit-ms 1`
  - `dotnet bin\VerificationAudioBuilderSplit2\RType.dll --garage-mod-installer-probe`

## Phase 44 Checkpoint

- Moved the old monolithic EK9 reference file out of the active `Data/Vehicles` root:
  - from `Data/Vehicles/ek9_reference_2000.json`
  - to `Data/Legacy/Vehicles/ek9_reference_2000.json`
- `Data/Vehicles` no longer carries an active-looking vehicle definition file.
- `Data/Legacy/Setups/ek9_factory.json` now points at the legacy vehicle location.
- `RType.csproj` excludes `Data/Legacy/**/*` from runtime content copying. Legacy reference JSON remains in source for audit/history, but it is not shipped as active game data.
- Compatibility redirects for the old EK9 path remain in code:
  - `Data/Vehicles/ek9_reference_2000.json` still resolves to `Data/PurchaseCars/2000_Ek9_Stock.json` for runtime/probe convenience.
- `--part-catalog-integrity-probe` now guards this boundary:
  - `Data/Vehicles` must not contain live files.
  - runtime output must not contain `Data/Legacy` files.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationLegacyVehicleDemoted2`
  - `dotnet bin\VerificationLegacyVehicleDemoted2\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationLegacyVehicleDemoted2\RType.dll --vehicle-assembly-probe --vehicle Data/Vehicles/ek9_reference_2000.json`
  - `dotnet bin\VerificationLegacyVehicleDemoted2\RType.dll --vehicle-catalog-probe`
  - runtime output check: `bin\VerificationLegacyVehicleDemoted2\Data\Legacy` absent

## Phase 45 Checkpoint

- Centralized legacy vehicle path aliases in `VehiclePathMigration`.
- The only source-code references to these retired paths now live in that migration helper:
  - `Data/VehicleBuilds/ek9_showroom_stock.json`
  - `Data/Vehicles/ek9_reference_2000.json`
- `VehicleBuildDefinitionLoader`, `VehicleAssemblyResolver`, and `VehicleRuntimeLoader` now call the shared migration helper instead of each carrying their own hardcoded alias logic.
- `--vehicle-assembly-probe` and the racing car picker use the same helper for old EK9 reference-path compatibility.
- `ResolvedVehicleAssembly.BuildPath` now reports the canonical resolved data path. Passing the old `Data/VehicleBuilds/ek9_showroom_stock.json` alias resolves and prints `Data/PurchaseCars/2000_Ek9_Stock.json`, not the retired alias.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationVehiclePathMigration2`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --vehicle-assembly-probe --vehicle Data/VehicleBuilds/ek9_showroom_stock.json`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --vehicle-assembly-probe --vehicle Data/Vehicles/ek9_reference_2000.json`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --physics-smoke-test --auto-exit-ms 1`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --audio-probe`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationVehiclePathMigration2\RType.dll --garage-active-vehicle-probe`

## Phase 46 Checkpoint

- Owned vehicle provenance validation is now stricter in `VehicleAssemblyResolver`.
- Owned vehicles must record:
  - `template.sourcePurchaseCar`
  - `template.purchaseCarId`
- Missing provenance is now a warning, not passive info. Because `--vehicle-catalog-probe` fails on warnings, checked-in owned vehicles cannot silently lose their purchase-car origin.
- If `template.sourcePurchaseCar` resolves, the resolver now validates that the source:
  - has role `purchase_car_stock`
  - is not marked `ownership.playerOwned`
  - contains an `assembly` block
  - has an `id` matching the owned vehicle's `template.purchaseCarId`
- This protects the future career model: purchase cars stay immutable templates, and owned vehicles stay mutable garage records seeded from a known purchase car.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationOwnedVehicleProvenance`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --garage-vehicle-purchase-probe`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --garage-active-vehicle-probe`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --garage-saved-setup-probe`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --vehicle-assembly-probe --vehicle Data\Garage\OwnedVehicles\vehicle_0001.json`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationOwnedVehicleProvenance\RType.dll --physics-smoke-test --auto-exit-ms 1`

## Phase 47 Checkpoint

- Engine installed-part validation now checks both identity and slot ownership in `EngineAssemblyResolver`.
- A known engine part ID is no longer enough to be accepted in any installed slot. The resolver records each catalog item's source catalog slot, maps that through `GarageModSlotMap.EngineCatalogSlotToInstalledSlot`, and warns when the resolved installed slot does not match the slot being populated.
- Example protected case: `"cams": "flywheel_stock"` now emits `engine_part_slot_mismatch` instead of resolving silently because `flywheel_stock` exists.
- Unknown installed engine slots emit `unknown_engine_installed_slot`, keeping future slot additions visible until the garage slot map is deliberately extended.
- `--engine-compatibility-probe` now includes a synthetic invalid build for the flywheel-in-cam-slot case.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineSlotGuard`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --engine-compatibility-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --engine-assembly-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --vehicle-mod-path-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --engine-mod-path-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationEngineSlotGuard\RType.dll --physics-smoke-test --auto-exit-ms 1`

## Phase 48 Checkpoint

- Vehicle-side installed part validation now mirrors the engine slot guard.
- `VehicleAssemblyResolver` records each vehicle catalog item's source slot and warns with `vehicle_part_slot_mismatch` if a build installs a known catalog ID into the wrong assembly field.
- Covered assembly paths include body shell, gearbox, final drive, differential, front/rear suspension, alignment, front/rear brakes, brake system, front/rear wheels, front/rear tyre compounds, front/rear tyre models, aero package, and swap-kit entries.
- `--part-catalog-integrity-probe` now scans `Data/PurchaseCars` and `Data/Garage/OwnedVehicles` directly to ensure checked-in build JSON references the expected catalog slot before runtime shape assumptions can hide a bad install.
- This protects the future garage/career model: owned vehicle JSON can be hand-authored or mutated by installer code, but a catalog ID must still live in the correct mechanical slot before compatibility, fitment, mass, and physics resolution are trusted.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationVehicleSlotGuardFinal2`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --vehicle-mod-path-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --engine-assembly-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --engine-compatibility-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationVehicleSlotGuardFinal2\RType.dll --physics-smoke-test --auto-exit-ms 1`

## Phase 49 Checkpoint

- Resolved engine assemblies now carry an explicit power-composition trace.
- `EnginePowerComposer.ResolveCompositionTrace` exposes the major scale factors used to convert hand-authored baseline curves into the final runtime curves:
  - baseline and resolved displacement
  - base and resolved compression
  - displacement scale
  - compression scale
  - low-cam and high-cam scale
  - intake and exhaust scale
  - low-flow and high-flow scale
  - effective fuel multiplier
  - VTEC activation/transition metadata
  - baseline and resolved peak drive torque
  - baseline and resolved peak engine-brake torque
  - engine-brake displacement/compression/inertia scales
- `ResolvedEngineAssembly.PowerComposition` makes this trace available to vehicle, garage, audio generation, and diagnostic systems.
- `--engine-power-composer-probe` now asserts that the trace preserves the baseline peak, matches the resolved torque curve peak, exposes displacement/high-cam flow gains, and matches the engine-brake scale.
- `--vehicle-catalog-probe` now fails active purchase/owned vehicles if the engine resolves without a baseline peak torque trace or if the trace peaks drift from the actual resolved drive/engine-brake curves.
- `--engine-assembly-probe` and `--vehicle-assembly-probe` print the composition trace so stock cars, modified cars, and swaps can be audited without reverse-engineering the curve math from final values alone.
- Verified behavior:
  - stock EK9 resolves from B16B baseline at x1.000 scales
  - modified EK9 resolves from the B16B baseline with displacement/compression/fuel/flow gains
  - K20A swap resolves from its own K20A baseline rather than the EK9/B16B baseline
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationEngineCompositionTraceFinal`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --engine-power-composer-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --vehicle-mod-path-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationEngineCompositionTraceFinal\RType.dll --physics-smoke-test --auto-exit-ms 1`

## Phase 50 Checkpoint

- Resolved vehicle mass now carries an explicit mass-resolution trace.
- `VehicleMassResolver` still resolves the actual physics mass, front/rear distribution, CG height, CG longitudinal position, and yaw inertia from assembled catalog components, but now records the major intermediate values used to get there.
- `ResolvedMassProperties.Trace` exposes:
  - body-shell mass
  - bolt-on/component mass
  - catalog mass before residual calibration
  - calibration residual mass
  - final total mass
  - component count
  - vertical and longitudinal mass moments
  - resolved CG height and longitudinal CG
  - resolved front weight distribution
  - raw yaw inertia
  - yaw inertia calibration scale
  - calibrated yaw inertia
  - final clamped yaw inertia
- `--vehicle-assembly-probe` now prints mass and yaw traces for stock, owned, modified, and swapped vehicles.
- `--vehicle-catalog-probe` now fails active purchase/owned vehicles when:
  - trace component count does not match resolved components
  - trace total mass does not match resolved total mass
  - trace final yaw inertia does not match resolved yaw inertia
  - trace catalog mass is empty
  - trace raw yaw inertia is empty
- Verified behavior:
  - stock EK9 resolves to 1060.0kg, 62.0% front, 0.480m CG height, 1450kgm2 yaw inertia
  - modified EK9 resolves lighter/lower at 1052.2kg, 0.468m CG height, 1440kgm2 yaw inertia
  - K20A swap resolves heavier at 1115.4kg, 62.3% front, 1477kgm2 yaw inertia
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationMassTraceResume`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --vehicle-assembly-probe --vehicle Data\PurchaseCars\2000_Ek9_Stock.json`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --vehicle-assembly-probe --vehicle Data\Garage\OwnedVehicles\vehicle_0002_modified_ek9.json`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --vehicle-assembly-probe --vehicle Data\Garage\OwnedVehicles\vehicle_0003_k20a_swap_ek9.json`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --vehicle-mod-path-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --garage-mod-installer-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --vehicle-modification-comparison-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --engine-power-composer-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --engine-audio-generation-target-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --engine-audio-coverage-probe`
  - `dotnet bin\VerificationMassTraceResume\RType.dll --physics-smoke-test --auto-exit-ms 1`

## Phase 51 Checkpoint

- Added `--runtime-data-isolation-probe` as a dedicated guard for the active runtime/data boundary.
- The probe fails if active source code directly calls `VehicleDefinitionLoader.*` outside the retired loader file itself.
- The probe fails if retired active roots contain live files:
  - `Data/RTypeEngineProfiles`
  - `Data/Setups`
  - `Data/Tyres`
  - `Data/Vehicles`
- The probe fails if `Data/Legacy` files are packaged into the runtime output.
- The probe verifies old stock EK9 aliases still resolve to `Data/PurchaseCars/2000_Ek9_Stock.json`.
- Current validation result:
  - `dotnet build RType.csproj --no-restore -o bin\VerificationRuntimeIsolation`
  - `dotnet bin\VerificationRuntimeIsolation\RType.dll --runtime-data-isolation-probe`
  - `dotnet bin\VerificationRuntimeIsolation\RType.dll --part-catalog-integrity-probe`
  - `dotnet bin\VerificationRuntimeIsolation\RType.dll --vehicle-catalog-probe`
  - `dotnet bin\VerificationRuntimeIsolation\RType.dll --vehicle-assembly-probe --vehicle Data\Vehicles\ek9_reference_2000.json`

Reasoning:

- `VehicleDefinitionLoader` can remain as an explicit reference/compatibility parser while migration work continues, but active gameplay, probes, garage runtime, and car selection should resolve assembled purchase/owned vehicles.
- Legacy data should be source-reference only. It must not quietly ship into runtime output or become an accidental fallback path.

## Original 10-Phase Completion Audit

Status: complete for the current vehicle assembly/data architecture scope.

Scope boundary:

- This completion does not include a sample exporter.
- Future Andre/Sim-Engine work remains manual/offline setup work for creating engine-specific source recordings or loops.
- RType now owns the data contract those future samples must satisfy: resolved engine identity, desired profile path, sample roles, generation key, exact/fallback coverage state, and tracked backlog entries.

Completion evidence:

- Phase 1: resolved vehicle and engine assembly models exist through `ResolvedVehicleAssembly`, `ResolvedEngineAssembly`, `VehicleAssemblyResolver`, and `EngineAssemblyResolver`.
- Phase 2: active runtime loads assembled purchase/owned vehicles through `VehicleRuntimeLoader` and `VehicleBuildDefinitionLoader`.
- Phase 3: engine assembly synthesizes block, head, installed parts, fuel, and tune data through `EngineAssemblyResolver`.
- Phase 4: compatibility and mod-path validation is covered by engine/vehicle assembly validation, `EngineModPathResolver`, `VehicleModPathResolver`, and garage install probes.
- Phase 5: mass, CG, and yaw inertia resolve from installed part masses and expose `ResolvedMassProperties.Trace`.
- Phase 6: drivetrain data resolves from gearbox, final drive, and differential catalogs; FF remains the tested target while non-FF tokens are represented for later.
- Phase 7: chassis hard points and suspension kit adjustments are split between body shell data and suspension/alignment catalogs.
- Phase 8: engine audio recipes resolve from engine audio DSP/profile data and expose stable per-build generation keys.
- Phase 9: probes and diagnostics use the assembled build path, including vehicle catalog, assembly, garage, mod-path, mass, power, and audio checks.
- Phase 10: old runtime-looking roots and monolithic vehicle definitions are demoted to `Data/Legacy` or compatibility aliases, and `--runtime-data-isolation-probe` enforces the boundary.

Final validation result:

- `dotnet build RType.csproj --no-restore -o bin\VerificationPhase10Complete`
- `--runtime-data-isolation-probe`
- `--part-catalog-integrity-probe`
- `--vehicle-catalog-probe`
- `--vehicle-assembly-probe --vehicle Data\PurchaseCars\2000_Ek9_Stock.json`
- `--vehicle-assembly-probe --vehicle Data\Garage\OwnedVehicles\vehicle_0002_modified_ek9.json`
- `--vehicle-assembly-probe --vehicle Data\Garage\OwnedVehicles\vehicle_0003_k20a_swap_ek9.json`
- `--vehicle-engine-swap-probe`
- `--vehicle-modification-comparison-probe`
- `--engine-assembly-probe`
- `--engine-compatibility-probe`
- `--engine-power-composer-probe`
- `--engine-mod-path-probe`
- `--vehicle-mod-path-probe`
- `--engine-audio-profile-catalog-probe`
- `--engine-audio-generation-target-probe`
- `--engine-audio-coverage-probe`
- all current garage factory/install/inventory/profile/purchase/saved-setup/active-selection probes
- `--physics-smoke-test --auto-exit-ms 1`

All listed checks passed from `bin\VerificationPhase10Complete`.
