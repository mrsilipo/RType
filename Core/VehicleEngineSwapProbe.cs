using RType.Data;

namespace RType.Core;

internal static class VehicleEngineSwapProbe
{
    private const string StockPurchaseCarPath = "Data/PurchaseCars/2000_Ek9_Stock.json";
    private const string K20SwapPath = "Data/Garage/OwnedVehicles/vehicle_0003_k20a_swap_ek9.json";

    public static void Run()
    {
        ResolvedVehicleAssembly stock = VehicleAssemblyResolver.Resolve(StockPurchaseCarPath);
        ResolvedVehicleAssembly swap = VehicleAssemblyResolver.Resolve(K20SwapPath);

        Console.WriteLine("Vehicle engine swap probe");
        Console.WriteLine($"  stock: {stock.BuildId}, {stock.Engine.EngineCode}, {stock.Engine.Family}, gears {stock.RuntimeBuild.Drivetrain.ForwardGearRatios.Length}, mass {stock.MassProperties.TotalMassKg:0.0}kg");
        Console.WriteLine($"  swap: {swap.BuildId}, {swap.Engine.EngineCode}, {swap.Engine.Family}, gears {swap.RuntimeBuild.Drivetrain.ForwardGearRatios.Length}, mass {swap.MassProperties.TotalMassKg:0.0}kg");
        Console.WriteLine($"  swap drivetrain: {swap.RuntimeBuild.Drivetrain.GearboxId}, {swap.RuntimeBuild.Drivetrain.FinalDriveId}, {swap.RuntimeBuild.Drivetrain.DifferentialId}");
        Console.WriteLine($"  swap kits: {string.Join(", ", swap.RuntimeBuild.SwapKits.InstalledParts.Select(part => $"{part.Key}={part.Value}"))}");
        Console.WriteLine($"  swap engine defaults: displacement {swap.Engine.InstalledParts.GetValueOrDefault("displacement")}, flywheel {swap.Engine.InstalledParts.GetValueOrDefault("flywheel")}, valve springs {swap.Engine.InstalledParts.GetValueOrDefault("valveSprings")}");
        Console.WriteLine($"  torque: stock {FindPeakTorque(stock.Engine.TorqueCurve):0.0}Nm -> swap {FindPeakTorque(swap.Engine.TorqueCurve):0.0}Nm");
        Console.WriteLine($"  limiter: stock {stock.Engine.LimiterHardCutRpm:0}rpm -> swap {swap.Engine.LimiterHardCutRpm:0}rpm");

        Require(swap.PlayerOwned, "swap fixture must be an owned vehicle");
        Require(swap.DrivetrainLayout.Equals("FF", StringComparison.OrdinalIgnoreCase), "swap fixture must remain FF for this phase");
        Require(swap.Engine.EngineId.Equals("engine_k20a", StringComparison.OrdinalIgnoreCase), "swap fixture did not resolve K20A engine");
        Require(swap.Engine.Family.Equals("honda_k_series", StringComparison.OrdinalIgnoreCase), "swap fixture did not resolve K-series engine family");
        Require(swap.Engine.InstalledParts.TryGetValue("displacement", out string? displacement) && displacement.Equals("displacement_stock_k20a", StringComparison.OrdinalIgnoreCase), "K20A default displacement package was not applied");
        Require(swap.Engine.InstalledParts.TryGetValue("flywheel", out string? flywheel) && flywheel.Equals("flywheel_stock_k20a", StringComparison.OrdinalIgnoreCase), "K20A default flywheel was not applied");
        Require(swap.Engine.InstalledParts.TryGetValue("valveSprings", out string? valveSprings) && valveSprings.Equals("valve_springs_stock_b18c_k20a", StringComparison.OrdinalIgnoreCase), "K20A default valve springs were not applied");
        Require(swap.RuntimeBuild.Drivetrain.ForwardGearRatios.Length == 6, "K20A swap should resolve a 6-speed gearbox");
        Require(swap.RuntimeBuild.Drivetrain.GearboxId.Equals("stock_k20a_6_speed", StringComparison.OrdinalIgnoreCase), "K20A swap should use K-series gearbox");
        Require(swap.RuntimeBuild.Drivetrain.FinalDriveId.Equals("stock_k20a_final_drive", StringComparison.OrdinalIgnoreCase), "K20A swap should use K-series final drive");
        Require(swap.RuntimeBuild.Drivetrain.DifferentialId.Equals("stock_k20a_helical_lsd", StringComparison.OrdinalIgnoreCase), "K20A swap should use K-series differential");
        Require(swap.RuntimeBuild.SwapKits.InstalledParts.Count == 4, "K20A swap should install the four required chassis-side swap-kit parts");
        Require(swap.RuntimeBuild.SwapKits.InstalledParts.ContainsKey("engineMounts"), "K20A swap should install engine mounts");
        Require(swap.RuntimeBuild.SwapKits.InstalledParts.ContainsKey("wiringLoom"), "K20A swap should install wiring loom");
        Require(swap.RuntimeBuild.SwapKits.InstalledParts.ContainsKey("driveshafts"), "K20A swap should install driveshafts");
        Require(swap.RuntimeBuild.SwapKits.InstalledParts.ContainsKey("shiftLinkage"), "K20A swap should install shift linkage");
        Require(swap.RuntimeBuild.SwapKits.TotalMassKg > 0f, "K20A swap should add swap-kit mass");
        Require(FindPeakTorque(swap.Engine.TorqueCurve) > FindPeakTorque(stock.Engine.TorqueCurve), "K20A swap should resolve more peak torque than stock B16B");
        Require(swap.MassProperties.TotalMassKg > stock.MassProperties.TotalMassKg, "K20A swap fixture should resolve heavier than stock EK9");
        Require(swap.Validation.All(message => message.Severity != VehicleAssemblyValidationSeverity.Warning), "K20A swap should not produce vehicle validation warnings");
        Require(swap.Engine.Validation.All(message => message.Severity != EngineAssemblyValidationSeverity.Warning), "K20A swap should not produce engine validation warnings");

        Console.WriteLine("  result: PASS");
    }

    private static float FindPeakTorque(RType.Vehicle.TorqueCurvePoint[] curve)
    {
        return curve.Length == 0 ? 0f : curve.Max(point => point.TorqueNm);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Vehicle engine swap probe failed: {message}.");
        }
    }
}
