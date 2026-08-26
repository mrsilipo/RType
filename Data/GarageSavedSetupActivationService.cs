using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageSavedSetupActivationService
{
    public static GarageSavedSetupActivationResult SetActiveSetup(
        string profilePath,
        string ownedVehicleIdOrPath,
        string setupIdOrPath)
    {
        GarageResolvedSetupVehicle validated = GarageSavedSetupResolver.ResolveWithSetup(
            profilePath,
            ownedVehicleIdOrPath,
            setupIdOrPath);
        ThrowIfWarnings(validated);

        string resolvedProfilePath = ResolveDataPath(profilePath);
        JsonObject profileJson = ReadObject(resolvedProfilePath);
        JsonArray setups = profileJson["savedSetups"] as JsonArray ??
            throw new InvalidOperationException($"Garage profile {validated.Profile.Id} has no savedSetups array.");

        string previousActiveSetupId = string.Empty;
        bool found = false;
        foreach (JsonNode? node in setups)
        {
            if (node is not JsonObject setup)
            {
                continue;
            }

            string vehicleId = ReadString(setup, "vehicleId");
            if (!vehicleId.Equals(validated.Vehicle.VehicleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string setupId = ReadString(setup, "setupId");
            bool wasActive = setup["active"]?.GetValue<bool>() ?? false;
            if (wasActive)
            {
                previousActiveSetupId = setupId;
            }

            bool isTarget = setupId.Equals(validated.SetupReference.SetupId, StringComparison.OrdinalIgnoreCase) ||
                ReadString(setup, "path").Equals(validated.SetupReference.Path, StringComparison.OrdinalIgnoreCase);
            setup["active"] = isTarget;
            found |= isTarget;
        }

        if (!found)
        {
            throw new InvalidOperationException($"Garage profile {validated.Profile.Id} has no saved setup {setupIdOrPath} for vehicle {validated.Vehicle.VehicleId}.");
        }

        File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateJsonOptions()));
        GarageProfile after = GarageProfileLoader.Load(resolvedProfilePath);
        GarageSavedSetupReference active = after.SavedSetups.First(setup =>
            setup.VehicleId.Equals(validated.Vehicle.VehicleId, StringComparison.OrdinalIgnoreCase) &&
            setup.Active);

        return new GarageSavedSetupActivationResult(
            resolvedProfilePath,
            validated.Vehicle.VehicleId,
            previousActiveSetupId,
            active.SetupId,
            validated);
    }

    public static GarageSavedSetupActivationResult ClearActiveSetup(
        string profilePath,
        string ownedVehicleIdOrPath)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        string resolvedProfilePath = ResolveDataPath(profilePath);
        JsonObject profileJson = ReadObject(resolvedProfilePath);
        JsonArray setups = profileJson["savedSetups"] as JsonArray ??
            throw new InvalidOperationException($"Garage profile {profile.Id} has no savedSetups array.");

        string previousActiveSetupId = string.Empty;
        foreach (JsonNode? node in setups)
        {
            if (node is not JsonObject setup ||
                !ReadString(setup, "vehicleId").Equals(vehicle.VehicleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (setup["active"]?.GetValue<bool>() ?? false)
            {
                previousActiveSetupId = ReadString(setup, "setupId");
            }

            setup["active"] = false;
        }

        File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateJsonOptions()));
        ResolvedVehicleAssembly resolvedVehicle = VehicleAssemblyResolver.Resolve(vehicle.Path);

        return new GarageSavedSetupActivationResult(
            resolvedProfilePath,
            vehicle.VehicleId,
            previousActiveSetupId,
            string.Empty,
            new GarageResolvedSetupVehicle(profile, vehicle, new GarageSavedSetupReference(string.Empty, vehicle.VehicleId, string.Empty, string.Empty, false), new GarageSavedSetup(string.Empty, string.Empty, profile.Id, vehicle.VehicleId, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), vehicle.Path, string.Empty, resolvedVehicle));
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
        throw new InvalidOperationException($"Saved setup activation would produce validation warnings. Vehicle: {vehicleMessages}. Engine: {engineMessages}.");
    }

    private static GarageOwnedVehicleReference FindOwnedVehicle(GarageProfile profile, string ownedVehicleIdOrPath)
    {
        return profile.OwnedVehicles.FirstOrDefault(candidate =>
            candidate.VehicleId.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            candidate.Path.Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(candidate.Path).Equals(ownedVehicleIdOrPath, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Garage profile {profile.Id} does not own vehicle {ownedVehicleIdOrPath}.");
    }

    private static JsonObject ReadObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? throw new InvalidDataException($"JSON file is not an object: {path}");
    }

    private static string ReadString(JsonObject root, string propertyName)
    {
        return root[propertyName]?.GetValue<string>() ?? string.Empty;
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

        throw new FileNotFoundException($"Garage profile file was not found: {path}", path);
    }
}

internal sealed record GarageSavedSetupActivationResult(
    string ProfilePath,
    string VehicleId,
    string PreviousActiveSetupId,
    string ActiveSetupId,
    GarageResolvedSetupVehicle Resolved);
