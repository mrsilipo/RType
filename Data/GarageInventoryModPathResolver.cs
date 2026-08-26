namespace RType.Data;

internal static class GarageInventoryModPathResolver
{
    public static GarageInventoryModPathReport BuildReport(string profilePath, string ownedVehicleIdOrPath)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicleReference = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        VehicleModPathReport modPath = VehicleModPathResolver.BuildReport(vehicleReference.Path);

        if (!modPath.CurrentVehicle.PlayerOwned ||
            !modPath.CurrentVehicle.OwnerProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Vehicle {vehicleReference.VehicleId} is not owned by garage profile {profile.Id}.");
        }

        GarageInventoryModOption[] options =
        [
            .. modPath.Engine.Options.Select(option => FromEngineOption(option, profile.Inventory)),
            .. modPath.VehicleOptions.Select(option => FromVehicleOption(option, profile.Inventory))
        ];

        return new GarageInventoryModPathReport(profile, vehicleReference, modPath, options);
    }

    private static GarageOwnedVehicleReference FindOwnedVehicle(GarageProfile profile, string ownedVehicleIdOrPath)
    {
        GarageOwnedVehicleReference? vehicle = profile.OwnedVehicles.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(candidate.Path).Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase));

        return vehicle ?? throw new InvalidOperationException(
            $"Garage profile {profile.Id} does not own vehicle {ownedVehicleIdOrPath}.");
    }

    private static GarageInventoryModOption FromEngineOption(EngineModOption option, GarageInventory inventory)
    {
        GarageInventoryAvailability availability = Classify(option.Id, option.IsInstalled, ConvertStatus(option.Status), inventory);
        return new GarageInventoryModOption(
            option.Id,
            option.DisplayName,
            option.Slot,
            option.CatalogSlot,
            GarageModInstallKind.Engine,
            ConvertStatus(option.Status),
            availability,
            option.IsInstalled,
            option.Selectable,
            inventory.Owns(option.Id),
            inventory.IsPurchasable(option.Id),
            inventory.IsLocked(option.Id),
            option.WarningCodes,
            option.InfoCodes,
            option.Tier,
            option.Category,
            option.DisplacementCc,
            option.CompressionRatio,
            option.LimiterHardCutRpm,
            option.PeakTorqueNm,
            0f,
            0f,
            0f);
    }

    private static GarageInventoryModOption FromVehicleOption(VehicleModOption option, GarageInventory inventory)
    {
        GarageInventoryAvailability availability = Classify(option.Id, option.IsInstalled, ConvertStatus(option.Status), inventory);
        return new GarageInventoryModOption(
            option.Id,
            option.DisplayName,
            option.Slot,
            option.CatalogSlot,
            GarageModInstallKind.Vehicle,
            ConvertStatus(option.Status),
            availability,
            option.IsInstalled,
            option.Selectable,
            inventory.Owns(option.Id),
            inventory.IsPurchasable(option.Id),
            inventory.IsLocked(option.Id),
            option.WarningCodes,
            option.InfoCodes,
            option.Tier,
            option.Category,
            0f,
            0f,
            0f,
            0f,
            option.TotalMassKg,
            option.FrontWeightDistribution,
            option.YawInertiaKgM2);
    }

    private static GarageInventoryAvailability Classify(
        string optionId,
        bool installed,
        GarageModInstallStatus buildStatus,
        GarageInventory inventory)
    {
        if (installed)
        {
            return GarageInventoryAvailability.Installed;
        }

        if (buildStatus == GarageModInstallStatus.Blocked)
        {
            return GarageInventoryAvailability.BlockedByBuild;
        }

        if (inventory.IsLocked(optionId))
        {
            return GarageInventoryAvailability.Locked;
        }

        if (inventory.Owns(optionId))
        {
            return GarageInventoryAvailability.OwnedReady;
        }

        if (inventory.IsPurchasable(optionId))
        {
            return GarageInventoryAvailability.Purchasable;
        }

        return GarageInventoryAvailability.NotOwned;
    }

    private static GarageModInstallStatus ConvertStatus(EngineModOptionStatus status) => status switch
    {
        EngineModOptionStatus.Ready => GarageModInstallStatus.Ready,
        EngineModOptionStatus.Advisory => GarageModInstallStatus.Advisory,
        EngineModOptionStatus.Blocked => GarageModInstallStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static GarageModInstallStatus ConvertStatus(VehicleModOptionStatus status) => status switch
    {
        VehicleModOptionStatus.Ready => GarageModInstallStatus.Ready,
        VehicleModOptionStatus.Advisory => GarageModInstallStatus.Advisory,
        VehicleModOptionStatus.Blocked => GarageModInstallStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}

internal sealed record GarageInventoryModPathReport(
    GarageProfile Profile,
    GarageOwnedVehicleReference Vehicle,
    VehicleModPathReport ModPath,
    IReadOnlyList<GarageInventoryModOption> Options)
{
    public IEnumerable<GarageInventoryModOption> Installed => Options.Where(option => option.Availability == GarageInventoryAvailability.Installed);
    public IEnumerable<GarageInventoryModOption> OwnedReady => Options.Where(option => option.Availability == GarageInventoryAvailability.OwnedReady);
    public IEnumerable<GarageInventoryModOption> Purchasable => Options.Where(option => option.Availability == GarageInventoryAvailability.Purchasable);
    public IEnumerable<GarageInventoryModOption> Locked => Options.Where(option => option.Availability == GarageInventoryAvailability.Locked);
    public IEnumerable<GarageInventoryModOption> NotOwned => Options.Where(option => option.Availability == GarageInventoryAvailability.NotOwned);
    public IEnumerable<GarageInventoryModOption> BlockedByBuild => Options.Where(option => option.Availability == GarageInventoryAvailability.BlockedByBuild);
    public IEnumerable<GarageInventoryModOption> Installable => Options.Where(option =>
        option.Availability == GarageInventoryAvailability.OwnedReady ||
        option.Availability == GarageInventoryAvailability.Purchasable);
}

internal sealed record GarageInventoryModOption(
    string Id,
    string DisplayName,
    string Slot,
    string CatalogSlot,
    GarageModInstallKind Kind,
    GarageModInstallStatus BuildStatus,
    GarageInventoryAvailability Availability,
    bool IsInstalled,
    bool BuildSelectable,
    bool Owned,
    bool Purchasable,
    bool Locked,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> InfoCodes,
    string Tier,
    string Category,
    float DisplacementCc,
    float CompressionRatio,
    float LimiterHardCutRpm,
    float PeakTorqueNm,
    float TotalMassKg,
    float FrontWeightDistribution,
    float YawInertiaKgM2);

internal enum GarageInventoryAvailability
{
    Installed,
    OwnedReady,
    Purchasable,
    Locked,
    NotOwned,
    BlockedByBuild
}
