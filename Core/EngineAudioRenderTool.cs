using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using RetroRacer.Audio;
using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class EngineAudioRenderTool
{
    private const int SampleRate = 44100;
    private const string VehiclePath = "Data/Vehicles/ek9_reference_2000.json";
    private const string OutputDirectory = "Exports/EngineAudio";
    private const float PrimeSeconds = 0.35f;

    public static void Run()
    {
        VehicleSimulationParameters vehicle = VehicleDefinitionLoader.LoadSimulationParameters(VehiclePath);
        string outputDirectory = Path.Combine(Environment.CurrentDirectory, OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_idle.wav",
            4.0f,
            t =>
            {
                float idleRpm = vehicle.IdleRpm + MathF.Sin(t * MathF.Tau * 1.6f) * 26f;
                return BuildTarget(vehicle, idleRpm, 0.055f, 0.18f, 0f, 0f, 0f);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_slow_rev_sweep.wav",
            10.0f,
            t =>
            {
                float u = SmoothStep(0.15f, 0.96f, t / 10.0f);
                float rpm = MathHelper.Lerp(vehicle.IdleRpm, vehicle.RedlineRpm - 140f, u);
                float throttle = MathHelper.Lerp(0.08f, 0.88f, SmoothStep(0.0f, 0.25f, t / 10.0f));
                return BuildTarget(vehicle, rpm, throttle, 0.18f + throttle * 0.70f, 0f, 0f, 0f);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_full_throttle_pull.wav",
            8.0f,
            t =>
            {
                float u = SmoothStep(0.02f, 0.98f, t / 8.0f);
                float rpm = MathHelper.Lerp(2500f, vehicle.RedlineRpm - 80f, u);
                return BuildTarget(vehicle, rpm, 1f, 0.96f, 0f, 0f, 0f);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_vtec_crossover.wav",
            5.0f,
            t =>
            {
                float u = SmoothStep(0.04f, 0.96f, t / 5.0f);
                float rpm = MathHelper.Lerp(vehicle.Audio.HighRpmBlendInRpm - 800f, vehicle.Audio.HighRpmBlendInRpm + 1500f, u);
                float vtecKick = Pulse(t, 1.95f, 0.22f);
                return BuildTarget(vehicle, rpm, 0.96f, 0.92f, 0f, 0f, 0f, vtecKick);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_limiter_bounce.wav",
            5.0f,
            t =>
            {
                float bounce = MathF.Sin(t * MathF.Tau * 8.6f);
                float clipped = bounce > 0.32f ? 1f : 0f;
                float rpm = vehicle.RedlineRpm - 160f + MathF.Max(0f, bounce) * 210f - clipped * 90f;
                return BuildTarget(vehicle, rpm, 1f, 0.98f, 0.70f + clipped * 0.30f, 0f, 0.10f);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_lift_off_overrun.wav",
            6.0f,
            t =>
            {
                float u = SmoothStep(0.02f, 1f, t / 6.0f);
                float rpm = MathHelper.Lerp(vehicle.RedlineRpm - 600f, 4200f, u);
                float overrun = MathHelper.Lerp(1f, 0.35f, u);
                return BuildTarget(vehicle, rpm, 0f, 0.16f, 0f, overrun, 0f);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_shift_kick.wav",
            6.0f,
            t =>
            {
                const float shiftTime = 2.65f;
                float rpm;
                if (t < shiftTime)
                {
                    rpm = MathHelper.Lerp(4300f, vehicle.RedlineRpm - 160f, SmoothStep(0f, shiftTime, t));
                }
                else
                {
                    float u = SmoothStep(shiftTime, 6.0f, t);
                    rpm = MathHelper.Lerp(5700f, vehicle.RedlineRpm - 120f, u);
                }

                float shock = Pulse(t, shiftTime, 0.16f);
                return BuildTarget(vehicle, rpm, 1f, 0.96f, 0f, 0f, shock);
            }));

        RenderClip(vehicle, outputDirectory, new RenderClipDefinition(
            "ek9_bad_downshift.wav",
            5.0f,
            t =>
            {
                float shock = Pulse(t, 1.10f, 0.28f);
                float limiter = SmoothStep(0.95f, 1.12f, t) * (1f - SmoothStep(3.40f, 4.25f, t));
                float bounce = limiter * MathF.Sin(t * MathF.Tau * 9.5f);
                float baseRpm = t < 1.10f
                    ? MathHelper.Lerp(5200f, 6100f, SmoothStep(0f, 1.10f, t))
                    : MathHelper.Lerp(vehicle.RedlineRpm - 80f, vehicle.RedlineRpm - 360f, SmoothStep(1.10f, 5.0f, t));
                float rpm = baseRpm + bounce * 170f;
                float overrun = MathHelper.Clamp(0.62f + shock * 0.38f, 0f, 1f);
                return BuildTarget(vehicle, rpm, 0.08f, 0.28f, limiter, overrun, shock);
            }));

        Console.WriteLine();
        Console.WriteLine($"Rendered EK9 gas-flow engine sim clips to {outputDirectory}");
        Console.WriteLine("Use these for quick tuning against the in-game sound path before changing sample or physics parameters.");
    }

    private static void RenderClip(VehicleSimulationParameters vehicle, string outputDirectory, RenderClipDefinition clip)
    {
        string path = Path.Combine(outputDirectory, clip.FileName);
        EngineSimulatorSampleSynth synth = new(vehicle.Audio, SampleRate);
        EngineSimulatorSynthesisTarget firstTarget = clip.TargetAt(0f);
        synth.SetTarget(firstTarget);

        int primeFrames = (int)(PrimeSeconds * SampleRate);
        for (int i = 0; i < primeFrames; i++)
        {
            _ = synth.NextSample();
        }

        int frameCount = Math.Max(1, (int)(clip.DurationSeconds * SampleRate));
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: false);
        WriteWavHeader(writer, frameCount, SampleRate);

        double sumSquares = 0.0;
        float peak = 0f;
        for (int frame = 0; frame < frameCount; frame++)
        {
            float seconds = frame / (float)SampleRate;
            synth.SetTarget(clip.TargetAt(seconds));
            float faded = ApplyEdgeFade(synth.NextSample(), frame, frameCount);
            peak = MathF.Max(peak, MathF.Abs(faded));
            sumSquares += faded * faded;
            short sample = (short)MathF.Round(MathHelper.Clamp(faded, -1f, 1f) * short.MaxValue);
            writer.Write(sample);
        }

        float rms = (float)Math.Sqrt(sumSquares / frameCount);
        Console.WriteLine($"{clip.FileName}: {clip.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s, peak {peak:0.000}, rms {rms:0.0000}");
    }

    private static EngineSimulatorSynthesisTarget BuildTarget(
        VehicleSimulationParameters vehicle,
        float rpm,
        float throttle,
        float requestedLoad,
        float limiter,
        float overrun,
        float shock,
        float vtecKick = 0f)
    {
        VehicleAudioParameters audio = vehicle.Audio;
        float shapedThrottle = MathF.Pow(MathHelper.Clamp(throttle, 0f, 1f), MathF.Max(0.1f, audio.EngineSimulatorThrottleGamma));
        vtecKick = MathHelper.Clamp(vtecKick, 0f, 1f);
        float load = MathHelper.Clamp(0.14f + shapedThrottle * 0.82f + requestedLoad * 0.38f + shock * 0.16f + vtecKick * 0.12f - overrun * 0.10f, 0f, 1f);
        float highRpmBlend = throttle >= audio.HighRpmMinimumThrottle
            ? SmoothStep(audio.HighRpmBlendInRpm, audio.HighRpmBlendInRpm + MathF.Max(1f, audio.HighRpmBlendWidthRpm), rpm)
            : 0f;
        highRpmBlend = MathHelper.Clamp(highRpmBlend + vtecKick * 0.32f, 0f, 1f);
        shock = MathHelper.Clamp(MathF.Max(shock, vtecKick * 0.42f), 0f, 1f);

        return new EngineSimulatorSynthesisTarget(
            MathHelper.Clamp(rpm, 450f, MathF.Max(450f, vehicle.RedlineRpm + vehicle.RevLimiterBounceRpm)),
            throttle,
            MathHelper.Clamp(load + overrun * 0.22f, 0f, 1f),
            highRpmBlend,
            limiter,
            overrun,
            shock);
    }

    private static float ApplyEdgeFade(float sample, int frame, int frameCount)
    {
        int fadeFrames = Math.Min(frameCount / 6, (int)(0.035f * SampleRate));
        if (fadeFrames <= 0)
        {
            return sample;
        }

        float fadeIn = frame < fadeFrames ? frame / (float)fadeFrames : 1f;
        float fadeOut = frame >= frameCount - fadeFrames ? (frameCount - frame - 1) / (float)fadeFrames : 1f;
        return sample * MathHelper.Clamp(MathF.Min(fadeIn, fadeOut), 0f, 1f);
    }

    private static float Pulse(float seconds, float center, float halfWidth)
    {
        float distance = MathF.Abs(seconds - center);
        if (distance >= halfWidth)
        {
            return 0f;
        }

        float t = 1f - distance / MathF.Max(0.001f, halfWidth);
        return t * t * (3f - 2f * t);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static void WriteWavHeader(BinaryWriter writer, int frameCount, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        short blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        int dataBytes = frameCount * blockAlign;

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
    }

    private sealed record RenderClipDefinition(string FileName, float DurationSeconds, Func<float, EngineSimulatorSynthesisTarget> TargetAt);
}
