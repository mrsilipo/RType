using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBaseTurnInEquilibriumProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float RunSeconds = 1.0f;
    private const float SteerCommand = 1.0f;

    private static readonly float[] CheckpointsSeconds = [0.05f, 0.10f, 0.25f, 0.50f, 1.00f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic base turn-in equilibrium probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production steering, brake, tyre, yaw, cleanup, and load-transfer values unchanged");
        Console.WriteLine("  sequence: 150km/h, gear=4, steer=1 from t0, first 1.0s sampled");
        Console.WriteLine("  healthy target: positive front/rear slip, beta settles, yaw settles, rear remains below saturation");

        ProbeCase coast = RunCase(parameters, engine, geometry, "coast-steer", throttle: 0f, brake: 0f);
        ProbeCase throttle = RunCase(parameters, engine, geometry, "throttle25-steer", throttle: 0.25f, brake: 0f);
        ProbeCase brake = RunCase(parameters, engine, geometry, "brake-steer", throttle: 0f, brake: 1f);

        PrintCase(coast);
        PrintCase(throttle);
        PrintCase(brake);
        PrintComparison(coast, throttle, brake);

        Console.WriteLine("Classic base turn-in equilibrium probe complete.");
    }

    private static ProbeCase RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        string label,
        float throttle,
        float brake)
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

        List<TurnInSample> allSamples = [];
        List<TurnInSample> checkpoints = [];
        TurnInSample? firstFrontSlipZero = null;
        TurnInSample? firstRearSlipZero = null;
        TurnInSample? firstRearSaturation = null;
        TurnInSample? firstFrontForceFall = null;
        TurnInSample? firstHealthySettle = null;
        int checkpointIndex = 0;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float previousFrontSlip = 0f;
        float previousRearSlip = 0f;
        float peakFrontForce = 0f;
        float? peakFrontForceTime = null;

        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            simulator.Update(new VehicleInput(throttle, brake, SteerCommand, brakeAssistEnabled: true), Dt);
            TurnInSample sample = BuildSample(i * Dt, simulator.State, parameters, geometry, previousBeta, previousYaw);
            allSamples.Add(sample);

            if (firstFrontSlipZero is null &&
                i > 1 &&
                MathF.Sign(previousFrontSlip) != 0f &&
                MathF.Sign(sample.FrontSlipDegrees) != 0f &&
                MathF.Sign(previousFrontSlip) != MathF.Sign(sample.FrontSlipDegrees))
            {
                firstFrontSlipZero = sample;
            }

            if (firstRearSlipZero is null &&
                i > 1 &&
                MathF.Sign(previousRearSlip) != 0f &&
                MathF.Sign(sample.RearSlipDegrees) != 0f &&
                MathF.Sign(previousRearSlip) != MathF.Sign(sample.RearSlipDegrees))
            {
                firstRearSlipZero = sample;
            }

            if (firstRearSaturation is null && sample.RearGripUsage >= 0.98f)
            {
                firstRearSaturation = sample;
            }

            float frontAbsForce = MathF.Abs(sample.FrontLateralForceN);
            if (frontAbsForce > peakFrontForce)
            {
                peakFrontForce = frontAbsForce;
                peakFrontForceTime = sample.TimeSeconds;
            }
            else if (firstFrontForceFall is null &&
                peakFrontForceTime.HasValue &&
                sample.TimeSeconds > peakFrontForceTime.Value &&
                frontAbsForce < peakFrontForce * 0.70f &&
                sample.TimeSeconds <= 0.60f)
            {
                firstFrontForceFall = sample;
            }

            if (firstHealthySettle is null &&
                sample.TimeSeconds >= 0.30f &&
                sample.FrontSlipDegrees > 0.5f &&
                sample.RearSlipDegrees > 0.25f &&
                sample.RearGripUsage < 0.90f &&
                MathF.Abs(sample.BetaDotDegreesPerSecond) <= 2f &&
                MathF.Abs(sample.CalculatedYawAccelerationDegreesPerSecondSquared) <= 15f)
            {
                firstHealthySettle = sample;
            }

            if (checkpointIndex < CheckpointsSeconds.Length &&
                sample.TimeSeconds + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                checkpoints.Add(sample with { TimeSeconds = CheckpointsSeconds[checkpointIndex] });
                checkpointIndex++;
            }

            previousBeta = sample.BetaDegrees;
            previousYaw = sample.YawRateDegreesPerSecond;
            previousFrontSlip = sample.FrontSlipDegrees;
            previousRearSlip = sample.RearSlipDegrees;
        }

        float speedDrop = StartSpeedKmh - allSamples[^1].SpeedKmh;
        return new ProbeCase(
            label,
            throttle,
            brake,
            speedDrop,
            allSamples,
            checkpoints,
            firstFrontSlipZero,
            firstRearSlipZero,
            firstRearSaturation,
            firstFrontForceFall,
            firstHealthySettle);
    }

    private static TurnInSample BuildSample(
        float time,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float previousBetaDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontLongitudinal = state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN;
        float rearLongitudinal = state.RearLeftLongitudinalForceN + state.RearRightLongitudinalForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float yawRate = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float betaDot = (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt;
        float measuredYawAcceleration = (yawRate - previousYawRateDegreesPerSecond) / Dt;
        float calculatedYawAcceleration =
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared;
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;

        return new TurnInSample(
            time,
            state.Throttle,
            state.Brake,
            state.SpeedMetersPerSecond * 3.6f,
            (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f,
            state.ClassicBodySlipAngleDegrees,
            betaDot,
            yawRate,
            measuredYawAcceleration,
            calculatedYawAcceleration,
            frontSlip,
            rearSlip,
            frontLateral,
            rearLateral,
            frontMoment,
            rearMoment,
            frontMoment + rearMoment,
            state.ClassicDynamicFrontAxleLoadN,
            state.ClassicDynamicRearAxleLoadN,
            frontLongitudinal,
            rearLongitudinal,
            state.LongitudinalAcceleration / 9.81f,
            state.LateralAcceleration / 9.81f,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));
    }

    private static void PrintCase(ProbeCase probeCase)
    {
        Console.WriteLine();
        Console.WriteLine($"  case: {probeCase.Label} throttle={probeCase.Throttle:F2} brake={probeCase.Brake:F2}");
        Console.WriteLine("    t speed angle beta betaDot yaw yawAcc(m/c) slipF/R latF/R yawM F/R/net loadF/R longF/R longG latG gripF/R");
        foreach (TurnInSample sample in probeCase.Checkpoints)
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,4:F2} {sample.SpeedKmh,6:F1} {sample.SteerAngleDegrees,5:F2} " +
                $"{sample.BetaDegrees,5:F2} {sample.BetaDotDegreesPerSecond,7:F1} {sample.YawRateDegreesPerSecond,6:F1} " +
                $"{sample.MeasuredYawAccelerationDegreesPerSecondSquared,7:F0}/{sample.CalculatedYawAccelerationDegreesPerSecondSquared,7:F0} " +
                $"{sample.FrontSlipDegrees,6:F2}/{sample.RearSlipDegrees,6:F2} " +
                $"{sample.FrontLateralForceN,7:F0}/{sample.RearLateralForceN,7:F0} " +
                $"{sample.FrontYawMomentNm,7:F0}/{sample.RearYawMomentNm,7:F0}/{sample.NetYawMomentNm,7:F0} " +
                $"{sample.FrontLoadN,6:F0}/{sample.RearLoadN,6:F0} " +
                $"{sample.FrontLongitudinalForceN,7:F0}/{sample.RearLongitudinalForceN,7:F0} " +
                $"{sample.LongitudinalG,5:F2} {sample.LateralG,5:F2} {sample.FrontGripUsage,5:F2}/{sample.RearGripUsage,5:F2}");
        }

        Console.WriteLine(
            $"    events: healthySettle={FormatEvent(probeCase.FirstHealthySettle)} " +
            $"frontForceFall={FormatEvent(probeCase.FirstFrontForceFall)} " +
            $"frontSlipZero={FormatEvent(probeCase.FirstFrontSlipZero)} " +
            $"rearSlipZero={FormatEvent(probeCase.FirstRearSlipZero)} " +
            $"rearSat={FormatEvent(probeCase.FirstRearSaturation)} speedDrop={probeCase.SpeedDropKmh:F1}km/h");
        Console.WriteLine($"    classification: {Classify(probeCase)}");
    }

    private static void PrintComparison(ProbeCase coast, ProbeCase throttle, ProbeCase brake)
    {
        Console.WriteLine();
        Console.WriteLine("  comparison:");
        Console.WriteLine(
            $"    front-slip zero coast/throttle/brake={FormatTime(coast.FirstFrontSlipZero)}/{FormatTime(throttle.FirstFrontSlipZero)}/{FormatTime(brake.FirstFrontSlipZero)}");
        Console.WriteLine(
            $"    front-force fall coast/throttle/brake={FormatTime(coast.FirstFrontForceFall)}/{FormatTime(throttle.FirstFrontForceFall)}/{FormatTime(brake.FirstFrontForceFall)}");
        Console.WriteLine(
            $"    rear-saturation coast/throttle/brake={FormatTime(coast.FirstRearSaturation)}/{FormatTime(throttle.FirstRearSaturation)}/{FormatTime(brake.FirstRearSaturation)}");

        TurnInSample c = coast.Checkpoints[^1];
        TurnInSample t = throttle.Checkpoints[^1];
        TurnInSample b = brake.Checkpoints[^1];
        Console.WriteLine(
            $"    at 1.00s beta coast/throttle/brake={c.BetaDegrees:F2}/{t.BetaDegrees:F2}/{b.BetaDegrees:F2}deg, " +
            $"yaw={c.YawRateDegreesPerSecond:F1}/{t.YawRateDegreesPerSecond:F1}/{b.YawRateDegreesPerSecond:F1}deg/s, " +
            $"slipF={c.FrontSlipDegrees:F2}/{t.FrontSlipDegrees:F2}/{b.FrontSlipDegrees:F2}deg");

        Console.WriteLine($"    root classification: {ClassifyRoot(coast, throttle, brake)}");
    }

    private static string Classify(ProbeCase probeCase)
    {
        if (probeCase.FirstHealthySettle is not null)
        {
            return "healthy settle appears before front-slip collapse";
        }

        if (probeCase.FirstFrontSlipZero is not null)
        {
            if (probeCase.FirstFrontForceFall is not null &&
                probeCase.FirstFrontForceFall.Value.TimeSeconds <= probeCase.FirstFrontSlipZero.Value.TimeSeconds)
            {
                return "front lateral force falls before/into front-slip zero crossing";
            }

            return "front slip crosses negative before the state reaches healthy settle";
        }

        if (probeCase.FirstRearSaturation is not null)
        {
            return "rear saturates before healthy settle";
        }

        TurnInSample end = probeCase.Checkpoints[^1];
        if (MathF.Abs(end.BetaDotDegreesPerSecond) > 2f ||
            MathF.Abs(end.CalculatedYawAccelerationDegreesPerSecondSquared) > 15f)
        {
            return "state does not settle in the first second";
        }

        return "near settle but misses positive-slip/rear-reserve target";
    }

    private static string ClassifyRoot(ProbeCase coast, ProbeCase throttle, ProbeCase brake)
    {
        if (coast.FirstFrontSlipZero is not null)
        {
            TurnInSample cEnd = coast.Checkpoints[^1];
            if (MathF.Abs(cEnd.RearYawMomentNm) > MathF.Abs(cEnd.FrontYawMomentNm) * 1.5f)
            {
                return "base coast-turn already becomes rear-yaw-dominant after front force reversal; fix base cornering before brake logic";
            }

            if (MathF.Abs(cEnd.BetaDotDegreesPerSecond) > 2f)
            {
                return "base coast-turn beta keeps growing through front-slip reversal; beta/yaw equilibrium is upstream";
            }

            return "base coast-turn front slip crosses negative; steering command/tyre transient cannot settle from current turn-in shape";
        }

        if (brake.FirstFrontSlipZero is not null && coast.FirstFrontSlipZero is null)
        {
            return "braking-specific front-slip collapse; return to brake/load/combined-grip path";
        }

        return "no primary collapse in these cases; broaden to longer window or lower steering command";
    }

    private static string FormatEvent(TurnInSample? sample)
    {
        return sample is null ? "none" : $"t{sample.Value.TimeSeconds:F3}s";
    }

    private static string FormatTime(TurnInSample? sample)
    {
        return sample is null ? "none" : $"{sample.Value.TimeSeconds:F3}s";
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
        float Throttle,
        float Brake,
        float SpeedDropKmh,
        IReadOnlyList<TurnInSample> AllSamples,
        IReadOnlyList<TurnInSample> Checkpoints,
        TurnInSample? FirstFrontSlipZero,
        TurnInSample? FirstRearSlipZero,
        TurnInSample? FirstRearSaturation,
        TurnInSample? FirstFrontForceFall,
        TurnInSample? FirstHealthySettle);

    private readonly record struct TurnInSample(
        float TimeSeconds,
        float Throttle,
        float Brake,
        float SpeedKmh,
        float SteerAngleDegrees,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float CalculatedYawAccelerationDegreesPerSecondSquared,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float FrontLoadN,
        float RearLoadN,
        float FrontLongitudinalForceN,
        float RearLongitudinalForceN,
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
