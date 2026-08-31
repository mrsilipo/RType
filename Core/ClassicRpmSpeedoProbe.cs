using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicRpmSpeedoProbe
{
    private const float Dt = 1f / 120f;
    private const float OmegaToRpm = 60f / MathF.Tau;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic RPM/speedo probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine($"  wheelRadius={parameters.WheelRadiusMeters:0.000}m finalDrive={parameters.FinalDriveRatio:0.000} limiter={parameters.LimiterHardCutRpm:0}rpm cutMultiplier={parameters.RevLimiterCutTorqueMultiplier:0.00}");
        Console.WriteLine("  gear ratio limiterSpeedKmh speedBefore/After rpmBefore/After roadRpmAfter limiter driveBefore/After overByKmh classification");

        for (int gear = 1; gear <= parameters.ForwardGearRatios.Length; gear++)
        {
            RunGear(parameters, engine, gear);
        }

        Console.WriteLine("Classic RPM/speedo probe complete.");
    }

    private static void RunGear(VehicleSimulationParameters parameters, SimulationEngineParameters engine, int gear)
    {
        float ratio = parameters.ForwardGearRatios[gear - 1];
        float limiterSpeed = SpeedKmhAtRpm(parameters, gear, parameters.LimiterHardCutRpm);
        float startSpeed = MathF.Max(5f, limiterSpeed - 4f);
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = gear;
        simulator.State.Velocity = new Vector2(0f, startSpeed / 3.6f);

        Sample before = default;
        Sample after = default;
        for (int i = 0; i < SecondsToTicks(2.0f); i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, brakeAssistEnabled: true), Dt);
            Sample sample = Capture(simulator.State, parameters, gear);
            if (!before.Captured && sample.RoadRpm >= parameters.LimiterHardCutRpm - 20f)
            {
                before = sample;
            }

            if (sample.RoadRpm >= parameters.LimiterHardCutRpm + 120f || sample.Limiter)
            {
                after = sample;
            }
        }

        if (!before.Captured)
        {
            before = Capture(simulator.State, parameters, gear);
        }

        if (!after.Captured)
        {
            after = Capture(simulator.State, parameters, gear);
        }

        float overBy = after.SpeedKmh - limiterSpeed;
        string classification = !after.Limiter && after.RoadRpm < parameters.LimiterHardCutRpm - 20f
            ? "not-reaching-limiter"
            : after.DriveForceN <= before.DriveForceN * 0.35f && overBy < 2.0f
                ? "limited"
                : "still-driving-past-limit";
        Console.WriteLine(
            $"  {gear,4} {ratio,5:0.000} {limiterSpeed,7:0.0} " +
            $"{before.SpeedKmh,6:0.0}/{after.SpeedKmh,6:0.0} " +
            $"{before.Rpm,6:0}/{after.Rpm,6:0} {after.RoadRpm,7:0} {after.Limiter,-7} " +
            $"{before.DriveForceN,6:0}/{after.DriveForceN,6:0} {overBy,7:0.0} {classification}");
    }

    private static Sample Capture(VehicleState state, VehicleSimulationParameters parameters, int gear)
    {
        float ratio = parameters.ForwardGearRatios[Math.Clamp(gear, 1, parameters.ForwardGearRatios.Length) - 1];
        float roadRpm = MathF.Abs(state.SignedForwardSpeed) /
            MathF.Max(0.05f, parameters.WheelRadiusMeters) *
            ratio *
            parameters.FinalDriveRatio *
            OmegaToRpm;
        return new Sample(
            true,
            state.SpeedMetersPerSecond * 3.6f,
            state.Rpm,
            roadRpm,
            state.RevLimiterActive,
            state.DriveForce);
    }

    private static float SpeedKmhAtRpm(VehicleSimulationParameters parameters, int gear, float rpm)
    {
        float ratio = parameters.ForwardGearRatios[Math.Clamp(gear, 1, parameters.ForwardGearRatios.Length) - 1];
        float wheelRpm = rpm / MathF.Max(0.001f, ratio * parameters.FinalDriveRatio);
        return wheelRpm / 60f * MathF.Tau * parameters.WheelRadiusMeters * 3.6f;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct Sample(
        bool Captured,
        float SpeedKmh,
        float Rpm,
        float RoadRpm,
        bool Limiter,
        float DriveForceN);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
