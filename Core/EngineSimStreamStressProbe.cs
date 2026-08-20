using Microsoft.Xna.Framework;
using RetroRacer.Audio;
using RetroRacer.Camera;
using RetroRacer.Data;
using RetroRacer.Vehicle;
using RetroRacer.World;

namespace RetroRacer.Core;

public static class EngineSimStreamStressProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        using VehicleAudioSystem audio = new();
        audio.SetVehicle(parameters.Audio);

        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 60f;
        const int ticks = 60 * 12;
        for (int i = 0; i < ticks; i++)
        {
            float time = i * dt;
            VehicleInput input = BuildInput(time);
            simulator.Update(input, dt);
            audio.Update(simulator.State, CameraMode.Chase1, active: true, paused: false, dt);
            Thread.Sleep(16);
        }

        Thread.Sleep(250);
        audio.Stop();

        StreamDiagnosticsSummary summary = ReadSummary(AudioDiagnostics.LogFilePath);
        Console.WriteLine("Engine Sim stream stress probe");
        Console.WriteLine($"  vehicle: {parameters.DisplayName}");
        Console.WriteLine($"  audio: {parameters.Audio.EngineSimulatorSimulationFrequencyHz:0} Hz, fluid steps {parameters.Audio.EngineSimulatorFluidSimulationSteps}, taps {parameters.Audio.EngineSimulatorImpulseResponseTaps}");
        Console.WriteLine($"  diagnostics: {AudioDiagnostics.LogFilePath}");
        Console.WriteLine($"  low-buffer events: {summary.LowBufferEvents}");
        Console.WriteLine($"  emergency recovery events: {summary.RecoveryEvents}");
        Console.WriteLine($"  worst reported fill: {summary.MaximumFillMilliseconds:0.00} ms");
        Console.WriteLine($"  worst reported emergency fill: {summary.MaximumEmergencyFillMilliseconds:0.00} ms");
    }

    private static VehicleInput BuildInput(float time)
    {
        if (time < 1.7f)
        {
            return new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true);
        }

        if (time < 3.2f)
        {
            return new VehicleInput(1f, 0f, 0.35f, throttleAssistEnabled: true);
        }

        if (time < 4.0f)
        {
            return new VehicleInput(0f, 0.75f, -0.2f, throttleAssistEnabled: true);
        }

        if (time < 6.5f)
        {
            return new VehicleInput(1f, 0f, -0.45f, throttleAssistEnabled: true);
        }

        if (time < 8.0f)
        {
            return new VehicleInput(0.2f, 0.55f, 0.55f, throttleAssistEnabled: true);
        }

        return new VehicleInput(1f, 0f, MathF.Sin(time * 4f) * 0.25f, throttleAssistEnabled: true);
    }

    private static StreamDiagnosticsSummary ReadSummary(string path)
    {
        if (!File.Exists(path))
        {
            return new StreamDiagnosticsSummary();
        }

        StreamDiagnosticsSummary summary = new();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains("[engine-sim-buffer-low]", StringComparison.Ordinal))
            {
                summary.LowBufferEvents++;
            }
            else if (line.Contains("[engine-sim-stream-recovery]", StringComparison.Ordinal))
            {
                summary.RecoveryEvents++;
                summary.MaximumEmergencyFillMilliseconds = Math.Max(
                    summary.MaximumEmergencyFillMilliseconds,
                    ExtractMilliseconds(line, "max fill "));
            }
            else if (line.Contains("[engine-sim-stream-health]", StringComparison.Ordinal))
            {
                summary.MaximumFillMilliseconds = Math.Max(
                    summary.MaximumFillMilliseconds,
                    ExtractMilliseconds(line, "max fill "));
                summary.MaximumEmergencyFillMilliseconds = Math.Max(
                    summary.MaximumEmergencyFillMilliseconds,
                    ExtractMilliseconds(line, "max emergency "));
            }
        }

        return summary;
    }

    private static double ExtractMilliseconds(string line, string marker)
    {
        int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return 0.0;
        }

        int start = markerIndex + marker.Length;
        int end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
        {
            end++;
        }

        return double.TryParse(
            line[start..end],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0.0;
    }

    private sealed class StreamDiagnosticsSummary
    {
        public int LowBufferEvents { get; set; }

        public int RecoveryEvents { get; set; }

        public double MaximumFillMilliseconds { get; set; }

        public double MaximumEmergencyFillMilliseconds { get; set; }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
