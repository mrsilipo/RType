using System.Text.Json;
using System.Text.Json.Nodes;

namespace RType.Data;

internal static class VehicleAssemblyResolver
{
    private const string VehicleCatalogIndexPath = "Data/Parts/part_catalog_index.json";

    public static ResolvedVehicleAssembly Resolve(string buildPath)
    {
        string resolvedBuildPath = ResolveDataPath(buildPath);
        using FileStream stream = File.OpenRead(resolvedBuildPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        JsonElement assembly = Require(root, "assembly");
        JsonElement chassis = Require(assembly, "chassis");
        JsonElement engine = Require(assembly, "engine");

        ResolvedVehicleBuild runtimeBuild = VehicleBuildDefinitionLoader.Load(buildPath);
        ResolvedEngineAssembly engineAssembly = EngineAssemblyResolver.Resolve(engine);
        ResolvedMassProperties massProperties = VehicleMassResolver.Resolve(runtimeBuild, engineAssembly);
        CatalogSnapshot catalogs = CatalogSnapshot.Load(VehicleCatalogIndexPath);
        VehicleAssemblyValidationMessage[] validation = Validate(root, assembly, chassis, engine, runtimeBuild, engineAssembly, massProperties, catalogs);

        return new ResolvedVehicleAssembly
        {
            BuildId = ReadString(root, Path.GetFileNameWithoutExtension(resolvedBuildPath), "id"),
            DisplayName = ReadString(root, string.Empty, "displayName"),
            BuildPath = ToDisplayDataPath(resolvedBuildPath),
            VehicleDefinitionPath = ReadString(root, string.Empty, "vehicleDefinitionPath"),
            Classification = ReadString(root, string.Empty, "role"),
            SourcePurchaseCarPath = ReadString(root, string.Empty, "template", "sourcePurchaseCar"),
            PurchaseCarId = ReadString(root, string.Empty, "template", "purchaseCarId"),
            PlayerOwned = ReadBoolean(root, false, "ownership", "playerOwned"),
            OwnerProfileId = ReadString(root, string.Empty, "ownership", "ownerProfileId"),
            GarageSlot = ReadInt32(root, 0, "ownership", "garageSlot"),
            ChassisCode = ReadString(chassis, string.Empty, "chassisCode"),
            DrivetrainLayout = ReadString(chassis, string.Empty, "drivetrainLayout"),
            BodyShellId = ReadString(chassis, string.Empty, "bodyShell"),
            RuntimeBuild = runtimeBuild,
            Mass = runtimeBuild.Mass,
            MassProperties = massProperties,
            Engine = engineAssembly,
            Validation = validation
        };
    }

    private static VehicleAssemblyValidationMessage[] Validate(
        JsonElement root,
        JsonElement assembly,
        JsonElement chassis,
        JsonElement engine,
        ResolvedVehicleBuild runtimeBuild,
        ResolvedEngineAssembly engineAssembly,
        ResolvedMassProperties massProperties,
        CatalogSnapshot catalogs)
    {
        List<VehicleAssemblyValidationMessage> messages = [];
        string role = ReadString(root, string.Empty, "role");
        if (!string.Equals(role, "purchase_car_stock", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "owned_vehicle", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "test_build", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Info("unknown_build_role", $"Build role '{role}' is not one of purchase_car_stock, owned_vehicle, or test_build."));
        }

        string vehicleDefinitionPath = ReadString(root, string.Empty, "vehicleDefinitionPath");
        if (!string.IsNullOrWhiteSpace(vehicleDefinitionPath))
        {
            messages.Add(Info("legacy_vehicle_metadata_present", $"Build still declares legacy/reference vehicle metadata: {vehicleDefinitionPath}. Active runtime should be assembled from catalogs."));
        }

        ValidateOwnershipLayer(root, role, messages);

        string chassisCode = ReadString(chassis, string.Empty, "chassisCode");
        string drivetrainLayout = ReadString(chassis, string.Empty, "drivetrainLayout").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(chassisCode))
        {
            messages.Add(Warning("missing_chassis_code", "Build chassis does not declare a chassisCode."));
        }

        if (!string.Equals(drivetrainLayout, "FF", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Info("drivetrain_layout_untested", $"Drivetrain layout {drivetrainLayout} is data-supported but not validated in gameplay yet. FF remains the active target."));
        }

        if (string.Equals(drivetrainLayout, "FF", StringComparison.OrdinalIgnoreCase) &&
            runtimeBuild.Drivetrain.DifferentialTorqueBiasRatio <= 1.05f)
        {
            messages.Add(Info("ff_open_or_low_bias_diff", $"FF build uses differential TBR {runtimeBuild.Drivetrain.DifferentialTorqueBiasRatio:0.00}; corner-exit bite will behave close to an open diff."));
        }

        ValidateCatalogCompatibility(assembly, chassisCode, drivetrainLayout, engineAssembly, catalogs, messages);
        ValidateSuspensionHardPoints(runtimeBuild, messages);

        if (runtimeBuild.Drivetrain.ForwardGearRatios.Length == 0)
        {
            messages.Add(Warning("gearbox_has_no_forward_gears", $"Gearbox {runtimeBuild.Drivetrain.GearboxId} resolved with no forward ratios."));
        }

        ValidateFuelSelection(engine, engineAssembly, messages);
        ValidateRequiredEngineSlots(engineAssembly, messages);
        ValidateAudioRecipe(engineAssembly, messages);
        ValidateMass(massProperties, messages);

        return [.. messages];
    }

