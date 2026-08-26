using System.Text.Json;
using RType.Data;

namespace RType.Core;

internal static class CatalogInheritanceProbe
{
    private static readonly string[] BodyShellIds =
    [
        "stock_ek9_body_shell",
        "street_reinforced_ek9_body_shell",
        "club_sport_seam_welded_ek9_body_shell",
        "pro_racing_lightweight_ek9_body_shell"
    ];

    public static void Run()
    {
        foreach (string bodyShellId in BodyShellIds)
        {
            JsonElement bodyShell = VehicleBuildDefinitionLoader.LoadVehicleCatalogItemForDiagnostics(bodyShellId);
            JsonElement data = Require(bodyShell, "data");
            float shellWeight = RequirePositive(bodyShell, "weightKg");
            float wheelbase = RequirePositive(data, "wheelbaseMeters");
            float frontTrack = RequirePositive(data, "frontTrackMeters");
            float rearTrack = RequirePositive(data, "rearTrackMeters");
            float baseCurbMass = RequirePositive(data, "baseCurbMassKg");
            float torsionalRigidity = RequirePositive(data, "torsionalRigidityNmPerDeg");
            JsonElement frontHardPoints = Require(data, "suspensionHardPoints", "front");
            JsonElement rearHardPoints = Require(data, "suspensionHardPoints", "rear");

            Console.WriteLine($"{bodyShellId}: shell {shellWeight:0.0}kg, wheelbase {wheelbase:0.000}m, tracks {frontTrack:0.000}/{rearTrack:0.000}m, stock reference mass {baseCurbMass:0.0}kg, rigidity {torsionalRigidity:0}Nm/deg");
            Console.WriteLine($"  front hard-points: {ReadString(frontHardPoints, string.Empty, "type")}, camber gain {RequireSingle(frontHardPoints, "camberGainDegreesPerMeter"):0.0}deg/m");
            Console.WriteLine($"  rear hard-points: {ReadString(rearHardPoints, string.Empty, "type")}, camber gain {RequireSingle(rearHardPoints, "camberGainDegreesPerMeter"):0.0}deg/m");
        }
    }

    private static JsonElement Require(JsonElement root, params string[] path)
    {
        return TryGet(root, out JsonElement value, path)
            ? value
            : throw new InvalidDataException($"Missing required JSON path '{string.Join(".", path)}'.");
    }

    private static float RequirePositive(JsonElement root, params string[] path)
    {
        float value = RequireSingle(root, path);
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidDataException($"Field '{string.Join(".", path)}' must be positive.");
        }

        return value;
    }

    private static float RequireSingle(JsonElement root, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.TryGetSingle(out float result)
            ? result
            : throw new InvalidDataException($"Field '{string.Join(".", path)}' must be numeric.");
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
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }
}
