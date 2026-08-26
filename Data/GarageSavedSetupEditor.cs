using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageSavedSetupEditor
{
    public static GarageSavedSetupEditResult UpdateSetup(
        string profilePath,
        string ownedVehicleIdOrPath,
        string setupIdOrPath,
        GarageSavedSetupOverrides overrides)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        GarageSavedSetupReference setupReference = FindSetup(profile, vehicle.VehicleId, setupIdOrPath);
        string setupPath = ResolveDataPath(setupReference.Path);
        GarageSavedSetup before = GarageSavedSetupLoader.Load(setupPath);

        if (!before.OwnerProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Saved setup {before.Id} belongs to {before.OwnerProfileId}, not {profile.Id}.");
        }

        if (!before.VehicleId.Equals(vehicle.VehicleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Saved setup {before.Id} belongs to {before.VehicleId}, not {vehicle.VehicleId}.");
        }

        JsonObject setupJson = ReadObject(setupPath);
        JsonObject candidate = CloneObject(setupJson);
        string[] changedFields = ApplyOverrides(candidate, overrides);

        if (changedFields.Length == 0)
        {
            GarageResolvedSetupVehicle unchanged = GarageSavedSetupResolver.ResolveWithSetup(profilePath, vehicle.VehicleId, setupReference.SetupId);
            return new GarageSavedSetupEditResult(setupPath, before, before, unchanged, []);
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-setup-edit-validation", Guid.NewGuid().ToString("N"));
        string tempSetupPath = Path.Combine(tempRoot, Path.GetFileName(setupPath));
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(tempSetupPath, candidate.ToJsonString(CreateJsonOptions()));
            GarageResolvedSetupVehicle validated = GarageSavedSetupResolver.ResolveWithSetupFile(profilePath, vehicle.VehicleId, tempSetupPath);
            ThrowIfWarnings(validated);

            File.WriteAllText(setupPath, candidate.ToJsonString(CreateJsonOptions()));
            GarageSavedSetup after = GarageSavedSetupLoader.Load(setupPath);
            GarageResolvedSetupVehicle resolved = GarageSavedSetupResolver.ResolveWithSetup(profilePath, vehicle.VehicleId, setupReference.SetupId);
            return new GarageSavedSetupEditResult(setupPath, before, after, resolved, changedFields);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string[] ApplyOverrides(JsonObject setup, GarageSavedSetupOverrides overrides)
    {
        List<string> changed = [];
        JsonObject root = EnsureObject(setup, "overrides");
        JsonObject engine = EnsureObject(root, "engine");
        JsonObject suspension = EnsureObject(root, "suspension");
        JsonObject tuning = EnsureObject(root, "tuning");

        SetIfRequested(engine, "tuneId", overrides.EngineTuneId, "engine.tuneId", changed);
        SetIfRequested(engine, "fuelSelected", overrides.FuelId, "engine.fuelSelected", changed);
        SetIfRequested(suspension, "alignment", overrides.AlignmentId, "suspension.alignment", changed);
        SetIfRequested(tuning, "steering", overrides.SteeringSetupId, "tuning.steering", changed);
        SetIfRequested(tuning, "handling", overrides.HandlingSetupId, "tuning.handling", changed);

        return [.. changed];
    }

    private static void SetIfRequested(JsonObject root, string jsonName, string? value, string fieldName, List<string> changed)
    {
        if (value is null)
        {
            return;
        }

        string current = root[jsonName]?.GetValue<string>() ?? string.Empty;
        if (current.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        root[jsonName] = value;
        changed.Add(fieldName);
    }

    private static void ThrowIfWarnings(GarageResolvedSetupVehicle validated)
    {
        VehicleAssemblyValidationMessage[] vehicleWarnings = [.. validated.Resolved.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)];
        EngineAssemblyValidationMessage[] engineWarnings = [.. validated.Resolved.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];

        if (vehicleWarnings.Length == 0 && engineWarnings.Length == 0)
        {
            return;
        }

        string vehicleMessages = string.Join("; ", vehicleWarnings.Select(message => message.Message));
        string engineMessages = string.Join("; ", engineWarnings.Select(message => message.Message));
        throw new InvalidOperationException($"Saved setup edit would produce validation warnings. Vehicle: {vehicleMessages}. Engine: {engineMessages}.");
    }

    private static GarageOwnedVehicleReference FindOwnedVehicle(GarageProfile profile, string ownedVehicleIdOrPath)
    {
        return profile.OwnedVehicles.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(candidate.Path).Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Garage profile {profile.Id} does not own vehicle {ownedVehicleIdOrPath}.");
    }

    private static GarageSavedSetupReference FindSetup(GarageProfile profile, string vehicleId, string setupIdOrPath)
    {
        return profile.SavedSetups.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(vehicleId, StringComparison.OrdinalIgnoreCase) &&
            (candidate.SetupId.Equals(setupIdOrPath, StringComparison.OrdinalIgnoreCase) ||
             candidate.Path.Equals(setupIdOrPath, StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileNameWithoutExtension(candidate.Path).Equals(setupIdOrPath, StringComparison.OrdinalIgnoreCase))) ??
            throw new InvalidOperationException($"Garage profile {profile.Id} has no saved setup {setupIdOrPath} for vehicle {vehicleId}.");
    }

    private static JsonObject EnsureObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = [];
        root[name] = created;
        return created;
    }

    private static JsonObject ReadObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? throw new InvalidDataException($"JSON file is not an object: {path}");
    }

    private static JsonObject CloneObject(JsonObject source)
    {
        return JsonNode.Parse(source.ToJsonString()) as JsonObject ??
            throw new InvalidDataException("Failed to clone JSON object.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
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

        throw new FileNotFoundException($"Saved setup file was not found: {path}", path);
    }
}

internal sealed record GarageSavedSetupOverrides(
    string? EngineTuneId = null,
    string? FuelId = null,
    string? AlignmentId = null,
    string? SteeringSetupId = null,
    string? HandlingSetupId = null);

internal sealed record GarageSavedSetupEditResult(
    string SetupPath,
    GarageSavedSetup Before,
    GarageSavedSetup After,
    GarageResolvedSetupVehicle Resolved,
    IReadOnlyList<string> ChangedFields);
