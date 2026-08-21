using System.Globalization;
using Microsoft.Xna.Framework;
using RetroRacer.Audio;
using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class EngineSimWaveformCompareTool
{
    private const int CaptureRate = 44100;
    private const float CaptureSeconds = 3f;

    public static void Run(GameLaunchOptions options, string referencePath)
    {
        VehicleSimulationParameters vehicle = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        WavLoopSource reference = WavLoopSource.Load(referencePath);
        float[] generated = RenderReferenceCase(vehicle, reference.FrameCount, reference.SampleRate);
        float[] expected = ResampleMono(reference, generated.Length, reference.SampleRate);

        WaveformMetrics actualMetrics = WaveformMetrics.Measure(generated);
        WaveformMetrics expectedMetrics = WaveformMetrics.Measure(expected);
        double correlation = WaveformMetrics.Correlation(generated, expected);

        Console.WriteLine("Engine Sim waveform comparison");
        Console.WriteLine($"  reference: {referencePath}");
        Console.WriteLine($"  generated: {generated.Length / (double)CaptureRate:0.00} s @ {CaptureRate} Hz");
        Console.WriteLine($"  rms       {actualMetrics.Rms:0.0000} vs {expectedMetrics.Rms:0.0000}");
        Console.WriteLine($"  peak      {actualMetrics.Peak:0.0000} vs {expectedMetrics.Peak:0.0000}");
        Console.WriteLine($"  zero-cross {actualMetrics.ZeroCrossingsPerSecond:0.0} vs {expectedMetrics.ZeroCrossingsPerSecond:0.0} /s");
        Console.WriteLine($"  centroid  {actualMetrics.SpectralCentroidHz:0} vs {expectedMetrics.SpectralCentroidHz:0} Hz");
        Console.WriteLine($"  correlation {correlation:0.0000}");
        Console.WriteLine("  bands Hz | generated | reference");
        for (int i = 0; i < actualMetrics.BandEnergy.Length; i++)
        {
            Console.WriteLine($"  {WaveformMetrics.BandsHz[i],7} | {actualMetrics.BandEnergy[i]:0.000000} | {expectedMetrics.BandEnergy[i]:0.000000}");
        }
    }

    private static float[] RenderReferenceCase(VehicleSimulationParameters vehicle, int referenceFrames, int referenceRate)
    {
        int frameCount = Math.Max(1, (int)MathF.Round(referenceFrames * (CaptureRate / (float)Math.Max(1, referenceRate))));
        EngineSimulatorSampleSynth synth = new(vehicle.Audio, CaptureRate);
        float durationSeconds = referenceFrames / (float)Math.Max(1, referenceRate);
        synth.SetTarget(BuildFullThrottleTarget(vehicle, 0f, durationSeconds));
        for (int i = 0; i < CaptureRate / 4; i++)
        {
            _ = synth.NextSample();
        }

        float[] samples = new float[frameCount];
        for (int i = 0; i < samples.Length; i++)
        {
            float seconds = i / (float)CaptureRate;
            synth.SetTarget(BuildFullThrottleTarget(vehicle, seconds, durationSeconds));
            samples[i] = synth.NextSample();
        }

        return samples;
    }

    private static EngineSimulatorSynthesisTarget BuildFullThrottleTarget(VehicleSimulationParameters vehicle, float seconds, float durationSeconds)
    {
        float duration = MathF.Max(0.1f, durationSeconds);
        float t = MathHelper.Clamp(seconds / duration, 0f, 1f);
        float u = SmoothStep(0.02f, 0.98f, t);
        float rpm = MathHelper.Lerp(2500f, vehicle.RedlineRpm - 80f, u);
        float vtec = SmoothStep(
            vehicle.Audio.HighRpmBlendInRpm,
            vehicle.Audio.HighRpmBlendInRpm + MathF.Max(1f, vehicle.Audio.HighRpmBlendWidthRpm),
            rpm);
        return new EngineSimulatorSynthesisTarget(rpm, 1f, 0.96f, vtec, 0f, 0f, 0f);
    }

    private static float[] ResampleMono(WavLoopSource source, int frameCount, int sourceRate)
    {
        float[] result = new float[frameCount];
        for (int i = 0; i < result.Length; i++)
        {
            float sourceFrame = MathF.Min(
                MathF.Max(0f, i * sourceRate / (float)CaptureRate),
                MathF.Max(0, source.FrameCount - 1));
            int lower = Math.Clamp((int)sourceFrame, 0, source.FrameCount - 1);
            int upper = Math.Min(source.FrameCount - 1, lower + 1);
            float alpha = sourceFrame - lower;
            result[i] = MathHelper.Lerp(source.Samples[lower * source.ChannelCount], source.Samples[upper * source.ChannelCount], alpha);
        }

        return result;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private readonly record struct WaveformMetrics(
        float Rms,
        float Peak,
        float ZeroCrossingsPerSecond,
        float SpectralCentroidHz,
        float[] BandEnergy)
    {
        public static readonly int[] BandsHz = [100, 300, 600, 1200, 2400, 4800, 9600];

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = Math.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        public static WaveformMetrics Measure(float[] samples)
        {
            if (samples.Length == 0)
            {
                return new(0f, 0f, 0f, 0f, new float[BandsHz.Length]);
            }

            double sumSquares = 0.0;
            float peak = 0f;
            int zeroCrossings = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float sample = samples[i];
                sumSquares += sample * sample;
                peak = MathF.Max(peak, MathF.Abs(sample));
                if (i > 0 && (sample < 0f) != (samples[i - 1] < 0f))
                {
                    zeroCrossings++;
                }
            }

            float[] bands = new float[BandsHz.Length];
            double weightedFrequency = 0.0;
            double totalEnergy = 0.0;
            int analysisFrames = Math.Min(samples.Length, CaptureRate * 2);
            for (int i = 0; i < BandsHz.Length; i++)
            {
                double real = 0.0;
                double imaginary = 0.0;
                double frequency = BandsHz[i];
                for (int frame = 0; frame < analysisFrames; frame++)
                {
                    double phase = Math.Tau * frequency * frame / CaptureRate;
                    real += samples[frame] * Math.Cos(phase);
                    imaginary -= samples[frame] * Math.Sin(phase);
                }

                double energy = (real * real + imaginary * imaginary) / Math.Max(1, analysisFrames * analysisFrames);
                bands[i] = (float)energy;
                weightedFrequency += frequency * energy;
                totalEnergy += energy;
            }

            return new(
                (float)Math.Sqrt(sumSquares / samples.Length),
                peak,
                zeroCrossings * CaptureRate / (float)samples.Length,
                totalEnergy > 1.0e-12 ? (float)(weightedFrequency / totalEnergy) : 0f,
                bands);
        }

        public static double Correlation(float[] first, float[] second)
        {
            int count = Math.Min(first.Length, second.Length);
            if (count == 0)
            {
                return 0.0;
            }

            double firstMean = first.Take(count).Average();
            double secondMean = second.Take(count).Average();
            double numerator = 0.0;
            double firstEnergy = 0.0;
            double secondEnergy = 0.0;
            for (int i = 0; i < count; i++)
            {
                double a = first[i] - firstMean;
                double b = second[i] - secondMean;
                numerator += a * b;
                firstEnergy += a * a;
                secondEnergy += b * b;
            }

            return numerator / Math.Sqrt(Math.Max(1.0e-12, firstEnergy * secondEnergy));
        }
    }
}
