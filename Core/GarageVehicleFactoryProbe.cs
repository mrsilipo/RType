using System.Security.Cryptography;
using RType.Data;

namespace RType.Core;

internal static class GarageVehicleFactoryProbe
{
    public static void Run()
    {
        const string purchasePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
        byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(purchasePath));

        string tempRoot = Path.Combine(Path.GetTempPath(), "rtype-garage-factory-probe", Guid.NewGuid().ToString("N"));
        try
        {
            System.Text.Json.Nodes.JsonObject owned = GarageVehicleFactory.CreateOwnedVehicleFromPurchaseCar(
                purchasePath,
                "vehicle_probe_ek9",
                "probe_profile",
                7,
                "Probe EK9 Type R");
            string ownedPath = GarageVehicleFactory.SaveOwnedVehicle(owned, tempRoot);
            ResolvedVehicleAssembly assembly = VehicleAssemblyResolver.Resolve(ownedPath);

            Console.WriteLine("Garage vehicle factory probe");
            Console.WriteLine($"  owned path: {ownedPath}");
            Console.WriteLine($"  build: {assembly.BuildId}, role {assembly.Classification}, owner {assembly.OwnerProfileId}, slot {assembly.GarageSlot}");
            Console.WriteLine($"  source: {assembly.PurchaseCarId} {assembly.SourcePurchaseCarPath}");
            Console.WriteLine($"  engine: {assembly.Engine.EngineCode}, {assembly.Engine.DisplacementCc:0}cc, fuel {assembly.Engine.FuelId}");
            Console.WriteLine($"  mass: {assembly.MassProperties.TotalMassKg:0.0}kg, front {assembly.MassProperties.FrontWeightDistribution:P1}, cgY {assembly.MassProperties.CenterOfGravityHeightMeters:0.000}m");

            Require(assembly.BuildId.Equals("vehicle_probe_ek9", StringComparison.OrdinalIgnoreCase), "owned id did not resolve");
            Require(assembly.Classification.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase), "owned role did not resolve");
            Require(assembly.PlayerOwned, "owned vehicle was not marked player-owned");
            Require(assembly.OwnerProfileId.Equals("probe_profile", StringComparison.OrdinalIgnoreCase), "owner profile did not resolve");
            Require(assembly.GarageSlot == 7, "garage slot did not resolve");
            Require(assembly.PurchaseCarId.Equals("2000_Ek9_Stock", StringComparison.OrdinalIgnoreCase), "source purchase car id did not resolve");
            Require(assembly.SourcePurchaseCarPath.Equals("Data/PurchaseCars/2000_Ek9_Stock.json", StringComparison.OrdinalIgnoreCase), "source purchase car path did not resolve");
            Require(assembly.Engine.EngineId.Equals("engine_b16b", StringComparison.OrdinalIgnoreCase), "engine did not copy from purchase template");
            Require(assembly.Engine.InstalledParts.TryGetValue("cams", out string? cams) && cams.Equals("cam_set_stock", StringComparison.OrdinalIgnoreCase), "stock cam install did not copy");
            Require(assembly.Validation.All(message => message.Severity != VehicleAssemblyValidationSeverity.Warning), "owned vehicle produced vehicle warnings");
            Require(assembly.Engine.Validation.All(message => message.Severity != EngineAssemblyValidationSeverity.Warning), "owned vehicle produced engine warnings");

            byte[] afterHash = SHA256.HashData(File.ReadAllBytes(purchasePath));
            Require(beforeHash.SequenceEqual(afterHash), "purchase-car template was modified");

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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Garage vehicle factory probe failed: {message}.");
        }
    }
}
