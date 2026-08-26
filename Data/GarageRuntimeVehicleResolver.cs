namespace RType.Data;

internal static class GarageRuntimeVehicleResolver
{
    public static GarageRuntimeVehicleSelection Resolve(
        string profilePath,
        string? ownedVehicleIdOrPath = null,
        string? setupIdOrPath = null)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        GarageSavedSetupReference? setup = FindSetup(profile, vehicle.VehicleId, setupIdOrPath);

        if (setup is not null)
        {
            GarageResolvedSetupVehicle resolvedSetup = GarageSavedSetupResolver.ResolveWithSetup(
                profilePath,
                vehicle.VehicleId,
                setup.SetupId);
            return new GarageRuntimeVehicleSelection(
                profile,
                vehicle,
                setup,
                resolvedSetup.SourceVehiclePath,
                resolvedSetup.OverlayVehiclePath,
                VehicleBuildDefinitionLoader.LoadSimulationParameters(resolvedSetup.OverlayVehiclePath),
                resolvedSetup.Resolved);
        }

        ResolvedVehicleAssembly resolvedVehicle = VehicleAssemblyResolver.Resolve(vehicle.Path);
        return new GarageRuntimeVehicleSelection(
            profile,
            vehicle,
            null,
            vehicle.Path,
            string.Empty,
            VehicleRuntimeLoader.LoadSimulationParameters(vehicle.Path),
            resolvedVehicle);
    }

    private static GarageOwnedVehicleReference FindOwnedVehicle(GarageProfile profile, string? ownedVehicleIdOrPath)
    {
        if (string.IsNullOrWhiteSpace(ownedVehicleIdOrPath))
        {
            if (!string.IsNullOrWhiteSpace(profile.ActiveVehicleId))
            {
                return profile.OwnedVehicles.FirstOrDefault(candidate =>
                    candidate.VehicleId.Equals(profile.ActiveVehicleId, StringComparison.OrdinalIgnoreCase)) ??
                    throw new InvalidOperationException($"Garage profile {profile.Id} activeVehicleId {profile.ActiveVehicleId} is not owned by the profile.");
            }

            return profile.OwnedVehicles
                .OrderBy(vehicle => vehicle.GarageSlot)
                .FirstOrDefault() ??
                throw new InvalidOperationException($"Garage profile {profile.Id} has no owned vehicles.");
        }

        return profile.OwnedVehicles.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(candidate.Path).Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Garage profile {profile.Id} does not own vehicle {ownedVehicleIdOrPath}.");
    }

    private static GarageSavedSetupReference? FindSetup(GarageProfile profile, string vehicleId, string? setupIdOrPath)
    {
        if (setupIdOrPath is not null && setupIdOrPath.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(setupIdOrPath) ||
            setupIdOrPath.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return profile.SavedSetups.FirstOrDefault(candidate =>
                candidate.VehicleId.Equals(vehicleId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Active);
        }

        return profile.SavedSetups.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(vehicleId, StringComparison.OrdinalIgnoreCase) &&
            (candidate.SetupId.Equals(setupIdOrPath, StringComparison.OrdinalIgnoreCase) ||
             candidate.Path.Equals(setupIdOrPath, StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileNameWithoutExtension(candidate.Path).Equals(setupIdOrPath, StringComparison.OrdinalIgnoreCase))) ??
            throw new InvalidOperationException($"Garage profile {profile.Id} has no saved setup {setupIdOrPath} for vehicle {vehicleId}.");
    }
}

internal sealed record GarageRuntimeVehicleSelection(
    GarageProfile Profile,
    GarageOwnedVehicleReference Vehicle,
    GarageSavedSetupReference? Setup,
    string SourceVehiclePath,
    string OverlayVehiclePath,
    RType.Vehicle.VehicleSimulationParameters Parameters,
    ResolvedVehicleAssembly Resolved);
