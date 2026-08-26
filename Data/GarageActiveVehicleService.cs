using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageActiveVehicleService
{
    public static GarageActiveVehicleSelectionResult SetActiveVehicle(
        string profilePath,
        string ownedVehicleIdOrPath)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        GarageOwnedVehicleReference vehicle = FindOwnedVehicle(profile, ownedVehicleIdOrPath);
        ResolvedVehicleAssembly resolved = VehicleAssemblyResolver.Resolve(vehicle.Path);
        ThrowIfWarnings(resolved);

        string resolvedProfilePath = ResolveDataPath(profilePath);
        JsonObject profileJson = ReadObject(resolvedProfilePath);
        string previousActiveVehicleId = profile.ActiveVehicleId;
        profileJson["activeVehicleId"] = vehicle.VehicleId;
        File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateJsonOptions()));

        GarageRuntimeVehicleSelection runtime = GarageRuntimeVehicleResolver.Resolve(resolvedProfilePath);
        return new GarageActiveVehicleSelectionResult(
            resolvedProfilePath,
            previousActiveVehicleId,
            vehicle.VehicleId,
            vehicle,
            runtime);
    }

    public static GarageActiveVehicleSelectionResult ClearActiveVehicle(string profilePath)
    {
        GarageProfile profile = GarageProfileLoader.Load(profilePath);
        string resolvedProfilePath = ResolveDataPath(profilePath);
        JsonObject profileJson = ReadObject(resolvedProfilePath);
        string previousActiveVehicleId = profile.ActiveVehicleId;
        profileJson.Remove("activeVehicleId");
        File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateJsonOptions()));

        GarageRuntimeVehicleSelection runtime = GarageRuntimeVehicleResolver.Resolve(resolvedProfilePath);
        return new GarageActiveVehicleSelectionResult(
            resolvedProfilePath,
            previousActiveVehicleId,
            runtime.Vehicle.VehicleId,
            runtime.Vehicle,
            runtime);
    }

    private static void ThrowIfWarnings(ResolvedVehicleAssembly resolved)
    {
        VehicleAssemblyValidationMessage[] vehicleWarnings = [.. resolved.Validation
            .Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)];
        EngineAssemblyValidationMessage[] engineWarnings = [.. resolved.Engine.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];

        if (vehicleWarnings.Length == 0 && engineWarnings.Length == 0)
        {
            return;
        }

        string vehicleMessages = string.Join("; ", vehicleWarnings.Select(message => message.Message));
        string engineMessages = string.Join("; ", engineWarnings.Select(message => message.Message));
        throw new InvalidOperationException($"Active vehicle selection would produce validation warnings. Vehicle: {vehicleMessages}. Engine: {engineMessages}.");
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

internal sealed record GarageActiveVehicleSelectionResult(
    string ProfilePath,
    string PreviousActiveVehicleId,
    string ActiveVehicleId,
    GarageOwnedVehicleReference Vehicle,
    GarageRuntimeVehicleSelection Runtime);