    private static void ValidateCatalogCompatibility(
        JsonElement assembly,
        string chassisCode,
        string drivetrainLayout,
        ResolvedEngineAssembly engineAssembly,
        CatalogSnapshot catalogs,
        List<VehicleAssemblyValidationMessage> messages)
    {
        string chassisToken = NormalizeToken(chassisCode);
        string drivetrainToken = DrivetrainCompatibilityToken(drivetrainLayout);
        ValidateInstalledVehicleCatalogSlots(assembly, catalogs, messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "chassis", "bodyShell"), "body shell", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "drivetrain", "gearbox"), "gearbox", [engineAssembly.Family, drivetrainToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "drivetrain", "finalDrive"), "final drive", [engineAssembly.Family, drivetrainToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "drivetrain", "differential"), "differential", [engineAssembly.Family, drivetrainToken], messages);
        ValidateEngineBayCompatibility(
            catalogs,
            assembly,
            ReadString(assembly, string.Empty, "chassis", "bodyShell"),
            chassisToken,
            drivetrainToken,
            engineAssembly,
            messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "suspension", "front"), "front suspension", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "suspension", "rear"), "rear suspension", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "suspension", "alignment"), "alignment", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "brakes", "front"), "front brakes", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "brakes", "rear"), "rear brakes", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "brakes", "system"), "brake system", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "wheels", "front"), "front wheel", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "wheels", "rear"), "rear wheel", [chassisToken], messages);
        ValidateCompatibility(catalogs, ReadString(assembly, string.Empty, "aero", "package"), "aero package", [chassisToken], messages);
        ValidateAxle(catalogs, ReadString(assembly, string.Empty, "suspension", "front"), "front suspension", "front", messages);
        ValidateAxle(catalogs, ReadString(assembly, string.Empty, "suspension", "rear"), "rear suspension", "rear", messages);
        ValidateAxle(catalogs, ReadString(assembly, string.Empty, "brakes", "front"), "front brakes", "front", messages);
        ValidateAxle(catalogs, ReadString(assembly, string.Empty, "brakes", "rear"), "rear brakes", "rear", messages);
        ValidateWheelTyreFitment(assembly, catalogs, messages);
    }

    private static void ValidateInstalledVehicleCatalogSlots(
        JsonElement assembly,
        CatalogSnapshot catalogs,
        List<VehicleAssemblyValidationMessage> messages)
    {
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "chassis", "bodyShell"), "chassis.bodyShell", "bodyShell", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "drivetrain", "gearbox"), "drivetrain.gearbox", "gearbox", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "drivetrain", "finalDrive"), "drivetrain.finalDrive", "finalDrive", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "drivetrain", "differential"), "drivetrain.differential", "differential", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "suspension", "front"), "suspension.front", "suspension", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "suspension", "rear"), "suspension.rear", "suspension", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "suspension", "alignment"), "suspension.alignment", "alignment", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "brakes", "front"), "brakes.front", "brakes", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "brakes", "rear"), "brakes.rear", "brakes", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "brakes", "system"), "brakes.system", "brakeSystem", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "wheels", "front"), "wheels.front", "wheels", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "wheels", "rear"), "wheels.rear", "wheels", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "tyres", "frontCompound"), "tyres.frontCompound", "tyres", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "tyres", "rearCompound"), "tyres.rearCompound", "tyres", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "tyres", "frontModel"), "tyres.frontModel", "tyreModel", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "tyres", "rearModel"), "tyres.rearModel", "tyreModel", messages);
        ValidateInstalledVehicleCatalogSlot(catalogs, ReadString(assembly, string.Empty, "aero", "package"), "aero.package", "aeroPackage", messages);

        foreach ((string slot, string partId) in ReadStringMap(assembly, "swapKits"))
        {
            ValidateInstalledVehicleCatalogSlot(catalogs, partId, $"swapKits.{slot}", "swapKit", messages);
        }
    }

    private static void ValidateInstalledVehicleCatalogSlot(
        CatalogSnapshot catalogs,
        string partId,
        string assemblyPath,
        string expectedCatalogSlot,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(partId) || !catalogs.TryGetSlot(partId, out string actualCatalogSlot))
        {
            return;
        }

        if (!actualCatalogSlot.Equals(expectedCatalogSlot, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning("vehicle_part_slot_mismatch", $"Part {partId} is installed at assembly.{assemblyPath}, which expects catalog slot {expectedCatalogSlot}, but the part belongs to catalog slot {actualCatalogSlot}."));
        }
    }

    private static void ValidateEngineBayCompatibility(
        CatalogSnapshot catalogs,
        JsonElement assembly,
        string bodyShellId,
        string chassisToken,
        string drivetrainToken,
        ResolvedEngineAssembly engineAssembly,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(bodyShellId) || !catalogs.TryGet(bodyShellId, out JsonElement bodyShell))
        {
            return;
        }

        string[] allowedFamilies = ReadStringArray(bodyShell, "data", "engineBay", "allowedEngineFamilies");
        if (allowedFamilies.Length > 0 &&
            !allowedFamilies.Any(family => family.Equals(engineAssembly.Family, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Warning("engine_family_not_allowed_in_body_shell", $"Body shell {bodyShellId} does not list engine family {engineAssembly.Family} as an allowed engine-bay family."));
        }

        string[] swapKitFamilies = ReadStringArray(bodyShell, "data", "engineBay", "requiresSwapKitForFamilies");
        if (swapKitFamilies.Any(family => family.Equals(engineAssembly.Family, StringComparison.OrdinalIgnoreCase)))
        {
            string[] requiredSlots = ReadRequiredSwapKitSlots(bodyShell, engineAssembly.Family);
            IReadOnlyDictionary<string, string> installedSwapKits = ReadStringMap(assembly, "swapKits");

            if (requiredSlots.Length == 0)
            {
                messages.Add(Info("engine_family_requires_swap_kit", $"Body shell {bodyShellId} can accept {engineAssembly.Family}, but no detailed swap-kit slot list is declared yet."));
            }
            else
            {
                ValidateRequiredSwapKits(catalogs, installedSwapKits, requiredSlots, chassisToken, drivetrainToken, engineAssembly.Family, messages);
            }
        }

        float maxDisplacementCc = ReadSingle(bodyShell, 0f, "data", "engineBay", "maxDisplacementCcWithoutBodyModification");
        if (maxDisplacementCc > 0f && engineAssembly.DisplacementCc > maxDisplacementCc)
        {
            messages.Add(Warning("engine_displacement_exceeds_body_shell_limit", $"Engine {engineAssembly.EngineId} resolves to {engineAssembly.DisplacementCc:0}cc, above body shell {bodyShellId} limit {maxDisplacementCc:0}cc without body modification."));
        }
    }

    private static void ValidateRequiredSwapKits(
        CatalogSnapshot catalogs,
        IReadOnlyDictionary<string, string> installedSwapKits,
        IReadOnlyList<string> requiredSlots,
        string chassisToken,
        string drivetrainToken,
        string engineFamily,
        List<VehicleAssemblyValidationMessage> messages)
    {
        foreach (string slot in requiredSlots)
        {
            if (!installedSwapKits.TryGetValue(slot, out string? partId) || string.IsNullOrWhiteSpace(partId))
            {
                messages.Add(Warning("required_swap_kit_missing", $"Engine family {engineFamily} requires swap-kit slot {slot}, but the vehicle does not install one."));
                continue;
            }

            if (!catalogs.TryGet(partId, out JsonElement part))
            {
                messages.Add(Warning("required_swap_kit_part_missing", $"Swap-kit slot {slot} references missing catalog id {partId}."));
                continue;
            }

            string declaredSlot = ReadString(part, string.Empty, "data", "slot");
            if (!declaredSlot.Equals(slot, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(Warning("swap_kit_slot_mismatch", $"Swap-kit part {partId} is installed in slot {slot}, but declares slot {declaredSlot}."));
            }

            ValidateCompatibility(catalogs, partId, $"swap kit {slot}", [chassisToken, engineFamily, drivetrainToken, slot], messages);
        }
    }

    private static void ValidateOwnershipLayer(
        JsonElement root,
        string role,
        List<VehicleAssemblyValidationMessage> messages)
    {
        bool playerOwned = ReadBoolean(root, false, "ownership", "playerOwned");
        string sourcePurchaseCar = ReadString(root, string.Empty, "template", "sourcePurchaseCar");
        string purchaseCarId = ReadString(root, string.Empty, "template", "purchaseCarId");

        if (string.Equals(role, "purchase_car_stock", StringComparison.OrdinalIgnoreCase) && playerOwned)
        {
            messages.Add(Warning("purchase_template_marked_player_owned", "Purchase-car templates should not be marked player-owned."));
        }

        if (string.Equals(role, "owned_vehicle", StringComparison.OrdinalIgnoreCase))
        {
            if (!playerOwned)
            {
                messages.Add(Warning("owned_vehicle_not_marked_player_owned", "Owned vehicle record should set ownership.playerOwned to true."));
            }

            if (string.IsNullOrWhiteSpace(sourcePurchaseCar))
            {
                messages.Add(Warning("owned_vehicle_source_template_missing", "Owned vehicle does not record the purchase-car template it was created from."));
            }
            else if (!CanResolveDataPath(sourcePurchaseCar))
            {
                messages.Add(Warning("owned_vehicle_source_template_missing_file", $"Owned vehicle source purchase car was not found: {sourcePurchaseCar}."));
            }
            else
            {
                ValidateOwnedVehicleSourcePurchaseCar(sourcePurchaseCar, purchaseCarId, messages);
            }

            if (string.IsNullOrWhiteSpace(purchaseCarId))
            {
                messages.Add(Warning("owned_vehicle_purchase_car_id_missing", "Owned vehicle does not record its source purchase-car id."));
            }
        }
    }

    private static void ValidateOwnedVehicleSourcePurchaseCar(
        string sourcePurchaseCar,
        string purchaseCarId,
        List<VehicleAssemblyValidationMessage> messages)
    {
        string resolvedSourcePath = ResolveDataPath(sourcePurchaseCar);
        using FileStream stream = File.OpenRead(resolvedSourcePath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        string sourceRole = ReadString(root, string.Empty, "role");
        string sourceId = ReadString(root, string.Empty, "id");
        bool sourcePlayerOwned = ReadBoolean(root, false, "ownership", "playerOwned");

        if (!sourceRole.Equals("purchase_car_stock", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning("owned_vehicle_source_not_purchase_car", $"Owned vehicle source {sourcePurchaseCar} has role '{sourceRole}', expected purchase_car_stock."));
        }

        if (sourcePlayerOwned)
        {
            messages.Add(Warning("owned_vehicle_source_marked_player_owned", $"Owned vehicle source {sourcePurchaseCar} is marked player-owned."));
        }

        if (!root.TryGetProperty("assembly", out _))
        {
            messages.Add(Warning("owned_vehicle_source_missing_assembly", $"Owned vehicle source {sourcePurchaseCar} does not contain an assembly block."));
        }

        if (!string.IsNullOrWhiteSpace(purchaseCarId) &&
            !sourceId.Equals(purchaseCarId, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning("owned_vehicle_purchase_car_id_mismatch", $"Owned vehicle records purchaseCarId {purchaseCarId}, but source template id is {sourceId}."));
        }
    }

    private static void ValidateWheelTyreFitment(
        JsonElement assembly,
        CatalogSnapshot catalogs,
        List<VehicleAssemblyValidationMessage> messages)
    {
        string frontWheelId = ReadString(assembly, string.Empty, "wheels", "front");
        string rearWheelId = ReadString(assembly, string.Empty, "wheels", "rear");
        string frontTyreId = ReadString(assembly, string.Empty, "tyres", "frontCompound");
        string rearTyreId = ReadString(assembly, string.Empty, "tyres", "rearCompound");
        string frontTyreModelId = ReadString(assembly, string.Empty, "tyres", "frontModel");
        string rearTyreModelId = ReadString(assembly, string.Empty, "tyres", "rearModel");

        ValidateTyreFitsWheel(catalogs, frontTyreId, frontWheelId, "front", messages);
        ValidateTyreFitsWheel(catalogs, rearTyreId, rearWheelId, "rear", messages);
        ValidateCompatibility(catalogs, frontTyreModelId, "front tyre model", [frontTyreId], messages);
        ValidateCompatibility(catalogs, rearTyreModelId, "rear tyre model", [rearTyreId], messages);
        ValidateAxle(catalogs, frontTyreModelId, "front tyre model", "front", messages);
        ValidateAxle(catalogs, rearTyreModelId, "rear tyre model", "rear", messages);
    }

    private static void ValidateTyreFitsWheel(
        CatalogSnapshot catalogs,
        string tyreId,
        string wheelId,
        string axle,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (!catalogs.TryGet(tyreId, out JsonElement tyre) || !catalogs.TryGet(wheelId, out JsonElement wheel))
        {
            return;
        }

        float tyreRim = ReadSingle(tyre, 0f, "data", "rimDiameterIn");
        float wheelDiameter = ReadSingle(wheel, 0f, "data", "diameterIn");
        if (tyreRim > 0f && wheelDiameter > 0f && MathF.Abs(tyreRim - wheelDiameter) > 0.01f)
        {
            messages.Add(Warning($"{axle}_tyre_wheel_diameter_mismatch", $"{axle} tyre {tyreId} expects {tyreRim:0.#}in rim, but wheel {wheelId} is {wheelDiameter:0.#}in."));
        }

        ValidateCompatibility(catalogs, tyreId, $"{axle} tyre", [$"{wheelDiameter:0}_inch"], messages);
    }

    private static void ValidateCompatibility(
        CatalogSnapshot catalogs,
        string partId,
        string role,
        IReadOnlyList<string> expectedTokens,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(partId) || !catalogs.TryGet(partId, out JsonElement part))
        {
            return;
        }

        string[] compatibility = ReadCompatibility(part);
        if (compatibility.Length == 0)
        {
            messages.Add(Info($"{NormalizeToken(role)}_compatibility_missing", $"{role} {partId} does not declare compatibility tags."));
            return;
        }

        foreach (string expectedToken in expectedTokens.Where(token => !string.IsNullOrWhiteSpace(token)))
        {
            if (!compatibility.Any(token => token.Equals(expectedToken, StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(Warning($"{NormalizeToken(role)}_compatibility_mismatch", $"{role} {partId} does not declare compatibility with {expectedToken}."));
            }
        }
    }

    private static void ValidateAxle(
        CatalogSnapshot catalogs,
        string partId,
        string role,
        string expectedAxle,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(partId) || !catalogs.TryGet(partId, out JsonElement part))
        {
            return;
        }

        string axle = ReadString(part, string.Empty, "axle");
        if (!string.IsNullOrWhiteSpace(axle) &&
            !axle.Equals("both", StringComparison.OrdinalIgnoreCase) &&
            !axle.Equals(expectedAxle, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(Warning($"{NormalizeToken(role)}_axle_mismatch", $"{role} {partId} is tagged for {axle}, but build installs it on {expectedAxle}."));
        }
    }

    private static void ValidateSuspensionHardPoints(
        ResolvedVehicleBuild runtimeBuild,
        List<VehicleAssemblyValidationMessage> messages)
    {
        ValidateSuspensionHardPointSet(runtimeBuild.Chassis.FrontSuspensionHardPoints, "front", messages);
        ValidateSuspensionHardPointSet(runtimeBuild.Chassis.RearSuspensionHardPoints, "rear", messages);
    }

    private static void ValidateSuspensionHardPointSet(
        ResolvedSuspensionHardPoints hardPoints,
        string axle,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(hardPoints.Type))
        {
            messages.Add(Warning($"{axle}_suspension_hardpoint_type_missing", $"{axle} suspension hard-points do not declare a geometry type."));
        }

        if (hardPoints.MaxCompressionMeters <= 0f || hardPoints.MaxDroopMeters <= 0f)
        {
            messages.Add(Warning($"{axle}_suspension_travel_missing", $"{axle} suspension hard-points do not declare positive compression/droop travel."));
        }

        if (MathF.Abs(hardPoints.CamberGainDegreesPerMeter) <= 0.001f)
        {
            messages.Add(Info($"{axle}_camber_gain_neutral", $"{axle} suspension hard-points have no camber gain."));
        }
    }

    private static void ValidateFuelSelection(
        JsonElement engine,
        ResolvedEngineAssembly engineAssembly,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (!TryGet(engine, out JsonElement fuel, "fuel"))
        {
            messages.Add(Info("fuel_selection_implicit", $"Engine build does not declare fuel; resolver uses {engineAssembly.FuelId}."));
            return;
        }

        if (!TryGet(fuel, out JsonElement allowed, "allowed") || allowed.ValueKind != JsonValueKind.Array)
        {
            messages.Add(Info("fuel_allowed_list_missing", $"Fuel {engineAssembly.FuelId} is selected, but this build does not declare an allowed fuel list."));
            return;
        }

        bool selectedAllowed = allowed.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            item.GetString()?.Equals(engineAssembly.FuelId, StringComparison.OrdinalIgnoreCase) == true);
        if (!selectedAllowed)
        {
            messages.Add(Warning("selected_fuel_not_allowed", $"Fuel {engineAssembly.FuelId} is selected but is not listed in the build's allowed fuel ids."));
        }
    }

    private static void ValidateRequiredEngineSlots(
        ResolvedEngineAssembly engineAssembly,
        List<VehicleAssemblyValidationMessage> messages)
    {
        string[] requiredSlots =
        [
            "blockUpgrade",
            "headUpgrade",
            "cams",
            "displacement",
            "portPolishing",
            "throttleBody",
            "intake",
            "intakeRunnerLength",
            "valveSprings",
            "headers",
            "exhaust",
            "flywheel",
            "clutch",
            "engineAudioDsp"
        ];

        foreach (string slot in requiredSlots)
        {
            if (!engineAssembly.InstalledParts.TryGetValue(slot, out string? partId) ||
                string.IsNullOrWhiteSpace(partId))
            {
                messages.Add(Warning("engine_slot_missing", $"Engine slot '{slot}' is not installed for {engineAssembly.EngineId}."));
            }
        }
    }

    private static void ValidateAudioRecipe(
        ResolvedEngineAssembly engineAssembly,
        List<VehicleAssemblyValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(engineAssembly.EngineAudioProfilePath))
        {
            messages.Add(Info("engine_audio_recipe_missing", $"Engine build does not resolve a sample audio profile. Engine {engineAssembly.EngineId} has no catalog-driven race audio recipe yet."));
            return;
        }

        if (!CanResolveDataPath(engineAssembly.EngineAudioProfilePath))
        {
            messages.Add(Warning("engine_audio_profile_missing", $"Resolved engine audio profile was not found: {engineAssembly.EngineAudioProfilePath}."));
        }

        if (!string.IsNullOrWhiteSpace(engineAssembly.EngineAudioProfileEngineId) &&
            !engineAssembly.EngineAudioProfileEngineId.Equals(engineAssembly.EngineId, StringComparison.OrdinalIgnoreCase))
        {
            VehicleAssemblyValidationSeverity severity = engineAssembly.EngineAudioFallbackAllowed
                ? VehicleAssemblyValidationSeverity.Info
                : VehicleAssemblyValidationSeverity.Warning;
            messages.Add(new VehicleAssemblyValidationMessage(
                severity,
                engineAssembly.EngineAudioFallbackAllowed ? "engine_audio_profile_fallback" : "engine_audio_profile_engine_mismatch",
                $"Engine {engineAssembly.EngineId} is using audio profile source {engineAssembly.EngineAudioProfileEngineId}."));
        }

        if (!string.IsNullOrWhiteSpace(engineAssembly.EngineAudioProfileEngineFamily) &&
            !engineAssembly.EngineAudioProfileEngineFamily.Equals(engineAssembly.Family, StringComparison.OrdinalIgnoreCase))
        {
            VehicleAssemblyValidationSeverity severity = engineAssembly.EngineAudioFallbackAllowed
                ? VehicleAssemblyValidationSeverity.Info
                : VehicleAssemblyValidationSeverity.Warning;
            messages.Add(new VehicleAssemblyValidationMessage(
                severity,
                engineAssembly.EngineAudioFallbackAllowed ? "engine_audio_profile_family_fallback" : "engine_audio_profile_family_mismatch",
                $"Engine family {engineAssembly.Family} is using audio profile source family {engineAssembly.EngineAudioProfileEngineFamily}."));
        }

        if (string.IsNullOrWhiteSpace(engineAssembly.EngineAudioGenerationMethod))
        {
            messages.Add(Info("engine_audio_generation_method_missing", $"Engine audio DSP {engineAssembly.EngineAudioDspId} does not describe how its samples are generated."));
        }
    }

    private static void ValidateMass(ResolvedMassProperties massProperties, List<VehicleAssemblyValidationMessage> messages)
    {
        if (massProperties.TotalMassKg <= 0f)
        {
            messages.Add(Warning("resolved_mass_invalid", $"Resolved mass is invalid: {massProperties.TotalMassKg:0.0}kg."));
            return;
        }

        float residualRatio = MathF.Abs(massProperties.CalibrationResidualMassKg) / massProperties.TotalMassKg;
        if (residualRatio > 0.10f)
        {
            messages.Add(Warning("large_mass_calibration_residual", $"Mass resolver residual is {massProperties.CalibrationResidualMassKg:0.0}kg ({residualRatio * 100f:0.0}% of total); more component masses need explicit catalog data."));
        }
        else if (residualRatio > 0.025f)
        {
            messages.Add(Info("mass_calibration_residual", $"Mass resolver keeps a {massProperties.CalibrationResidualMassKg:0.0}kg calibration residual for fluids/driver/interior/unmodelled stock mass."));
        }

        if (massProperties.FrontWeightDistribution < 0.35f || massProperties.FrontWeightDistribution > 0.75f)
        {
            messages.Add(Warning("front_weight_distribution_extreme", $"Resolved front weight distribution is {massProperties.FrontWeightDistribution * 100f:0.0}%."));
        }

        if (massProperties.CenterOfGravityHeightMeters < 0.25f || massProperties.CenterOfGravityHeightMeters > 0.75f)
        {
            messages.Add(Warning("cg_height_extreme", $"Resolved CG height is {massProperties.CenterOfGravityHeightMeters:0.000}m."));
        }
    }

    private static VehicleAssemblyValidationMessage Info(string code, string message)
    {
        return new VehicleAssemblyValidationMessage(VehicleAssemblyValidationSeverity.Info, code, message);
    }

    private static VehicleAssemblyValidationMessage Warning(string code, string message)
    {
        return new VehicleAssemblyValidationMessage(VehicleAssemblyValidationSeverity.Warning, code, message);
    }

    private static JsonElement Require(JsonElement root, params string[] path)
    {
        return TryGet(root, out JsonElement value, path)
            ? value
            : throw new InvalidDataException($"Missing required JSON path '{string.Join(".", path)}'.");
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.TryGetSingle(out float result)
            ? result
            : fallback;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static int ReadInt32(JsonElement root, int fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.TryGetInt32(out int result)
            ? result
            : fallback;
    }

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] ReadCompatibility(JsonElement item)
    {
        if (!TryGet(item, out JsonElement compatibility, "compatibility"))
        {
            return [];
        }

        if (compatibility.ValueKind == JsonValueKind.String)
        {
            string? value = compatibility.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [NormalizeToken(value)];
        }

        if (compatibility.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. compatibility.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => NormalizeToken(value.GetString() ?? string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))];
    }

    private static string[] ReadStringArray(JsonElement root, params string[] path)
    {
        if (!TryGet(root, out JsonElement array, path) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))];
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement root, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path) || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string partId = property.Value.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(partId))
                {
                    result[property.Name] = partId;
                }
            }
        }

        return result;
    }

    private static string[] ReadRequiredSwapKitSlots(JsonElement bodyShell, string engineFamily)
    {
        if (!TryGet(bodyShell, out JsonElement slotsByFamily, "data", "engineBay", "requiredSwapKitSlotsByFamily") ||
            slotsByFamily.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (JsonProperty property in slotsByFamily.EnumerateObject())
        {
            if (property.Name.Equals(engineFamily, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Array)
            {
                return [.. property.Value.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))];
            }
        }

        return [];
    }

    private static string NormalizeToken(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string DrivetrainCompatibilityToken(string drivetrainLayout)
    {
        return drivetrainLayout.Trim().ToUpperInvariant() switch
        {
            "FF" => "ff_transverse",
            "FR" => "fr_longitudinal",
            "MR" => "mr_longitudinal",
            "RR" => "rr_longitudinal",
            "AWD" or "4WD" => "awd",
            _ => string.Empty
        };
    }

    private static string ResolveDataPath(string path)
    {
        path = VehiclePathMigration.ResolveLegacyBuildPath(path);

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Data file was not found: {path}", path);
    }

    private static string ToDisplayDataPath(string resolvedPath)
    {
        foreach (string root in CandidateRoots())
        {
            string relative = Path.GetRelativePath(root, resolvedPath);
            if (!relative.StartsWith("..", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative))
            {
                return relative.Replace('\\', '/');
            }
        }

        return resolvedPath.Replace('\\', '/');
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Environment.CurrentDirectory;

        if (!AppContext.BaseDirectory.Equals(Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return AppContext.BaseDirectory;
        }
    }

    private static bool CanResolveDataPath(string path)
    {
        try
        {
            _ = ResolveDataPath(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private sealed class CatalogSnapshot
    {
        private readonly Dictionary<string, JsonElement> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _slots = new(StringComparer.OrdinalIgnoreCase);

        public static CatalogSnapshot Load(string catalogIndexPath)
        {
            CatalogSnapshot snapshot = new();
            snapshot.LoadIndex(catalogIndexPath);
            return snapshot;
        }

        public bool TryGet(string id, out JsonElement item)
        {
            return _items.TryGetValue(id, out item);
        }

        public bool TryGetSlot(string id, out string slot)
        {
            if (_slots.TryGetValue(id, out string? value))
            {
                slot = value;
                return true;
            }

            slot = string.Empty;
            return false;
        }

        private void LoadIndex(string catalogIndexPath)
        {
            string resolvedIndexPath = ResolveDataPath(catalogIndexPath);
            using FileStream stream = File.OpenRead(resolvedIndexPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("catalogs", out JsonElement catalogs) ||
                catalogs.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement catalog in catalogs.EnumerateArray())
            {
                string path = ReadString(catalog, string.Empty, "path");
                string slot = ReadString(catalog, string.Empty, "slot");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    LoadCatalog(path, slot);
                }
            }

            ResolveInheritedItems();
        }

        private void LoadCatalog(string catalogPath, string slotHint)
        {
            string resolvedCatalogPath = ResolveDataPath(catalogPath);
            using FileStream stream = File.OpenRead(resolvedCatalogPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement;
            string catalogSlot = ReadString(root, slotHint, "slot");
            AddItems(root, "parts", catalogSlot);
            AddItems(root, "engines", catalogSlot);
            AddItems(root, "blocks", catalogSlot);
            AddItems(root, "heads", catalogSlot);
            AddItems(root, "tunes", catalogSlot);
            AddItems(root, "fuels", catalogSlot);
        }

        private void AddItems(JsonElement root, string propertyName, string catalogSlot)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                string id = ReadString(item, string.Empty, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _items[id] = item.Clone();
                    if (!string.IsNullOrWhiteSpace(catalogSlot))
                    {
                        _slots[id] = catalogSlot;
                    }
                }
            }
        }

        private void ResolveInheritedItems()
        {
            foreach (string id in _items.Keys.ToArray())
            {
                _items[id] = ResolveInheritedItem(id, []);
            }
        }

        private JsonElement ResolveInheritedItem(string id, HashSet<string> stack)
        {
            if (!_items.TryGetValue(id, out JsonElement item))
            {
                throw new InvalidDataException($"Missing inherited catalog id '{id}'.");
            }

            string baseId = ReadString(item, string.Empty, "inherits");
            if (string.IsNullOrWhiteSpace(baseId))
            {
                return item;
            }

            if (!stack.Add(id))
            {
                throw new InvalidDataException($"Catalog inheritance cycle detected at '{id}'.");
            }

            JsonElement inherited = ResolveInheritedItem(baseId, stack);
            stack.Remove(id);

            JsonNode? baseNode = JsonNode.Parse(inherited.GetRawText());
            JsonNode? overrideNode = JsonNode.Parse(item.GetRawText());
            if (baseNode is not JsonObject baseObject || overrideNode is not JsonObject overrideObject)
            {
                return item;
            }

            DeepMerge(baseObject, overrideObject);
            using JsonDocument mergedDocument = JsonDocument.Parse(baseObject.ToJsonString());
            return mergedDocument.RootElement.Clone();
        }

        private static void DeepMerge(JsonObject target, JsonObject overlay)
        {
            foreach (KeyValuePair<string, JsonNode?> property in overlay)
            {
                if (target[property.Key] is JsonObject targetChild &&
                    property.Value is JsonObject overlayChild)
                {
                    DeepMerge(targetChild, overlayChild);
                }
                else
                {
                    target[property.Key] = property.Value?.DeepClone();
                }
            }
        }
    }
}
