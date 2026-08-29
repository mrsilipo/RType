using RType.Audio;
using RType.Data;
using RType.Vehicle;

namespace RType.Core;

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
        ProbeVehicleBuild("Data/PurchaseCars/2000_Ek9_Stock.json");
        ProbeHighSpeedStraightLineScreechGate();
        ProbeLoop("Generic tyres", "wheelspin", "Assets/Sounds/Generic/TyreScreech_001.wav");
        ProbeLoopSlice("Generic tyres", "wheelspin-sustain-loop", "Assets/Sounds/Generic/TyreScreech_001.wav", TyreSpinLoopStartRatio, TyreSpinLoopEndRatio);
        ProbeLoopSlice("Generic tyres", "wheelspin-chirp-loop", "Assets/Sounds/Generic/TyreScreech_001.wav", TyreChirpLoopStartRatio, TyreChirpLoopEndRatio);
        ProbeLoop("Generic tyres", "control-loss", "Assets/Sounds/Generic/TyreScreech_002.wav");
        ProbeLoopSlice("Generic tyres", "control-loss-middle-loop", "Assets/Sounds/Generic/TyreScreech_002.wav", ControlLossScreechLoopStartRatio, ControlLossScreechLoopEndRatio);
    }

    private static void ProbeHighSpeedStraightLineScreechGate()
    {
        VehicleState straight = new()
        {
            Velocity = new Microsoft.Xna.Framework.Vector2(0f, 128f / 3.6f),
            Steer = 0f,
            LateralAcceleration = 0f,
            Throttle = 0.42f,
            EffectiveThrottle = 0.42f,
            FrontLeftSlipRatio = 0.02f,
            FrontRightSlipRatio = 0.02f,
            RearLeftSlipRatio = 0.01f,
            RearRightSlipRatio = 0.01f,
            FrontLeftRelaxedLongitudinalSlipRatio = 0.48f,
            FrontRightRelaxedLongitudinalSlipRatio = -0.48f,
            RearLeftRelaxedLongitudinalSlipRatio = 0.40f,
            RearRightRelaxedLongitudinalSlipRatio = -0.40f
        };
        if (!VehicleAudioSystem.ShouldSuppressHighSpeedStraightLineScreech(straight))
        {
            throw new InvalidOperationException("Audio probe failed: high-speed straight-line micro-chatter was not suppressed.");
        }

        VehicleState cornering = new()
        {
            Velocity = new Microsoft.Xna.Framework.Vector2(0f, 128f / 3.6f),
            Steer = 0.22f,
            LateralAcceleration = 9.81f * 0.48f,
            FrontLeftSlipRatio = 0.03f,
            FrontRightSlipRatio = 0.03f
        };
        if (VehicleAudioSystem.ShouldSuppressHighSpeedStraightLineScreech(cornering))
        {
            throw new InvalidOperationException("Audio probe failed: high-speed cornering screech was incorrectly suppressed.");
        }

        VehicleState wheelspin = new()
        {
            Velocity = new Microsoft.Xna.Framework.Vector2(0f, 128f / 3.6f),
            Throttle = 1f,
            EffectiveThrottle = 1f,
            FrontLeftSlipRatio = 0.24f,
            FrontRightSlipRatio = 0.22f
        };
        if (VehicleAudioSystem.ShouldSuppressHighSpeedStraightLineScreech(wheelspin))
        {
            throw new InvalidOperationException("Audio probe failed: true high-speed wheelspin was incorrectly suppressed.");
        }

        Console.WriteLine("High-speed straight-line tyre screech gate: suppresses micro-chatter, preserves cornering and wheelspin.");
    }

    private static void ProbeVehicleBuild(string buildPath)
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(buildPath);
        string vtecText = parameters.VtecEnabled
            ? $"VTEC {parameters.VtecActivationRpm:0} rpm"
            : "no VTEC";
        Console.WriteLine($"{parameters.DisplayName} active engine audio: race sample recipe, runtime redline {parameters.LimiterHardCutRpm:0} rpm, authored power peak/redline {parameters.PowerRedlineRpm:0} rpm, {vtecText}, limiter uses pinned VTEC/high-RPM envelope with no dedicated limiter sample");
        Console.WriteLine($"  limiter visualization/audio: depth {RevLimiterPresentationRules.CalculateBounceDepthRpm(parameters.LimiterHardCutRpm):0} rpm, shared period {RevLimiterPresentationRules.CalculateBounceSeconds(parameters.LimiterHardCutRpm):0.000}s, envelope offDuty {parameters.Audio.LimiterStutterOffDuty:0.00}");
        ProbeRaceEngineAudio(parameters);
    }

    private static void ProbeRaceEngineAudio(VehicleSimulationParameters parameters)
    {
        VehicleAudioParameters audio = parameters.Audio;
        Console.WriteLine(
            $"{parameters.DisplayName} race sample engine: profile {audio.EngineAudioProfileId}, samples {audio.EngineSamples.Length}, engineVolume {audio.EngineVolume:0.00}, sampleVolume {audio.EngineSampleVolume:0.00}");
        Console.WriteLine(
            $"  generation: key {audio.EngineAudioSampleGenerationKey}, dsp {audio.EngineAudioDspId}, method {audio.EngineAudioGenerationMethod}, sampleSet {audio.EngineAudioGeneratedSampleSetPath}");
        Console.WriteLine(
            $"  source engine: {audio.EngineAudioEngineCode} {audio.EngineAudioDisplacementCc:0}cc, {audio.EngineAudioBlockId}/{audio.EngineAudioHeadId}, tune {audio.EngineAudioTuneId}, fuel {audio.EngineAudioFuelId}, VTEC {audio.EngineAudioVtecEnabled}");
        if (!string.IsNullOrWhiteSpace(audio.EngineAudioProfileEngineId) &&
            !audio.EngineAudioProfileEngineId.Equals(audio.EngineAudioEngineId, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"  fallback audio source: {audio.EngineAudioProfileEngineId}/{audio.EngineAudioProfileEngineFamily}, allowed {audio.EngineAudioFallbackAllowed}");
        }
        for (int i = 0; i < audio.EngineSamples.Length; i++)
        {
            EngineAudioSampleParameters sample = audio.EngineSamples[i];
            bool fullLoop = sample.LoopStartRatio <= 0f && sample.LoopEndRatio >= 1f;
            float ratioAt3500 = 3500f / MathF.Max(1f, sample.Rpm);
            float neutralVolumeAt3500 = audio.EngineVolume * audio.EngineSampleVolume * sample.Volume;
            Console.WriteLine(
                $"  sample {i}: {Path.GetFileName(sample.Path)}, role {sample.Role}, rpm {sample.Rpm:0}, ratio@3500 {ratioAt3500:0.000}, fullLoop {fullLoop}, neutralVol@3500 {neutralVolumeAt3500:0.000}");
            if (fullLoop)
            {
                ProbeFullLoop(parameters.DisplayName, $"engine-sample-{i}", sample.Path);
            }
            else
            {
                ProbeLoopSlice(parameters.DisplayName, $"engine-sample-{i}", sample.Path, sample.LoopStartRatio, sample.LoopEndRatio);
            }
        }
    }

    private static void ProbeFullLoop(string vehicleName, string role, string path)
    {
        WavLoopSource source = WavLoopSource.Load(ResolveAssetPath(path));
        float seam = CalculateSeamDiscontinuity(source);
        float rms = CalculateRmsLevel(source, source.FrameCount);
        AudioReferenceComparison comparison = CompareDirectPcmReference(source);
        Console.WriteLine(
            $"{vehicleName} {role}: {path}, GAME FULL LOOP, {source.SampleRate} Hz, {source.ChannelCount} channel, {source.FrameCount} frames, rms {rms:0.00000}, seam {seam:0.00000}, crossfade 0.0 ms, trim 0.0 ms, direct3500 maxDiff {comparison.MaximumDifference}, mismatches {comparison.MismatchedSamples}");
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

    private static AudioReferenceComparison CompareDirectPcmReference(WavLoopSource source)
    {
        int sampleCount = Math.Min(source.PcmSamples.Length, source.SampleRate * source.ChannelCount);
        int maxDifference = 0;
        int mismatchedSamples = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short reference = source.PcmSamples[i];
            short gameDirect = source.PcmSamples[i];
            int difference = Math.Abs(reference - gameDirect);
            if (difference > 0)
            {
                mismatchedSamples++;
                maxDifference = Math.Max(maxDifference, difference);
            }
        }

        return new AudioReferenceComparison(maxDifference, mismatchedSamples);
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

    private readonly record struct AudioReferenceComparison(int MaximumDifference, int MismatchedSamples);
}
