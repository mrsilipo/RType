using System.Text.Json;
using RType.Vehicle;

namespace RType.Data;

internal static class VehicleRuntimeLoader
{
    public static VehicleSimulationParameters LoadSimulationParameters(
        string path,
        string garageProfilePath,
        string garageVehicleIdOrPath,
        string garageSetupIdOrPath)
    {
        if (!string.IsNullOrWhiteSpace(garageProfilePath))
        {
            GarageRuntimeVehicleSelection selected = GarageRuntimeVehicleResolver.Resolve(
                garageProfilePath,
                garageVehicleIdOrPath,
                string.IsNullOrWhiteSpace(garageSetupIdOrPath) ? "active" : garageSetupIdOrPath);
            return selected.Parameters;
        }

        return LoadSimulationParameters(path);
    }

    public static VehicleSimulationParameters LoadSimulationParameters(string path)
    {
        string resolvedPath = ResolveDataPath(path);
        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });

        if (!document.RootElement.TryGetProperty("assembly", out _))
        {
            throw new InvalidDataException(
                $"Vehicle runtime JSON must be an assembled purchase/owned vehicle with an assembly block: {path}");
        }

        return VehicleBuildDefinitionLoader.LoadSimulationParameters(resolvedPath);
    }

    private static string ResolveDataPath(string path)
    {
        path = VehiclePathMigration.ResolveLegacyRuntimeVehiclePath(path);

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string normalized = path;
        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, normalized),
            Path.Combine(AppContext.BaseDirectory, normalized)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Vehicle runtime JSON was not found: {path}", path);
    }

}
