using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageVehiclePurchaseProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        byte[] purchaseBeforeHash = SHA256.HashData(File.ReadAllBytes(purchasePath));
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-vehicle-purchase-probe", Guid.NewGuid().ToString("N"));

        try
        {
            string profilePath = WriteProfile(tempRoot, credits: 40000f);
            string ownedVehicleDirectory = Path.Combine(tempRoot, "OwnedVehicles");

            GarageVehiclePurchaseResult firstPurchase = GarageShopService.PurchaseVehicle(
                profilePath,
                purchasePath,
                ownedVehicleDirectory);
            Require(firstPurchase.OwnedVehicleId.Equals("vehicle_0001", StringComparison.OrdinalIgnoreCase), "first owned vehicle id did not allocate");
            Require(firstPurchase.GarageSlot == 1, "first garage slot did not allocate");
            Require(Math.Abs(firstPurchase.CreditsBefore - 40000f) < 0.01f, "first purchase before credits did not resolve");
            Require(Math.Abs(firstPurchase.CreditsAfter - 21500f) < 0.01f, "first purchase after credits did not resolve");
            Require(File.Exists(firstPurchase.OwnedVehiclePath), "first owned vehicle file was not created");
            Require(firstPurchase.OwnedAssembly.Classification.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase), "first purchase did not create owned vehicle");
            Require(firstPurchase.OwnedAssembly.PlayerOwned, "first purchase did not mark vehicle player-owned");
            Require(firstPurchase.OwnedAssembly.OwnerProfileId.Equals("probe_vehicle_purchase_profile", StringComparison.OrdinalIgnoreCase), "first purchase owner profile mismatch");
            Require(firstPurchase.OwnedAssembly.Engine.EngineId.Equals("engine_b16b", StringComparison.OrdinalIgnoreCase), "first purchase did not preserve stock B16B assembly");
            Require(firstPurchase.BecameActiveVehicle, "first purchase did not initialize active vehicle");

            GarageProfile profileAfterFirst = GarageProfileLoader.Load(profilePath);
            Require(profileAfterFirst.ActiveVehicleId.Equals("vehicle_0001", StringComparison.OrdinalIgnoreCase), "profile active vehicle was not initialized after first purchase");

            GarageVehiclePurchaseResult secondPurchase = GarageShopService.PurchaseVehicle(
                profilePath,
                purchasePath,
                ownedVehicleDirectory);
            Require(secondPurchase.OwnedVehicleId.Equals("vehicle_0002", StringComparison.OrdinalIgnoreCase), "second owned vehicle id did not allocate");
            Require(secondPurchase.GarageSlot == 2, "second garage slot did not allocate");
            Require(Math.Abs(secondPurchase.CreditsAfter - 3000f) < 0.01f, "second purchase after credits did not resolve");
            Require(!secondPurchase.BecameActiveVehicle, "second purchase incorrectly replaced active vehicle");

            GarageProfile profile = GarageProfileLoader.Load(profilePath);
            Require(profile.OwnedVehicles.Count == 2, "profile did not register two owned vehicles");
            Require(profile.OwnedVehicles.Any(vehicle => vehicle.VehicleId.Equals("vehicle_0001", StringComparison.OrdinalIgnoreCase)), "profile missing first owned vehicle reference");
            Require(profile.OwnedVehicles.Any(vehicle => vehicle.VehicleId.Equals("vehicle_0002", StringComparison.OrdinalIgnoreCase)), "profile missing second owned vehicle reference");
            Require(profile.ActiveVehicleId.Equals("vehicle_0001", StringComparison.OrdinalIgnoreCase), "second purchase changed active vehicle");

            JsonObject profileJson = ReadObject(profilePath);
            JsonArray history = profileJson["transactionHistory"] as JsonArray ??
                throw new InvalidOperationException("Garage vehicle purchase probe failed: vehicle purchase transaction history was not recorded.");
            Require(history.Count == 2, "vehicle purchase transaction history was not recorded");
            Require(history[0]?["becameActiveVehicle"]?.GetValue<bool>() == true, "first transaction did not record active vehicle initialization");
            Require(history[1]?["becameActiveVehicle"]?.GetValue<bool>() == false, "second transaction did not record active vehicle preservation");

            RequireThrows(
                () => GarageShopService.PurchaseVehicle(profilePath, purchasePath, ownedVehicleDirectory),
                "insufficient-credit vehicle purchase was allowed");

            RequireThrows(
                () => GarageShopService.PurchaseVehicle(profilePath, firstPurchase.OwnedVehiclePath, ownedVehicleDirectory),
                "owned vehicle was allowed as a purchase-car template");

            string wrongPathPriceCatalog = WriteVehiclePriceCatalog(
                tempRoot,
                "wrong_path_vehicle_prices.json",
                "2000_Ek9_Stock",
                "Data/PurchaseCars/Missing_Ek9_Stock.json",
                18500f);
            RequireThrows(
                () => GarageShopService.PurchaseVehicle(
                    WriteProfile(tempRoot, credits: 40000f, fileName: "probe_wrong_path_profile.json"),
                    purchasePath,
                    Path.Combine(tempRoot, "WrongPathOwnedVehicles"),
                    wrongPathPriceCatalog),
                "vehicle price row with matching id but wrong path was accepted");

            string wrongIdPriceCatalog = WriteVehiclePriceCatalog(
                tempRoot,
                "wrong_id_vehicle_prices.json",
                "Missing_Ek9_Stock",
                "Data/PurchaseCars/2000_Ek9_Stock.json",
                18500f);
            RequireThrows(
                () => GarageShopService.PurchaseVehicle(
                    WriteProfile(tempRoot, credits: 40000f, fileName: "probe_wrong_id_profile.json"),
                    purchasePath,
                    Path.Combine(tempRoot, "WrongIdOwnedVehicles"),
                    wrongIdPriceCatalog),
                "vehicle price row with matching path but wrong id was accepted");

            byte[] purchaseAfterHash = SHA256.HashData(File.ReadAllBytes(purchasePath));
            Require(purchaseBeforeHash.SequenceEqual(purchaseAfterHash), "purchase-car template hash changed");

            Console.WriteLine("Garage vehicle purchase probe");
            Console.WriteLine($"  first: {firstPurchase.PurchaseCarId} -> {firstPurchase.OwnedVehicleId}, slot {firstPurchase.GarageSlot}, credits {firstPurchase.CreditsBefore:0}->{firstPurchase.CreditsAfter:0}");
            Console.WriteLine($"  second: {secondPurchase.PurchaseCarId} -> {secondPurchase.OwnedVehicleId}, slot {secondPurchase.GarageSlot}, credits {secondPurchase.CreditsBefore:0}->{secondPurchase.CreditsAfter:0}");
            Console.WriteLine($"  profile vehicles: {profile.OwnedVehicles.Count}, active: {profile.ActiveVehicleId}, transactions: 2");
            Console.WriteLine("  result: PASS");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string WriteProfile(string tempRoot, float credits, string fileName = "probe_vehicle_purchase_profile.json")
    {
        Directory.CreateDirectory(tempRoot);
        string profilePath = Path.Combine(tempRoot, fileName);
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_vehicle_purchase_profile",
            ["displayName"] = "Probe Vehicle Purchase Profile",
            ["credits"] = credits,
            ["ownedVehicles"] = new JsonArray(),
            ["inventory"] = new JsonObject
            {
                ["ownedPartIds"] = new JsonArray(),
                ["purchasablePartIds"] = new JsonArray { "*" },
                ["lockedPartIds"] = new JsonArray()
            }
        };

        File.WriteAllText(profilePath, profile.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
        return profilePath;
    }

    private static string WriteVehiclePriceCatalog(
        string tempRoot,
        string fileName,
        string purchaseCarId,
        string purchaseCarPath,
        float price)
    {
        string catalogPath = Path.Combine(tempRoot, fileName);
        JsonObject catalog = new()
        {
            ["schemaVersion"] = 1,
            ["currency"] = "credits",
            ["prices"] = new JsonArray
            {
                new JsonObject
                {
                    ["purchaseCarId"] = purchaseCarId,
                    ["path"] = purchaseCarPath,
                    ["price"] = price
                }
            }
        };

        File.WriteAllText(catalogPath, catalog.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
        return catalogPath;
    }

    private static JsonObject ReadObject(string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? throw new InvalidDataException($"JSON file is not an object: {path}");
    }

    private static void RequireThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException($"Garage vehicle purchase probe failed: {message}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage vehicle purchase probe failed: {message}.");
        }
    }
}
