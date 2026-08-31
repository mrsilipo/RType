using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTurnRadiusBudgetProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const int Ticks = 180;

    private static readonly float[] SpeedsKmh = [80f, 100f, 120f, 150f, 180f, 200f];
    private static readonly float[] Commands = [0.25f, 0.50f, 0.75f, 1.00f];
    private static readonly float[] ReferenceRadiiMeters = [60f, 90f, 120f, 150f, 200f];

    private static readonly ClassicFourWheelAssistOptions CleanupOff = new()
    {
        BodySlipDampingEnabled = false,
        LateralVelocityDampingEnabled = false,
        RearFollowEnabled = false,
        YawRecoveryEnabled = false,
        SpeedRetentionEnabled = false
    };

    private static readonly Profile[] Profiles =
    [
        new("all-on", ClassicFourWheelAssistOptions.Default),
        new("no-yawRec", new ClassicFourWheelAssistOptions { YawRecoveryEnabled = false }),
        new("cleanup-off", CleanupOff)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic turn-radius budget probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production steering/tyres/yaw/brakes/load-transfer unchanged; compares assist profiles");
        Console.WriteLine("  reference lateral-g required by radius:");
        Console.Write("    speed ");
        foreach (float radius in ReferenceRadiiMeters)
        {
            Console.Write($"{radius,8:F0}m");
        }

        Console.WriteLine();
        foreach (float speedKmh in SpeedsKmh)
        {
            Console.Write($"    {speedKmh,5:F0}");
            foreach (float radius in ReferenceRadiiMeters)
            {
                Console.Write($"{RequiredLateralG(speedKmh, radius),9:F2}");
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("  actual current model response after 1.5s constant command:");
        Console.WriteLine("    profile     speed cmd angle yaw latG peakG yawRadius accelRadius slipF/R beta gripF/R speedDrop class");
        foreach (Profile profile in Profiles)
        {
            foreach (float speedKmh in SpeedsKmh)
            {
                foreach (float command in Commands)
                {
                    Result result = RunCase(parameters, engine, profile.AssistOptions, speedKmh, command);
                    Console.WriteLine(
                        $"    {profile.Label,-10} {speedKmh,5:F0} {command,4:F2} {result.RoadWheelAngleDegrees,5:F2} " +
                        $"{result.YawRateDegreesPerSecond,6:F1} {result.LateralG,5:F2} {result.PeakLateralG,5:F2} " +
                        $"{FormatRadius(result.YawRadiusMeters),9} {FormatRadius(result.AccelRadiusMeters),11} " +
                        $"{result.FrontSlipDegrees,6:F2}/{result.RearSlipDegrees,6:F2} " +
                        $"{result.BetaDegrees,6:F2} {result.FrontGripUsage,5:F2}/{result.RearGripUsage,5:F2} " +
                        $"{result.SpeedDropKmh,7:F2} {Classify(result)}");
                }
            }
        }

        Console.WriteLine("Classic turn-radius budget probe complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ClassicFourWheelAssistOptions assistOptions,
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
            AssistOptions = assistOptions
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);

        float startSpeed = simulator.State.SpeedMetersPerSecond * 3.6f;
        float peakLateralG = 0f;
        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, command), Dt);
            peakLateralG = MathF.Max(peakLateralG, MathF.Abs(simulator.State.LateralAcceleration) / 9.81f);
        }

        VehicleState state = simulator.State;
        float speed = state.SpeedMetersPerSecond;
        float yawRate = MathF.Abs(state.YawRateRadiansPerSecond);
        float lateralAcceleration = MathF.Abs(state.LateralAcceleration);
        float roadWheel = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float yawRadius = yawRate > 0.001f ? speed / yawRate : float.PositiveInfinity;
        float accelRadius = lateralAcceleration > 0.05f ? speed * speed / lateralAcceleration : float.PositiveInfinity;

        return new Result(
            speedKmh,
            command,
            roadWheel,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            state.LateralAcceleration / 9.81f,
            peakLateralG,
            yawRadius,
            accelRadius,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            state.ClassicBodySlipAngleDegrees,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            startSpeed - state.SpeedMetersPerSecond * 3.6f);
    }

    private static float RequiredLateralG(float speedKmh, float radiusMeters)
    {
        float speed = speedKmh / 3.6f;
        return speed * speed / MathF.Max(1f, radiusMeters) / 9.81f;
    }

    private static string FormatRadius(float radius)
    {
        return float.IsFinite(radius) ? $"{radius,7:F0}m" : "    inf";
    }

    private static string Classify(Result result)
    {
        if (result.PeakLateralG < 0.55f && result.Command >= 0.50f)
        {
            return "dead";
        }

        if (result.FrontGripUsage > 0.90f || result.RearGripUsage > 0.90f)
        {
            return "grip-limited";
        }

        if (MathF.Abs(result.BetaDegrees) > 8f)
        {
            return "beta-heavy";
        }

        return "has-reserve";
    }

    private readonly record struct Result(
        float StartSpeedKmh,
        float Command,
        float RoadWheelAngleDegrees,
        float YawRateDegreesPerSecond,
        float LateralG,
        float PeakLateralG,
        float YawRadiusMeters,
        float AccelRadiusMeters,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float BetaDegrees,
        float FrontGripUsage,
        float RearGripUsage,
        float SpeedDropKmh);

    private readonly record struct Profile(string Label, ClassicFourWheelAssistOptions AssistOptions);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
