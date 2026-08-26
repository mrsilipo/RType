using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using RType.Data;

namespace RType.Core;

internal static class GarageSavedSetupEditorProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-setup-editor-probe", Guid.NewGuid().ToString("N"));

        try
        {
            JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_setup_editor",
                "probe_setup_editor_profile",
                1,
                "Probe Setup Editor EK9");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
            string setupPath = WriteSetup(tempRoot);
            string profilePath = WriteProfile(tempRoot, ownedPath, setupPath);
            byte[] ownedBeforeHash = SHA256.HashData(File.ReadAllBytes(ownedPath));
            byte[] setupBeforeHash = SHA256.HashData(File.ReadAllBytes(setupPath));

            GarageSavedSetupEditResult edited = GarageSavedSetupEditor.UpdateSetup(
                profilePath,
                "vehicle_probe_setup_editor",
                "probe_editable_setup",
                new GarageSavedSetupOverrides(
                    AlignmentId: "club_sport_alignment",
                    SteeringSetupId: "club_sport_steering_setup",
                    HandlingSetupId: "pro_racing_arcade_handling_setup"));

            Require(edited.ChangedFields.SequenceEqual(
                ["suspension.alignment", "tuning.steering", "tuning.handling"]),
                "changed field list was unexpected");
            Require(edited.Before.AlignmentId.Equals("street_sport_alignment", StringComparison.OrdinalIgnoreCase), "before setup alignment did not load");
            Require(edited.After.AlignmentId.Equals("club_sport_alignment", StringComparison.OrdinalIgnoreCase), "setup alignment edit failed");
            Require(edited.Resolved.Resolved.RuntimeBuild.Suspension.AlignmentId.Equals("club_sport_alignment", StringComparison.OrdinalIgnoreCase), "resolved alignment edit failed");
            Require(edited.Resolved.Resolved.RuntimeBuild.Steering.Id.Equals("club_sport_steering_setup", StringComparison.OrdinalIgnoreCase), "resolved steering edit failed");
            Require(edited.Resolved.Resolved.RuntimeBuild.Handling.Id.Equals("pro_racing_arcade_handling_setup", StringComparison.OrdinalIgnoreCase), "resolved handling edit failed");

            byte[] ownedAfterHash = SHA256.HashData(File.ReadAllBytes(ownedPath));
            Require(ownedBeforeHash.SequenceEqual(ownedAfterHash), "saved setup editor mutated owned vehicle file");
            Require(!setupBeforeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(setupPath))), "saved setup file was not changed");

            byte[] setupAfterValidEditHash = SHA256.HashData(File.ReadAllBytes(setupPath));
            RequireThrows(
                () => GarageSavedSetupEditor.UpdateSetup(
                    profilePath,
                    "vehicle_probe_setup_editor",
                    "probe_editable_setup",
                    new GarageSavedSetupOverrides(AlignmentId: "missing_alignment_setup")),
                "invalid alignment edit was accepted");
            Require(setupAfterValidEditHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(setupPath))), "failed saved setup edit partially wrote the setup file");

            GarageSavedSetupEditResult unchanged = GarageSavedSetupEditor.UpdateSetup(
                profilePath,
                "vehicle_probe_setup_editor",
                "probe_editable_setup",
                new GarageSavedSetupOverrides(AlignmentId: "club_sport_alignment"));
            Require(unchanged.ChangedFields.Count == 0, "unchanged setup edit reported changes");

            Console.WriteLine("Garage saved setup editor probe");
            Console.WriteLine($"  changed fields: {string.Join(", ", edited.ChangedFields)}");
            Console.WriteLine($"  resolved alignment: {edited.Resolved.Resolved.RuntimeBuild.Suspension.AlignmentId}");
            Console.WriteLine($"  resolved steering: {edited.Resolved.Resolved.RuntimeBuild.Steering.Id}");
            Console.WriteLine($"  resolved handling: {edited.Resolved.Resolved.RuntimeBuild.Handling.Id}");
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
        string setupPath = Path.Combine(tempRoot, "probe_editable_setup.json");
        JsonObject setup = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_editable_setup",
            ["displayName"] = "Probe Editable Setup",
            ["ownerProfileId"] = "probe_setup_editor_profile",
            ["vehicleId"] = "vehicle_probe_setup_editor",
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

    private static string WriteProfile(string tempRoot, string ownedPath, string setupPath)
    {
        string profilePath = Path.Combine(tempRoot, "probe_setup_editor_profile.json");
        JsonObject profile = new()
        {
            ["schemaVersion"] = 1,
            ["id"] = "probe_setup_editor_profile",
            ["displayName"] = "Probe Setup Editor Profile",
            ["credits"] = 10000,
            ["ownedVehicles"] = new JsonArray
            {
                new JsonObject
                {
                    ["vehicleId"] = "vehicle_probe_setup_editor",
                    ["path"] = ownedPath,
                    ["garageSlot"] = 1
                }
            },
            ["savedSetups"] = new JsonArray
            {
                new JsonObject
                {
                    ["setupId"] = "probe_editable_setup",
                    ["vehicleId"] = "vehicle_probe_setup_editor",
                    ["path"] = setupPath,
                    ["displayName"] = "Probe Editable Setup",
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
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException($"Garage saved setup editor probe failed: {message}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage saved setup editor probe failed: {message}.");
        }
    }
}
