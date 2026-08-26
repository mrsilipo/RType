using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageActiveSetupProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-active-setup-probe", Guid.NewGuid().ToString("N"));

        try
        {
            JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_active_setup",
                "probe_active_setup_profile",
                1,
                "Probe Active Setup EK9");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
            string streetSetupPath = WriteSetup(
                tempRoot,
                "probe_street_setup",
                "street_sport_alignment",
                "street_quick_steering_setup",
                "street_arcade_handling_setup");
            string clubSetupPath = WriteSetup(
                tempRoot,
                "probe_club_setup",
                "club_sport_alignment",
                "club_sport_steering_setup",
                "club_sport_arcade_handling_setup");
            string profilePath = WriteProfile(tempRoot, ownedPath, streetSetupPath, clubSetupPath);
            byte[] ownedBeforeHash = SHA256.HashData(File.ReadAllBytes(ownedPath));

            GarageRuntimeVehicleSelection activeStreet = GarageRuntimeVehicleResolver.Resolve(
                profilePath,
                "vehicle_probe_active_setup",
                "active");
            Require(activeStreet.Setup?.SetupId == "probe_street_setup", "initial active setup was not street setup");
            Require(activeStreet.Resolved.RuntimeBuild.Suspension.AlignmentId == "street_sport_alignment", "initial active alignment failed");

            GarageSavedSetupActivationResult activation = GarageSavedSetupActivationService.SetActiveSetup(
                profilePath,
                "vehicle_probe_active_setup",
                "probe_club_setup");
            Require(activation.PreviousActiveSetupId == "probe_street_setup", "previous active setup was not reported");
            Require(activation.ActiveSetupId == "probe_club_setup", "new active setup was not reported");

            GarageRuntimeVehicleSelection activeClub = GarageRuntimeVehicleResolver.Resolve(
                profilePath,
                "vehicle_probe_active_setup");
            Require(activeClub.Setup?.SetupId == "probe_club_setup", "runtime resolver did not select new active setup");
            Require(activeClub.Resolved.RuntimeBuild.Suspension.AlignmentId == "club_sport_alignment", "active setup alignment failed");
            Require(activeClub.Parameters.SteeringRatio > 0f, "runtime parameters were not created from active setup overlay");

            byte[] profileAfterActivationHash = SHA256.HashData(File.ReadAllBytes(profilePath));
            RequireThrows(
                () => GarageSavedSetupActivationService.SetActiveSetup(
                    profilePath,
                    "vehicle_probe_active_setup",
                    "missing_setup"),
                "missing active setup was accepted");
            Require(profileAfterActivationHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profilePath))), "failed activation partially wrote profile");

            GarageSavedSetupActivationResult clear = GarageSavedSetupActivationService.ClearActiveSetup(
                profilePath,
                "vehicle_probe_active_setup");
            Require(clear.PreviousActiveSetupId == "probe_club_setup", "clear did not report previous active setup");
            Require(string.IsNullOrWhiteSpace(clear.ActiveSetupId), "clear still reports an active setup");

            GarageRuntimeVehicleSelection noSetup = GarageRuntimeVehicleResolver.Resolve(
                profilePath,
                "vehicle_probe_active_setup",
                "active");
            Require(noSetup.Setup is null, "runtime resolver selected setup after active setup was cleared");
            Require(noSetup.Resolved.RuntimeBuild.Suspension.AlignmentId == "stock_ek9_alignment", "cleared active setup did not return to owned vehicle assembly");

            byte[] ownedAfterHash = SHA256.HashData(File.ReadAllBytes(ownedPath));
            Require(ownedBeforeHash.SequenceEqual(ownedAfterHash), "active setup selection mutated owned vehicle file");

            Console.WriteLine("Garage active setup probe");
            Console.WriteLine($"  initial active: {activeStreet.Setup?.SetupId}, alignment {activeStreet.Resolved.RuntimeBuild.Suspension.AlignmentId}");
            Console.WriteLine($"  switched active: {activeClub.Setup?.SetupId}, alignment {activeClub.Resolved.RuntimeBuild.Suspension.AlignmentId}");
            Console.WriteLine($"  cleared active alignment: {noSetup.Resolved.RuntimeBuild.Suspension.AlignmentId}");
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

    private static string WriteSetup(
        string tempRoot,
        string setupId,
        string alignmentId,
        string steeringId,
        string handlingId)
    {
        string setupPath = Path.Combine(tempRoot, $"{setupId}.json");
        JsonObject setup = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = setupId,
            ["displayName"] = setupId,
            ["ownerProfileId"] = "probe_active_setup_profile",
            ["vehicleId"] = "vehicle_probe_active_setup",
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
                    ["alignment"] = alignmentId
                },
                ["tuning"] = new JsonObject
                {
                    ["steering"] = steeringId,
                    ["handling"] = handlingId
                }
            }
        };

        File.WriteAllText(setupPath, setup.ToJsonString(CreateJsonOptions()));
        return setupPath;
    }

    private static string WriteProfile(string tempRoot, string ownedPath, string streetSetupPath, string clubSetupPath)
    {
        string profilePath = Path.Combine(tempRoot, "probe_active_setup_profile.json");
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_active_setup_profile",
            ["displayName"] = "Probe Active Setup Profile",
            ["credits"] = 10000,
            ["ownedVehicles"] = new JsonArray
            {
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_active_setup",
                    ["path"] = ownedPath,
                    ["garageSlot"] = 1
                }
            },
            ["savedSetups"] = new JsonArray
            {
                new JsonObject
                {
                    ["setupId"] = "probe_street_setup",
                    ["vehicleId"] = "vehicle_probe_active_setup",
                    ["path"] = streetSetupPath,
                    ["displayName"] = "Street Setup",
                    ["active"] = true
                },
                new JsonObject
                {
                    ["setupId"] = "probe_club_setup",
                    ["vehicleId"] = "vehicle_probe_active_setup",
                    ["path"] = clubSetupPath,
                    ["displayName"] = "Club Setup",
                    ["active"] = false
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
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException($"Garage active setup probe failed: {message}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage active setup probe failed: {message}.");
        }
    }
}
