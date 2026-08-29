using System.Text.Json;
using RType.Vehicle;

namespace RType.Data;

internal static class VehicleRaceSampleAudioBuilder
{
    public static VehicleAudioParameters Build(
        ResolvedEngineAssembly engine,
        ResolvedDrivetrainBuild drivetrain,
        string buildPath)
    {
        EngineAudioProfile? engineAudioProfile = LoadEngineAudioProfile(engine.EngineAudioProfilePath);
        float vtecBlendInRpm = engine.VtecEnabled
            ? MathF.Max(engine.IdleRpm, engine.VtecActivationRpm - MathF.Max(1f, engine.VtecTransitionWidthRpm))
            : engine.PowerRedlineRpm;
        float vtecBlendWidthRpm = engine.VtecEnabled
            ? MathF.Max(1f, engine.VtecTransitionWidthRpm)
            : MathF.Max(1f, engine.LimiterHardCutRpm - engine.PowerRedlineRpm);

        return new VehicleAudioParameters
        {
            EngineLoopPath = ReadProfileString(engineAudioProfile, string.Empty, "engineLoop"),
            HighRpmLoopPath = ReadProfileString(engineAudioProfile, string.Empty, "highRpmLoop"),
            EngineSamples = ReadSamples(engineAudioProfile),
            EngineAudioProfilePath = engineAudioProfile?.ResolvedPath ?? engine.EngineAudioProfilePath,
            EngineAudioProfileId = engineAudioProfile?.Id ?? string.Empty,
            EngineAudioProfileEngineId = engine.EngineAudioProfileEngineId,
            EngineAudioProfileEngineFamily = engine.EngineAudioProfileEngineFamily,
            EngineAudioFallbackAllowed = engine.EngineAudioFallbackAllowed,
            EngineAudioSourceRecordingPath = engineAudioProfile?.SourceRecordingPath ?? engine.EngineAudioSourceRecordingPath,
            EngineAudioGeneratedSampleSetPath = engine.EngineAudioGeneratedSampleSetPath,
            EngineAudioGenerationMethod = engine.EngineAudioGenerationMethod,
            EngineAudioDspId = engine.EngineAudioDspId,
            EngineAudioDspDisplayName = engine.EngineAudioDspDisplayName,
            EngineAudioSampleGenerationKey = BuildSampleGenerationKey(engine),
            EngineAudioEngineId = engine.EngineId,
            EngineAudioEngineCode = engine.EngineCode,
            EngineAudioEngineFamily = engine.Family,
            EngineAudioEngineCombinationId = engine.EngineCombinationId,
            EngineAudioBlockId = engine.BlockId,
            EngineAudioHeadId = engine.HeadId,
            EngineAudioValvetrain = engine.Valvetrain,
            EngineAudioTuneId = engine.TuneId,
            EngineAudioFuelId = engine.FuelId,
            EngineAudioDisplacementCc = engine.DisplacementCc,
            EngineAudioCompressionRatio = engine.CompressionRatio,
            EngineAudioVtecEnabled = engine.VtecEnabled,
            EngineAudioVtecActivationRpm = engine.VtecActivationRpm,
            BaseSampleRpm = ReadProfileSingle(engineAudioProfile, 3500f, "baseSampleRpm"),
            MinimumPlaybackRatio = ReadProfileSingle(engineAudioProfile, 0.32f, "minimumPlaybackRatio"),
            MaximumPlaybackRatio = ReadProfileSingle(engineAudioProfile, 3.3f, "maximumPlaybackRatio"),
            EngineSampleCrossfadeWidthRpm = ReadProfileSingle(engineAudioProfile, 24f, "engineSampleCrossfadeWidthRpm"),
            EngineIdleBlendOutRpm = ReadProfileSingle(engineAudioProfile, 1650f, "engineIdleBlendOutRpm"),
            EngineSampleVolume = ReadProfileSingle(engineAudioProfile, 0.72f, "engineSampleVolume"),
            EngineVolume = ReadProfileSingle(engineAudioProfile, 1f, "engineVolume"),
            IdleVolume = ReadProfileSingle(engineAudioProfile, 0f, "idleVolume"),
            ThrottleVolume = ReadProfileSingle(engineAudioProfile, 0f, "throttleVolume"),
            OverrunVolume = ReadProfileSingle(engineAudioProfile, 0f, "overrunVolume"),
            EngineBrakeVolume = ReadProfileSingle(engineAudioProfile, 0f, "engineBrakeVolume"),
            ShiftKickVolume = ReadProfileSingle(engineAudioProfile, 0f, "shiftKickVolume"),
            HighRpmBlendInRpm = ReadProfileSingle(engineAudioProfile, vtecBlendInRpm, "highRpmBlendInRpm"),
            HighRpmBlendWidthRpm = ReadProfileSingle(engineAudioProfile, vtecBlendWidthRpm, "highRpmBlendWidthRpm"),
            HighRpmMinimumThrottle = ReadProfileSingle(engineAudioProfile, 0f, "highRpmMinimumThrottle"),
            HighRpmMinimumSpeedMetersPerSecond = ReadProfileSingle(engineAudioProfile, 0f, "highRpmMinimumSpeedMetersPerSecond"),
            HighRpmVolumeBoost = ReadProfileSingle(engineAudioProfile, 0f, "highRpmVolumeBoost"),
            LimiterStutterFrequencyHz = ReadProfileSingle(engineAudioProfile, 15f, "limiter", "stutterHz"),
            LimiterStutterOffDuty = ReadProfileSingle(engineAudioProfile, 0.50f, "limiter", "offDuty"),
            LimiterStutterIntensity = ReadProfileSingle(engineAudioProfile, 1f, "limiter", "intensity"),
            RTypeEngineEnabled = false,
            RTypeEngineBuildPath = buildPath,
            RTypeEngineVolume = 0f,
            RaceAudioThrottleGamma = engine.ThrottleGamma,
            RaceAudioGearRatios = drivetrain.ForwardGearRatios.Length > 0
                ? [.. drivetrain.ForwardGearRatios]
                : [3.23f, 2.105f, 1.458f, 1.107f, 0.848f],
            RaceAudioFinalDriveRatio = drivetrain.FinalDriveRatio,
            EngineSimulatorEnabled = false,
            EngineSimulatorVolume = 0f,
            EngineSimulatorProfileMaxTorqueNm = FindPeakTorque(engine.TorqueCurve),
            EngineSimulatorProfileMaxEngineBrakeTorqueNm = FindPeakTorque(engine.EngineBrakeTorqueCurve),
            EngineSimulatorProfileTorqueCurveRpm = [.. engine.TorqueCurve.Select(point => point.Rpm)],
            EngineSimulatorProfileTorqueCurveNm = [.. engine.TorqueCurve.Select(point => point.TorqueNm)],
            EngineSimulatorProfileEngineBrakeCurveRpm = [.. engine.EngineBrakeTorqueCurve.Select(point => point.Rpm)],
            EngineSimulatorProfileEngineBrakeCurveNm = [.. engine.EngineBrakeTorqueCurve.Select(point => point.TorqueNm)]
        };
    }

