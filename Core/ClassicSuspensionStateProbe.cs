using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSuspensionStateProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;

    private static readonly float[] CheckpointsSeconds =
    [
        0.10f,
        0.25f,
        0.40f,
        0.60f,
        0.80f,
        1.00f,
        1.20f,
        1.40f
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic suspension-state probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine(
            $"  owner map: static load=vehicle data; transfer targets=classic load-transfer state; " +
            $"spring/damper per-corner state now owns tyre normal loads; visual pitch/roll follows the same travel state.");
        Console.WriteLine(
            $"  suspension: spring F/R={parameters.FrontSpringRateNPerM:0}/{parameters.RearSpringRateNPerM:0}N/m " +
            $"bump F/R={parameters.FrontBumpDampingNsPerM:0}/{parameters.RearBumpDampingNsPerM:0}Ns/m " +
            $"rebound F/R={parameters.FrontReboundDampingNsPerM:0}/{parameters.RearReboundDampingNsPerM:0}Ns/m");
        Console.WriteLine(
            "  columns: case t speed brake steer pitch roll beta yaw loadTarget FL/FR/RL/RR loadUsed FL/FR/RL/RR travel FL/FR/RL/RR vel FL/FR/RL/RR spring FL/FR/RL/RR damper FL/FR/RL/RR yawF/R");

        RunCase("steady-turn", parameters, engine, time => new VehicleInput(0.25f, 0f, 0.85f));
        RunCase("lift-turn", parameters, engine, time => new VehicleInput(time < 0.30f ? 0.25f : 0f, 0f, 0.85f));
        RunCase("trail-brake", parameters, engine, time => new VehicleInput(0f, Brake(time), 0.85f));
        RunCase("left-right", parameters, engine, time => new VehicleInput(0.20f, 0f, time < 0.72f ? 0.85f : -0.85f));
        RunCase("countersteer", parameters, engine, time => new VehicleInput(0f, time < 0.55f ? 0.75f : 0.10f, time < 0.92f ? 0.85f : -0.60f));

        Console.WriteLine("Classic suspension-state probe complete.");
    }

    private static void RunCase(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        Func<float, VehicleInput> inputForTime)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, StartSpeedKmh / 3.6f);

        int checkpointIndex = 0;
        for (int tick = 1; tick <= SecondsToTicks(1.40f); tick++)
        {
            float time = tick * Dt;
            simulator.Update(inputForTime(time), Dt);
            if (checkpointIndex < CheckpointsSeconds.Length &&
                time + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(label, CheckpointsSeconds[checkpointIndex], simulator.State);
                checkpointIndex++;
            }
        }
    }

    private static float Brake(float time)
    {
        if (time < 0.35f)
        {
            return 0.90f;
        }

        return MathHelper.Lerp(0.90f, 0.12f, SmoothStep01((time - 0.35f) / 0.45f));
    }

    private static void PrintSample(string label, float time, VehicleState state)
    {
        Console.WriteLine(
            $"  {label,-12} {time,4:F2} {state.SpeedMetersPerSecond * 3.6f,6:F1} {state.Brake,5:F2} {state.Steer,5:F2} " +
            $"{MathHelper.ToDegrees(state.BodyPitchRadians),5:F2} {MathHelper.ToDegrees(state.BodyRollRadians),5:F2} " +
            $"{state.ClassicBodySlipAngleDegrees,5:F2} {MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:F1} " +
            $"{state.FrontLeftSuspensionTargetLoadN,5:0}/{state.FrontRightSuspensionTargetLoadN,5:0}/{state.RearLeftSuspensionTargetLoadN,5:0}/{state.RearRightSuspensionTargetLoadN,5:0} " +
            $"{state.FrontLeftLoadN,5:0}/{state.FrontRightLoadN,5:0}/{state.RearLeftLoadN,5:0}/{state.RearRightLoadN,5:0} " +
            $"{state.FrontLeftSuspensionTravelMeters,6:0.000}/{state.FrontRightSuspensionTravelMeters,6:0.000}/{state.RearLeftSuspensionTravelMeters,6:0.000}/{state.RearRightSuspensionTravelMeters,6:0.000} " +
            $"{state.FrontLeftSuspensionVelocityMetersPerSecond,6:0.000}/{state.FrontRightSuspensionVelocityMetersPerSecond,6:0.000}/{state.RearLeftSuspensionVelocityMetersPerSecond,6:0.000}/{state.RearRightSuspensionVelocityMetersPerSecond,6:0.000} " +
            $"{state.FrontLeftSuspensionSpringForceN,6:0}/{state.FrontRightSuspensionSpringForceN,6:0}/{state.RearLeftSuspensionSpringForceN,6:0}/{state.RearRightSuspensionSpringForceN,6:0} " +
            $"{state.FrontLeftSuspensionDamperForceN,6:0}/{state.FrontRightSuspensionDamperForceN,6:0}/{state.RearLeftSuspensionDamperForceN,6:0}/{state.RearRightSuspensionDamperForceN,6:0} " +
            $"{state.ClassicFrontYawAccelerationDegreesPerSecondSquared,6:0}/{state.ClassicRearYawAccelerationDegreesPerSecondSquared,6:0}");
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
