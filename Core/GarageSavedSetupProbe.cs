using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageSavedSetupProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-setup-probe", Guid.NewGuid().ToString("N"));

        try
        {
            JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_setup",
                "probe_setup_profile",
                1,
                "Probe Setup EK9");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
            byte[] ownedBeforeHash = SHA256.HashData(File.ReadAllBytes(ownedPath));

            string setupPath = WriteSetup(tempRoot);
            string profilePath = WriteProfile(tempRoot, ownedPath, setupPath);

            ResolvedVehicleAssembly baseAssembly = VehicleAssemblyResolver.Resolve(ownedPath);
            GarageResolvedSetupVehicle resolved = GarageSavedSetupResolver.ResolveWithSetup(
                profilePath,
                "vehicle_probe_setup",
                "probe_track_day_setup");

            Require(resolved.Profile.Id.Equals("probe_setup_profile", StringComparison.OrdinalIgnoreCase), "profile did not load");
            Require(resolved.Setup.Id.Equals("probe_track_day_setup", StringComparison.OrdinalIgnoreCase), "setup did not load");
            Require(File.Exists(resolved.OverlayVehiclePath), "overlay build file was not created");
            Require(resolved.Resolved.BuildId.Equals(baseAssembly.BuildId, StringComparison.OrdinalIgnoreCase), "overlay changed vehicle identity");
            Require(resolved.Resolved.Engine.TuneId.Equals("tune_b16b_factory", StringComparison.OrdinalIgnoreCase), "engine tune overlay failed");
            Require(resolved.Resolved.Engine.FuelId.Equals("fuel_98ron", StringComparison.OrdinalIgnoreCase), "fuel overlay failed");
            Require(resolved.Resolved.RuntimeBuild.Suspension.AlignmentId.Equals("street_sport_alignment", StringComparison.OrdinalIgnoreCase), "alignment overlay failed");
            Require(resolved.Resolved.RuntimeBuild.Handling.Id.Equals("club_sport_arcade_handling_setup", StringComparison.OrdinalIgnoreCase), "handling setup overlay failed");
            Require(resolved.Resolved.RuntimeBuild.Steering.Id.Equals("street_quick_steering_setup", StringComparison.OrdinalIgnoreCase), "steering setup overlay failed");
            Require(baseAssembly.RuntimeBuild.Suspension.AlignmentId.Equals("stock_ek9_alignment", StringComparison.OrdinalIgnoreCase), "base alignment was not stock before overlay");

            byte[] ownedAfterHash = SHA256.HashData(File.ReadAllBytes(ownedPath));
            Require(ownedBeforeHash.SequenceEqual(ownedAfterHash), "saved setup overlay mutated owned vehicle file");

            RequireThrows(
                () => GarageSavedSetupResolver.ResolveWithSetup(profilePath, "vehicle_probe_setup", "missing_setup"),
                "missing setup was accepted");

            Console.WriteLine("Garage saved setup probe");
            Console.WriteLine($"  base alignment: {baseAssembly.RuntimeBuild.Suspension.AlignmentId}");
            Console.WriteLine($"  overlay alignment: {resolved.Resolved.RuntimeBuild.Suspension.AlignmentId}");
            Console.WriteLine($"  overlay steering: {resolved.Resolved.RuntimeBuild.Steering.Id}");
            Console.WriteLine($"  overlay handling: {resolved.Resolved.RuntimeBuild.Handling.Id}");
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

    private static string WriteSetup(string tempRoot)
    {
        string setupPath = Path.Combine(tempRoot, "probe_track_day_setup.json");
        JsonObject setup = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_track_day_setup",
            ["displayName"] = "Probe Track Day Setup",
            ["ownerProfileId"] = "probe_setup_profile",
            ["vehicleId"] = "vehicle_probe_setup",
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
                    ["handling"] = "club_sport_arcade_handling_setup"
                }
            }
        };

        File.WriteAllText(setupPath, setup.ToJsonString(CreateJsonOptions()));
        return setupPath;
    }

    private static string WriteProfile(string tempRoot, string ownedPath, string setupPath)
    {
        string profilePath = Path.Combine(tempRoot, "probe_setup_profile.json");
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_setup_profile",
            ["displayName"] = "Probe Setup Profile",
            ["credits"] = 10000,
            ["ownedVehicles"] = new JsonArray
            {
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_setup",
                    ["path"] = ownedPath,
                    ["garageSlot"] = 1
                }
            },
            ["savedSetups"] = new JsonArray
            {
                new JsonObject
                {
                    ["setupId"] = "probe_track_day_setup",
                    ["vehicleId"] = "vehicle_probe_setup",
                    ["path"] = setupPath,
                    ["displayName"] = "Probe Track Day Setup",
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
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException($"Garage saved setup probe failed: {message}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage saved setup probe failed: {message}.");
        }
    }
}
