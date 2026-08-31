using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTyreRelaxationArchitectureProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;

    private static readonly float[] Commands = [0.60f, 0.80f, 1.00f];
    private static readonly RelaxationVariant[] Variants =
    [
        new("instant", 0f),
        new("short-016m", 0.16f),
        new("resolved", float.NaN),
        new("long-060m", 0.60f)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);

        Console.WriteLine($"Classic tyre relaxation architecture probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only variants: production uses resolved tyre lengths unless override is shown");
        Console.WriteLine(
            $"  resolved tyre relaxation: front={tyres.Front.RelaxationLengthMeters:F2}m rear={tyres.Rear.RelaxationLengthMeters:F2}m");
        Console.WriteLine("  turn-in cases: coast-turn, 150km/h, gear=4, first 0.5s; turn-normalized values");
        Console.WriteLine("  columns: yaw@0.25 beta@0.25 frontYawShare@0.25 rearYawShare@0.25 avgLagF/R@0.25 yaw@0.50 beta@0.50 rearYawShare@0.50 gripR@0.50");

        List<VariantResult> results = [];
        foreach (RelaxationVariant variant in Variants)
        {
            List<TurnCase> cases = [];
            foreach (float command in Commands)
            {
                cases.Add(RunTurnIn(parameters, engine, geometry, variant, command));
            }

            ReversalCase reversal = RunReversal(parameters, engine, geometry, variant);
            VariantResult result = new(variant, cases, reversal, Score(cases, reversal));
            results.Add(result);
            PrintVariant(result);
        }

        PrintRecommendation(results);
        Console.WriteLine("Classic tyre relaxation architecture probe complete.");
    }

    private static TurnCase RunTurnIn(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        RelaxationVariant variant,
        float command)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, variant);
        List<RelaxationSample> samples = [];
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        for (int i = 1; i <= SecondsToTicks(0.5f); i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, command, brakeAssistEnabled: true), Dt);
            RelaxationSample sample = BuildSample(i * Dt, command, simulator.State, geometry, previousBeta, previousYaw);
            samples.Add(sample);
            previousBeta = sample.RawBetaDegrees;
            previousYaw = sample.RawYawRateDegreesPerSecond;
        }

        return new TurnCase(
            command,
            Nearest(samples, 0.10f),
            Nearest(samples, 0.25f),
            Nearest(samples, 0.50f),
            samples.Max(s => MathF.Abs(s.FrontLateralLagN)),
            samples.Max(s => MathF.Abs(s.RearLateralLagN)));
    }

    private static ReversalCase RunReversal(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        RelaxationVariant variant)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, variant);
        List<RelaxationSample> samples = [];
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);

        for (int i = 1; i <= SecondsToTicks(0.8f); i++)
        {
            float command = i * Dt < 0.35f ? 0.80f : -0.80f;
            simulator.Update(new VehicleInput(0f, 0f, command, brakeAssistEnabled: true), Dt);
            RelaxationSample sample = BuildSample(i * Dt, command, simulator.State, geometry, previousBeta, previousYaw);
            samples.Add(sample);
            previousBeta = sample.RawBetaDegrees;
            previousYaw = sample.RawYawRateDegreesPerSecond;
        }

        RelaxationSample switchSample = Nearest(samples, 0.35f);
        RelaxationSample afterSwitch = Nearest(samples, 0.45f);
        RelaxationSample end = Nearest(samples, 0.80f);
        float peakUnwindLag = samples
            .Where(s => s.TimeSeconds >= 0.35f)
            .Select(s => MathF.Abs(s.FrontLateralLagN) + MathF.Abs(s.RearLateralLagN))
            .DefaultIfEmpty(0f)
            .Max();

        return new ReversalCase(switchSample, afterSwitch, end, peakUnwindLag);
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        RelaxationVariant variant)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            TyreRelaxationLengthOverrideForProbe = variant.OverrideLengthMeters
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, StartSpeedKmh / 3.6f);
        return simulator;
    }

    private static RelaxationSample BuildSample(
        float time,
        float command,
        VehicleState state,
        VehicleAxleGeometry geometry,
        float previousBetaDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float turnMultiplier = -MathF.Sign(roadAngle == 0f ? command : roadAngle);
        if (turnMultiplier == 0f)
        {
            turnMultiplier = -1f;
        }

        float rawYaw = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float frontLat = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLat = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float totalYawAbs = MathF.Abs(frontMoment) + MathF.Abs(rearMoment);

        return new RelaxationSample(
            time,
            command,
            state.SpeedMetersPerSecond * 3.6f,
            roadAngle,
            state.ClassicBodySlipAngleDegrees,
            state.ClassicBodySlipAngleDegrees * turnMultiplier,
            (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnMultiplier,
            rawYaw,
            rawYaw * turnMultiplier,
            (rawYaw - previousYawRateDegreesPerSecond) / Dt * turnMultiplier,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f * turnMultiplier,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f * turnMultiplier,
            frontLat * turnMultiplier,
            rearLat * turnMultiplier,
            frontMoment * turnMultiplier,
            rearMoment * turnMultiplier,
            totalYawAbs > 1f ? MathF.Abs(frontMoment) / totalYawAbs : 0f,
            totalYawAbs > 1f ? MathF.Abs(rearMoment) / totalYawAbs : 0f,
            (state.FrontLeftRequestedLateralForceN + state.FrontRightRequestedLateralForceN) * turnMultiplier,
            (state.FrontLeftRelaxedLateralForceN + state.FrontRightRelaxedLateralForceN) * turnMultiplier,
            (state.RearLeftRequestedLateralForceN + state.RearRightRequestedLateralForceN) * turnMultiplier,
            (state.RearLeftRelaxedLateralForceN + state.RearRightRelaxedLateralForceN) * turnMultiplier,
            (state.FrontLeftLateralRelaxationDeltaN + state.FrontRightLateralRelaxationDeltaN) * turnMultiplier,
            (state.RearLeftLateralRelaxationDeltaN + state.RearRightLateralRelaxationDeltaN) * turnMultiplier,
            (state.FrontLeftLateralRelaxationTimeSeconds + state.FrontRightLateralRelaxationTimeSeconds) * 0.5f,
            (state.RearLeftLateralRelaxationTimeSeconds + state.RearRightLateralRelaxationTimeSeconds) * 0.5f,
            (state.FrontLeftLateralRelaxationLengthMeters + state.FrontRightLateralRelaxationLengthMeters) * 0.5f,
            (state.RearLeftLateralRelaxationLengthMeters + state.RearRightLateralRelaxationLengthMeters) * 0.5f,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));
    }

    private static void PrintVariant(VariantResult result)
    {
        Console.WriteLine();
        Console.WriteLine($"  variant {result.Variant.Label} override={FormatOverride(result.Variant.OverrideLengthMeters)} score={result.Score:F1}");
        foreach (TurnCase c in result.TurnCases)
        {
            Console.WriteLine(
                $"    cmd={c.Command:F2} " +
                $"0.25 yaw={c.At025.NormalizedYawRateDegreesPerSecond,5:F1} beta={c.At025.NormalizedBetaDegrees,6:F2} " +
                $"yawShareF/R={c.At025.FrontYawShare:P0}/{c.At025.RearYawShare:P0} lagF/R={c.At025.FrontLateralLagN,7:F0}/{c.At025.RearLateralLagN,7:F0}N " +
                $"0.50 yaw={c.At050.NormalizedYawRateDegreesPerSecond,5:F1} beta={c.At050.NormalizedBetaDegrees,6:F2} " +
                $"yawShareF/R={c.At050.FrontYawShare:P0}/{c.At050.RearYawShare:P0} gripR={c.At050.RearGripUsage:F2}");
        }

        ReversalCase r = result.Reversal;
        Console.WriteLine(
            $"    reversal: switchLag={r.SwitchSample.FrontLateralLagN + r.SwitchSample.RearLateralLagN:F0}N " +
            $"0.45 yaw={r.AfterSwitch.NormalizedYawRateDegreesPerSecond:F1} beta={r.AfterSwitch.NormalizedBetaDegrees:F2} " +
            $"end yaw={r.End.NormalizedYawRateDegreesPerSecond:F1} beta={r.End.NormalizedBetaDegrees:F2} " +
            $"peakUnwindLag={r.PeakUnwindLagN:F0}N");
    }

    private static void PrintRecommendation(IReadOnlyList<VariantResult> results)
    {
        VariantResult best = results
            .Where(r => r.Variant.Label != "instant")
            .OrderBy(r => r.Score)
            .First();
        Console.WriteLine();
        Console.WriteLine("  interpretation:");
        Console.WriteLine($"    best diagnostic candidate by balance/numbness guard: {best.Variant.Label}");
        Console.WriteLine("    use telemetry as direction only; road feel decides whether it is tyre memory or steering lag.");
    }

    private static float Score(IEnumerable<TurnCase> turnCases, ReversalCase reversal)
    {
        float score = 0f;
        foreach (TurnCase c in turnCases)
        {
            score += MathF.Max(0f, c.At025.FrontYawShare - 0.98f) * 4f;
            score += MathF.Max(0f, c.At025.RearYawShare - 0.90f) * 10f;
            score += MathF.Max(0f, MathF.Abs(c.At050.NormalizedBetaDegrees) - 5.5f) * 1.2f;
            score += MathF.Max(0f, c.At050.RearGripUsage - 0.65f) * 8f;
            score += MathF.Max(0f, 6f - c.At050.NormalizedYawRateDegreesPerSecond) * 0.6f;
        }

        score += MathF.Max(0f, 1200f - reversal.PeakUnwindLagN) / 500f;
        return score;
    }

    private static RelaxationSample Nearest(IReadOnlyList<RelaxationSample> samples, float time)
    {
        return samples.OrderBy(s => MathF.Abs(s.TimeSeconds - time)).First();
    }

    private static string FormatOverride(float value)
    {
        return float.IsFinite(value) ? $"{value:F2}m" : "resolved";
    }

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct RelaxationVariant(string Label, float OverrideLengthMeters);

    private readonly record struct VariantResult(
        RelaxationVariant Variant,
        IReadOnlyList<TurnCase> TurnCases,
        ReversalCase Reversal,
        float Score);

    private readonly record struct TurnCase(
        float Command,
        RelaxationSample At010,
        RelaxationSample At025,
        RelaxationSample At050,
        float PeakFrontLagN,
        float PeakRearLagN);

    private readonly record struct ReversalCase(
        RelaxationSample SwitchSample,
        RelaxationSample AfterSwitch,
        RelaxationSample End,
        float PeakUnwindLagN);

    private readonly record struct RelaxationSample(
        float TimeSeconds,
        float Command,
        float SpeedKmh,
        float RoadWheelAngleDegrees,
        float RawBetaDegrees,
        float NormalizedBetaDegrees,
        float BetaDotDegreesPerSecond,
        float RawYawRateDegreesPerSecond,
        float NormalizedYawRateDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float NormalizedFrontSlipDegrees,
        float NormalizedRearSlipDegrees,
        float NormalizedFrontLateralForceN,
        float NormalizedRearLateralForceN,
        float NormalizedFrontYawMomentNm,
        float NormalizedRearYawMomentNm,
        float FrontYawShare,
        float RearYawShare,
        float FrontTargetLateralForceN,
        float FrontRelaxedLateralForceN,
        float RearTargetLateralForceN,
        float RearRelaxedLateralForceN,
        float FrontLateralLagN,
        float RearLateralLagN,
        float FrontRelaxationTimeSeconds,
        float RearRelaxationTimeSeconds,
        float FrontRelaxationLengthMeters,
        float RearRelaxationLengthMeters,
        float FrontGripUsage,
        float RearGripUsage);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
