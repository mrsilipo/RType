using System.Security.Cryptography;
using System.Text.Json.Nodes;
using RType.Data;

namespace RType.Core;

internal static class GarageModInstallerProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        byte[] purchaseBeforeHash = SHA256.HashData(File.ReadAllBytes(purchasePath));
        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-installer-probe", Guid.NewGuid().ToString("N"));

        try
        {
            JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_mod_install",
                "probe_profile",
                8,
                "Probe Mod Install EK9");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);

            Console.WriteLine("Garage mod installer probe");
            Console.WriteLine($"  owned path: {ownedPath}");

            RequireThrows(
                () => GarageModInstaller.ApplyOptionToFile(ownedPath, "displacement", "displacement_pro_high_comp"),
                "blocked high-compression displacement install was allowed");

            GarageModInstallResult fuelInstall = GarageModInstaller.ApplyOptionToFile(ownedPath, "fuel", "fuel_e85");
            Require(fuelInstall.Kind == GarageModInstallKind.Engine, "fuel install was not classified as engine");
            Require(fuelInstall.After.Engine.FuelId.Equals("fuel_e85", StringComparison.OrdinalIgnoreCase), "fuel did not install");
            Require(fuelInstall.Receipt.OwnerProfileId.Equals("probe_profile", StringComparison.OrdinalIgnoreCase), "receipt owner did not resolve");
            Require(fuelInstall.Receipt.GarageSlot == 8, "receipt garage slot did not resolve");
            Require(fuelInstall.Receipt.BeforeFuelId.Equals("fuel_98ron", StringComparison.OrdinalIgnoreCase), "receipt before fuel did not resolve");
            Require(fuelInstall.Receipt.AfterFuelId.Equals("fuel_e85", StringComparison.OrdinalIgnoreCase), "receipt after fuel did not resolve");
            Console.WriteLine($"  fuel install: {fuelInstall.Status}, {fuelInstall.After.Engine.FuelId}");

            GarageModInstallResult tyreInstall = GarageModInstaller.ApplyOptionToFile(ownedPath, "tyrePackage", "tyre_package_sports_medium_balanced");
            Require(tyreInstall.Kind == GarageModInstallKind.Vehicle, "tyre package install was not classified as vehicle");
            JsonObject afterTyres = ReadObject(ownedPath);
            Require(ReadString(afterTyres, string.Empty, "assembly", "tyres", "frontCompound").Equals("sports_medium_reference", StringComparison.OrdinalIgnoreCase), "front tyre compound did not install");
            Require(ReadString(afterTyres, string.Empty, "assembly", "tyres", "rearCompound").Equals("sports_medium_reference", StringComparison.OrdinalIgnoreCase), "rear tyre compound did not install");
            Require(ReadString(afterTyres, string.Empty, "assembly", "tyres", "frontModel").Equals("sports_medium_balanced_model", StringComparison.OrdinalIgnoreCase), "front tyre model did not install");
            Require(ReadString(afterTyres, string.Empty, "assembly", "tyres", "rearModel").Equals("sports_medium_balanced_model", StringComparison.OrdinalIgnoreCase), "rear tyre model did not install");
            Console.WriteLine($"  tyre package install: {tyreInstall.Status}, sports medium balanced");

            GarageModInstallResult differentialInstall = GarageModInstaller.ApplyOptionToFile(ownedPath, "differential", "club_sport_plate_lsd");
            Require(differentialInstall.Kind == GarageModInstallKind.Vehicle, "differential install was not classified as vehicle");
            Require(differentialInstall.After.RuntimeBuild.Drivetrain.DifferentialId.Equals("club_sport_plate_lsd", StringComparison.OrdinalIgnoreCase), "differential did not install");
            Require(differentialInstall.Receipt.BeforeTotalMassKg > 0f, "receipt before mass did not resolve");
            Require(differentialInstall.Receipt.AfterTotalMassKg > 0f, "receipt after mass did not resolve");
            Console.WriteLine($"  differential install: {differentialInstall.Status}, {differentialInstall.After.RuntimeBuild.Drivetrain.DifferentialId}");

            GarageModInstallResult displacementInstall = GarageModInstaller.ApplyOptionToFile(ownedPath, "displacement", "displacement_pro_high_comp");
            Require(displacementInstall.Kind == GarageModInstallKind.Engine, "displacement install was not classified as engine");
            Require(displacementInstall.After.Engine.InstalledParts.TryGetValue("displacement", out string? displacement) &&
                    displacement.Equals("displacement_pro_high_comp", StringComparison.OrdinalIgnoreCase), "displacement did not install after E85");
            Console.WriteLine($"  displacement install after E85: {displacementInstall.Status}, {displacementInstall.After.Engine.DisplacementCc:0}cc");

            RequireThrows(
                () => GarageModInstaller.ApplyOptionToFile(purchasePath, "fuel", "fuel_e85"),
                "purchase-car stock template mutation was allowed");

            byte[] purchaseAfterHash = SHA256.HashData(File.ReadAllBytes(purchasePath));
            Require(purchaseBeforeHash.SequenceEqual(purchaseAfterHash), "purchase-car template hash changed");

            ResolvedVehicleAssembly finalAssembly = VehicleAssemblyResolver.Resolve(ownedPath);
            Require(finalAssembly.Classification.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase), "final build lost owned role");
            Require(finalAssembly.PlayerOwned, "final build lost player-owned flag");
            Require(finalAssembly.Validation.All(message => message.Severity != VehicleAssemblyValidationSeverity.Warning), "final build produced vehicle warnings");
            Require(finalAssembly.Engine.Validation.All(message => message.Severity != EngineAssemblyValidationSeverity.Warning), "final build produced engine warnings");

            Console.WriteLine($"  final: {finalAssembly.Engine.EngineCode}, fuel {finalAssembly.Engine.FuelId}, diff {finalAssembly.RuntimeBuild.Drivetrain.DifferentialId}, mass {finalAssembly.MassProperties.TotalMassKg:0.0}kg");
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

        throw new InvalidOperationException($"Garage mod installer probe failed: {message}.");
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

        return node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? value.GetValue<string>()
            : fallback;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage mod installer probe failed: {message}.");
        }
    }
}
