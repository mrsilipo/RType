using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBrakeTurnYawBetaProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float SetupSeconds = 0.50f;
    private const float RunSeconds = 0.50f;
    private const float SteerCommand = 1.0f;

    private static readonly float[] CheckpointsSeconds = [0.05f, 0.10f, 0.25f, 0.50f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic brake-turn yaw/beta probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production steering, brake, tyre, yaw, cleanup, and load-transfer values unchanged");
        Console.WriteLine("  sequence: 150km/h, gear=4, case-specific 0.5s setup, then steer=1 for first 0.5s sampled");

        ProbeCase coast = RunCase(parameters, engine, geometry, "coast-steer", setupBrake: 0f, _ => 0f);
        ProbeCase brake = RunCase(parameters, engine, geometry, "brake-steer", setupBrake: 1f, _ => 1f);
        ProbeCase trail = RunCase(parameters, engine, geometry, "trail-release", setupBrake: 1f, elapsed =>
            MathHelper.Lerp(1f, 0.25f, MathHelper.Clamp(elapsed / RunSeconds, 0f, 1f)));

        PrintCase(coast);
        PrintCase(brake);
        PrintCase(trail);
        PrintDivergence(coast, brake);
        PrintDivergence(coast, trail);
        PrintClassification(coast, brake, trail);

        Console.WriteLine("Classic brake-turn yaw/beta probe complete.");
    }

    private static ProbeCase RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        string label,
        float setupBrake,
        Func<float, float> brakeForTime)
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
        for (int i = 0; i < SecondsToTicks(SetupSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, setupBrake, 0f, brakeAssistEnabled: true), Dt);
        }

        List<YawBetaSample> allSamples = [];
        List<YawBetaSample> checkpoints = [];
        int checkpointIndex = 0;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYawRate = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);

        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            float elapsed = i * Dt;
            float brake = MathHelper.Clamp(brakeForTime(elapsed), 0f, 1f);
            simulator.Update(new VehicleInput(0f, brake, SteerCommand, brakeAssistEnabled: true), Dt);
            YawBetaSample sample = BuildSample(elapsed, simulator.State, parameters, geometry, previousBeta, previousYawRate);
            allSamples.Add(sample);

            if (checkpointIndex < CheckpointsSeconds.Length &&
                elapsed + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                checkpoints.Add(sample with { TimeSeconds = CheckpointsSeconds[checkpointIndex] });
                checkpointIndex++;
            }

            previousBeta = sample.BetaDegrees;
            previousYawRate = sample.YawRateDegreesPerSecond;
        }

        return new ProbeCase(label, allSamples, checkpoints);
    }

    private static YawBetaSample BuildSample(
        float elapsed,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float previousBetaDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontBrake = state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN;
        float rearBrake = state.RearLeftLongitudinalForceN + state.RearRightLongitudinalForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float betaDot = (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt;
        float measuredYawAcceleration = (MathHelper.ToDegrees(state.YawRateRadiansPerSecond) - previousYawRateDegreesPerSecond) / Dt;
        float calculatedYawAcceleration =
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared;

        return new YawBetaSample(
            elapsed,
            state.Brake,
            state.SpeedMetersPerSecond * 3.6f,
            (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f,
            state.ClassicBodySlipAngleDegrees,
            betaDot,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            measuredYawAcceleration,
            calculatedYawAcceleration,
            state.ClassicFrontYawAccelerationDegreesPerSecondSquared,
            state.ClassicRearYawAccelerationDegreesPerSecondSquared,
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared,
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared,
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared,
            frontMoment,
            rearMoment,
            frontMoment + rearMoment,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            frontLateral,
            rearLateral,
            state.ClassicDynamicFrontAxleLoadN,
            state.ClassicDynamicRearAxleLoadN,
            frontBrake,
            rearBrake,
            state.LongitudinalAcceleration / 9.81f,
            state.LateralAcceleration / 9.81f,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));
    }

    private static void PrintCase(ProbeCase probeCase)
    {
        Console.WriteLine();
        Console.WriteLine($"  case: {probeCase.Label}");
        Console.WriteLine("    t brake speed angle beta betaDot yaw yawAcc(m/c) yawAccF/R/N/D/Rec/Follow momentF/R/net slipF/R latF/R loadF/R brakeF/R longG latG gripF/R");
        foreach (YawBetaSample sample in probeCase.Checkpoints)
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,4:F2} {sample.Brake,5:F2} {sample.SpeedKmh,6:F1} {sample.SteerAngleDegrees,5:F2} " +
                $"{sample.BetaDegrees,5:F2} {sample.BetaDotDegreesPerSecond,7:F1} {sample.YawRateDegreesPerSecond,6:F1} " +
                $"{sample.MeasuredYawAccelerationDegreesPerSecondSquared,7:F0}/{sample.CalculatedYawAccelerationDegreesPerSecondSquared,7:F0} " +
                $"{sample.FrontYawAccelerationDegreesPerSecondSquared,6:F0}/{sample.RearYawAccelerationDegreesPerSecondSquared,6:F0}/" +
                $"{sample.NaturalYawAccelerationDegreesPerSecondSquared,6:F0}/{sample.YawDampingAccelerationDegreesPerSecondSquared,6:F0}/" +
                $"{sample.YawRecoveryAccelerationDegreesPerSecondSquared,6:F0}/{sample.RearFollowAccelerationDegreesPerSecondSquared,6:F0} " +
                $"{sample.FrontYawMomentNm,7:F0}/{sample.RearYawMomentNm,7:F0}/{sample.NetYawMomentNm,7:F0} " +
                $"{sample.FrontSlipDegrees,6:F2}/{sample.RearSlipDegrees,6:F2} " +
                $"{sample.FrontLateralForceN,7:F0}/{sample.RearLateralForceN,7:F0} " +
                $"{sample.FrontLoadN,6:F0}/{sample.RearLoadN,6:F0} " +
                $"{sample.FrontBrakeForceN,7:F0}/{sample.RearBrakeForceN,7:F0} " +
                $"{sample.LongitudinalG,5:F2} {sample.LateralG,5:F2} {sample.FrontGripUsage,5:F2}/{sample.RearGripUsage,5:F2}");
        }
    }

    private static void PrintDivergence(ProbeCase coast, ProbeCase other)
    {
        Divergence? divergence = FindFirstDivergence(coast.AllSamples, other.AllSamples);
        Console.WriteLine();
        Console.WriteLine($"  divergence coast-steer -> {other.Label}:");
        if (divergence is null)
        {
            Console.WriteLine("    no material divergence found in first 0.5s");
            return;
        }

        Divergence d = divergence.Value;
        Console.WriteLine(
            $"    first material divergence at t={d.TimeSeconds:F3}s: {d.Term} changed by {d.Delta:F1} {d.Units}");
        Console.WriteLine(
            $"    coast beta/yaw/yawAcc/netMoment={d.Coast.BetaDegrees:F2}deg/{d.Coast.YawRateDegreesPerSecond:F1}deg/s/" +
            $"{d.Coast.CalculatedYawAccelerationDegreesPerSecondSquared:F0}deg/s2/{d.Coast.NetYawMomentNm:F0}Nm");
        Console.WriteLine(
            $"    other beta/yaw/yawAcc/netMoment={d.Other.BetaDegrees:F2}deg/{d.Other.YawRateDegreesPerSecond:F1}deg/s/" +
            $"{d.Other.CalculatedYawAccelerationDegreesPerSecondSquared:F0}deg/s2/{d.Other.NetYawMomentNm:F0}Nm");

        Divergence? dynamics = FindFirstDivergence(coast.AllSamples, other.AllSamples, skipBrakeAndLoad: true);
        if (dynamics is not null)
        {
            Divergence dy = dynamics.Value;
            Console.WriteLine(
                $"    first dynamics divergence at t={dy.TimeSeconds:F3}s: {dy.Term} changed by {dy.Delta:F1} {dy.Units}");
        }
    }

    private static Divergence? FindFirstDivergence(
        IReadOnlyList<YawBetaSample> coast,
        IReadOnlyList<YawBetaSample> other,
        bool skipBrakeAndLoad = false)
    {
        int count = Math.Min(coast.Count, other.Count);
        for (int i = 0; i < count; i++)
        {
            YawBetaSample a = coast[i];
            YawBetaSample b = other[i];
            (string Term, float Delta, string Units)[] terms =
            [
                ("front brake force", b.FrontBrakeForceN - a.FrontBrakeForceN, "N"),
                ("rear brake force", b.RearBrakeForceN - a.RearBrakeForceN, "N"),
                ("front load", b.FrontLoadN - a.FrontLoadN, "N"),
                ("rear load", b.RearLoadN - a.RearLoadN, "N"),
                ("front yaw moment", b.FrontYawMomentNm - a.FrontYawMomentNm, "Nm"),
                ("rear yaw moment", b.RearYawMomentNm - a.RearYawMomentNm, "Nm"),
                ("net yaw moment", b.NetYawMomentNm - a.NetYawMomentNm, "Nm"),
                ("betaDot", b.BetaDotDegreesPerSecond - a.BetaDotDegreesPerSecond, "deg/s"),
                ("yaw acceleration", b.CalculatedYawAccelerationDegreesPerSecondSquared - a.CalculatedYawAccelerationDegreesPerSecondSquared, "deg/s2")
            ];

            (string Term, float Delta, string Units)? first = null;
            foreach ((string Term, float Delta, string Units) term in terms
                .Where(term => !skipBrakeAndLoad ||
                    term.Term is not ("front brake force" or "rear brake force" or "front load" or "rear load"))
                .Where(term => IsMaterial(term.Term, term.Delta))
                .OrderByDescending(term => NormalizedMagnitude(term.Term, term.Delta)))
            {
                first = term;
                break;
            }

            if (first.HasValue)
            {
                return new Divergence(a.TimeSeconds, first.Value.Term, first.Value.Delta, first.Value.Units, a, b);
            }
        }

        return null;
    }

    private static bool IsMaterial(string term, float delta)
    {
        float abs = MathF.Abs(delta);
        return term switch
        {
            "front brake force" or "rear brake force" => abs >= 250f,
            "front load" or "rear load" => abs >= 120f,
            "front yaw moment" or "rear yaw moment" or "net yaw moment" => abs >= 400f,
            "betaDot" => abs >= 8f,
            "yaw acceleration" => abs >= 35f,
            _ => false
        };
    }

    private static float NormalizedMagnitude(string term, float delta)
    {
        float scale = term switch
        {
            "front brake force" or "rear brake force" => 1000f,
            "front load" or "rear load" => 500f,
            "front yaw moment" or "rear yaw moment" or "net yaw moment" => 1000f,
            "betaDot" => 20f,
            "yaw acceleration" => 100f,
            _ => 1f
        };
        return MathF.Abs(delta) / scale;
    }

    private static void PrintClassification(ProbeCase coast, ProbeCase brake, ProbeCase trail)
    {
        YawBetaSample c = coast.Checkpoints[^1];
        YawBetaSample b = brake.Checkpoints[^1];
        YawBetaSample t = trail.Checkpoints[^1];
        Console.WriteLine();
        Console.WriteLine("  classification:");
        Console.WriteLine(
            $"    at 0.50s coast/brake/trail beta={c.BetaDegrees:F2}/{b.BetaDegrees:F2}/{t.BetaDegrees:F2}deg, " +
            $"yaw={c.YawRateDegreesPerSecond:F1}/{b.YawRateDegreesPerSecond:F1}/{t.YawRateDegreesPerSecond:F1}deg/s, " +
            $"frontSlip={c.FrontSlipDegrees:F2}/{b.FrontSlipDegrees:F2}/{t.FrontSlipDegrees:F2}deg");
        Console.WriteLine(
            $"    rear yaw moment at 0.50s coast/brake/trail={c.RearYawMomentNm:F0}/{b.RearYawMomentNm:F0}/{t.RearYawMomentNm:F0}Nm; " +
            $"front yaw moment={c.FrontYawMomentNm:F0}/{b.FrontYawMomentNm:F0}/{t.FrontYawMomentNm:F0}Nm");

        if (MathF.Abs(b.RearYawMomentNm - c.RearYawMomentNm) > MathF.Abs(b.FrontYawMomentNm - c.FrontYawMomentNm) * 1.25f &&
            MathF.Abs(b.RearYawMomentNm) > MathF.Abs(c.RearYawMomentNm) * 1.35f)
        {
            Console.WriteLine("    result: braking changes rear yaw contribution more than front contribution; rear axle yaw balance is the first strong suspect.");
            return;
        }

        if (MathF.Abs(b.FrontYawMomentNm) < MathF.Abs(c.FrontYawMomentNm) * 0.65f)
        {
            Console.WriteLine("    result: braking materially weakens front leading yaw contribution; front axle participation is the first strong suspect.");
            return;
        }

        if (MathF.Abs(b.BetaDotDegreesPerSecond) > MathF.Abs(c.BetaDotDegreesPerSecond) * 1.35f &&
            MathF.Abs(b.NetYawMomentNm - c.NetYawMomentNm) < 750f)
        {
            Console.WriteLine("    result: beta growth diverges without a matching moment change; body/path evolution is the first strong suspect.");
            return;
        }

        Console.WriteLine("    result: braking changes multiple yaw/beta terms together; use the divergence rows above to choose the smallest next audit.");
    }

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct ProbeCase(
        string Label,
        IReadOnlyList<YawBetaSample> AllSamples,
        IReadOnlyList<YawBetaSample> Checkpoints);

    private readonly record struct Divergence(
        float TimeSeconds,
        string Term,
        float Delta,
        string Units,
        YawBetaSample Coast,
        YawBetaSample Other);

    private readonly record struct YawBetaSample(
        float TimeSeconds,
        float Brake,
        float SpeedKmh,
        float SteerAngleDegrees,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float CalculatedYawAccelerationDegreesPerSecondSquared,
        float FrontYawAccelerationDegreesPerSecondSquared,
        float RearYawAccelerationDegreesPerSecondSquared,
        float NaturalYawAccelerationDegreesPerSecondSquared,
        float YawDampingAccelerationDegreesPerSecondSquared,
        float YawRecoveryAccelerationDegreesPerSecondSquared,
        float RearFollowAccelerationDegreesPerSecondSquared,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontLoadN,
        float RearLoadN,
        float FrontBrakeForceN,
        float RearBrakeForceN,
        float LongitudinalG,
        float LateralG,
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
