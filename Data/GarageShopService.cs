using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RType.Data;

internal static class GarageShopService
{
    private const string DefaultPriceCatalogPath = "Data/Garage/part_prices.json";
    private const string DefaultVehiclePriceCatalogPath = "Data/Garage/vehicle_prices.json";

    public static GaragePartPurchaseResult PurchasePart(
        string profilePath,
        string partId,
        string priceCatalogPath = DefaultPriceCatalogPath)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            throw new ArgumentException("A part id must be provided.", nameof(partId));
        }

        string resolvedProfilePath = ResolveDataPath(profilePath);
        JsonObject profileJson = ReadObject(resolvedProfilePath);
        GarageProfile profile = GarageProfileLoader.Load(resolvedProfilePath);

        if (profile.Inventory.Owns(partId))
        {
            throw new InvalidOperationException($"Profile {profile.Id} already owns part {partId}.");
        }

        if (profile.Inventory.IsLocked(partId))
        {
            throw new InvalidOperationException($"Part {partId} is locked for profile {profile.Id}.");
        }

        if (!profile.Inventory.IsPurchasable(partId))
        {
            throw new InvalidOperationException($"Part {partId} is not purchasable for profile {profile.Id}.");
        }

        GarageCatalogIdentityReport knownCatalogIds = GarageCatalogIdentityIndex.Load();
        if (!knownCatalogIds.IsClean)
        {
            string warnings = string.Join("; ", knownCatalogIds.Warnings.Select(warning => $"{warning.Code}: {warning.Message}"));
            throw new InvalidOperationException($"Garage catalog identity index could not load cleanly: {warnings}");
        }

        if (!knownCatalogIds.Contains(partId))
        {
            throw new InvalidOperationException($"Part {partId} is not present in known part/tune/fuel catalogs.");
        }

        float price = LoadPrice(partId, priceCatalogPath);
        if (profile.Credits < price)
        {
            throw new InvalidOperationException(
                $"Profile {profile.Id} has {profile.Credits:0} credits but part {partId} costs {price:0}.");
        }

        float remainingCredits = profile.Credits - price;
        profileJson["credits"] = remainingCredits;
        JsonObject inventory = EnsureObject(profileJson, "inventory");
        JsonArray ownedPartIds = EnsureArray(inventory, "ownedPartIds");
        if (!ownedPartIds.Any(node => IsString(node, partId)))
        {
            ownedPartIds.Add(partId);
        }

        JsonArray transactionHistory = EnsureArray(profileJson, "transactionHistory");
        transactionHistory.Add(new JsonObject
        {
            ["type"] = "part_purchase",
            ["partId"] = partId,
            ["price"] = price,
            ["currency"] = "credits",
            ["purchasedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["creditsBefore"] = profile.Credits,
            ["creditsAfter"] = remainingCredits
        });

        File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateIndentedJsonOptions()));
        GarageProfile updatedProfile = GarageProfileLoader.Load(resolvedProfilePath);

        return new GaragePartPurchaseResult(
            resolvedProfilePath,
            profile.Id,
            partId,
            price,
            profile.Credits,
            remainingCredits,
            updatedProfile.Inventory.Owns(partId));
    }

    public static GarageVehiclePurchaseResult PurchaseVehicle(
        string profilePath,
        string purchaseCarPath,
        string ownedVehicleOutputDirectory,
        string vehiclePriceCatalogPath = DefaultVehiclePriceCatalogPath)
    {
        string resolvedProfilePath = ResolveDataPath(profilePath);
        string resolvedPurchaseCarPath = ResolveDataPath(purchaseCarPath);
        JsonObject profileJson = ReadObject(resolvedProfilePath);
        GarageProfile profile = GarageProfileLoader.Load(resolvedProfilePath);
        JsonObject purchaseCar = ReadObject(resolvedPurchaseCarPath);
        string purchaseCarId = ReadString(purchaseCar, Path.GetFileNameWithoutExtension(resolvedPurchaseCarPath), "id");
        string role = ReadString(purchaseCar, string.Empty, "role");

        if (!role.Equals("purchase_car_stock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Vehicle purchases must start from a purchase-car template. {purchaseCarPath} is role '{role}'.");
        }

        ResolvedVehicleAssembly purchaseAssembly = VehicleAssemblyResolver.Resolve(resolvedPurchaseCarPath);
        VehicleAssemblyValidationMessage[] purchaseWarnings =
            [.. purchaseAssembly.Validation.Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)];
        EngineAssemblyValidationMessage[] engineWarnings =
            [.. purchaseAssembly.Engine.Validation.Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];
        if (purchaseWarnings.Length > 0 || engineWarnings.Length > 0)
        {
            string warnings = string.Join("; ",
                purchaseWarnings.Select(message => $"{message.Code}: {message.Message}")
                    .Concat(engineWarnings.Select(message => $"{message.Code}: {message.Message}")));
            throw new InvalidOperationException($"Purchase car {purchaseCarId} does not resolve cleanly: {warnings}");
        }

        string normalizedPurchaseCarPath = NormalizeCatalogPath(resolvedPurchaseCarPath);
        float price = LoadVehiclePrice(purchaseCarId, normalizedPurchaseCarPath, vehiclePriceCatalogPath);
        if (profile.Credits < price)
        {
            throw new InvalidOperationException(
                $"Profile {profile.Id} has {profile.Credits:0} credits but vehicle {purchaseCarId} costs {price:0}.");
        }

        int garageSlot = NextGarageSlot(profile);
        string ownedVehicleId = NextOwnedVehicleId(profile);
        JsonObject ownedVehicle = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
            purchaseCarPath,
            ownedVehicleId,
            profile.Id,
            garageSlot);
        string ownedVehiclePath = GarageVehicleFactory.SaveOwnedVehicle(ownedVehicle, ownedVehicleOutputDirectory);
        ResolvedVehicleAssembly ownedAssembly = VehicleAssemblyResolver.Resolve(ownedVehiclePath);

        float remainingCredits = profile.Credits - price;
        profileJson["credits"] = remainingCredits;
        JsonArray ownedVehicles = EnsureArray(profileJson, "ownedVehicles");
        ownedVehicles.Add(new JsonObject
        {
            ["vehicleId"] = ownedVehicleId,
            ["path"] = NormalizeDataPath(ownedVehiclePath),
            ["garageSlot"] = garageSlot
        });

        bool becameActiveVehicle = string.IsNullOrWhiteSpace(profile.ActiveVehicleId);
        if (becameActiveVehicle)
        {
            profileJson["activeVehicleId"] = ownedVehicleId;
        }

        JsonArray transactionHistory = EnsureArray(profileJson, "transactionHistory");
        transactionHistory.Add(new JsonObject
        {
            ["type"] = "vehicle_purchase",
            ["purchaseCarId"] = purchaseCarId,
            ["purchaseCarPath"] = normalizedPurchaseCarPath,
            ["ownedVehicleId"] = ownedVehicleId,
            ["ownedVehiclePath"] = NormalizeDataPath(ownedVehiclePath),
            ["garageSlot"] = garageSlot,
            ["becameActiveVehicle"] = becameActiveVehicle,
            ["price"] = price,
            ["currency"] = "credits",
            ["purchasedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["creditsBefore"] = profile.Credits,
            ["creditsAfter"] = remainingCredits
        });

        File.WriteAllText(resolvedProfilePath, profileJson.ToJsonString(CreateIndentedJsonOptions()));

        return new GarageVehiclePurchaseResult(
            resolvedProfilePath,
            profile.Id,
            purchaseCarId,
            normalizedPurchaseCarPath,
            ownedVehicleId,
            NormalizeDataPath(ownedVehiclePath),
            garageSlot,
            price,
            profile.Credits,
            remainingCredits,
            becameActiveVehicle,
            ownedAssembly);
    }

    private static float LoadPrice(string partId, string priceCatalogPath)
    {
        JsonObject catalog = ReadObject(ResolveDataPath(priceCatalogPath));
        JsonArray prices = catalog["prices"] as JsonArray ??
            throw new InvalidDataException($"Garage price catalog does not contain prices array: {priceCatalogPath}");

        foreach (JsonNode? node in prices)
        {
            if (node is not JsonObject priceObject ||
                !ReadString(priceObject, string.Empty, "partId").Equals(partId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ReadSingle(priceObject, -1f, "price");
        }

        throw new InvalidOperationException($"No shop price is defined for purchasable part {partId}.");
    }

    private static float LoadVehiclePrice(string purchaseCarId, string normalizedPurchaseCarPath, string priceCatalogPath)
    {
        JsonObject catalog = ReadObject(ResolveDataPath(priceCatalogPath));
        JsonArray prices = catalog["prices"] as JsonArray ??
            throw new InvalidDataException($"Garage vehicle price catalog does not contain prices array: {priceCatalogPath}");

        foreach (JsonNode? node in prices)
        {
            if (node is not JsonObject priceObject)
            {
                continue;
            }

            string candidateId = ReadString(priceObject, string.Empty, "purchaseCarId");
            string candidatePath = NormalizeDataPath(ReadString(priceObject, string.Empty, "path"));
            if (!candidateId.Equals(purchaseCarId, StringComparison.OrdinalIgnoreCase) ||
                !candidatePath.Equals(normalizedPurchaseCarPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ReadSingle(priceObject, -1f, "price");
        }

        throw new InvalidOperationException($"No shop price is defined for purchase car {purchaseCarId}.");
    }

    private static int NextGarageSlot(GarageProfile profile)
    {
        int slot = 1;
        HashSet<int> used = [.. profile.OwnedVehicles.Select(vehicle => vehicle.GarageSlot)];
        while (used.Contains(slot))
        {
            slot++;
        }

        return slot;
    }

    private static string NextOwnedVehicleId(GarageProfile profile)
    {
        int next = 1;
        foreach (GarageOwnedVehicleReference vehicle in profile.OwnedVehicles)
        {
            if (TryReadVehicleNumber(vehicle.VehicleId, out int numeric))
            {
                next = Math.Max(next, numeric + 1);
            }
        }

        return $"vehicle_{next:0000}";
    }

    private static bool TryReadVehicleNumber(string vehicleId, out int number)
    {
        number = 0;
        string id = Path.GetFileNameWithoutExtension(vehicleId);
        const string prefix = "vehicle_";
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int start = prefix.Length;
        int end = start;
        while (end < id.Length && char.IsDigit(id[end]))
        {
            end++;
        }

        return end > start && int.TryParse(id[start..end], out number);
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

    private static JsonArray EnsureArray(JsonObject root, string name)
    {
        if (root[name] is JsonArray existing)
        {
            return existing;
        }

        JsonArray created = [];
        root[name] = created;
        return created;
    }

    private static bool IsString(JsonNode? node, string expected)
    {
        return node is JsonValue value &&
               value.GetValueKind() == JsonValueKind.String &&
               value.GetValue<string>().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject ReadObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? throw new InvalidDataException($"JSON file is not an object: {path}");
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

    private static float ReadSingle(JsonObject root, float fallback, params string[] path)
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

        return node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? value.GetValue<float>()
            : fallback;
    }

    private static JsonSerializerOptions CreateIndentedJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private static string NormalizeDataPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string NormalizeCatalogPath(string path)
    {
        string normalized = NormalizeDataPath(path);
        if (!Path.IsPathRooted(path))
        {
            return normalized;
        }

        string relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            return NormalizeDataPath(relative);
        }

        return normalized;
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

internal sealed record GaragePartPurchaseResult(
    string ProfilePath,
    string ProfileId,
    string PartId,
    float Price,
    float CreditsBefore,
    float CreditsAfter,
    bool OwnedAfterPurchase);

internal sealed record GarageVehiclePurchaseResult(
    string ProfilePath,
    string ProfileId,
    string PurchaseCarId,
    string PurchaseCarPath,
    string OwnedVehicleId,
    string OwnedVehiclePath,
    int GarageSlot,
    float Price,
    float CreditsBefore,
    float CreditsAfter,
    bool BecameActiveVehicle,
    ResolvedVehicleAssembly OwnedAssembly);
