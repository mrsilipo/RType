using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageSavedSetupResolver
{
    public static GarageResolvedSetupVehicle ResolveWithSetup(
        string profilePath,
        string ownedVehicleIdOrPath,
        string setupIdOrPath)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        GarageSavedSetupReference setupReference = FindSetup(profile, vehicle.VehicleId, setupIdOrPath);

        return ResolveWithSetupReference(profile, vehicle, setupReference);
    }

    public static GarageResolvedSetupVehicle ResolveWithSetupFile(
        string profilePath,
        string ownedVehicleIdOrPath,
        string setupPath)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        GarageSavedSetup setup = GarageSavedSetupLoader.Load(setupPath);
        GarageSavedSetupReference setupReference = new(
            setup.Id,
            setup.VehicleId,
            setupPath,
            setup.DisplayName,
            Active: false);

        return ResolveWithSetupReference(profile, vehicle, setupReference);
    }

    private static GarageResolvedSetupVehicle ResolveWithSetupReference(
        GarageProfile profile,
        GarageOwnedVehicleReference vehicle,
        GarageSavedSetupReference setupReference)
    {
        GarageSavedSetup setup = GarageSavedSetupLoader.Load(setupReference.Path);

        if (!setup.OwnerProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Saved setup {setup.Id} belongs to {setup.OwnerProfileId}, not {profile.Id}.");
        }

        if (!setup.VehicleId.Equals(vehicle.VehicleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Saved setup {setup.Id} belongs to {setup.VehicleId}, not {vehicle.VehicleId}.");
        }

        JsonObject vehicleJson = ReadObject(ResolveDataPath(vehicle.Path));
        JsonObject overlay = CloneObject(vehicleJson);
        ApplySetup(overlay, setup);

        string tempPath = Path.Combine(Path.GetTempPath(), "rtype-setup-overlays", Guid.NewGuid().ToString("N"), $"{vehicle.VehicleId}_{setup.Id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        File.WriteAllText(tempPath, overlay.ToJsonString(CreateIndentedJsonOptions()));
        ResolvedVehicleAssembly resolved = VehicleAssemblyResolver.Resolve(tempPath);

        return new GarageResolvedSetupVehicle(profile, vehicle, setupReference, setup, vehicle.Path, tempPath, resolved);
    }

    private static void ApplySetup(JsonObject vehicle, GarageSavedSetup setup)
    {
        JsonObject assembly = EnsureObject(vehicle, "assembly");
        JsonObject engine = EnsureObject(assembly, "engine");
        JsonObject suspension = EnsureObject(assembly, "suspension");
        JsonObject tuning = EnsureObject(assembly, "tuning");

        if (!string.IsNullOrWhiteSpace(setup.EngineTuneId))
        {
            engine["tuneId"] = setup.EngineTuneId;
        }

        if (!string.IsNullOrWhiteSpace(setup.FuelId))
        {
            EnsureObject(engine, "fuel")["selected"] = setup.FuelId;
        }

        if (!string.IsNullOrWhiteSpace(setup.AlignmentId))
        {
            suspension["alignment"] = setup.AlignmentId;
        }

        if (!string.IsNullOrWhiteSpace(setup.SteeringSetupId))
        {
            tuning["steering"] = setup.SteeringSetupId;
        }

        if (!string.IsNullOrWhiteSpace(setup.HandlingSetupId))
        {
            tuning["handling"] = setup.HandlingSetupId;
        }
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

    private static JsonSerializerOptions CreateIndentedJsonOptions()
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

        throw new FileNotFoundException($"Data file was not found: {path}", path);
    }
}

internal static class GarageSavedSetupLoader
{
    public static GarageSavedSetup Load(string setupPath)
    {
        string resolvedPath = ResolveDataPath(setupPath);
        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;

        return new GarageSavedSetup(
            ReadString(root, Path.GetFileNameWithoutExtension(resolvedPath), "id"),
            ReadString(root, string.Empty, "displayName"),
            ReadString(root, string.Empty, "ownerProfileId"),
            ReadString(root, string.Empty, "vehicleId"),
            ReadString(root, string.Empty, "overrides", "engine", "tuneId"),
            ReadString(root, string.Empty, "overrides", "engine", "fuelSelected"),
            ReadString(root, string.Empty, "overrides", "suspension", "alignment"),
            ReadString(root, string.Empty, "overrides", "tuning", "steering"),
            ReadString(root, string.Empty, "overrides", "tuning", "handling"));
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
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

        throw new FileNotFoundException($"Saved setup file was not found: {path}", path);
    }
}

internal sealed record GarageSavedSetup(
    string Id,
    string DisplayName,
    string OwnerProfileId,
    string VehicleId,
    string EngineTuneId,
    string FuelId,
    string AlignmentId,
    string SteeringSetupId,
    string HandlingSetupId);

internal sealed record GarageResolvedSetupVehicle(
    GarageProfile Profile,
    GarageOwnedVehicleReference Vehicle,
    GarageSavedSetupReference SetupReference,
    GarageSavedSetup Setup,
    string SourceVehiclePath,
    string OverlayVehiclePath,
    ResolvedVehicleAssembly Resolved);
