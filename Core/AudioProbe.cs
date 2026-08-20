using RetroRacer.Audio;
using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class AudioProbe
{
    private const float TyreSpinLoopStartRatio = 0f;
    private const float TyreSpinLoopEndRatio = 1f;
    private const float TyreChirpLoopStartRatio = 0.02f;
    private const float TyreChirpLoopEndRatio = 0.22f;
    private const float ControlLossScreechLoopStartRatio = 0.18f;
    private const float ControlLossScreechLoopEndRatio = 0.86f;

    public static void Run()
    {
        ProbeVehicle("Data/Vehicles/ek9_reference_2000.json");
        ProbeVehicle("Data/Vehicles/r33_gtr_reference_1995.json");
        ProbeHondaB18c5Cuts();
        ProbeLoop("Generic tyres", "wheelspin", "Assets/Sounds/Generic/TyreScreech_001.wav");
        ProbeLoopSlice("Generic tyres", "wheelspin-sustain-loop", "Assets/Sounds/Generic/TyreScreech_001.wav", TyreSpinLoopStartRatio, TyreSpinLoopEndRatio);
        ProbeLoopSlice("Generic tyres", "wheelspin-chirp-loop", "Assets/Sounds/Generic/TyreScreech_001.wav", TyreChirpLoopStartRatio, TyreChirpLoopEndRatio);
        ProbeLoop("Generic tyres", "control-loss", "Assets/Sounds/Generic/TyreScreech_002.wav");
        ProbeLoopSlice("Generic tyres", "control-loss-middle-loop", "Assets/Sounds/Generic/TyreScreech_002.wav", ControlLossScreechLoopStartRatio, ControlLossScreechLoopEndRatio);
    }

    private static void ProbeVehicle(string vehiclePath)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(vehiclePath);
        Console.WriteLine(
            $"{parameters.DisplayName} gas-flow engine sim: enabled {parameters.Audio.EngineSimulatorEnabled}, volume {parameters.Audio.EngineSimulatorVolume:0.00}, sample trim {parameters.Audio.EngineSampleVolume:0.00}, mr {FormatMrPath(parameters.Audio.EngineSimulatorMrScriptPath)}, sim {parameters.Audio.EngineSimulatorSimulationFrequencyHz:0} Hz, order {string.Join("-", parameters.Audio.EngineSimulatorFiringOrder)}, route {string.Join("/", parameters.Audio.EngineSimulatorCylinderExhaust)}, exhaust {string.Join("/", parameters.Audio.EngineSimulatorExhaustVolumes.Select(v => v.ToString("0.00")))}, ir taps {parameters.Audio.EngineSimulatorImpulseResponseTaps}, dsp scale/gain {parameters.Audio.EngineSimulatorDspPressureScale:0}/{parameters.Audio.EngineSimulatorDspOutputGain:0.00}, timing {string.Join("/", parameters.Audio.EngineSimulatorIgnitionTimingDegrees.Select(v => v.ToString("0")))}, cam {parameters.Audio.EngineSimulatorLowIntakeDurationDegrees:0}/{parameters.Audio.EngineSimulatorLowIntakeLiftMillimeters:0.0}->{parameters.Audio.EngineSimulatorVtecIntakeDurationDegrees:0}/{parameters.Audio.EngineSimulatorVtecIntakeLiftMillimeters:0.0}, jitter {parameters.Audio.EngineSimulatorJitter:0.000}, noise {parameters.Audio.EngineSimulatorNoise:0.000}, gains {parameters.Audio.EngineSimulatorOverrunGain:0.00}/{parameters.Audio.EngineSimulatorShockGain:0.00}/{parameters.Audio.EngineSimulatorLimiterGain:0.00}");
        if (parameters.Audio.EngineSamples.Length > 0)
        {
            foreach (EngineAudioSampleParameters sample in parameters.Audio.EngineSamples)
            {
                string role = sample.Limiter
                    ? $"limiter-{sample.Rpm:0}"
                    : sample.HighRpm
                        ? $"high-rpm-{sample.Rpm:0}"
                        : $"engine-{sample.Rpm:0}";
                ProbeLoop(parameters.DisplayName, role, sample.Path);
            }

            return;
        }

        ProbeLoop(parameters.DisplayName, "engine", parameters.Audio.EngineLoopPath);
        ProbeLoop(parameters.DisplayName, "high-rpm", parameters.Audio.HighRpmLoopPath);
    }

    private static void ProbeHondaB18c5Cuts()
    {
        ProbeLoop("Honda B18C5 cuts", "idle-0900", "Assets/Sounds/Honda/idle_0900.wav");
        ProbeLoop("Honda B18C5 cuts", "normal-2000", "Assets/Sounds/Honda/normal_2000.wav");
        ProbeLoop("Honda B18C5 cuts", "normal-3500", "Assets/Sounds/Honda/normal_3500.wav");
        ProbeLoop("Honda B18C5 cuts", "normal-5000", "Assets/Sounds/Honda/normal_5000.wav");
        ProbeLoop("Honda B18C5 cuts", "vtec-6200", "Assets/Sounds/Honda/vtec_6200.wav");
        ProbeLoop("Honda B18C5 cuts", "vtec-7600", "Assets/Sounds/Honda/vtec_7600.wav");
        ProbeLoop("Honda B18C5 cuts", "vtec-8200", "Assets/Sounds/Honda/vtec_8200.wav");
        ProbeLoop("Honda B18C5 cuts", "limiter-8200", "Assets/Sounds/Honda/limiter_8200.wav");
    }

    private static string FormatMrPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "none" : Path.GetFileName(path);
    }

    private static void ProbeLoop(string vehicleName, string role, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine($"{vehicleName} {role}: none");
            return;
        }

        WavLoopSource source = WavLoopSource.Load(ResolveAssetPath(path));
        ProbeSource(vehicleName, role, path, source);
    }

    private static void ProbeLoopSlice(string vehicleName, string role, string path, float startRatio, float endRatio)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine($"{vehicleName} {role}: none");
            return;
        }

        WavLoopSource source = WavLoopSource.Load(ResolveAssetPath(path)).Slice(startRatio, endRatio);
        ProbeSource(vehicleName, role, $"{path} [{startRatio:0.00}-{endRatio:0.00}]", source);
    }

    private static void ProbeSource(string vehicleName, string role, string path, WavLoopSource source)
    {
        float seam = CalculateSeamDiscontinuity(source);
        LoopWindow loopWindow = LoopWindowPlanner.Plan(source);
        float rms = CalculateRmsLevel(source, loopWindow.EndFrame);
        float rawOverlapError = LoopWindowPlanner.CalculateMatchError(source, source.FrameCount, loopWindow.CrossfadeFrames);
        float plannedOverlapError = LoopWindowPlanner.CalculateMatchError(source, loopWindow.EndFrame, loopWindow.CrossfadeFrames);
        float crossfadeMs = loopWindow.CrossfadeFrames / (float)source.SampleRate * 1000f;
        float trimmedMs = (source.FrameCount - loopWindow.EndFrame) / (float)source.SampleRate * 1000f;
        Console.WriteLine(
            $"{vehicleName} {role}: {path}, {source.SampleRate} Hz, {source.ChannelCount} channel, {source.FrameCount} frames, rms {rms:0.00000}, seam {seam:0.00000}, crossfade {crossfadeMs:0.0} ms, trim {trimmedMs:0.0} ms, overlap {rawOverlapError:0.00000}->{plannedOverlapError:0.00000}");
    }

    private static float CalculateSeamDiscontinuity(WavLoopSource source)
    {
        float maxDifference = 0f;
        int lastFrame = source.FrameCount - 1;
        for (int channel = 0; channel < source.ChannelCount; channel++)
        {
            float first = source.Samples[channel];
            float last = source.Samples[lastFrame * source.ChannelCount + channel];
            maxDifference = MathF.Max(maxDifference, MathF.Abs(last - first));
        }

        return maxDifference;
    }

    private static float CalculateRmsLevel(WavLoopSource source, int endFrame)
    {
        int sampleCount = Math.Clamp(endFrame, 1, source.FrameCount) * source.ChannelCount;
        double sumSquares = 0.0;
        for (int i = 0; i < sampleCount; i++)
        {
            float sample = source.Samples[i];
            sumSquares += sample * sample;
        }

        return (float)Math.Sqrt(sumSquares / sampleCount);
    }

    private static string ResolveAssetPath(string path)
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

        throw new FileNotFoundException($"Audio asset was not found: {path}", path);
    }
}
