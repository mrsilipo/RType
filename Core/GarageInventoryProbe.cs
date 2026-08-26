using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageInventoryProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-inventory-probe", Guid.NewGuid().ToString("N"));

        try
        {
            JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_inventory",
                "probe_inventory_profile",
                4,
                "Probe Inventory EK9");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
            string profilePath = WriteProfile(tempRoot, ownedPath);

            GarageInventoryModPathReport report = GarageInventoryModPathResolver.BuildReport(profilePath, "vehicle_probe_inventory");
            Console.WriteLine("Garage inventory probe");
            Console.WriteLine($"  profile: {report.Profile.Id}, credits {report.Profile.Credits:0}");
            Console.WriteLine($"  vehicle: {report.Vehicle.VehicleId}, slot {report.Vehicle.GarageSlot}");
            Console.WriteLine(
                $"  options: installed {report.Installed.Count()}, owned-ready {report.OwnedReady.Count()}, purchasable {report.Purchasable.Count()}, locked {report.Locked.Count()}, blocked {report.BlockedByBuild.Count()}, not-owned {report.NotOwned.Count()}");

            RequireAvailability(report, "fuel", "fuel_e85", GarageInventoryAvailability.OwnedReady);
            RequireAvailability(report, "engineAudioDsp", "engine_audio_street", GarageInventoryAvailability.Purchasable);
            RequireAvailability(report, "tyrePackage", "tyre_package_semi_slick_aggressive", GarageInventoryAvailability.Locked);
            RequireAvailability(report, "displacement", "displacement_pro_high_comp", GarageInventoryAvailability.BlockedByBuild);

            GarageModInstallResult fuelInstall = GarageModInstaller.ApplyProfileOwnedOption(
                profilePath,
                "vehicle_probe_inventory",
                "fuel",
                "fuel_e85");
            Require(fuelInstall.After.Engine.FuelId.Equals("fuel_e85", StringComparison.OrdinalIgnoreCase), "profile-owned fuel install failed");
            Console.WriteLine($"  owned install: fuel -> {fuelInstall.After.Engine.FuelId}");

            RequireThrows(
                () => GarageModInstaller.ApplyProfileOwnedOption(profilePath, "vehicle_probe_inventory", "engineAudioDsp", "engine_audio_street"),
                "purchasable unowned part installed before purchase");

            GaragePartPurchaseResult purchase = GarageShopService.PurchasePart(profilePath, "engine_audio_street");
            Require(purchase.OwnedAfterPurchase, "purchased part was not added to inventory");
            Require(Math.Abs(purchase.CreditsBefore - 15000f) < 0.01f, "purchase receipt before credits did not resolve");
            Require(Math.Abs(purchase.CreditsAfter - 12500f) < 0.01f, "purchase receipt after credits did not resolve");
            Console.WriteLine($"  purchase: engine_audio_street for {purchase.Price:0} credits, balance {purchase.CreditsAfter:0}");

            GarageInventoryModPathReport afterPurchaseReport = GarageInventoryModPathResolver.BuildReport(profilePath, "vehicle_probe_inventory");
            RequireAvailability(afterPurchaseReport, "engineAudioDsp", "engine_audio_street", GarageInventoryAvailability.OwnedReady);

            GarageModInstallResult purchasedInstall = GarageModInstaller.ApplyProfileOwnedOption(
                profilePath,
                "vehicle_probe_inventory",
                "engineAudioDsp",
                "engine_audio_street");
            Require(purchasedInstall.After.Engine.EngineAudioDspId.Equals("engine_audio_street", StringComparison.OrdinalIgnoreCase), "transaction-overridden purchasable install failed");
            Console.WriteLine($"  purchased install: audio dsp -> {purchasedInstall.After.Engine.EngineAudioDspId}");

            RequireThrows(
                () => GarageShopService.PurchasePart(profilePath, "engine_audio_street"),
                "already-owned part was purchased twice");

            RequireThrows(
                () => GarageModInstaller.ApplyProfileOwnedOption(profilePath, "vehicle_probe_inventory", "tyrePackage", "tyre_package_semi_slick_aggressive"),
                "locked tyre package installed");

            RequireThrows(
                () => GarageShopService.PurchasePart(profilePath, "tyre_package_semi_slick_aggressive"),
                "locked tyre package was purchased");

            string stalePricePath = WriteStalePriceCatalog(tempRoot);
            RequireThrows(
                () => GarageShopService.PurchasePart(profilePath, "missing_shop_part", stalePricePath),
                "stale priced part missing from catalogs was purchased");

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

    private static void RequireAvailability(
        GarageInventoryModPathReport report,
        string slot,
        string optionId,
        GarageInventoryAvailability expected)
    {
        GarageInventoryModOption option = report.Options.FirstOrDefault(candidate =>
            candidate.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
            candidate.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Garage inventory probe failed: option {slot}/{optionId} missing.");

        Require(option.Availability == expected, $"option {slot}/{optionId} expected {expected}, got {option.Availability}");
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

        throw new InvalidOperationException($"Garage inventory probe failed: {message}.");
    }

    private static string WriteProfile(string tempRoot, string ownedPath)
    {
        string profilePath = Path.Combine(tempRoot, "probe_inventory_profile.json");
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_inventory_profile",
            ["displayName"] = "Probe Inventory Profile",
            ["credits"] = 15000,
            ["ownedVehicles"] = new JsonArray
            {
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_inventory",
                    ["path"] = ownedPath,
                    ["garageSlot"] = 4
                }
            },
            ["inventory"] = new JsonObject
            {
                ["ownedPartIds"] = new JsonArray
                {
                    "fuel_98ron",
                    "fuel_e85",
                    "tyre_package_sports_hard_ek9"
                },
                ["purchasablePartIds"] = new JsonArray
                {
                    "*",
                    "missing_shop_part"
                },
                ["lockedPartIds"] = new JsonArray
                {
                    "tyre_package_semi_slick_aggressive",
                    "engine_audio_pro_racing"
                }
            }
        };

        File.WriteAllText(profilePath, profile.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
        return profilePath;
    }

    private static string WriteStalePriceCatalog(string tempRoot)
    {
        string pricePath = Path.Combine(tempRoot, "stale_part_prices.json");
        JsonObject catalog = new()
        {
            ["schemaVersion"] = 1,
            ["currency"] = "credits",
            ["prices"] = new JsonArray
            {
                new JsonObject
                {
                    ["partId"] = "missing_shop_part",
                    ["price"] = 1
                }
            }
        };

        File.WriteAllText(pricePath, catalog.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }));
        return pricePath;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage inventory probe failed: {message}.");
        }
    }
}
