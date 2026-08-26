using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageProfileIntegrityProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-profile-integrity-probe", Guid.NewGuid().ToString("N"));

        try
        {
            string cleanOwnedPath = CreateOwnedVehicle(tempRoot, purchasePath, "vehicle_probe_integrity_0001", "probe_integrity_profile", 1);
            string cleanSetupPath = WriteSetup(tempRoot, "probe_integrity_setup", "probe_integrity_profile", "vehicle_probe_integrity_0001");
            string cleanProfilePath = WriteProfile(
                tempRoot,
                "probe_integrity_profile",
                "vehicle_probe_integrity_0001",
                [("vehicle_probe_integrity_0001", cleanOwnedPath, 1)],
                [("probe_integrity_setup", "vehicle_probe_integrity_0001", cleanSetupPath, true)],
                ownedPartIds: ["fuel_98ron"],
                purchasablePartIds: ["*"],
                lockedPartIds: ["engine_audio_pro_racing"]);

            GarageProfileIntegrityReport clean = GarageProfileIntegrityValidator.Validate(cleanProfilePath);
            Require(clean.IsClean, $"clean profile reported warnings: {string.Join("; ", clean.Warnings.Select(warning => warning.Code))}");

            string wrongOwnerOwnedPath = CreateOwnedVehicle(tempRoot, purchasePath, "vehicle_probe_integrity_wrong_owner", "other_profile", 2);
            string brokenSetupPath = WriteSetup(tempRoot, "probe_integrity_bad_setup_file", "other_profile", "vehicle_probe_integrity_0001");
            string brokenProfilePath = WriteProfile(
                tempRoot,
                "probe_integrity_broken_profile",
                "missing_active_vehicle",
                [
                    ("vehicle_probe_integrity_0001", cleanOwnedPath, 1),
                    ("vehicle_probe_integrity_0001", cleanOwnedPath, 1),
                    ("vehicle_probe_integrity_wrong_owner", wrongOwnerOwnedPath, 1)
                ],
                [
                    ("probe_integrity_setup", "vehicle_probe_integrity_0001", cleanSetupPath, true),
                    ("probe_integrity_setup", "vehicle_probe_integrity_0001", cleanSetupPath, true),
                    ("probe_integrity_missing_setup", "vehicle_probe_integrity_missing", "missing_setup.json", false),
                    ("probe_integrity_bad_setup_ref", "vehicle_probe_integrity_0001", brokenSetupPath, false)
                ],
                ownedPartIds: ["fuel_98ron", "engine_audio_pro_racing", "missing_owned_part"],
                purchasablePartIds: ["fuel_e85", "missing_purchasable_part"],
                lockedPartIds: ["engine_audio_pro_racing", "missing_locked_part"]);

            GarageProfileIntegrityReport broken = GarageProfileIntegrityValidator.Validate(brokenProfilePath);
            Require(!broken.IsClean, "broken profile reported clean");
            RequireWarning(broken, "active_vehicle_not_owned");
            RequireWarning(broken, "duplicate_owned_vehicle_id");
            RequireWarning(broken, "duplicate_garage_slot");
            RequireWarning(broken, "owned_vehicle_owner_mismatch");
            RequireWarning(broken, "duplicate_saved_setup_id");
            RequireWarning(broken, "multiple_active_setups_for_vehicle");
            RequireWarning(broken, "saved_setup_vehicle_not_owned");
            RequireWarning(broken, "saved_setup_owner_mismatch");
            RequireWarning(broken, "inventory_owned_part_is_locked");
            RequireWarning(broken, "inventory_owned_part_missing_catalog");
            RequireWarning(broken, "inventory_locked_part_missing_catalog");
            RequireWarning(broken, "inventory_purchasable_part_missing_catalog");
            RequireWarning(broken, "inventory_purchasable_part_missing_price");

            Console.WriteLine("Garage profile integrity probe");
            Console.WriteLine($"  clean: warnings {clean.Warnings.Count}, info {clean.Info.Count}");
            Console.WriteLine($"  broken warnings: {string.Join(", ", broken.Warnings.Select(warning => warning.Code).Distinct())}");
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

    private static string CreateOwnedVehicle(
        string tempRoot,
        string purchasePath,
        string vehicleId,
        string ownerProfileId,
        int garageSlot)
    {
        JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
            purchasePath,
            vehicleId,
            ownerProfileId,
            garageSlot,
            $"Probe Integrity Vehicle {garageSlot}");
        return GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
    }

    private static string WriteSetup(string tempRoot, string setupId, string ownerProfileId, string vehicleId)
    {
        string setupPath = Path.Combine(tempRoot, $"{setupId}.json");
        JsonObject setup = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = setupId,
            ["displayName"] = setupId,
            ["ownerProfileId"] = ownerProfileId,
            ["vehicleId"] = vehicleId,
            ["setupType"] = "saved_setup",
            ["overrides"] = new JsonObject
            {
                ["engine"] = new JsonObject
                {
                    ["tuneId"] = "tune_b16b_factory",
                    ["fuelSelected"] = "fuel_98ron"
                },
                ["suspension"] = new JsonObject
                {
                    ["alignment"] = "stock_ek9_alignment"
                },
                ["tuning"] = new JsonObject
                {
                    ["steering"] = "stock_ek9_steering_setup",
                    ["handling"] = "stock_ek9_arcade_handling_setup"
                }
            }
        };

        File.WriteAllText(setupPath, setup.ToJsonString(CreateJsonOptions()));
        return setupPath;
    }

    private static string WriteProfile(
        string tempRoot,
        string profileId,
        string activeVehicleId,
        IReadOnlyList<(string VehicleId, string Path, int GarageSlot)> vehicles,
        IReadOnlyList<(string SetupId, string VehicleId, string Path, bool Active)> setups,
        IReadOnlyList<string> ownedPartIds,
        IReadOnlyList<string> purchasablePartIds,
        IReadOnlyList<string> lockedPartIds)
    {
        string profilePath = Path.Combine(tempRoot, $"{profileId}.json");
        JsonArray ownedVehicles = [];
        foreach ((string vehicleId, string path, int garageSlot) in vehicles)
        {
            ownedVehicles.Add(new JsonObject
            {
                ["vehicleId"] = vehicleId,
                ["path"] = path,
                ["garageSlot"] = garageSlot
            });
        }

        JsonArray savedSetups = [];
        foreach ((string setupId, string vehicleId, string path, bool active) in setups)
        {
            savedSetups.Add(new JsonObject
            {
                ["setupId"] = setupId,
                ["vehicleId"] = vehicleId,
                ["path"] = path,
                ["displayName"] = setupId,
                ["active"] = active
            });
        }

        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = profileId,
            ["displayName"] = profileId,
            ["credits"] = 10000,
            ["activeVehicleId"] = activeVehicleId,
            ["ownedVehicles"] = ownedVehicles,
            ["savedSetups"] = savedSetups,
            ["inventory"] = new JsonObject
            {
                ["ownedPartIds"] = new JsonArray(ownedPartIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>()),
                ["purchasablePartIds"] = new JsonArray(purchasablePartIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>()),
                ["lockedPartIds"] = new JsonArray(lockedPartIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>())
            }
        };

        File.WriteAllText(profilePath, profile.ToJsonString(CreateJsonOptions()));
        return profilePath;
    }

    private static System.Text.Json.JsonSerializerOptions CreateJsonOptions()
    {
        return new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private static void RequireWarning(GarageProfileIntegrityReport report, string code)
    {
        Require(report.Warnings.Any(warning => warning.Code.Equals(code, StringComparison.OrdinalIgnoreCase)), $"expected warning {code}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage profile integrity probe failed: {message}.");
        }
    }
}
