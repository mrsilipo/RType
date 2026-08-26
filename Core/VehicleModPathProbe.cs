using RType.Data;

namespace RType.Core;

internal static class VehicleModPathProbe
{
    public static void Run()
    {
        VehicleModPathReport stock = VehicleModPathResolver.BuildReport("Data/PurchaseCars/2000_Ek9_Stock.json");
        Print("stock_ek9", stock);
        Require(stock.CurrentBuildIsClean, "stock EK9 current build should be warning-clean");
        Require(stock.CurrentVehicle.Classification.Equals("purchase_car_stock", StringComparison.OrdinalIgnoreCase), "stock EK9 should be a purchase car");
        Require(stock.ReadyEngineOptionCount > 0, "stock EK9 should expose ready engine options");
        Require(stock.AdvisoryEngineOptionCount > 0, "stock EK9 should expose advisory engine options");
        Require(stock.BlockedEngineOptionCount > 0, "stock EK9 should expose blocked engine options");
        Require(stock.InstalledEngineOptionCount > 0, "stock EK9 should mark installed engine options");
        Require(stock.ReadyVehicleOptions.Any(), "stock EK9 should expose ready non-engine vehicle options");
        Require(stock.InstalledVehicleOptions.Any(), "stock EK9 should mark installed non-engine vehicle options");
        RequireInstalled(stock, "gearbox", "stock_ek9_5_speed");
        RequireReady(stock, "gearbox", "street_close_ratio_5_speed");
        RequireReady(stock, "differential", "club_sport_plate_lsd");
        RequireReady(stock, "frontSuspension", "club_sport_front_suspension");
        RequireReady(stock, "rearSuspension", "club_sport_rear_suspension");
        RequireMissing(stock, "frontSuspension", "club_sport_rear_suspension");
        RequireMissing(stock, "rearSuspension", "club_sport_front_suspension");
        RequireReady(stock, "frontBrakes", "club_sport_front_brakes");
        RequireReady(stock, "rearBrakes", "club_sport_rear_brakes");
        RequireReady(stock, "brakeSystem", "club_sport_brake_system");
        RequireInstalled(stock, "frontTyres", "sports_hard_reference");
        RequireInstalled(stock, "rearTyres", "sports_hard_reference");
        RequireBlocked(stock, "frontTyres", "sports_soft_reference", "front_tyre_model_compatibility_mismatch");
        RequireBlocked(stock, "rearTyres", "sports_soft_reference", "rear_tyre_model_compatibility_mismatch");
        RequireReady(stock, "frontWheels", "club_sport_15x7_wheel");
        RequireReady(stock, "rearWheels", "club_sport_15x7_wheel");
        RequireReady(stock, "aeroPackage", "club_sport_aero_package");
        RequireInstalled(stock, "tyrePackage", "tyre_package_sports_hard_ek9");
        RequireReady(stock, "tyrePackage", "tyre_package_sports_medium_balanced");
        RequireReady(stock, "tyrePackage", "tyre_package_semi_slick_aggressive");

        VehicleModPathReport modified = VehicleModPathResolver.BuildReport("Data/Garage/OwnedVehicles/vehicle_0002_modified_ek9.json");
        Print("modified_ek9", modified);
        Require(modified.CurrentBuildIsClean, "modified EK9 current build should be warning-clean");
        Require(modified.CurrentVehicle.Classification.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase), "modified EK9 should be an owned vehicle");
        Require(modified.CurrentVehicle.PlayerOwned, "modified EK9 should be marked player-owned");
        Require(modified.ReadyEngineOptionCount > 0, "modified EK9 should expose ready engine options");
        Require(modified.BlockedEngineOptionCount > 0, "modified EK9 should expose blocked engine options");
        Require(modified.ReadyVehicleOptions.Any(), "modified EK9 should expose ready non-engine vehicle options");
        RequireInstalled(modified, "gearbox", "stock_ek9_5_speed");
        RequireInstalled(modified, "tyrePackage", "tyre_package_sports_hard_ek9");
        RequireReady(modified, "tyrePackage", "tyre_package_sports_medium_balanced");

        Console.WriteLine("Vehicle mod path probe: PASS");
    }

    private static void Print(string label, VehicleModPathReport report)
    {
        ResolvedVehicleAssembly vehicle = report.CurrentVehicle;
        Console.WriteLine($"{label}: {vehicle.BuildId}, {vehicle.Classification}, {vehicle.ChassisCode}, {vehicle.DrivetrainLayout}, {vehicle.Engine.EngineCode}, {vehicle.MassProperties.TotalMassKg:0.0}kg");
        Console.WriteLine($"  current clean: {report.CurrentBuildIsClean}, vehicle warnings {report.VehicleWarnings.Count}, engine warnings {report.EngineWarnings.Count}, info {report.VehicleInfo.Count + report.EngineInfo.Count}");
        Console.WriteLine($"  engine mod options: ready {report.ReadyEngineOptionCount}, advisory {report.AdvisoryEngineOptionCount}, blocked {report.BlockedEngineOptionCount}, installed {report.InstalledEngineOptionCount}");
        Console.WriteLine($"  engine mod groups: {string.Join(", ", report.Engine.Groups.Select(group => $"{group.Slot}={group.Ready.Count}/{group.Advisory.Count}/{group.Blocked.Count}"))}");
        Console.WriteLine($"  vehicle mod options: ready {report.ReadyVehicleOptions.Count()}, advisory {report.AdvisoryVehicleOptions.Count()}, blocked {report.BlockedVehicleOptions.Count()}, installed {report.InstalledVehicleOptions.Count()}");
        Console.WriteLine($"  vehicle mod groups: {string.Join(", ", report.VehicleGroups.Select(group => $"{group.Slot}={group.Ready.Count}/{group.Advisory.Count}/{group.Blocked.Count}"))}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Vehicle mod path probe failed: {message}.");
        }
    }

    private static void RequireInstalled(VehicleModPathReport report, string slot, string id)
    {
        VehicleModOption option = Find(report, slot, id);
        Require(option.IsInstalled, $"{slot}/{id} should be marked installed");
        Require(option.Status != VehicleModOptionStatus.Blocked, $"{slot}/{id} should not be blocked");
    }

    private static void RequireReady(VehicleModPathReport report, string slot, string id)
    {
        VehicleModOption option = Find(report, slot, id);
        Require(option.Status == VehicleModOptionStatus.Ready, $"{slot}/{id} should be ready but was {option.Status}");
    }

    private static void RequireBlocked(VehicleModPathReport report, string slot, string id, string expectedWarningCode)
    {
        VehicleModOption option = Find(report, slot, id);
        Require(option.Status == VehicleModOptionStatus.Blocked, $"{slot}/{id} should be blocked but was {option.Status}");
        Require(option.WarningCodes.Any(code => code.Equals(expectedWarningCode, StringComparison.OrdinalIgnoreCase)),
            $"{slot}/{id} should include warning {expectedWarningCode}, actual [{string.Join(", ", option.WarningCodes)}]");
    }

    private static void RequireMissing(VehicleModPathReport report, string slot, string id)
    {
        if (report.VehicleOptions.Any(option =>
            option.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
            option.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Vehicle mod path probe failed: {slot}/{id} should not be present.");
        }
    }

    private static VehicleModOption Find(VehicleModPathReport report, string slot, string id)
    {
        return report.VehicleOptions.FirstOrDefault(option =>
            option.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase) &&
            option.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException($"Vehicle mod path probe failed: {slot}/{id} was not found.");
    }
}
