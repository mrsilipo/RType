using RType.Data;
using RType.Vehicle;

namespace RType.Core;

internal static class VehicleModificationComparisonProbe
{
    private const string StockPurchaseCarPath = "Data/PurchaseCars/2000_Ek9_Stock.json";
    private const string ModifiedOwnedVehiclePath = "Data/Garage/OwnedVehicles/vehicle_0002_modified_ek9.json";

    public static void Run()
    {
        ResolvedVehicleAssembly stock = VehicleAssemblyResolver.Resolve(StockPurchaseCarPath);
        ResolvedVehicleAssembly modified = VehicleAssemblyResolver.Resolve(ModifiedOwnedVehiclePath);

        float stockPeakTorque = FindPeakTorque(stock.Engine.TorqueCurve);
        float modifiedPeakTorque = FindPeakTorque(modified.Engine.TorqueCurve);
        float stockPeakEngineBrake = FindPeakTorque(stock.Engine.EngineBrakeTorqueCurve);
        float modifiedPeakEngineBrake = FindPeakTorque(modified.Engine.EngineBrakeTorqueCurve);

        Console.WriteLine("Vehicle modification comparison probe");
        Console.WriteLine($"  stock: {stock.BuildId} ({stock.Classification})");
        Console.WriteLine($"    path: {stock.BuildPath}");
        Console.WriteLine($"    ownership: playerOwned {stock.PlayerOwned}, slot {stock.GarageSlot}");
        Console.WriteLine($"    body shell: {stock.BodyShellId}");
        Console.WriteLine($"    engine: {stock.Engine.EngineId}, fuel {stock.Engine.FuelId}, {stock.Engine.DisplacementCc:0}cc, {stock.Engine.CompressionRatio:0.0}:1");
        Console.WriteLine($"    torque: peak {stockPeakTorque:0.0}Nm, engine brake peak {stockPeakEngineBrake:0.0}Nm");
        Console.WriteLine($"    mass: {stock.MassProperties.TotalMassKg:0.0}kg, front {stock.MassProperties.FrontWeightDistribution * 100f:0.0}%, cgY {stock.MassProperties.CenterOfGravityHeightMeters:0.000}m, yaw {stock.MassProperties.YawInertiaKgM2:0}kgm2");
        Console.WriteLine($"    clutch: {stock.Engine.ClutchTorqueCapacityNm:0}Nm, bite {stock.Engine.ClutchBitePoint:0.00}, coupling {stock.Engine.ClutchCouplingRate:0.0}");

        Console.WriteLine($"  modified: {modified.BuildId} ({modified.Classification})");
        Console.WriteLine($"    path: {modified.BuildPath}");
        Console.WriteLine($"    ownership: playerOwned {modified.PlayerOwned}, source {FormatOptional(modified.PurchaseCarId)}, slot {modified.GarageSlot}");
        Console.WriteLine($"    body shell: {modified.BodyShellId}");
        Console.WriteLine($"    engine: {modified.Engine.EngineId}, fuel {modified.Engine.FuelId}, {modified.Engine.DisplacementCc:0}cc, {modified.Engine.CompressionRatio:0.0}:1");
        Console.WriteLine($"    torque: peak {modifiedPeakTorque:0.0}Nm, engine brake peak {modifiedPeakEngineBrake:0.0}Nm");
        Console.WriteLine($"    mass: {modified.MassProperties.TotalMassKg:0.0}kg, front {modified.MassProperties.FrontWeightDistribution * 100f:0.0}%, cgY {modified.MassProperties.CenterOfGravityHeightMeters:0.000}m, yaw {modified.MassProperties.YawInertiaKgM2:0}kgm2");
        Console.WriteLine($"    clutch: {modified.Engine.ClutchTorqueCapacityNm:0}Nm, bite {modified.Engine.ClutchBitePoint:0.00}, coupling {modified.Engine.ClutchCouplingRate:0.0}");

        Console.WriteLine("  deltas:");
        Console.WriteLine($"    displacement: {modified.Engine.DisplacementCc - stock.Engine.DisplacementCc:+0;-0;0}cc");
        Console.WriteLine($"    compression: {modified.Engine.CompressionRatio - stock.Engine.CompressionRatio:+0.0;-0.0;0.0}");
        Console.WriteLine($"    peak torque: {modifiedPeakTorque - stockPeakTorque:+0.0;-0.0;0.0}Nm");
        Console.WriteLine($"    peak engine brake: {modifiedPeakEngineBrake - stockPeakEngineBrake:+0.0;-0.0;0.0}Nm");
        Console.WriteLine($"    engine inertia: {modified.Engine.RotationalInertiaKgM2 - stock.Engine.RotationalInertiaKgM2:+0.000;-0.000;0.000}kgm2");
        Console.WriteLine($"    total mass: {modified.MassProperties.TotalMassKg - stock.MassProperties.TotalMassKg:+0.0;-0.0;0.0}kg");
        Console.WriteLine($"    front distribution: {(modified.MassProperties.FrontWeightDistribution - stock.MassProperties.FrontWeightDistribution) * 100f:+0.00;-0.00;0.00}pp");
        Console.WriteLine($"    cg height: {modified.MassProperties.CenterOfGravityHeightMeters - stock.MassProperties.CenterOfGravityHeightMeters:+0.000;-0.000;0.000}m");
        Console.WriteLine($"    yaw inertia: {modified.MassProperties.YawInertiaKgM2 - stock.MassProperties.YawInertiaKgM2:+0;-0;0}kgm2");
        Console.WriteLine($"    clutch capacity: {modified.Engine.ClutchTorqueCapacityNm - stock.Engine.ClutchTorqueCapacityNm:+0;-0;0}Nm");
        Console.WriteLine($"    fuel multiplier: {modified.Engine.FuelEffectivePowerMultiplier - stock.Engine.FuelEffectivePowerMultiplier:+0.000;-0.000;0.000}");

        Console.WriteLine("  changed engine slots:");
        foreach (string line in ChangedParts(stock.Engine.InstalledParts, modified.Engine.InstalledParts))
        {
            Console.WriteLine($"    {line}");
        }

        Require(modified.PlayerOwned, "modified fixture must resolve as a player-owned vehicle");
        Require(string.Equals(modified.PurchaseCarId, "2000_Ek9_Stock", StringComparison.OrdinalIgnoreCase), "modified fixture must retain purchase-car source id");
        Require(!string.Equals(stock.BodyShellId, modified.BodyShellId, StringComparison.OrdinalIgnoreCase), "body shell should differ from the stock purchase template");
        Require(string.Equals(modified.Engine.FuelId, "fuel_e85", StringComparison.OrdinalIgnoreCase), "modified fixture should resolve selected E85 fuel");
        Require(modified.Engine.DisplacementCc > stock.Engine.DisplacementCc, "modified engine displacement should increase");
        Require(modified.Engine.CompressionRatio > stock.Engine.CompressionRatio, "modified engine compression should increase");
        Require(modifiedPeakTorque > stockPeakTorque, "modified peak torque should increase");
        Require(modifiedPeakEngineBrake > stockPeakEngineBrake, "modified engine braking should increase");
        Require(MathF.Abs(modified.MassProperties.TotalMassKg - stock.MassProperties.TotalMassKg) > 0.1f, "modified mass should differ from stock");
        Require(modified.Engine.ClutchTorqueCapacityNm > stock.Engine.ClutchTorqueCapacityNm, "modified clutch capacity should increase");

        Console.WriteLine("  result: PASS");
    }

    private static IEnumerable<string> ChangedParts(
        IReadOnlyDictionary<string, string> stock,
        IReadOnlyDictionary<string, string> modified)
    {
        foreach (string slot in stock.Keys.Concat(modified.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(slot => slot, StringComparer.OrdinalIgnoreCase))
        {
            stock.TryGetValue(slot, out string? stockPart);
            modified.TryGetValue(slot, out string? modifiedPart);
            if (!string.Equals(stockPart, modifiedPart, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{slot}: {FormatOptional(stockPart)} -> {FormatOptional(modifiedPart)}";
            }
        }
    }

    private static float FindPeakTorque(TorqueCurvePoint[] curve)
    {
        return curve.Length == 0 ? 0f : curve.Max(point => point.TorqueNm);
    }

    private static string FormatOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Vehicle modification comparison probe failed: {message}.");
        }
    }
}
