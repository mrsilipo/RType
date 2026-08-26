using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageVehicleFactory
{
    public static JsonObject CreateOwnedVehicleFromPurchaseCar(
        string purchaseCarPath,
        string ownedVehicleId,
        string ownerProfileId,
        int garageSlot,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(ownedVehicleId))
        {
            throw new ArgumentException("Owned vehicle id must be provided.", nameof(ownedVehicleId));
        }

        if (string.IsNullOrWhiteSpace(ownerProfileId))
        {
            throw new ArgumentException("Owner profile id must be provided.", nameof(ownerProfileId));
        }

        string resolvedPurchaseCarPath = ResolveDataPath(purchaseCarPath);
        JsonObject purchaseCar = ReadObject(resolvedPurchaseCarPath);
        string purchaseCarId = ReadString(purchaseCar, Path.GetFileNameWithoutExtension(resolvedPurchaseCarPath), "id");
        string purchaseDisplayName = ReadString(purchaseCar, purchaseCarId, "displayName");

        JsonObject owned = CloneObject(purchaseCar);
        owned["id"] = ownedVehicleId;
        owned["displayName"] = string.IsNullOrWhiteSpace(displayName)
            ? $"Owned {purchaseDisplayName}"
            : displayName;
        owned["role"] = "owned_vehicle";
        owned["template"] = new JsonObject
        {
            ["sourcePurchaseCar"] = NormalizeDataPath(purchaseCarPath),
            ["purchaseCarId"] = purchaseCarId
        };
        owned["ownership"] = new JsonObject
        {
            ["source"] = "player_garage",
            ["playerOwned"] = true,
            ["ownerProfileId"] = ownerProfileId,
            ["garageSlot"] = garageSlot,
            ["purchaseState"] = "owned"
        };
        owned["notes"] = new JsonArray
        {
            $"Created from purchase car template {purchaseCarId}.",
            "Owned vehicle records are the modifiable garage state; purchase-car templates remain immutable."
        };

        return owned;
    }

    public static string SaveOwnedVehicle(
        JsonObject ownedVehicle,
        string outputDirectory,
        bool overwrite = false)
    {
        string id = ReadString(ownedVehicle, string.Empty, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidDataException("Owned vehicle JSON does not declare an id.");
        }

        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, $"{id}.json");
        if (File.Exists(outputPath) && !overwrite)
        {
            throw new IOException($"Owned vehicle already exists: {outputPath}");
        }

        File.WriteAllText(outputPath, ownedVehicle.ToJsonString(CreateIndentedJsonOptions()));
        return outputPath;
    }

    private static JsonSerializerOptions CreateIndentedJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
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

    private static string ReadString(JsonObject root, string fallback, params string[] path)
    {
        JsonNode? node = root;
        foreach (string segment in path)
        {
            node = node is JsonObject current ? current[segment] : null;
            if (node is null)
            {
                return fallback;
            }
        }

        return node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : fallback;
    }

    private static string NormalizeDataPath(string path)
    {
        return path.Replace('\\', '/');
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