    public static string BuildSampleGenerationKey(ResolvedEngineAssembly engine)
    {
        string[] parts =
        [
            engine.EngineId,
            string.IsNullOrWhiteSpace(engine.EngineCombinationId) ? "factory" : engine.EngineCombinationId,
            engine.BlockId,
            engine.HeadId,
            engine.TuneId,
            engine.FuelId,
            engine.InstalledParts.TryGetValue("blockUpgrade", out string? blockUpgrade) ? blockUpgrade : "block_upgrade_unknown",
            engine.InstalledParts.TryGetValue("headUpgrade", out string? headUpgrade) ? headUpgrade : "head_upgrade_unknown",
            engine.InstalledParts.TryGetValue("displacement", out string? displacement) ? displacement : "displacement_unknown",
            engine.InstalledParts.TryGetValue("portPolishing", out string? portPolishing) ? portPolishing : "ports_unknown",
            engine.InstalledParts.TryGetValue("throttleBody", out string? throttleBody) ? throttleBody : "throttle_unknown",
            engine.InstalledParts.TryGetValue("cams", out string? cams) ? cams : "cams_unknown",
            engine.InstalledParts.TryGetValue("intake", out string? intake) ? intake : "intake_unknown",
            engine.InstalledParts.TryGetValue("intakeRunnerLength", out string? runnerLength) ? runnerLength : "runner_unknown",
            engine.InstalledParts.TryGetValue("valveSprings", out string? valveSprings) ? valveSprings : "valve_springs_unknown",
            engine.InstalledParts.TryGetValue("headers", out string? headers) ? headers : "headers_unknown",
            engine.InstalledParts.TryGetValue("exhaust", out string? exhaust) ? exhaust : "exhaust_unknown",
            engine.InstalledParts.TryGetValue("flywheel", out string? flywheel) ? flywheel : "flywheel_unknown",
            engine.InstalledParts.TryGetValue("clutch", out string? clutch) ? clutch : "clutch_unknown",
            engine.InstalledParts.TryGetValue("engineAudioDsp", out string? engineAudioDsp) ? engineAudioDsp : "engine_audio_dsp_unknown"
        ];

        return string.Join("__", parts.Select(NormalizeKeyPart));
    }

