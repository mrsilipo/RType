using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageActiveVehicleProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-active-vehicle-probe", Guid.NewGuid().ToString("N"));

        try
        {
            string firstOwnedPath = CreateOwnedVehicle(tempRoot, purchasePath, "vehicle_probe_0001", 1);
            string secondOwnedPath = CreateOwnedVehicle(tempRoot, purchasePath, "vehicle_probe_0002", 2);
            string profilePath = WriteProfile(tempRoot, firstOwnedPath, secondOwnedPath);
            byte[] firstBeforeHash = SHA256.HashData(File.ReadAllBytes(firstOwnedPath));
            byte[] secondBeforeHash = SHA256.HashData(File.ReadAllBytes(secondOwnedPath));

            GarageRuntimeVehicleSelection initial = GarageRuntimeVehicleResolver.Resolve(profilePath);
            Require(initial.Vehicle.VehicleId == "vehicle_probe_0001", "initial active vehicle was not selected");
            Require(initial.Setup?.SetupId == "probe_0001_setup", "initial active setup was not selected");

            GarageActiveVehicleSelectionResult switched = GarageActiveVehicleService.SetActiveVehicle(
                profilePath,
                "vehicle_probe_0002");
            Require(switched.PreviousActiveVehicleId == "vehicle_probe_0001", "previous active vehicle was not reported");
            Require(switched.ActiveVehicleId == "vehicle_probe_0002", "new active vehicle was not reported");
            Require(switched.Runtime.Vehicle.VehicleId == "vehicle_probe_0002", "runtime did not select switched active vehicle");
            Require(switched.Runtime.Setup is null, "vehicle without saved setup unexpectedly selected a setup");

            byte[] profileAfterSwitchHash = SHA256.HashData(File.ReadAllBytes(profilePath));
            RequireThrows(
                () => GarageActiveVehicleService.SetActiveVehicle(profilePath, "missing_vehicle"),
                "missing owned vehicle was accepted");
            Require(profileAfterSwitchHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profilePath))), "failed active vehicle switch partially wrote profile");

            GarageActiveVehicleSelectionResult cleared = GarageActiveVehicleService.ClearActiveVehicle(profilePath);
            Require(cleared.PreviousActiveVehicleId == "vehicle_probe_0002", "clear did not report previous active vehicle");
            Require(cleared.ActiveVehicleId == "vehicle_probe_0001", "clear did not fall back to first garage slot");
            Require(cleared.Runtime.Setup?.SetupId == "probe_0001_setup", "clear did not fall back to first vehicle active setup");

            Require(firstBeforeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(firstOwnedPath))), "active vehicle selection mutated first owned vehicle");
            Require(secondBeforeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(secondOwnedPath))), "active vehicle selection mutated second owned vehicle");

            Console.WriteLine("Garage active vehicle probe");
            Console.WriteLine($"  initial: {initial.Vehicle.VehicleId}, setup {initial.Setup?.SetupId ?? "(none)"}");
            Console.WriteLine($"  switched: {switched.ActiveVehicleId}, setup {switched.Runtime.Setup?.SetupId ?? "(none)"}");
            Console.WriteLine($"  cleared fallback: {cleared.ActiveVehicleId}, setup {cleared.Runtime.Setup?.SetupId ?? "(none)"}");
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

    private static string CreateOwnedVehicle(string tempRoot, string purchasePath, string vehicleId, int garageSlot)
    {
        JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
            purchasePath,
            vehicleId,
            "probe_active_vehicle_profile",
            garageSlot,
            $"Probe Active Vehicle {garageSlot}");
        return GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
    }

    private static string WriteProfile(string tempRoot, string firstOwnedPath, string secondOwnedPath)
    {
        string setupPath = WriteSetup(tempRoot);
        string profilePath = Path.Combine(tempRoot, "probe_active_vehicle_profile.json");
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_active_vehicle_profile",
            ["displayName"] = "Probe Active Vehicle Profile",
            ["credits"] = 10000,
            ["activeVehicleId"] = "vehicle_probe_0001",
            ["ownedVehicles"] = new JsonArray
            {
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_0001",
                    ["path"] = firstOwnedPath,
                    ["garageSlot"] = 1
                },
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_0002",
                    ["path"] = secondOwnedPath,
                    ["garageSlot"] = 2
                }
            },
            ["savedSetups"] = new JsonArray
            {
                new JsonObject
                {
                    ["setupId"] = "probe_0001_setup",
                    ["vehicleId"] = "vehicle_probe_0001",
                    ["path"] = setupPath,
                    ["displayName"] = "Vehicle 1 Setup",
                    ["active"] = true
                }
            },
            ["inventory"] = new JsonObject
            {
                ["ownedPartIds"] = new JsonArray(),
                ["purchasablePartIds"] = new JsonArray { "*" },
                ["lockedPartIds"] = new JsonArray()
            }
        };

        File.WriteAllText(profilePath, profile.ToJsonString(CreateJsonOptions()));
        return profilePath;
    }

    private static string WriteSetup(string tempRoot)
    {
        string setupPath = Path.Combine(tempRoot, "probe_0001_setup.json");
        JsonObject setup = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_0001_setup",
            ["displayName"] = "Vehicle 1 Setup",
            ["ownerProfileId"] = "probe_active_vehicle_profile",
            ["vehicleId"] = "vehicle_probe_0001",
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
                    ["alignment"] = "street_sport_alignment"
                },
                ["tuning"] = new JsonObject
                {
                    ["steering"] = "street_quick_steering_setup",
                    ["handling"] = "street_arcade_handling_setup"
                }
            }
        };

        File.WriteAllText(setupPath, setup.ToJsonString(CreateJsonOptions()));
        return setupPath;
    }

    private static System.Text.Json.JsonSerializerOptions CreateJsonOptions()
    {
        return new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    private static void RequireThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException($"Garage active vehicle probe failed: {message}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage active vehicle probe failed: {message}.");
        }
    }
}
