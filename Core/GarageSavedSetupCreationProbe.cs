using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageSavedSetupCreationProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-setup-create-probe", Guid.NewGuid().ToString("N"));

        try
        {
            JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_setup_create",
                "probe_setup_create_profile",
                1,
                "Probe Setup Create EK9");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
            string profilePath = WriteProfile(tempRoot, ownedPath);
            string setupDirectory = Path.Combine(tempRoot, "SavedSetups");
            byte[] ownedBeforeHash = SHA256.HashData(File.ReadAllBytes(ownedPath));

            GarageSavedSetupCreationResult first = GarageSavedSetupCreationService.CreateFromOwnedVehicle(
                profilePath,
                "vehicle_probe_setup_create",
                setupDirectory,
                "Stock Baseline",
                makeActive: false);

            Require(first.SetupId == "vehicle_probe_setup_create_setup_001", "first setup id did not allocate");
            Require(File.Exists(first.SetupPath), "first setup file was not created");
            Require(!first.Active, "first setup should not be active");
            Require(first.Setup.EngineTuneId == "tune_b16b_factory", "engine tune snapshot failed");
            Require(first.Setup.FuelId == "fuel_98ron", "fuel snapshot failed");
            Require(first.Setup.AlignmentId == "stock_ek9_alignment", "alignment snapshot failed");
            Require(first.Setup.SteeringSetupId == "stock_ek9_steering_setup", "steering snapshot failed");
            Require(first.Setup.HandlingSetupId == "stock_ek9_arcade_handling_setup", "handling snapshot failed");

            GarageProfile profileAfterFirst = GarageProfileLoader.Load(profilePath);
            Require(profileAfterFirst.SavedSetups.Count == 1, "profile did not register first saved setup");
            Require(!profileAfterFirst.SavedSetups[0].Active, "non-active setup was registered active");

            GarageSavedSetupCreationResult second = GarageSavedSetupCreationService.CreateFromOwnedVehicle(
                profilePath,
                "vehicle_probe_setup_create",
                setupDirectory,
                "Active Stock Baseline",
                makeActive: true);

            Require(second.SetupId == "vehicle_probe_setup_create_setup_002", "second setup id did not allocate");
            Require(second.Active, "second setup should be active");
            GarageProfile profileAfterSecond = GarageProfileLoader.Load(profilePath);
            Require(profileAfterSecond.SavedSetups.Count == 2, "profile did not register second saved setup");
            Require(profileAfterSecond.SavedSetups.Count(setup => setup.Active) == 1, "profile has incorrect active setup count");
            Require(profileAfterSecond.SavedSetups.Single(setup => setup.Active).SetupId == second.SetupId, "new active setup was not selected");

            GarageRuntimeVehicleSelection runtime = GarageRuntimeVehicleResolver.Resolve(profilePath, "vehicle_probe_setup_create", "active");
            Require(runtime.Setup?.SetupId == second.SetupId, "runtime did not load newly active setup");
            Require(runtime.Resolved.RuntimeBuild.Suspension.AlignmentId == "stock_ek9_alignment", "runtime active setup did not resolve stock alignment");

            RequireThrows(
                () => GarageSavedSetupCreationService.CreateFromOwnedVehicle(
                    profilePath,
                    "missing_vehicle",
                    setupDirectory,
                    "Missing",
                    makeActive: true),
                "missing owned vehicle was accepted");

            byte[] ownedAfterHash = SHA256.HashData(File.ReadAllBytes(ownedPath));
            Require(ownedBeforeHash.SequenceEqual(ownedAfterHash), "saved setup creation mutated owned vehicle");

            Console.WriteLine("Garage saved setup creation probe");
            Console.WriteLine($"  first: {first.SetupId}, active {first.Active}");
            Console.WriteLine($"  second: {second.SetupId}, active {second.Active}");
            Console.WriteLine($"  runtime active setup: {runtime.Setup?.SetupId ?? "(none)"}");
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

    private static string WriteProfile(string tempRoot, string ownedPath)
    {
        string profilePath = Path.Combine(tempRoot, "probe_setup_create_profile.json");
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_setup_create_profile",
            ["displayName"] = "Probe Setup Create Profile",
            ["credits"] = 10000,
            ["activeVehicleId"] = "vehicle_probe_setup_create",
            ["ownedVehicles"] = new JsonArray
            {
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_setup_create",
                    ["path"] = ownedPath,
                    ["garageSlot"] = 1
                }
            },
            ["savedSetups"] = new JsonArray(),
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

        throw new InvalidOperationException($"Garage saved setup creation probe failed: {message}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage saved setup creation probe failed: {message}.");
        }
    }
}
