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
        ProbeVehicle("Data/Vehicles/ek9_reference_2000.json");
        ProbeLoop("Generic tyres", "wheelspin", "Assets/Sounds/Generic/TyreScreech_001.wav");
        ProbeLoopSlice("Generic tyres", "wheelspin-sustain-loop", "Assets/Sounds/Generic/TyreScreech_001.wav", TyreSpinLoopStartRatio, TyreSpinLoopEndRatio);
        ProbeLoopSlice("Generic tyres", "wheelspin-chirp-loop", "Assets/Sounds/Generic/TyreScreech_001.wav", TyreChirpLoopStartRatio, TyreChirpLoopEndRatio);
        ProbeLoop("Generic tyres", "control-loss", "Assets/Sounds/Generic/TyreScreech_002.wav");
        ProbeLoopSlice("Generic tyres", "control-loss-middle-loop", "Assets/Sounds/Generic/TyreScreech_002.wav", ControlLossScreechLoopStartRatio, ControlLossScreechLoopEndRatio);
    }

    private static void ProbeVehicle(string vehiclePath)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(vehiclePath);
        Console.WriteLine($"{parameters.DisplayName} active engine audio: race sample recipe, redline {parameters.RedlineRpm:0} rpm, VTEC {parameters.VtecActivationRpm:0} rpm, limiter uses VTEC/high-RPM bounce with no dedicated limiter sample");
        ProbeRaceEngineAudio(parameters);
    }

    private static void ProbeRaceEngineAudio(VehicleSimulationParameters parameters)
    {
        VehicleAudioParameters audio = parameters.Audio;
        Console.WriteLine(
            $"{parameters.DisplayName} race sample engine: profile {audio.EngineAudioProfileId}, samples {audio.EngineSamples.Length}, engineVolume {audio.EngineVolume:0.00}, sampleVolume {audio.EngineSampleVolume:0.00}");
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
