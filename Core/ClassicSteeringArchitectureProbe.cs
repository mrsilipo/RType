using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSteeringArchitectureProbe
{
    private const float Dt = 1f / 120f;
    private const int Ticks = 180;
    private const int Gear = 4;

    private static readonly float[] SpeedsKmh = [60f, 100f, 150f, 200f];
    private static readonly float[] Commands = [0.25f, 0.50f, 0.75f, 1.00f];

    private static readonly ClassicFourWheelAssistOptions CleanupOff = new()
    {
        BodySlipDampingEnabled = false,
        LateralVelocityDampingEnabled = false,
        RearFollowEnabled = false,
        YawRecoveryEnabled = false,
        SpeedRetentionEnabled = false
    };

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic steering architecture probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  cleanup=off, throttle=0.25, gear=4; reports 1.5s constant-hold response after two-layer steering");
        Console.WriteLine("  speed cmd norm envelopeN/O angle impliedG slipF/R beta yaw rearGrip speedLoss");

        foreach (float speedKmh in SpeedsKmh)
        {
            foreach (float command in Commands)
            {
                Result result = RunCase(parameters, engine, speedKmh, command);
                Console.WriteLine(
                    $"  {speedKmh,5:F0} {command,4:F2} {result.NormalizedCommand,5:F2} " +
                    $"{result.NormalEnvelopeDegrees,5:F2}/{result.OverdriveEnvelopeDegrees,5:F2} " +
                    $"{result.RoadWheelAngleDegrees,5:F2} {result.ImpliedLateralG,7:F2} " +
                    $"{result.FrontSlipDegrees,6:F2}/{result.RearSlipDegrees,6:F2} " +
                    $"{result.BetaDegrees,6:F2} {result.YawRateDegreesPerSecond,6:F1} " +
                    $"{result.RearGripUsage,6:F2} {result.SpeedLossKmh,7:F2}");
            }
        }

        Console.WriteLine("Classic steering architecture probe complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float speedKmh,
        float command)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            AssistOptions = CleanupOff
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);

        float startSpeed = simulator.State.SpeedMetersPerSecond * 3.6f;
        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, command), Dt);
        }

        VehicleState state = simulator.State;
        float roadWheel = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        return new Result(
            state.SteeringNormalizedCommand,
            state.SteeringPhysicalNormalAngleDegrees,
            state.SteeringPhysicalOverdriveAngleDegrees,
            roadWheel,
            CalculateLateralG(parameters.WheelbaseMeters, state.SpeedMetersPerSecond * 3.6f, roadWheel),
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            state.ClassicBodySlipAngleDegrees,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            startSpeed - state.SpeedMetersPerSecond * 3.6f);
    }

    private static float CalculateLateralG(float wheelbaseMeters, float speedKmh, float roadWheelDegrees)
    {
        if (speedKmh <= 0.1f || MathF.Abs(roadWheelDegrees) <= 0.001f)
        {
            return 0f;
        }

        float speed = speedKmh / 3.6f;
        float radius = wheelbaseMeters / MathF.Max(0.0001f, MathF.Tan(MathF.Abs(MathHelper.ToRadians(roadWheelDegrees))));
        return speed * speed / MathF.Max(0.1f, radius) / 9.81f;
    }

    private readonly record struct Result(
        float NormalizedCommand,
        float NormalEnvelopeDegrees,
        float OverdriveEnvelopeDegrees,
        float RoadWheelAngleDegrees,
        float ImpliedLateralG,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float BetaDegrees,
        float YawRateDegreesPerSecond,
        float RearGripUsage,
        float SpeedLossKmh);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
