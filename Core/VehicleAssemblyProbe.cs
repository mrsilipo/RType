using RType.Data;

namespace RType.Core;

internal static class VehicleAssemblyProbe
{
    public static void Run(GameLaunchOptions options)
    {
        string buildPath = string.IsNullOrWhiteSpace(options.VehiclePath) ||
            VehiclePathMigration.IsLegacyStockEk9VehicleDefinitionPath(options.VehiclePath)
            ? VehiclePathMigration.StockEk9PurchaseCarPath
            : options.VehiclePath;

        ResolvedVehicleAssembly assembly = VehicleAssemblyResolver.Resolve(buildPath);
        RType.Vehicle.VehicleSimulationParameters runtimeParameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(buildPath);
        Console.WriteLine($"{assembly.DisplayName} resolved assembly");
        Console.WriteLine($"  build: {assembly.BuildId} ({assembly.Classification})");
        Console.WriteLine($"  path: {assembly.BuildPath}");
        Console.WriteLine($"  ownership: playerOwned {assembly.PlayerOwned}, owner {FormatOptional(assembly.OwnerProfileId)}, garageSlot {assembly.GarageSlot}");
        Console.WriteLine($"  source template: {FormatOptional(assembly.PurchaseCarId)} {FormatOptional(assembly.SourcePurchaseCarPath)}");
        Console.WriteLine($"  chassis: {assembly.ChassisCode}, layout {assembly.DrivetrainLayout}, shell {assembly.BodyShellId}");
        Console.WriteLine($"  driven wheels: {FormatDrivenWheels(assembly.RuntimeBuild.DrivetrainLayout)}");
        Console.WriteLine($"  engine: {assembly.Engine.EngineId} ({assembly.Engine.EngineCode}), block {assembly.Engine.BlockId}, head {assembly.Engine.HeadId}, tune {assembly.Engine.TuneId} ({assembly.Engine.TuneTier})");
        Console.WriteLine($"  geometry: {assembly.Engine.DisplacementCc:0}cc, bore {assembly.Engine.BoreMm:0.0}mm, stroke {assembly.Engine.StrokeMm:0.0}mm, compression {assembly.Engine.CompressionRatio:0.0}:1");
        Console.WriteLine($"  catalog/tune limits: idle {assembly.Engine.IdleRpm:0}, power redline {assembly.Engine.PowerRedlineRpm:0}, limiter {assembly.Engine.LimiterHardCutRpm:0}, resume {assembly.Engine.LimiterResumeRpm:0}, max gauge {assembly.Engine.MaxGaugeRpm:0}");
        Console.WriteLine($"  limiter behavior: cut {assembly.Engine.LimiterFuelCutSeconds:0.000}s, restore {assembly.Engine.LimiterRestoreSeconds:0.000}s, torque multiplier {assembly.Engine.LimiterCutTorqueMultiplier:0.00}");
        Console.WriteLine($"  runtime build limits: idle {runtimeParameters.IdleRpm:0}, power redline {runtimeParameters.PowerRedlineRpm:0}, limiter {runtimeParameters.LimiterHardCutRpm:0}, max gauge {runtimeParameters.MaxGaugeRpm:0}");
        Console.WriteLine($"  fuel: {assembly.Engine.FuelId} ({assembly.Engine.FuelDisplayName}), octane {assembly.Engine.FuelOctaneRon:0}, ethanol {assembly.Engine.FuelEthanolContent * 100f:0}%, torque multiplier {assembly.Engine.FuelEffectivePowerMultiplier:0.000}, safe compression {assembly.Engine.FuelSafeCompressionRatio:0.0}:1");
        Console.WriteLine($"  vtec: {(assembly.Engine.VtecEnabled ? "yes" : "no")}, activation {assembly.Engine.VtecActivationRpm:0}, transition {assembly.Engine.VtecTransitionWidthRpm:0}");
        Console.WriteLine($"  flow: lowCam {assembly.Engine.LowCamFlowMultiplier:0.00}, highCam {assembly.Engine.HighCamFlowMultiplier:0.00}, intake {assembly.Engine.IntakeFlowScale:0.00}, exhaust {assembly.Engine.ExhaustFlowScale:0.00}, throttleGamma {assembly.Engine.ThrottleGamma:0.00}");
        Console.WriteLine($"  clutch: capacity {assembly.Engine.ClutchTorqueCapacityNm:0}Nm, bite {assembly.Engine.ClutchBitePoint:0.00}, coupling {assembly.Engine.ClutchCouplingRate:0.0}, low-speed assist {assembly.Engine.ClutchLowSpeedAssistStrength:0.00}, bite start x{assembly.Engine.ClutchBiteInputStartMultiplier:0.00}, launch gamma {assembly.Engine.ClutchLaunchAssistExponent:0.00}, throttle gamma {assembly.Engine.ClutchLowSpeedThrottleGamma:0.00}, throttle assist {assembly.Engine.ClutchLowSpeedThrottleAssist:0.00}, torque assist {assembly.Engine.ClutchLowSpeedTorqueAssistNm:0}Nm, rolling lock {assembly.Engine.ClutchRollingLockSpeedMetersPerSecond:0.00}m/s/{assembly.Engine.ClutchRollingLockSlipRadiansPerSecond:0}rad/s");
        Console.WriteLine($"  valve springs: safe {assembly.Engine.ValveSpringSafeContinuousRpm:0}rpm, float {assembly.Engine.ValveSpringFloatStartRpm:0}rpm");
        Console.WriteLine($"  audio recipe: {assembly.Engine.EngineAudioDspId} ({assembly.Engine.EngineAudioDspDisplayName}), profile {assembly.Engine.EngineAudioProfilePath}, method {assembly.Engine.EngineAudioGenerationMethod}");
        TorqueCurvePowerPoint peakPower = FindPeakPower(assembly.Engine.TorqueCurve);
        Console.WriteLine($"  torque curve: {assembly.Engine.TorqueCurve.Length} points, peak {FindPeakTorque(assembly.Engine.TorqueCurve):0.0}Nm, peak {peakPower.Horsepower:0.0}hp @ {peakPower.Rpm:0}rpm");
        Console.WriteLine($"  engine brake curve: {assembly.Engine.EngineBrakeTorqueCurve.Length} points, peak {FindPeakTorque(assembly.Engine.EngineBrakeTorqueCurve):0.0}Nm");
        Console.WriteLine($"  engine composition: baseline peak {assembly.Engine.PowerComposition.BaselinePeakTorqueNm:0.0}Nm -> resolved peak {assembly.Engine.PowerComposition.ResolvedPeakTorqueNm:0.0}Nm, displacement x{assembly.Engine.PowerComposition.DisplacementScale:0.000}, compression x{assembly.Engine.PowerComposition.CompressionScale:0.000}, low flow x{assembly.Engine.PowerComposition.LowFlowScale:0.000}, high flow x{assembly.Engine.PowerComposition.HighFlowScale:0.000}, fuel x{assembly.Engine.PowerComposition.FuelEffectivePowerMultiplier:0.000}");
        Console.WriteLine($"  engine brake composition: baseline peak {assembly.Engine.PowerComposition.BaselinePeakEngineBrakeTorqueNm:0.0}Nm -> resolved peak {assembly.Engine.PowerComposition.ResolvedPeakEngineBrakeTorqueNm:0.0}Nm, scale x{assembly.Engine.PowerComposition.EngineBrakeScale:0.000}");
        Console.WriteLine($"  engine mass estimate: {assembly.Engine.EstimatedAssemblyMassKg:0.0} kg");
        Console.WriteLine($"  engine mass components: {assembly.Engine.MassComponents.Count} parts, component sum {assembly.Engine.MassComponents.Sum(component => component.MassKg):0.0} kg");
        Console.WriteLine($"  catalog mass estimate: body {assembly.Mass.BodyShellKg:0.0} kg, engine {assembly.Mass.EngineAssemblyKg:0.0} kg, total catalog {assembly.MassProperties.CatalogMassKg:0.0} kg");
        Console.WriteLine($"  swap kit mass: {assembly.RuntimeBuild.SwapKits.TotalMassKg:0.0} kg");
        Console.WriteLine($"  resolved mass: {assembly.MassProperties.TotalMassKg:0.0} kg, front {assembly.MassProperties.FrontWeightDistribution * 100f:0.0}%, cgY {assembly.MassProperties.CenterOfGravityHeightMeters:0.000}m, yaw inertia {assembly.MassProperties.YawInertiaKgM2:0}kgm2, residual {assembly.MassProperties.CalibrationResidualMassKg:0.0} kg");
        Console.WriteLine($"  mass trace: body {assembly.MassProperties.Trace.BodyShellMassKg:0.0}kg, bolt-on {assembly.MassProperties.Trace.BoltOnMassKg:0.0}kg, catalog {assembly.MassProperties.Trace.CatalogMassKg:0.0}kg, residual {assembly.MassProperties.Trace.CalibrationResidualMassKg:+0.0;-0.0;0.0}kg, components {assembly.MassProperties.Trace.ComponentCount}");
        Console.WriteLine($"  yaw trace: raw {assembly.MassProperties.Trace.RawYawInertiaKgM2:0}kgm2, calibration x{assembly.MassProperties.Trace.YawInertiaCalibrationScale:0.000}, calibrated {assembly.MassProperties.Trace.CalibratedYawInertiaKgM2:0}kgm2, final {assembly.MassProperties.Trace.FinalYawInertiaKgM2:0}kgm2");
        Console.WriteLine($"  front hard-points: {assembly.RuntimeBuild.Chassis.FrontSuspensionHardPoints.Type}, caster {assembly.RuntimeBuild.Chassis.FrontSuspensionHardPoints.CasterDegrees:0.0}deg, camber gain {assembly.RuntimeBuild.Chassis.FrontSuspensionHardPoints.CamberGainDegreesPerMeter:0.0}deg/m, toe gain {assembly.RuntimeBuild.Chassis.FrontSuspensionHardPoints.ToeGainDegreesPerMeter:0.0}deg/m, travel +{assembly.RuntimeBuild.Chassis.FrontSuspensionHardPoints.MaxCompressionMeters:0.000}/-{assembly.RuntimeBuild.Chassis.FrontSuspensionHardPoints.MaxDroopMeters:0.000}m");
        Console.WriteLine($"  rear hard-points: {assembly.RuntimeBuild.Chassis.RearSuspensionHardPoints.Type}, caster {assembly.RuntimeBuild.Chassis.RearSuspensionHardPoints.CasterDegrees:0.0}deg, camber gain {assembly.RuntimeBuild.Chassis.RearSuspensionHardPoints.CamberGainDegreesPerMeter:0.0}deg/m, toe gain {assembly.RuntimeBuild.Chassis.RearSuspensionHardPoints.ToeGainDegreesPerMeter:0.0}deg/m, travel +{assembly.RuntimeBuild.Chassis.RearSuspensionHardPoints.MaxCompressionMeters:0.000}/-{assembly.RuntimeBuild.Chassis.RearSuspensionHardPoints.MaxDroopMeters:0.000}m");
        Console.WriteLine($"  suspension kit: front spring {assembly.RuntimeBuild.Suspension.FrontSpringRateNPerM:0}N/m, rear spring {assembly.RuntimeBuild.Suspension.RearSpringRateNPerM:0}N/m, front roll centre {assembly.RuntimeBuild.Suspension.FrontRollCentreHeightMeters:0.000}m, rear roll centre {assembly.RuntimeBuild.Suspension.RearRollCentreHeightMeters:0.000}m");
        Console.WriteLine($"  brakes: front {assembly.RuntimeBuild.Brakes.FrontDiscDiameterMm:0}mm/{assembly.RuntimeBuild.Brakes.FrontTotalPistonAreaSquareMeters * 10000f:0.00}cm2, rear {assembly.RuntimeBuild.Brakes.RearDiscDiameterMm:0}mm/{assembly.RuntimeBuild.Brakes.RearTotalPistonAreaSquareMeters * 10000f:0.00}cm2, bias {assembly.RuntimeBuild.Brakes.System.BrakeBiasFront * 100f:0.0}%");
        Console.WriteLine($"  tyres: front stiffness {assembly.RuntimeBuild.Tyres.FrontModel.CorneringStiffnessNPerRad:0}N/rad/{assembly.RuntimeBuild.Tyres.FrontModel.LongitudinalStiffnessN:0}N, rear stiffness {assembly.RuntimeBuild.Tyres.RearModel.CorneringStiffnessNPerRad:0}N/rad/{assembly.RuntimeBuild.Tyres.RearModel.LongitudinalStiffnessN:0}N");
        Console.WriteLine("  major mass components:");
        foreach (ResolvedMassComponent component in assembly.MassProperties.Components
                     .OrderByDescending(component => MathF.Abs(component.MassKg))
                     .Take(6))
        {
            Console.WriteLine($"    {component.Role}: {component.Id}, {component.MassKg:0.0}kg @ y {component.Y:0.000}m z {component.Z:0.000}m");
        }

        Console.WriteLine("  engine mass components:");
        foreach (ResolvedEngineMassComponent component in assembly.Engine.MassComponents
                     .OrderByDescending(component => MathF.Abs(component.MassKg))
                     .Take(10))
        {
            Console.WriteLine($"    {component.Role}: {component.Id}, {component.MassKg:0.0}kg @ local y {component.LocalY:0.000}m z {component.LocalZ:+0.000;-0.000;0.000}m");
        }

        Console.WriteLine("  installed engine parts:");
        foreach (KeyValuePair<string, string> part in assembly.Engine.InstalledParts.OrderBy(part => part.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"    {part.Key}: {part.Value}");
        }

        if (assembly.RuntimeBuild.SwapKits.InstalledParts.Count > 0)
        {
            Console.WriteLine("  installed swap kits:");
            foreach (KeyValuePair<string, string> part in assembly.RuntimeBuild.SwapKits.InstalledParts.OrderBy(part => part.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {part.Key}: {part.Value}");
            }
        }

        if (assembly.Validation.Count > 0)
        {
            Console.WriteLine("  assembly validation:");
            foreach (VehicleAssemblyValidationMessage message in assembly.Validation)
            {
                Console.WriteLine($"    {message.Severity} {message.Code}: {message.Message}");
            }
        }

        if (assembly.Engine.Validation.Count > 0)
        {
            Console.WriteLine("  engine validation:");
            foreach (EngineAssemblyValidationMessage message in assembly.Engine.Validation)
            {
                Console.WriteLine($"    {message.Severity} {message.Code}: {message.Message}");
            }
        }
    }

    private static float FindPeakTorque(RType.Vehicle.TorqueCurvePoint[] curve)
    {
        return curve.Length == 0 ? 0f : curve.Max(point => point.TorqueNm);
    }

    private static TorqueCurvePowerPoint FindPeakPower(RType.Vehicle.TorqueCurvePoint[] curve)
    {
        if (curve.Length == 0)
        {
            return new TorqueCurvePowerPoint(0f, 0f);
        }

        TorqueCurvePowerPoint peak = new(0f, 0f);
        foreach (RType.Vehicle.TorqueCurvePoint point in curve)
        {
            float horsepower = point.TorqueNm * point.Rpm / 7127f;
            if (horsepower > peak.Horsepower)
            {
                peak = new TorqueCurvePowerPoint(point.Rpm, horsepower);
            }
        }

        return peak;
    }

    private readonly record struct TorqueCurvePowerPoint(float Rpm, float Horsepower);

    private static string FormatDrivenWheels(string layout)
    {
        return layout.Trim().ToUpperInvariant() switch
        {
            "FF" => "FL/FR",
            "FR" or "MR" or "RR" => "RL/RR",
            "AWD" or "4WD" => "FL/FR/RL/RR",
            _ => "FL/FR fallback"
        };
    }

    private static string FormatOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
    }

}
