using System.Text.Json;
using RType.Data;

namespace RType.Core;

internal static class EngineAssemblyProbe
{
    public static void Run()
    {
        Probe("stock_b16b_98ron", LoadStockEngineJson());
        ProbeFactory("factory_b16a", "engine_b16a", "block_b16a", "head_b16a_vtec", "tune_b16a_factory");
        ProbeFactory("factory_b18c", "engine_b18c", "block_b18c", "head_b18c_vtec", "tune_b18c_factory");
        ProbeFactory("factory_b18a", "engine_b18a", "block_b18a", "head_b18a_non_vtec", "tune_b18a_factory");
        ProbeFactory("factory_b18b", "engine_b18b", "block_b18b", "head_b18b_non_vtec", "tune_b18b_factory");
        ProbeFactory("factory_b20b", "engine_b20b", "block_b20b", "head_b18b_non_vtec", "tune_b20b_factory");
        ProbeFactory("factory_d16y4", "engine_d16y4", "block_d16y4", "head_d16y4_non_vtec", "tune_d16y4_factory");
        ProbeFactory("factory_d16y8", "engine_d16y8", "block_d16y8", "head_d16y8_vtec", "tune_d16y8_factory");
        ProbeFactory("factory_k20a", "engine_k20a", "block_k20a", "head_k20a_vtec", "tune_k20a_factory");
        ProbeFactory("factory_k24a3", "engine_k24a3", "block_k24a3", "head_k24a3_vtec", "tune_k24a3_factory");
        Probe("high_compression_b16b_e85", CreateHighCompressionE85EngineJson(), allowInfoMessages: true);
    }

    private static void ProbeFactory(string label, string engineId, string blockId, string headId, string tuneId)
    {
        Probe(label, $$"""
        {
          "engineId": "{{engineId}}",
          "blockId": "{{blockId}}",
          "headId": "{{headId}}",
          "tuneId": "{{tuneId}}",
          "fuel": {
            "default": "fuel_98ron",
            "selected": "fuel_98ron",
            "allowed": [
              "fuel_98ron",
              "fuel_e85"
            ]
          },
          "installedParts": {}
        }
        """);
    }

