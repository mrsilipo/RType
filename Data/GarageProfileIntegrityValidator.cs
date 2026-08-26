using System.Text.Json;

namespace RType.Data;

internal static class GarageProfileIntegrityValidator
{
    private const string PartPricesPath = "Data/Garage/part_prices.json";

    public static GarageProfileIntegrityReport Validate(string profilePath)
    {
        string resolvedProfilePath = ResolveDataPath(profilePath);
        GarageProfile profile = GarageProfileLoader.Load(resolvedProfilePath);
        List<GarageProfileIntegrityMessage> messages = [];
        GarageCatalogIdentityReport knownCatalogIds = GarageCatalogIdentityIndex.Load();
        foreach (GarageCatalogIdentityWarning warning in knownCatalogIds.Warnings)
        {
            messages.Add(Warning(warning.Code, warning.Message));
        }

        HashSet<string> pricedPartIds = LoadPricedPartIds(messages);

        ValidateActiveVehicle(profile, messages);
        ValidateOwnedVehicles(profile, messages);
        ValidateSavedSetups(resolvedProfilePath, profile, messages);
        ValidateInventory(profile, knownCatalogIds, pricedPartIds, messages);

        return new GarageProfileIntegrityReport(
            resolvedProfilePath,
            profile,
            [.. messages]);
    }

    private static void ValidateActiveVehicle(GarageProfile profile, List<GarageProfileIntegrityMessage> messages)
    {
        if (profile.OwnedVehicles.Count == 0)
        {
            messages.Add(Warning("profile_has_no_owned_vehicles", $"Garage profile {profile.Id} has no owned vehicles."));
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.ActiveVehicleId))
        {
            messages.Add(Info("profile_active_vehicle_not_set", $"Garage profile {profile.Id} has no activeVehicleId and will fall back to first garage slot."));
            return;
        }

        if (!profile.OwnedVehicles.Any(vehicle => vehicle.VehicleId.Equals(profile.ActiveVehicleId, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(Warning("active_vehicle_not_owned", $"Garage profile {profile.Id} activeVehicleId {profile.ActiveVehicleId} is not listed in ownedVehicles."));
        }
    }

    private static void ValidateOwnedVehicles(GarageProfile profile, List<GarageProfileIntegrityMessage> messages)
    {
        foreach (IGrouping<string, GarageOwnedVehicleReference> group in profile.OwnedVehicles
                     .GroupBy(vehicle => vehicle.VehicleId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            messages.Add(Warning("duplicate_owned_vehicle_id", $"Garage profile {profile.Id} declares owned vehicle id {group.Key} {group.Count()} times."));
        }

        foreach (IGrouping<int, GarageOwnedVehicleReference> group in profile.OwnedVehicles
                     .Where(vehicle => vehicle.GarageSlot > 0)
                     .GroupBy(vehicle => vehicle.GarageSlot)
                     .Where(group => group.Count() > 1))
        {
            messages.Add(Warning("duplicate_garage_slot", $"Garage profile {profile.Id} uses garage slot {group.Key} {group.Count()} times."));
        }

        foreach (GarageOwnedVehicleReference vehicle in profile.OwnedVehicles)
        {
            if (string.IsNullOrWhiteSpace(vehicle.VehicleId))
            {
                messages.Add(Warning("owned_vehicle_missing_id", $"Garage profile {profile.Id} has an owned vehicle entry with no vehicleId."));
            }

            if (string.IsNullOrWhiteSpace(vehicle.Path))
            {
                messages.Add(Warning("owned_vehicle_missing_path", $"Garage profile {profile.Id} owned vehicle {vehicle.VehicleId} has no path."));
                continue;
            }

            if (vehicle.GarageSlot <= 0)
            {
                messages.Add(Warning("owned_vehicle_invalid_garage_slot", $"Owned vehicle {vehicle.VehicleId} has invalid garage slot {vehicle.GarageSlot}."));
            }

            try
            {
                ResolvedVehicleAssembly resolved = VehicleAssemblyResolver.Resolve(vehicle.Path);
                if (!resolved.PlayerOwned || !resolved.Classification.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Warning("owned_vehicle_not_owned_role", $"Profile vehicle {vehicle.VehicleId} resolves as role {resolved.Classification} with playerOwned={resolved.PlayerOwned}."));
                }

                if (!resolved.BuildId.Equals(vehicle.VehicleId, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Warning("owned_vehicle_id_mismatch", $"Profile vehicle id {vehicle.VehicleId} points at build id {resolved.BuildId}."));
                }

                if (!resolved.OwnerProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Warning("owned_vehicle_owner_mismatch", $"Owned vehicle {vehicle.VehicleId} belongs to {resolved.OwnerProfileId}, not profile {profile.Id}."));
                }

                AddResolverWarnings(messages, vehicle.VehicleId, resolved);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or JsonException)
            {
                messages.Add(Warning("owned_vehicle_resolve_failed", $"Owned vehicle {vehicle.VehicleId} could not resolve: {exception.Message}"));
            }
        }
    }