    private static EngineAudioSampleParameters[] ReadSamples(EngineAudioProfile? profile)
    {
        if (profile is null || profile.Samples.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<EngineAudioSampleParameters> samples = [];
        foreach (JsonElement sampleElement in profile.Samples.EnumerateArray())
        {
            string path = ReadString(sampleElement, string.Empty, "path");
            float rpm = ReadSingle(sampleElement, 0f, "rpm");
            if (string.IsNullOrWhiteSpace(path) || rpm <= 0f)
            {
                continue;
            }

            samples.Add(new EngineAudioSampleParameters(
                path,
                rpm,
                ReadBoolean(sampleElement, false, "highRpm"),
                ReadBoolean(sampleElement, false, "limiter"),
                ReadSingle(sampleElement, 1f, "volume"),
                ReadString(sampleElement, "normal", "role"),
                ReadSingle(sampleElement, 0f, "loopStart"),
                ReadSingle(sampleElement, 1f, "loopEnd")));
        }

        return [.. samples.OrderBy(sample => sample.Rpm)];
    }

    private static EngineAudioProfile? LoadEngineAudioProfile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? resolvedPath = ResolveOptionalDataPath(path);
        if (resolvedPath is null)
        {
            throw new FileNotFoundException($"Engine audio profile JSON was not found: {path}", path);
        }

        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });

        JsonElement root = document.RootElement;
        if (!TryGet(root, out JsonElement samplesElement, "samples"))
        {
            throw new InvalidDataException($"Engine audio profile must contain a samples array: {resolvedPath}");
        }

        return new EngineAudioProfile(
            resolvedPath,
            ReadString(root, Path.GetFileNameWithoutExtension(resolvedPath), "id"),
            ReadString(root, string.Empty, "sourceRecordingPath"),
            root.Clone(),
            samplesElement.Clone());
    }

    private static string ReadProfileString(EngineAudioProfile? profile, string fallback, params string[] path)
    {
        return profile is null ? fallback : ReadString(profile.Root, fallback, path);
    }

    private static float ReadProfileSingle(EngineAudioProfile? profile, float fallback, params string[] path)
    {
        return profile is null ? fallback : ReadSingle(profile.Root, fallback, path);
    }

    private static float FindPeakTorque(TorqueCurvePoint[] curve)
    {
        return curve.Length == 0 ? 0f : curve.Max(point => point.TorqueNm);
    }

    private static string NormalizeKeyPart(string value)
    {
        char[] chars = value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        string normalized = new(chars);
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized.Trim('_');
    }

    private static string? ResolveOptionalDataPath(string path)
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

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("value", out JsonElement nestedValue))
        {
            value = nestedValue;
        }

        return value.TryGetSingle(out float result) ? result : fallback;
    }

    private sealed record EngineAudioProfile(
        string ResolvedPath,
        string Id,
        string SourceRecordingPath,
        JsonElement Root,
        JsonElement Samples);
}
