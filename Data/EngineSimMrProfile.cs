using System.Globalization;
using System.Text.RegularExpressions;

namespace RetroRacer.Data;

internal sealed record EngineSimMrProfile(
    string ResolvedScriptPath,
    string Name,
    int CylinderCount,
    int[] FiringOrder,
    float[] CylinderAttenuation,
    int[] CylinderExhaust,
    float[] ExhaustVolumes,
    string ImpulseResponsePath,
    float ImpulseResponseVolume,
    float BoreMillimeters,
    float StrokeMillimeters,
    float RodLengthMillimeters,
    float FuelBurningEfficiency,
    float FuelTurbulence,
    float SimulationFrequencyHz,
    float ThrottleGamma,
    float HighFrequencyGain,
    float Noise,
    float Jitter,
    float LimiterDurationSeconds,
    float[] IgnitionTimingRpm,
    float[] IgnitionTimingDegrees,
    float IntakePlenumVolumeLiters,
    float IntakeRunnerLengthInches,
    float ExhaustPrimaryTubeLengthInches,
    float ExhaustVolumeLiters,
    float LowIntakeDurationDegrees,
    float LowIntakeLiftMillimeters,
    float LowExhaustDurationDegrees,
    float LowExhaustLiftMillimeters,
    float LowCamGamma,
    float LowIntakeCenterDegrees,
    float LowExhaustCenterDegrees,
    float VtecIntakeDurationDegrees,
    float VtecIntakeLiftMillimeters,
    float VtecExhaustDurationDegrees,
    float VtecExhaustLiftMillimeters,
    float VtecCamGamma,
    float VtecIntakeCenterDegrees,
    float VtecExhaustCenterDegrees,
    int FluidSimulationSteps,
    float StarterTorqueNm,
    float StarterSpeedRpm,
    float CrankshaftFrictionTorqueNm,
    float CrankshaftMomentOfInertiaKgM2,
    float CrankshaftMassKg,
    float FlywheelMassKg,
    float TransmissionMaxClutchTorqueNm,
    float[] TransmissionGearRatios,
    float VehicleMassKg,
    float VehicleDiffRatio,
    float VehicleTireRadiusMeters,
    float VehicleRollingResistanceN)
{
    public static EngineSimMrProfile? TryLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? resolvedPath = ResolveOptionalPath(path);
        if (resolvedPath is null)
        {
            return null;
        }

        string script = File.ReadAllText(resolvedPath);
        string? packageRoot = FindEngineSimPackageRoot(resolvedPath);
        string irLibraryPath = packageRoot is null
            ? string.Empty
            : Path.Combine(packageRoot, "es", "sound-library", "impulse_responses.mr");
        string irLibrary = File.Exists(irLibraryPath) ? File.ReadAllText(irLibraryPath) : string.Empty;
        (string impulseResponsePath, float impulseResponseVolume) = ResolveImpulseResponse(script, irLibrary, packageRoot);

        CamLobe lowIntake = ExtractCamLobe(script, "intake_lobe");
        CamLobe lowExhaust = ExtractCamLobe(script, "exhaust_lobe");
        CamLobe vtecIntake = ExtractCamLobe(script, "vtec_intake_lobe");
        CamLobe vtecExhaust = ExtractCamLobe(script, "vtec_exhaust_lobe");
        (float[] timingRpm, float[] timingDegrees) = ExtractTimingCurve(script);
        string objectsScript = ReadEngineSimObjects(packageRoot);
        string crankshaftBlock = ExtractBlock(script, @"crankshaft\s+\w+");
        string vehicleBlock = ExtractBlock(script, @"\bvehicle");
        string transmissionBlock = ExtractBlock(script, @"\btransmission");

        return new EngineSimMrProfile(
            resolvedPath,
            ExtractString(script, @"name:\s*""(?<value>[^""]+)""", Path.GetFileNameWithoutExtension(resolvedPath)),
            ExtractCylinderCount(script),
            ExtractFiringOrder(script),
            ExtractCylinderAttenuation(script),
            ExtractCylinderExhaust(script),
            ExtractExhaustVolumes(script),
            impulseResponsePath,
            impulseResponseVolume,
            ExtractLengthMillimeters(script, @"label\s+bore\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>mm|inch)", 81f),
            ExtractLengthMillimeters(script, @"label\s+stroke\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>mm|inch)", 87.2f),
            ExtractLengthMillimeters(script, @"label\s+rod_length\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>mm|inch)", 137.922f),
            ExtractSingle(script, @"max_burning_efficiency:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.75f),
            ExtractSingle(script, @"max_turbulence_effect:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 2.5f),
            ExtractSingle(script, @"simulation_frequency:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 20000f),
            ExtractSingle(script, @"throttle_gamma:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 2f),
            ExtractSingle(script, @"hf_gain:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.002f),
            ExtractSingle(script, @"noise:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.253f),
            ExtractSingle(script, @"jitter:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.195f),
            ExtractSingle(script, @"limiter_duration:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 0.05f),
            timingRpm,
            timingDegrees,
            ExtractVolumeLiters(script, @"plenum_volume:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>L|cc)", 1.325f),
            ExtractLengthInches(script, @"runner_length:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>inch|mm)", 7f),
            ExtractLengthInches(script, @"primary_tube_length:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>inch|mm)", 10f),
            ExtractVolumeLiters(script, @"(?<!_)volume:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>L|cc)", 100f),
            lowIntake.DurationAt50ThouDegrees,
            lowIntake.LiftMillimeters,
            lowExhaust.DurationAt50ThouDegrees,
            lowExhaust.LiftMillimeters,
            lowIntake.Gamma,
            ExtractSingle(script, @"intake_lobe_center:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg", 116f),
            ExtractSingle(script, @"exhaust_lobe_center:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg", 116f),
            vtecIntake.DurationAt50ThouDegrees,
            vtecIntake.LiftMillimeters,
            vtecExhaust.DurationAt50ThouDegrees,
            vtecExhaust.LiftMillimeters,
            vtecIntake.Gamma,
            ExtractSingle(script, @"vtec_intake_lobe_center:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg", 100f),
            ExtractSingle(script, @"vtec_exhaust_lobe_center:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg", 100f),
            ExtractInt(script, @"fluid_simulation_steps:\s*(?<value>\d+)", ExtractInt(objectsScript, @"fluid_simulation_steps:\s*(?<value>\d+)", 8)),
            ExtractTorqueNm(script, @"starter_torque:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>lb_ft|N_m|Nm)", 94.91f),
            ExtractSingle(script, @"starter_speed:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm", -500f),
            ExtractTorqueNm(crankshaftBlock, @"friction_torque:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>lb_ft|N_m|Nm)", 1.36f),
            EvaluateSimpleProduct(ExtractRaw(crankshaftBlock, @"moment_of_inertia:\s*(?<value>[^,\r\n]+)", "0.114934")),
            ExtractMassKg(crankshaftBlock, @"(?<!_)mass:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>lb|kg|g)", 16.10f),
            ExtractMassKg(crankshaftBlock, @"flywheel_mass:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>lb|kg|g)", 4.54f),
            ExtractTorqueNm(transmissionBlock, @"max_clutch_torque:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>lb_ft|N_m|Nm)", 406.75f),
            ExtractGearRatios(script),
            ExtractMassKg(vehicleBlock, @"mass:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>lb|kg|g)", 1088.62f),
            ExtractSingle(vehicleBlock, @"diff_ratio:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 3.55f),
            ExtractLengthMeters(vehicleBlock, @"tire_radius:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>inch|mm|m)", 0.254f),
            ExtractForceNewtons(vehicleBlock, @"rolling_resistance:\s*(?<value>[^,\r\n]+?)\s*\*\s*units\.(?<unit>N)", 300f));
    }

    private static int ExtractCylinderCount(string script)
    {
        return Math.Max(1, Regex.Matches(script, @"\.add_cylinder\s*\(", RegexOptions.CultureInvariant).Count);
    }

    private static float[] ExtractCylinderAttenuation(string script)
    {
        return
        [
            .. ExtractCylinderBodies(script)
                .Select(body => ExtractSingle(body, @"sound_attenuation:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 1f))
        ];
    }

    private static int[] ExtractCylinderExhaust(string script)
    {
        return
        [
            .. ExtractCylinderBodies(script)
                .Select(body => (int)ExtractSingle(body, @"exhaust_system:\s*exhaust(?<value>\d+)", 0f))
        ];
    }

    private static IEnumerable<string> ExtractCylinderBodies(string script)
    {
        return Regex.Matches(script, @"\.add_cylinder\s*\((?<body>.*?)\n\s*\)", RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["body"].Value);
    }

    private static float[] ExtractExhaustVolumes(string script)
    {
        return
        [
            .. Regex.Matches(
                    script,
                    @"exhaust_system\s+exhaust(?<index>\d+)\s*\((?<body>.*?)\n\s*\)",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant)
                .OrderBy(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
                .Select(match => EvaluateSimpleProduct(ExtractRaw(match.Groups["body"].Value, @"audio_volume:\s*(?<value>[^,\r\n]+)", "1")))
        ];
    }

    private static int[] ExtractFiringOrder(string script)
    {
        return
        [
            .. Regex.Matches(script, @"\.connect_wire\(wires\.wire(?<value>\d+)", RegexOptions.CultureInvariant)
                .Select(match => int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture))
        ];
    }

    private static (float[] Rpm, float[] Degrees) ExtractTimingCurve(string script)
    {
        var samples = Regex.Matches(
                script,
                @"\.add_sample\(\s*(?<rpm>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.rpm,\s*(?<degrees>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg",
                RegexOptions.CultureInvariant)
            .Select(match => (
                Rpm: ParseSingle(match.Groups["rpm"].Value, 0f),
                Degrees: ParseSingle(match.Groups["degrees"].Value, 0f)))
            .ToArray();

        return ([.. samples.Select(sample => sample.Rpm)], [.. samples.Select(sample => sample.Degrees)]);
    }

    private static CamLobe ExtractCamLobe(string script, string name)
    {
        Match match = Regex.Match(
            script,
            $@"harmonic_cam_lobe\s+{Regex.Escape(name)}\s*\((?<body>.*?)\n\s*\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        string body = match.Success ? match.Groups["body"].Value : string.Empty;
        return new CamLobe(
            ExtractSingle(body, @"duration_at_50_thou:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.deg", 0f),
            ExtractLengthMillimeters(body, @"lift:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\*\s*units\.(?<unit>mm|inch)", 0f),
            ExtractSingle(body, @"gamma:\s*(?<value>[-+]?\d+(?:\.\d+)?)", 1f));
    }

    private static (string Path, float Volume) ResolveImpulseResponse(string script, string irLibrary, string? packageRoot)
    {
        string label = ExtractRaw(script, @"impulse_response:\s*ir_lib\.(?<value>[A-Za-z0-9_]+)", string.Empty);
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(irLibrary) || packageRoot is null)
        {
            return (string.Empty, 0.01f);
        }

        Match match = Regex.Match(
            irLibrary,
            $@"output\s+{Regex.Escape(label)}\s*:\s*impulse_response\(filename:\s*""(?<filename>[^""]+)"",\s*volume:\s*(?<volume>[-+]?\d+(?:\.\d+)?)\)",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return (string.Empty, 0.01f);
        }

        string filename = match.Groups["filename"].Value.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(packageRoot, "es", "sound-library", filename);
        string projectRelative = Path.GetRelativePath(Environment.CurrentDirectory, fullPath).Replace('\\', '/');
        return (projectRelative, ParseSingle(match.Groups["volume"].Value, 0.01f));
    }

    private static string? FindEngineSimPackageRoot(string scriptPath)
    {
        DirectoryInfo? directory = new FileInfo(scriptPath).Directory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "es", "sound-library", "impulse_responses.mr")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ReadEngineSimObjects(string? packageRoot)
    {
        if (packageRoot is null)
        {
            return string.Empty;
        }

        string objectsPath = Path.Combine(packageRoot, "es", "objects", "objects.mr");
        return File.Exists(objectsPath) ? File.ReadAllText(objectsPath) : string.Empty;
    }

    private static string ExtractBlock(string script, string headerPattern)
    {
        Match match = Regex.Match(
            script,
            $@"{headerPattern}\s*\((?<body>.*?)\n\s*\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["body"].Value : string.Empty;
    }

    private static int ExtractInt(string text, string pattern, int fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success &&
               int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private static float ExtractTorqueNm(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        float value = EvaluateSimpleProduct(match.Groups["value"].Value);
        return match.Groups["unit"].Value switch
        {
            "lb_ft" => value * 1.35581795f,
            _ => value
        };
    }

    private static float ExtractMassKg(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        float value = EvaluateSimpleProduct(match.Groups["value"].Value);
        return match.Groups["unit"].Value switch
        {
            "lb" => value * 0.45359237f,
            "g" => value / 1000f,
            _ => value
        };
    }

    private static float ExtractLengthMeters(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        float value = EvaluateSimpleProduct(match.Groups["value"].Value);
        return match.Groups["unit"].Value switch
        {
            "inch" => value * 0.0254f,
            "mm" => value / 1000f,
            _ => value
        };
    }

    private static float ExtractForceNewtons(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? EvaluateSimpleProduct(match.Groups["value"].Value) : fallback;
    }

    private static float[] ExtractGearRatios(string script)
    {
        return
        [
            .. Regex.Matches(script, @"\.add_gear\(\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*\)", RegexOptions.CultureInvariant)
                .Select(match => ParseSingle(match.Groups["value"].Value, 0f))
                .Where(value => value > 0f)
        ];
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

    private static string ExtractString(string text, string pattern, string fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : fallback;
    }

    private static string ExtractRaw(string text, string pattern, string fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : fallback;
    }

    private static float ExtractSingle(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? ParseSingle(match.Groups["value"].Value, fallback) : fallback;
    }

    private static float ExtractLengthMillimeters(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        float value = ParseSingle(match.Groups["value"].Value, fallback);
        return match.Groups["unit"].Value == "inch" ? value * 25.4f : value;
    }

    private static float ExtractLengthInches(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        float value = ParseSingle(match.Groups["value"].Value, fallback);
        return match.Groups["unit"].Value == "mm" ? value / 25.4f : value;
    }

    private static float ExtractVolumeLiters(string text, string pattern, float fallback)
    {
        Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        float value = ParseSingle(match.Groups["value"].Value, fallback);
        return match.Groups["unit"].Value == "cc" ? value / 1000f : value;
    }

    private static float EvaluateSimpleProduct(string expression)
    {
        float product = 1f;
        bool foundNumber = false;
        foreach (Match match in Regex.Matches(expression, @"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant))
        {
            product *= ParseSingle(match.Value, 1f);
            foundNumber = true;
        }

        return foundNumber ? product : 0f;
    }

    private static float ParseSingle(string value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
    }

    private readonly record struct CamLobe(float DurationAt50ThouDegrees, float LiftMillimeters, float Gamma);
}
