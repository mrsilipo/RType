using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSteeringEnvelopeMatrixProbe
{
    private const float Dt = 1f / 120f;
    private const int Ticks = 180;
    private const int Gear = 4;

    private static readonly float[] SpeedsKmh = [100f, 150f, 200f];
    private static readonly float[] Commands = [0.25f, 0.50f, 0.75f, 1.00f];

    private static readonly SteeringVariant[] Variants =
    [
        new("current", 1.3225f, 1.61f, 0.092f, 0.345f, 40f, 95f, 0.368f, 0.42f),
        new("later-envelope", 1.3225f, 1.61f, 0.092f, 0.345f, 55f, 150f, 0.368f, 0.42f),
        new("more-authority", 1.55f, 1.90f, 0.12f, 0.43f, 40f, 115f, 0.42f, 0.42f),
        new("hybrid-raceable", 1.48f, 1.86f, 0.12f, 0.43f, 55f, 150f, 0.42f, 0.42f)
    ];

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
        SimulationEngineParameters baseEngine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic steering envelope matrix probe: {parameters.DisplayName}, model={baseEngine.HandlingModel}");
        Console.WriteLine("  steering-only variants; cleanup=off, throttle=0.25, gear=4, duration=1.5s");
        Console.WriteLine("  score penalizes dead steering, delayed yaw/lateral response, rear saturation, large beta, and excess speed loss.");
        Console.WriteLine("  variant           nG/oG slipN/O blend trans score dead neg<=75 rearSat maxBeta maxG avgLoss 100@75             150@75             200@75");

        foreach (SteeringVariant variant in Variants)
        {
            SimulationEngineParameters engine = CloneWithSteering(baseEngine, variant);
            List<CaseResult> results = [];
            foreach (float speedKmh in SpeedsKmh)
            {
                foreach (float command in Commands)
                {
                    results.Add(RunCase(parameters, engine, speedKmh, command));
                }
            }

            float score = Score(results);
            int negativeUseful = results.Count(r => r.Command <= 0.75f && r.FrontSlipDegrees < -0.25f);
            int dead = results.Count(r => r.Command >= 0.50f && r.MaxActualLateralG < 0.35f);
            int rearSat = results.Count(r => r.RearGripUsage >= 0.98f);
            float maxBeta = results.Max(r => MathF.Abs(r.BetaDegrees));
            float maxG = results.Max(r => r.MaxActualLateralG);
            float avgLoss = results.Average(r => r.SpeedLossKmh);
            string samples = string.Join(" ",
                results
                    .Where(r => MathF.Abs(r.Command - 0.75f) < 0.01f)
                    .OrderBy(r => r.SpeedKmh)
                    .Select(FormatCompact));

            Console.WriteLine(
                $"  {variant.Label,-16} {variant.NormalG:F2}/{variant.OverdriveG:F2} " +
                $"{variant.NormalSlipFraction:F2}/{variant.OverdriveSlipFraction:F2} " +
                $"{variant.BlendStartKmh:F0}-{variant.BlendFullKmh:F0} " +
                $"{variant.TransientSlipFraction:F2}/{variant.TransientSeconds:F2} " +
                $"{score,5:F1} {dead,4} {negativeUseful,7} {rearSat,7} " +
                $"{maxBeta,7:F2} {maxG,4:F2} {avgLoss,7:F2} {samples}");
        }

        Console.WriteLine();
        Console.WriteLine("  150 km/h decomposition: cmd angle(base+slip+transient=actual) impliedG actualG/maxG slipF/R beta rearGrip loss");
        foreach (SteeringVariant variant in Variants)
        {
            SimulationEngineParameters engine = CloneWithSteering(baseEngine, variant);
            Console.WriteLine($"  {variant.Label}:");
            foreach (float command in Commands)
            {
                CaseResult result = RunCase(parameters, engine, 150f, command);
                Console.WriteLine(
                    $"    {command:F2} {result.BaseEnvelopeDegrees:F2}+{result.SlipAllowanceDegrees:F2}+{result.TransientBoostDegrees:F2}={result.RoadWheelAngleDegrees:F2} " +
                    $"{result.ImpliedLateralG:F2}g {result.ActualLateralG:F2}/{result.MaxActualLateralG:F2}g " +
                    $"{result.FrontSlipDegrees:F2}/{result.RearSlipDegrees:F2} " +
                    $"beta={result.BetaDegrees:F2} rear={result.RearGripUsage:F2} loss={result.SpeedLossKmh:F2}");
            }
        }

        Console.WriteLine("Classic steering envelope matrix probe complete.");
    }

    private static CaseResult RunCase(
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
        float maxActualG = 0f;
        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, command), Dt);
            maxActualG = MathF.Max(maxActualG, MathF.Abs(simulator.State.LateralAcceleration) / 9.81f);
        }

        VehicleState state = simulator.State;
        float roadWheel = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float baseEnvelope = CalculateBaseEnvelopeAngleDegrees(parameters, engine, state.SpeedMetersPerSecond * 3.6f, command);
        return new CaseResult(
            speedKmh,
            command,
            roadWheel,
            baseEnvelope,
            MathF.Abs(roadWheel) - baseEnvelope - state.SteeringTransientBoostAngleDegrees,
            state.SteeringTransientBoostAngleDegrees,
            CalculateLateralG(parameters.WheelbaseMeters, state.SpeedMetersPerSecond * 3.6f, roadWheel),
            MathF.Abs(state.LateralAcceleration) / 9.81f,
            maxActualG,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            state.ClassicBodySlipAngleDegrees,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            startSpeed - state.SpeedMetersPerSecond * 3.6f);
    }

    private static float Score(IEnumerable<CaseResult> results)
    {
        float score = 0f;
        foreach (CaseResult result in results)
        {
            if (result.Command <= 0.75f && result.FrontSlipDegrees < -0.25f)
            {
                score += 12f + MathF.Abs(result.FrontSlipDegrees);
            }
            else if (result.Command <= 0.75f && result.FrontSlipDegrees < 0.15f)
            {
                score += 3f;
            }

            if (result.RearGripUsage >= 0.98f)
            {
                score += result.Command >= 1f ? 3f : 8f;
            }

            float usefulGTarget = result.SpeedKmh <= 100f ? 0.65f : result.SpeedKmh <= 150f ? 0.45f : 0.30f;
            if (result.Command >= 0.50f && result.MaxActualLateralG < usefulGTarget)
            {
                score += (usefulGTarget - result.MaxActualLateralG) * 30f;
            }

            if (result.Command >= 0.75f && MathF.Abs(result.YawRateDegreesPerSecond) < 4f)
            {
                score += 8f;
            }

            score += MathF.Max(0f, MathF.Abs(result.BetaDegrees) - 7f) * 0.8f;
            score += MathF.Max(0f, result.SpeedLossKmh - 10f) * 0.4f;
        }

        return score;
    }

    private static string FormatCompact(CaseResult result)
    {
        return $"{result.Command:F2}:{result.RoadWheelAngleDegrees:F2}/{result.MaxActualLateralG:F2}/{result.FrontSlipDegrees:F2}/{result.RearGripUsage:F2}/{result.SpeedLossKmh:F1}";
    }

    private static float CalculateBaseEnvelopeAngleDegrees(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float speedKmh,
        float command)
    {
        ClassicBicycleSteeringParameters steering = engine.ClassicFourWheel.Steering;
        float normalCommand = MathHelper.Clamp(steering.NormalCommand, 0.1f, 0.98f);
        float normalAngle = CalculateLateralGSteerAngleDegrees(parameters.WheelbaseMeters, speedKmh, steering.NormalLateralAccelerationG);
        float overdriveAngle = CalculateLateralGSteerAngleDegrees(parameters.WheelbaseMeters, speedKmh, steering.OverdriveLateralAccelerationG);
        float magnitude = MathF.Abs(command);
        return magnitude <= normalCommand
            ? normalAngle * (magnitude / normalCommand)
            : MathHelper.Lerp(normalAngle, overdriveAngle, SmoothStep01((magnitude - normalCommand) / (1f - normalCommand)));
    }

    private static float CalculateLateralGSteerAngleDegrees(float wheelbaseMeters, float speedKmh, float lateralG)
    {
        if (speedKmh <= 0.1f)
        {
            return 0f;
        }

        float speed = speedKmh / 3.6f;
        return MathHelper.ToDegrees(MathF.Atan(wheelbaseMeters * lateralG * 9.81f / MathF.Max(0.01f, speed * speed)));
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

    private static SimulationEngineParameters CloneWithSteering(SimulationEngineParameters source, SteeringVariant variant)
    {
        ClassicBicycleSteeringParameters steering = source.ClassicFourWheel.Steering;
        return new SimulationEngineParameters
        {
            HandlingModel = source.HandlingModel,
            Timing = source.Timing,
            VehicleSafety = source.VehicleSafety,
            StabilityAssist = source.StabilityAssist,
            DigitalThrottleAssist = source.DigitalThrottleAssist,
            DigitalBrakeAssist = source.DigitalBrakeAssist,
            BrakeThrottlePriority = source.BrakeThrottlePriority,
            SteeringAssist = source.SteeringAssist,
            TyreForce = source.TyreForce,
            RpmResponse = source.RpmResponse,
            ClassicBicycle = source.ClassicBicycle,
            ClassicFourWheel = new ClassicBicycleParameters
            {
                Steering = new ClassicBicycleSteeringParameters
                {
                    ZeroKmhAngleDegrees = steering.ZeroKmhAngleDegrees,
                    SixtyKmhAngleDegrees = steering.SixtyKmhAngleDegrees,
                    OneTwentyKmhAngleDegrees = steering.OneTwentyKmhAngleDegrees,
                    TwoHundredKmhAngleDegrees = steering.TwoHundredKmhAngleDegrees,
                    SteerSpeedDegreesPerSecond = steering.SteerSpeedDegreesPerSecond,
                    ReturnSpeedDegreesPerSecond = steering.ReturnSpeedDegreesPerSecond,
                    PhysicalEnvelopeBlendStartKmh = variant.BlendStartKmh,
                    PhysicalEnvelopeFullKmh = variant.BlendFullKmh,
                    NormalLateralAccelerationG = variant.NormalG,
                    OverdriveLateralAccelerationG = variant.OverdriveG,
                    NormalCommand = steering.NormalCommand,
                    MinimumHighSpeedAngleDegrees = steering.MinimumHighSpeedAngleDegrees,
                    NormalPeakSlipFraction = variant.NormalSlipFraction,
                    OverdrivePeakSlipFraction = variant.OverdriveSlipFraction,
                    TransientPeakSlipFraction = variant.TransientSlipFraction,
                    TransientBoostSeconds = variant.TransientSeconds,
                    DigitalInitialCommandRatePerSecond = steering.DigitalInitialCommandRatePerSecond,
                    DigitalSustainedCommandRatePerSecond = steering.DigitalSustainedCommandRatePerSecond,
                    DigitalRiseAccelerationSeconds = steering.DigitalRiseAccelerationSeconds,
                    DigitalReleaseCommandRatePerSecond = steering.DigitalReleaseCommandRatePerSecond,
                    DigitalCounterSteerRateMultiplier = steering.DigitalCounterSteerRateMultiplier
                },
                FrontTyres = source.ClassicFourWheel.FrontTyres,
                RearTyres = source.ClassicFourWheel.RearTyres,
                Yaw = source.ClassicFourWheel.Yaw,
                GripBudget = source.ClassicFourWheel.GripBudget,
                ChassisLoadTransfer = source.ClassicFourWheel.ChassisLoadTransfer,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance
            }
        };
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private readonly record struct SteeringVariant(
        string Label,
        float NormalG,
        float OverdriveG,
        float NormalSlipFraction,
        float OverdriveSlipFraction,
        float BlendStartKmh,
        float BlendFullKmh,
        float TransientSlipFraction,
        float TransientSeconds);

    private readonly record struct CaseResult(
        float SpeedKmh,
        float Command,
        float RoadWheelAngleDegrees,
        float BaseEnvelopeDegrees,
        float SlipAllowanceDegrees,
        float TransientBoostDegrees,
        float ImpliedLateralG,
        float ActualLateralG,
        float MaxActualLateralG,
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