    private static void Probe(string label, string engineJson, bool allowInfoMessages = false)
    {
        using JsonDocument document = JsonDocument.Parse(engineJson, new JsonDocumentOptions { AllowTrailingCommas = true });
        ResolvedEngineAssembly assembly = EngineAssemblyResolver.Resolve(document.RootElement);
        Console.WriteLine($"{label}: {assembly.EngineCode} {assembly.DisplayName}");
        Console.WriteLine($"  ids: engine {assembly.EngineId}, block {assembly.BlockId}, head {assembly.HeadId}, tune {assembly.TuneId} ({assembly.TuneTier}), fuel {assembly.FuelId}");
        Console.WriteLine($"  geometry: {assembly.DisplacementCc:0}cc, bore {assembly.BoreMm:0.0}mm, stroke {assembly.StrokeMm:0.0}mm, compression {assembly.CompressionRatio:0.0}:1");
        Console.WriteLine($"  limits: idle {assembly.IdleRpm:0}, power redline {assembly.PowerRedlineRpm:0}, limiter {assembly.LimiterHardCutRpm:0}, resume {assembly.LimiterResumeRpm:0}");
        Console.WriteLine($"  limiter behavior: cut {assembly.LimiterFuelCutSeconds:0.000}s, restore {assembly.LimiterRestoreSeconds:0.000}s, torque multiplier {assembly.LimiterCutTorqueMultiplier:0.00}");
        Console.WriteLine($"  fuel: {assembly.FuelDisplayName}, octane {assembly.FuelOctaneRon:0}, ethanol {assembly.FuelEthanolContent * 100f:0}%, torque multiplier {assembly.FuelEffectivePowerMultiplier:0.000}");
        Console.WriteLine($"  flow: low {assembly.LowCamFlowMultiplier:0.00}, high {assembly.HighCamFlowMultiplier:0.00}, intake {assembly.IntakeFlowScale:0.00}, exhaust {assembly.ExhaustFlowScale:0.00}, throttleGamma {assembly.ThrottleGamma:0.00}");
        Console.WriteLine($"  clutch: {assembly.ClutchTorqueCapacityNm:0}Nm, bite {assembly.ClutchBitePoint:0.00}, coupling {assembly.ClutchCouplingRate:0.0}, shift kick {assembly.ClutchShiftKickIntensity:0.00}, low-speed assist {assembly.ClutchLowSpeedAssistStrength:0.00}, bite start x{assembly.ClutchBiteInputStartMultiplier:0.00}, launch gamma {assembly.ClutchLaunchAssistExponent:0.00}, throttle gamma {assembly.ClutchLowSpeedThrottleGamma:0.00}, throttle assist {assembly.ClutchLowSpeedThrottleAssist:0.00}, torque assist {assembly.ClutchLowSpeedTorqueAssistNm:0}Nm, rolling lock {assembly.ClutchRollingLockSpeedMetersPerSecond:0.00}m/s/{assembly.ClutchRollingLockSlipRadiansPerSecond:0}rad/s, inertia {assembly.RotationalInertiaKgM2:0.000}kgm2");
        Console.WriteLine($"  audio recipe: {assembly.EngineAudioDspId} ({assembly.EngineAudioDspDisplayName}), profile {assembly.EngineAudioProfilePath}, method {assembly.EngineAudioGenerationMethod}");
        TorqueCurvePowerPoint peakPower = FindPeakPower(assembly.TorqueCurve);
        Console.WriteLine($"  torque curve: {assembly.TorqueCurve.Length} points, peak {FindPeakTorque(assembly.TorqueCurve):0.0}Nm, peak {peakPower.Horsepower:0.0}hp @ {peakPower.Rpm:0}rpm");
        Console.WriteLine($"  engine brake curve: {assembly.EngineBrakeTorqueCurve.Length} points, peak {FindPeakTorque(assembly.EngineBrakeTorqueCurve):0.0}Nm");
        Console.WriteLine($"  composition: baseline peak {assembly.PowerComposition.BaselinePeakTorqueNm:0.0}Nm -> resolved peak {assembly.PowerComposition.ResolvedPeakTorqueNm:0.0}Nm, displacement x{assembly.PowerComposition.DisplacementScale:0.000}, compression x{assembly.PowerComposition.CompressionScale:0.000}, low flow x{assembly.PowerComposition.LowFlowScale:0.000}, high flow x{assembly.PowerComposition.HighFlowScale:0.000}, fuel x{assembly.PowerComposition.FuelEffectivePowerMultiplier:0.000}");
        Console.WriteLine($"  engine brake composition: baseline peak {assembly.PowerComposition.BaselinePeakEngineBrakeTorqueNm:0.0}Nm -> resolved peak {assembly.PowerComposition.ResolvedPeakEngineBrakeTorqueNm:0.0}Nm, scale x{assembly.PowerComposition.EngineBrakeScale:0.000}");
        foreach (EngineAssemblyValidationMessage message in assembly.Validation)
        {
            Console.WriteLine($"  {message.Severity} {message.Code}: {message.Message}");
        }

        EngineAssemblyValidationMessage[] warnings = [.. assembly.Validation
            .Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];
        if (warnings.Length > 0)
        {
            throw new InvalidOperationException($"Engine assembly probe failed: {label} produced {warnings.Length} warning(s).");
        }

        if (!allowInfoMessages &&
            assembly.Validation.Any(message => message.Severity == EngineAssemblyValidationSeverity.Info))
        {
            throw new InvalidOperationException($"Engine assembly probe failed: {label} produced unexpected info validation messages.");
        }

        if (assembly.ClutchTorqueCapacityNm <= 0f)
        {
            throw new InvalidOperationException($"Engine assembly probe failed: {label} resolved no clutch torque capacity.");
        }

        if (string.IsNullOrWhiteSpace(assembly.EngineAudioProfilePath))
        {
            throw new InvalidOperationException($"Engine assembly probe failed: {label} resolved no engine audio profile path.");
        }
    }

    private static string LoadStockEngineJson()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "Data", "PurchaseCars", "2000_Ek9_Stock.json");
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        return document.RootElement.GetProperty("assembly").GetProperty("engine").GetRawText();
    }

    private static string CreateHighCompressionE85EngineJson()
    {
        return """
        {
          "engineId": "engine_b16b",
          "blockId": "block_b16b",
          "headId": "head_b16b_type_r",
          "tuneId": "tune_b16b_factory",
          "fuel": {
            "default": "fuel_98ron",
            "selected": "fuel_e85",
            "allowed": [
              "fuel_98ron",
              "fuel_e85"
            ]
          },
          "installedParts": {
            "blockUpgrade": "block_upgrade_stock_cast",
            "headUpgrade": "head_upgrade_stock_cast",
            "cams": "cam_set_stock",
            "displacement": "displacement_pro_high_comp",
            "portPolishing": "ports_stock",
            "throttleBody": "throttle_stock",
            "intake": "intake_stock",
            "intakeRunnerLength": "intake_length_stock",
            "valveSprings": "valve_springs_stock",
            "headers": "header_stock",
            "exhaust": "exhaust_stock",
            "flywheel": "flywheel_stock",
            "clutch": "clutch_stock",
            "engineAudioDsp": "engine_audio_stock"
          }
        }
        """;
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
}
