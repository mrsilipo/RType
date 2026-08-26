using System.Text.Json;

namespace RType.Data;

internal static class GarageProfileLoader
{
    public static GarageProfile Load(string profilePath)
    {
        string resolvedPath = ResolveDataPath(profilePath);
        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;

        return new GarageProfile(
            ReadString(root, Path.GetFileNameWithoutExtension(resolvedPath), "id"),
            ReadString(root, string.Empty, "displayName"),
            ReadSingle(root, 0f, "credits"),
            ReadString(root, string.Empty, "activeVehicleId"),
            [.. ReadOwnedVehicles(root)],
            [.. ReadSavedSetups(root)],
            new GarageInventory(
                new HashSet<string>(ReadStringArray(root, "inventory", "ownedPartIds"), StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(ReadStringArray(root, "inventory", "purchasablePartIds"), StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(ReadStringArray(root, "inventory", "lockedPartIds"), StringComparer.OrdinalIgnoreCase)));
    }

    private static IEnumerable<GarageOwnedVehicleReference> ReadOwnedVehicles(JsonElement root)
    {
        if (!TryGet(root, out JsonElement ownedVehicles, "ownedVehicles") ||
            ownedVehicles.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement vehicle in ownedVehicles.EnumerateArray())
        {
            yield return new GarageOwnedVehicleReference(
                ReadString(vehicle, string.Empty, "vehicleId"),
                ReadString(vehicle, string.Empty, "path"),
                ReadInt32(vehicle, 0, "garageSlot"));
        }
    }

    private static IEnumerable<GarageSavedSetupReference> ReadSavedSetups(JsonElement root)
    {
        if (!TryGet(root, out JsonElement savedSetups, "savedSetups") ||
            savedSetups.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement setup in savedSetups.EnumerateArray())
        {
            yield return new GarageSavedSetupReference(
                ReadString(setup, string.Empty, "setupId"),
                ReadString(setup, string.Empty, "vehicleId"),
                ReadString(setup, string.Empty, "path"),
                ReadString(setup, string.Empty, "displayName"),
                ReadBoolean(setup, false, "active"));
        }
    }

    private static string[] ReadStringArray(JsonElement root, params string[] path)
    {
        if (!TryGet(root, out JsonElement array, path) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)];
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }

    private static int ReadInt32(JsonElement root, int fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : fallback;
    }

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float result) ? result : fallback;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
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

internal sealed record GarageProfile(
    string Id,
    string DisplayName,
    float Credits,
    string ActiveVehicleId,
    IReadOnlyList<GarageOwnedVehicleReference> OwnedVehicles,
    IReadOnlyList<GarageSavedSetupReference> SavedSetups,
    GarageInventory Inventory);

internal sealed record GarageOwnedVehicleReference(
    string VehicleId,
    string Path,
    int GarageSlot);

internal sealed record GarageSavedSetupReference(
    string SetupId,
    string VehicleId,
    string Path,
    string DisplayName,
    bool Active);

internal sealed record GarageInventory(
    IReadOnlySet<string> OwnedPartIds,
    IReadOnlySet<string> PurchasablePartIds,
    IReadOnlySet<string> LockedPartIds)
{
    public bool Owns(string partId) => Contains(OwnedPartIds, partId);

    public bool IsLocked(string partId) => Contains(LockedPartIds, partId);

    public bool IsPurchasable(string partId) => Contains(PurchasablePartIds, partId) || Contains(PurchasablePartIds, "*");

    private static bool Contains(IReadOnlySet<string> ids, string id) =>
        ids.Any(candidate => candidate.Equals(id, StringComparison.OrdinalIgnoreCase));
}
