using System.Diagnostics;
using Microsoft.Xna.Framework;
using RetroRacer.Audio;
using RetroRacer.Data;
using RetroRacer.Vehicle;
using RetroRacer.World;

namespace RetroRacer.Core;

public static class PerformanceProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine("Performance probe");
        Console.WriteLine($"  vehicle: {parameters.DisplayName}");
        Console.WriteLine($"  audio sim rate: {parameters.Audio.EngineSimulatorSimulationFrequencyHz:0} Hz");
        Console.WriteLine($"  audio fluid steps: {parameters.Audio.EngineSimulatorFluidSimulationSteps}");
        Console.WriteLine($"  physics engine sim rate: {parameters.EngineSimulatorPhysicsSimulationFrequencyHz:0} Hz, fluid steps {parameters.EngineSimulatorPhysicsFluidSimulationSteps}, enabled {parameters.EngineSimulatorDrivesPhysics}, full driveline {parameters.EngineSimulatorFullDriveline}");

        MeasureVehiclePhysics(parameters, engineParameters);
        if (parameters.Audio.EngineSimulatorEnabled && parameters.Audio.EngineSimulatorVolume > 0f)
        {
            MeasureEngineSimAudio(parameters);
        }
    }

    private static void MeasureVehiclePhysics(VehicleSimulationParameters parameters, SimulationEngineParameters engineParameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 120f;
        const int steps = 1800;
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            float time = i * dt;
            VehicleInput input = time switch
            {
                < 4f => new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true),
                < 5f => new VehicleInput(0.8f, 0f, 0.45f, throttleAssistEnabled: true),
                < 6f => new VehicleInput(0.2f, 0f, -0.35f, throttleAssistEnabled: true),
                _ => new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true)
            };
            simulator.Update(input, dt);
        }

        stopwatch.Stop();
        double millisecondsPerStep = stopwatch.Elapsed.TotalMilliseconds / steps;
        Console.WriteLine($"  physics: {steps} ticks in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms, {millisecondsPerStep:0.000} ms/tick");
    }

    private static void MeasureEngineSimAudio(VehicleSimulationParameters parameters)
    {
        const int sampleRate = 44100;
        const int seconds = 5;
        const int frames = sampleRate * seconds;
        EngineSimulatorSampleSynth synth = new(parameters.Audio, sampleRate);
        synth.SetTarget(new EngineSimulatorSynthesisTarget(
            7200f,
            1f,
            0.96f,
            1f,
            0f,
            0f,
            0.2f));

        for (int i = 0; i < sampleRate / 4; i++)
        {
            _ = synth.NextSample();
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        float peak = 0f;
        for (int i = 0; i < frames; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(synth.NextSample()));
        }

        stopwatch.Stop();
        double realtimeRatio = seconds / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        Console.WriteLine($"  engine audio synth: {seconds:0.0}s in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms, {realtimeRatio:0.00}x realtime, peak {peak:0.000}");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