    private static void ValidateSavedSetups(
        string profilePath,
        GarageProfile profile,
        List<GarageProfileIntegrityMessage> messages)
    {
        foreach (IGrouping<string, GarageSavedSetupReference> group in profile.SavedSetups
                     .GroupBy(setup => $"{setup.VehicleId}\0{setup.SetupId}", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            GarageSavedSetupReference first = group.First();
            messages.Add(Warning("duplicate_saved_setup_id", $"Garage profile {profile.Id} declares saved setup {first.SetupId} for {first.VehicleId} {group.Count()} times."));
        }

        foreach (IGrouping<string, GarageSavedSetupReference> group in profile.SavedSetups
                     .Where(setup => setup.Active)
                     .GroupBy(setup => setup.VehicleId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            messages.Add(Warning("multiple_active_setups_for_vehicle", $"Garage profile {profile.Id} has {group.Count()} active setups for vehicle {group.Key}."));
        }

        foreach (GarageSavedSetupReference setupReference in profile.SavedSetups)
        {
            if (!profile.OwnedVehicles.Any(vehicle => vehicle.VehicleId.Equals(setupReference.VehicleId, StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(Warning("saved_setup_vehicle_not_owned", $"Saved setup {setupReference.SetupId} targets vehicle {setupReference.VehicleId}, which profile {profile.Id} does not own."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(setupReference.Path))
            {
                messages.Add(Warning("saved_setup_missing_path", $"Saved setup {setupReference.SetupId} has no path."));
                continue;
            }

            try
            {
                GarageSavedSetup setup = GarageSavedSetupLoader.Load(setupReference.Path);
                if (!setup.Id.Equals(setupReference.SetupId, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Warning("saved_setup_id_mismatch", $"Profile setup id {setupReference.SetupId} points at setup file id {setup.Id}."));
                }

                if (!setup.OwnerProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Warning("saved_setup_owner_mismatch", $"Saved setup {setup.Id} belongs to {setup.OwnerProfileId}, not profile {profile.Id}."));
                }

                if (!setup.VehicleId.Equals(setupReference.VehicleId, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(Warning("saved_setup_vehicle_mismatch", $"Saved setup {setup.Id} belongs to {setup.VehicleId}, not profile reference vehicle {setupReference.VehicleId}."));
                }

                GarageResolvedSetupVehicle resolved = GarageSavedSetupResolver.ResolveWithSetupFile(
                    profilePath,
                    setupReference.VehicleId,
                    setupReference.Path);
                AddResolverWarnings(messages, setupReference.SetupId, resolved.Resolved);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or JsonException)
            {
                messages.Add(Warning("saved_setup_resolve_failed", $"Saved setup {setupReference.SetupId} could not resolve: {exception.Message}"));
            }
        }
    }

    private static void ValidateInventory(
        GarageProfile profile,
        GarageCatalogIdentityReport knownCatalogIds,
        IReadOnlySet<string> pricedPartIds,
        List<GarageProfileIntegrityMessage> messages)
    {
        foreach (string id in profile.Inventory.OwnedPartIds)
        {
            if (profile.Inventory.IsLocked(id))
            {
                messages.Add(Warning("inventory_owned_part_is_locked", $"Part {id} is both owned and locked for profile {profile.Id}."));
            }

            if (!knownCatalogIds.Contains(id))
            {
                messages.Add(Warning("inventory_owned_part_missing_catalog", $"Owned part {id} is not present in known part/tune/fuel catalogs."));
            }
        }

        foreach (string id in profile.Inventory.LockedPartIds)
        {
            if (!knownCatalogIds.Contains(id))
            {
                messages.Add(Warning("inventory_locked_part_missing_catalog", $"Locked part {id} is not present in known part/tune/fuel catalogs."));
            }
        }

        foreach (string id in profile.Inventory.PurchasablePartIds)
        {
            if (id.Equals("*", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!knownCatalogIds.Contains(id))
            {
                messages.Add(Warning("inventory_purchasable_part_missing_catalog", $"Purchasable part {id} is not present in known part/tune/fuel catalogs."));
            }

            if (!pricedPartIds.Contains(id))
            {
                messages.Add(Warning("inventory_purchasable_part_missing_price", $"Purchasable part {id} does not have a price entry in {PartPricesPath}."));
            }
        }
    }

    private static HashSet<string> LoadPricedPartIds(List<GarageProfileIntegrityMessage> messages)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using FileStream stream = File.OpenRead(ResolveDataPath(PartPricesPath));
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (!TryGet(document.RootElement, out JsonElement prices, "prices") ||
                prices.ValueKind != JsonValueKind.Array)
            {
                messages.Add(Warning("part_prices_missing_prices", $"Price catalog {PartPricesPath} does not contain a prices array."));
                return ids;
            }

            foreach (JsonElement price in prices.EnumerateArray())
            {
                string partId = ReadString(price, string.Empty, "partId");
                if (!string.IsNullOrWhiteSpace(partId))
                {
                    ids.Add(partId);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            messages.Add(Warning("part_prices_load_failed", $"Price catalog {PartPricesPath} could not load: {exception.Message}"));
        }

        return ids;
    }

    private static void AddResolverWarnings(
        List<GarageProfileIntegrityMessage> messages,
        string subjectId,
        ResolvedVehicleAssembly resolved)
    {
        foreach (VehicleAssemblyValidationMessage warning in resolved.Validation
                     .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning))
        {
            messages.Add(Warning("vehicle_resolver_warning", $"{subjectId}: {warning.Code}: {warning.Message}"));
        }

        foreach (EngineAssemblyValidationMessage warning in resolved.Engine.Validation
                     .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning))
        {
            messages.Add(Warning("engine_resolver_warning", $"{subjectId}: {warning.Code}: {warning.Message}"));
        }
    }

    private static GarageProfileIntegrityMessage Info(string code, string message)
    {
        return new GarageProfileIntegrityMessage(GarageProfileIntegritySeverity.Info, code, message);
    }

    private static GarageProfileIntegrityMessage Warning(string code, string message)
    {
        return new GarageProfileIntegrityMessage(GarageProfileIntegritySeverity.Warning, code, message);
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static string ResolveDataPath(string path)
    {
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

        throw new FileNotFoundException($"Garage profile file was not found: {path}", path);
    }
}

internal sealed record GarageProfileIntegrityReport(
    string ProfilePath,
    GarageProfile Profile,
    IReadOnlyList<GarageProfileIntegrityMessage> Messages)
{
    public IReadOnlyList<GarageProfileIntegrityMessage> Warnings =>
        [.. Messages.Where(message => message.Severity == GarageProfileIntegritySeverity.Warning)];

    public IReadOnlyList<GarageProfileIntegrityMessage> Info =>
        [.. Messages.Where(message => message.Severity == GarageProfileIntegritySeverity.Info)];

    public bool IsClean => Warnings.Count == 0;
}

internal sealed record GarageProfileIntegrityMessage(
    GarageProfileIntegritySeverity Severity,
    string Code,
    string Message);

internal enum GarageProfileIntegritySeverity
{
    Info,
    Warning
}
