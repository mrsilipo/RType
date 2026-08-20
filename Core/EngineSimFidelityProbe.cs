using System.Diagnostics;
using RetroRacer.Audio;
using RetroRacer.Data;
using RetroRacer.Vehicle;

namespace RetroRacer.Core;

public static class EngineSimFidelityProbe
{
    private const int SampleRate = 44100;
    private const int Seconds = 3;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        VehicleAudioParameters audio = parameters.Audio;

        Console.WriteLine("Engine Sim fidelity probe");
        Console.WriteLine($"  vehicle: {parameters.DisplayName}");
        Console.WriteLine($"  runtime: {audio.EngineSimulatorSimulationFrequencyHz:0} Hz, fluid steps {audio.EngineSimulatorFluidSimulationSteps}, taps {audio.EngineSimulatorImpulseResponseTaps}");
        Console.WriteLine("  config | render | realtime | peak");

        Measure(audio, "runtime", (int)MathF.Round(audio.EngineSimulatorSimulationFrequencyHz), audio.EngineSimulatorFluidSimulationSteps);
        Measure(audio, "16000/2", 16000, 2);
        Measure(audio, "20000/1", 20000, 1);
        Measure(audio, "20000/2", 20000, 2);
        Measure(audio, "20000/4", 20000, 4);
    }

    private static void Measure(VehicleAudioParameters audio, string label, int simulationRate, int fluidSteps)
    {
        EngineSimulatorSampleSynth synth = new(audio, SampleRate, simulationRate, fluidSteps);
        synth.SetTarget(new EngineSimulatorSynthesisTarget(
            7200f,
            1f,
            0.96f,
            1f,
            0f,
            0f,
            0.2f));

        for (int i = 0; i < SampleRate / 4; i++)
        {
            _ = synth.NextSample();
        }

        int frames = SampleRate * Seconds;
        Stopwatch stopwatch = Stopwatch.StartNew();
        float peak = 0f;
        for (int i = 0; i < frames; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(synth.NextSample()));
        }

        stopwatch.Stop();
        double realtimeRatio = Seconds / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        Console.WriteLine($"  {label,-8} | {stopwatch.Elapsed.TotalMilliseconds,7:0.0} ms | {realtimeRatio,4:0.00}x | {peak:0.000}");
    }
}
