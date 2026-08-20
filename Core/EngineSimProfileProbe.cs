using System.Text.RegularExpressions;
using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class EngineSimProfileProbe
{
    private const string HondaVtecScriptPath = "Assets/Sounds/EngineSim/HondaB18C5/assets/engines/honda_b18c5_vtec.mr";
    private const string EngineSimObjectsPath = "Assets/Sounds/EngineSim/HondaB18C5/es/objects/objects.mr";
    private const string Ek9VehiclePath = "Data/Vehicles/ek9_reference_2000.json";

    public static void Run()
    {
        VehicleSimulationParameters ek9 = VehicleDefinitionLoader.LoadSimulationParameters(Ek9VehiclePath);
        string activeScriptPath = string.IsNullOrWhiteSpace(ek9.Audio.EngineSimulatorMrScriptPath)
            ? HondaVtecScriptPath
            : ek9.Audio.EngineSimulatorMrScriptPath;
        string scriptPath = ResolvePath(activeScriptPath);
        if (!string.IsNullOrWhiteSpace(ek9.Audio.EngineSimulatorProfilePath))
        {
            _ = ResolvePath(ek9.Audio.EngineSimulatorProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(ek9.Audio.EngineSimulatorImpulseResponsePath))
        {
            _ = ResolvePath(ek9.Audio.EngineSimulatorImpulseResponsePath);
        }

        string script = File.ReadAllText(scriptPath);
        string objects = File.Exists(ResolveOptionalPath(EngineSimObjectsPath) ?? string.Empty)
            ? File.ReadAllText(ResolvePath(EngineSimObjectsPath))
            : string.Empty;

        EngineSimHondaProfile profile = EngineSimHondaProfile.Parse(script, objects);

        Console.WriteLine("Engine Sim Honda profile");
        Console.WriteLine($"  source: {scriptPath}");
        Console.WriteLine($"  engine: {profile.Name}");
        Console.WriteLine($"  layout: inline {profile.CylinderCount}, bore {profile.BoreMm:0.0} mm, stroke {profile.StrokeMm:0.0} mm, estimated {profile.EstimatedDisplacementCc:0} cc");
        Console.WriteLine($"  redline: {profile.RedlineRpm:0} rpm, ignition cut {profile.IgnitionRevLimitRpm:0} rpm, limiter duration {profile.LimiterDurationSeconds:0.000} s");
        Console.WriteLine($"  VTEC defaults: min {profile.VtecMinRpm:0} rpm, throttle {profile.VtecMinThrottle:0.00}, speed {profile.VtecMinSpeedMph:0.0} mph");
        Console.WriteLine($"  sound model: sim {profile.SimulationFrequencyHz:0} Hz, hf {profile.HighFrequencyGain:0.0000}, noise {profile.Noise:0.000}, jitter {profile.Jitter:0.000}, exhaust vols {string.Join("/", profile.ExhaustAudioVolumes.Select(v => v.ToString("0.00")))}");
        Console.WriteLine($"  fuel/acoustics: burn {profile.MaxBurningEfficiency:0.00}, turbulence {profile.MaxTurbulenceEffect:0.00}, intake {profile.IntakePlenumVolumeLiters:0.000} L/{profile.IntakeRunnerLengthInches:0.0} in, exhaust {profile.ExhaustPrimaryTubeLengthInches:0.0} in/{profile.ExhaustVolumeLiters:0.0} L");
        Console.WriteLine($"  ignition script order: {string.Join("-", profile.IgnitionWireOrder)}");
        Console.WriteLine($"  timing curve: {string.Join(", ", profile.TimingCurve.Select(sample => $"{sample.Rpm:0}@{sample.Degrees:0}"))}");
        Console.WriteLine($"  low cam: intake {profile.IntakeLobe.DurationAt50ThouDegrees:0} deg/{profile.IntakeLobe.LiftMm:0.0} mm, exhaust {profile.ExhaustLobe.DurationAt50ThouDegrees:0} deg/{profile.ExhaustLobe.LiftMm:0.0} mm");
        Console.WriteLine($"  VTEC cam: intake {profile.VtecIntakeLobe.DurationAt50ThouDegrees:0} deg/{profile.VtecIntakeLobe.LiftMm:0.0} mm, exhaust {profile.VtecExhaustLobe.DurationAt50ThouDegrees:0} deg/{profile.VtecExhaustLobe.LiftMm:0.0} mm");
        Console.WriteLine($"  sim transmission: final {profile.DifferentialRatio:0.00}, gears {string.Join(", ", profile.GearRatios.Select(v => v.ToString("0.000")))}");
        Console.WriteLine();
        Console.WriteLine("EK9 runtime comparison");
        Console.WriteLine($"  vehicle: {ek9.DisplayName}");
        Console.WriteLine($"  shared profile: {FormatProfile(ek9.Audio.EngineSimulatorProfileId, ek9.Audio.EngineSimulatorProfileDisplayName, ek9.Audio.EngineSimulatorProfilePath)}");
        Console.WriteLine($"  runtime limiter: {ek9.RedlineRpm:0} rpm, resume {ek9.RevLimiterResumeRpm:0} rpm, bounce {ek9.RevLimiterBounceRpm:0} rpm");
        Console.WriteLine($"  VTEC audio latch: {ek9.Audio.HighRpmBlendInRpm:0} rpm, width {ek9.Audio.HighRpmBlendWidthRpm:0} rpm, throttle {ek9.Audio.HighRpmMinimumThrottle:0.00}, speed {ek9.Audio.HighRpmMinimumSpeedMetersPerSecond:0.00} m/s");
        Console.WriteLine($"  gas-flow engine sim: enabled {ek9.Audio.EngineSimulatorEnabled}, mr {FormatMrPath(ek9.Audio.EngineSimulatorMrScriptPath)}, volume {ek9.Audio.EngineSimulatorVolume:0.00}, sim {ek9.Audio.EngineSimulatorSimulationFrequencyHz:0} Hz, fluid steps {ek9.Audio.EngineSimulatorFluidSimulationSteps}, order {string.Join("-", ek9.Audio.EngineSimulatorFiringOrder)}, jitter {ek9.Audio.EngineSimulatorJitter:0.000}, noise {ek9.Audio.EngineSimulatorNoise:0.000}, dsp scale/gain {ek9.Audio.EngineSimulatorDspPressureScale:0}/{ek9.Audio.EngineSimulatorDspOutputGain:0.00}, gains overrun/shock/limiter {ek9.Audio.EngineSimulatorOverrunGain:0.00}/{ek9.Audio.EngineSimulatorShockGain:0.00}/{ek9.Audio.EngineSimulatorLimiterGain:0.00}");
        Console.WriteLine($"  Engine Sim power: drives physics {ek9.EngineSimulatorDrivesPhysics}, full driveline {ek9.EngineSimulatorFullDriveline}, sim {ek9.EngineSimulatorPhysicsSimulationFrequencyHz:0} Hz, fluid steps {ek9.EngineSimulatorPhysicsFluidSimulationSteps}, torque scale/blend {ek9.EngineSimulatorPhysicsTorqueScale:0.000}/{ek9.EngineSimulatorPhysicsTorqueBlend:0.00}, engine-brake scale/blend {ek9.EngineSimulatorPhysicsEngineBrakeScale:0.000}/{ek9.EngineSimulatorPhysicsEngineBrakeBlend:0.00}");
        Console.WriteLine($"  Engine Sim mechanics: crank inertia {ek9.Audio.EngineSimulatorCrankshaftMomentOfInertiaKgM2:0.000} kgm2, friction {ek9.Audio.EngineSimulatorCrankshaftFrictionTorqueNm:0.00} Nm, clutch {ek9.Audio.EngineSimulatorTransmissionMaxClutchTorqueNm:0} Nm, sim vehicle {ek9.Audio.EngineSimulatorVehicleMassKg:0} kg, diff {ek9.Audio.EngineSimulatorVehicleDiffRatio:0.00}, tire {ek9.Audio.EngineSimulatorVehicleTireRadiusMeters:0.000} m, rolling {ek9.Audio.EngineSimulatorVehicleRollingResistanceN:0} N");
        Console.WriteLine($"  runtime engine geometry: bore {ek9.Audio.EngineSimulatorBoreMillimeters:0.0} mm, stroke {ek9.Audio.EngineSimulatorStrokeMillimeters:0.0} mm, rod {ek9.Audio.EngineSimulatorRodLengthMillimeters:0.000} mm, fuel burn {ek9.Audio.EngineSimulatorFuelBurningEfficiency:0.00}, turbulence {ek9.Audio.EngineSimulatorFuelTurbulence:0.00}");
        Console.WriteLine($"  runtime timing: {string.Join(", ", ek9.Audio.EngineSimulatorIgnitionTimingRpm.Zip(ek9.Audio.EngineSimulatorIgnitionTimingDegrees).Select(pair => $"{pair.First:0}@{pair.Second:0}"))}");
        Console.WriteLine($"  cylinder audio: attenuation {string.Join("/", ek9.Audio.EngineSimulatorCylinderAttenuation.Select(v => v.ToString("0.00")))}, exhaust route {string.Join("/", ek9.Audio.EngineSimulatorCylinderExhaust)}, exhaust vols {string.Join("/", ek9.Audio.EngineSimulatorExhaustVolumes.Select(v => v.ToString("0.00")))}");
        Console.WriteLine($"  exhaust IR: {ek9.Audio.EngineSimulatorImpulseResponsePath}, volume {ek9.Audio.EngineSimulatorImpulseResponseVolume:0.000}, taps {ek9.Audio.EngineSimulatorImpulseResponseTaps}");
        Console.WriteLine($"  procedural cam: low {ek9.Audio.EngineSimulatorLowIntakeDurationDegrees:0}/{ek9.Audio.EngineSimulatorLowIntakeLiftMillimeters:0.0} intake, {ek9.Audio.EngineSimulatorLowExhaustDurationDegrees:0}/{ek9.Audio.EngineSimulatorLowExhaustLiftMillimeters:0.0} exhaust; VTEC {ek9.Audio.EngineSimulatorVtecIntakeDurationDegrees:0}/{ek9.Audio.EngineSimulatorVtecIntakeLiftMillimeters:0.0} intake, {ek9.Audio.EngineSimulatorVtecExhaustDurationDegrees:0}/{ek9.Audio.EngineSimulatorVtecExhaustLiftMillimeters:0.0} exhaust");
        string sampleBankSummary = ek9.Audio.EngineSamples.Length == 0
            ? "none"
            : string.Join(", ", ek9.Audio.EngineSamples.Select(sample => FormatSample(sample)));
        Console.WriteLine($"  current sample bank: trim {ek9.Audio.EngineSampleVolume:0.00}, {sampleBankSummary}");
        Console.WriteLine($"  game transmission: final {ek9.FinalDriveRatio:0.00}, gears {string.Join(", ", ek9.ForwardGearRatios.Select(v => v.ToString("0.000")))}");
        Console.WriteLine();
        Console.WriteLine("Recommended integration boundary");
        Console.WriteLine("  keep EK9 physics limits at the car data values; do not use the 9400 rpm Engine Sim ignition cut for this B16B");
        Console.WriteLine("  use the Engine Sim profile for live gas-flow audio plus dyno-constrained drivetrain torque and engine braking");
        Console.WriteLine("  keep the native Engine Sim executable out of the runtime unless we replace it with a callable SDK/library wrapper");
    }

    private static string FormatSample(EngineAudioSampleParameters sample)
    {
        string role = sample.Limiter ? "limiter" : sample.HighRpm ? "vtec" : "normal";
        return $"{role}@{sample.Rpm:0}";
    }

    private static string FormatProfile(string id, string displayName, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "none";
        }

        string name = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        return string.IsNullOrWhiteSpace(name)
            ? path
            : $"{name} ({Path.GetFileName(path)})";
    }

    private static string FormatMrPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "none" : Path.GetFileName(path);
    }

    private static string ResolvePath(string path)
    {
        string? resolvedPath = ResolveOptionalPath(path);
        if (resolvedPath is not null)
        {
            return resolvedPath;
        }

        throw new FileNotFoundException($"Engine Sim asset was not found: {path}", path);
    }

    private static string? ResolveOptionalPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed record EngineSimHondaProfile(
        string Name,
        int CylinderCount,
        float BoreMm,
        float StrokeMm,
        float EstimatedDisplacementCc,
        float RedlineRpm,
        float IgnitionRevLimitRpm,
        float LimiterDurationSeconds,
        float VtecMinRpm,
        float VtecMinThrottle,
        float VtecMinSpeedMph,
        float SimulationFrequencyHz,
        float HighFrequencyGain,
        float Noise,
        float Jitter,
        float MaxTurbulenceEffect,
        float MaxBurningEfficiency,
        float IntakePlenumVolumeLiters,
        float IntakeRunnerLengthInches,
        float ExhaustPrimaryTubeLengthInches,
        float ExhaustVolumeLiters,
        float[] ExhaustAudioVolumes,
        int[] IgnitionWireOrder,
        TimingSample[] TimingCurve,
        CamLobe IntakeLobe,
        CamLobe ExhaustLobe,
        CamLobe VtecIntakeLobe,
        CamLobe VtecExhaustLobe,
        float DifferentialRatio,
        float[] GearRatios)
    {
        public static EngineSimHondaProfile Parse(string script, string objects)
        {
            string engineBody = ExtractNodeBody(script, "honda_vtec_i4");
            string valvetrainBody = ExtractNodeBody(objects, "vtec_valvetrain");
            int cylinderCount = Regex.Matches(script, @"\.add_cylinder\s*\(", RegexOptions.CultureInvariant).Count;
            float boreMm = ExtractSingle(script, @"label\s+bore\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.mm", 0f);
            float strokeMm = ExtractSingle(script, @"label\s+stroke\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.mm", 0f);
            float displacementCc = cylinderCount > 0
                ? MathF.PI * MathF.Pow(boreMm * 0.5f, 2f) * strokeMm * cylinderCount / 1000f
                : 0f;

            return new EngineSimHondaProfile(
                ExtractString(engineBody, @"name:\s*""(?<value>[^""]+)""", "unknown"),
                cylinderCount,
                boreMm,
                strokeMm,
                displacementCc,
                ExtractSingle(engineBody, @"redline:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm", 0f),
                ExtractSingle(script, @"rev_limit:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm", 0f),
                ExtractSingle(script, @"limiter_duration:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0f),
                ExtractSingle(script, @"min_rpm:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm", ExtractSingle(valvetrainBody, @"min_rpm\s*\[float\]:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm", 5800f)),
                ExtractSingle(script, @"min_throttle_position:\s*(?<value>[-+]?\d+(?:\.\d+)?)", ExtractSingle(valvetrainBody, @"min_throttle_position\s*\[float\]:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.3f)),
                ExtractSingle(script, @"min_speed:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.mph", ExtractSingle(valvetrainBody, @"min_speed\s*\[float\]:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.mph", 10f)),
                ExtractSingle(engineBody, @"simulation_frequency:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 10000f),
                ExtractSingle(engineBody, @"hf_gain:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.01f),
                ExtractSingle(engineBody, @"noise:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 1f),
                ExtractSingle(engineBody, @"jitter:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.5f),
                ExtractSingle(engineBody, @"max_turbulence_effect:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0f),
                ExtractSingle(engineBody, @"max_burning_efficiency:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0f),
                ExtractSingle(script, @"plenum_volume:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.L", 0f),
                ExtractSingle(script, @"runner_length:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.inch", 0f),
                ExtractSingle(script, @"primary_tube_length:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.inch", 0f),
                ExtractSingle(script, @"(?<!_)volume:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.L", 0f),
                ExtractAudioVolumes(script),
                ExtractIgnitionWireOrder(script),
                ExtractTimingCurve(script),
                ExtractCamLobe(script, "intake_lobe"),
                ExtractCamLobe(script, "exhaust_lobe"),
                ExtractCamLobe(script, "vtec_intake_lobe"),
                ExtractCamLobe(script, "vtec_exhaust_lobe"),
                ExtractSingle(script, @"diff_ratio:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0f),
                ExtractGearRatios(script));
        }
    }

    private readonly record struct CamLobe(float DurationAt50ThouDegrees, float LiftMm, float Gamma);

    private readonly record struct TimingSample(float Rpm, float Degrees);

    private static CamLobe ExtractCamLobe(string text, string name)
    {
        Match match = Regex.Match(
            text,
            $@"harmonic_cam_lobe\s+{Regex.Escape(name)}\s*\((?<body>.*?)\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        string body = match.Success ? match.Groups["body"].Value : string.Empty;
        return new CamLobe(
            ExtractSingle(body, @"duration_at_50_thou:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg", 0f),
            ExtractSingle(body, @"lift:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.mm", 0f),
            ExtractSingle(body, @"gamma:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0f));
    }

    private static string ExtractNodeBody(string text, string nodeName)
    {
        Match match = Regex.Match(
            text,
            $@"public\s+node\s+{Regex.Escape(nodeName)}[^\{{]*\{{(?<body>.*?)\n\}}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["body"].Value : string.Empty;
    }

    private static float[] ExtractAudioVolumes(string text)
    {
        return
        [
            .. Regex.Matches(text, @"audio_volume:\s*(?<value>[^,\r\n]+)", RegexOptions.CultureInvariant)
                .Select(match => EvaluateSimpleProduct(match.Groups["value"].Value))
                .Where(value => value > 0f)
        ];
    }

    private static int[] ExtractIgnitionWireOrder(string text)
    {
        return
        [
            .. Regex.Matches(text, @"\.connect_wire\(wires\.wire(?<value>\d+)", RegexOptions.CultureInvariant)
                .Select(match => int.Parse(match.Groups["value"].Value))
        ];
    }

    private static TimingSample[] ExtractTimingCurve(string text)
    {
        return
        [
            .. Regex.Matches(
                    text,
                    @"\.add_sample\(\s*(?<rpm>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm,\s*(?<degrees>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg",
                    RegexOptions.CultureInvariant)
                .Select(match => new TimingSample(
                    float.Parse(match.Groups["rpm"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(match.Groups["degrees"].Value, System.Globalization.CultureInfo.InvariantCulture)))
        ];
    }

    private static float[] ExtractGearRatios(string text)
    {
        return
        [
            .. Regex.Matches(text, @"\.add_gear\((?<value>[-+]?\d+(?:\.\d+)?)\)", RegexOptions.CultureInvariant)
                .Select(match => float.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture))
        ];
    }

    private static string ExtractString(string text, string pattern, string fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : fallback;
    }

    private static float ExtractSingle(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success &&
               float.TryParse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;
    }

    private static float EvaluateSimpleProduct(string expression)
    {
        float product = 1f;
        bool foundNumber = false;
        foreach (Match match in Regex.Matches(expression, @"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant))
        {
            if (float.TryParse(match.Value, System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                product *= value;
                foundNumber = true;
            }
        }

        return foundNumber ? product : 0f;
    }
}
