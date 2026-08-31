using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSteeringCommandEquilibriumSweep
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float RunSeconds = 3.0f;

    private static readonly float[] Commands = [0.20f, 0.40f, 0.60f, 0.80f, 1.00f];
    private static readonly float[] CheckpointsSeconds = [0.25f, 0.50f, 1.00f, 2.00f, 3.00f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic steering-command equilibrium sweep: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production tyres, steering, yaw, brake, cleanup, and load-transfer values unchanged");
        Console.WriteLine("  sequence: coast-turn, 150km/h, gear=4, fixed normalized steering commands, duration=3.0s");
        Console.WriteLine("  target: positive front/rear slip, rear below saturation, betaDot and yaw acceleration trend toward zero");

        List<ProbeCase> cases = [];
        foreach (float command in Commands)
        {
            ProbeCase probeCase = RunCase(parameters, engine, geometry, command);
            cases.Add(probeCase);
            PrintCase(probeCase);
        }

        PrintSummary(cases);
        Console.WriteLine("Classic steering-command equilibrium sweep complete.");
    }

    private static ProbeCase RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        float command)
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

        List<TurnSample> checkpoints = [];
        TurnSample? frontSlipZero = null;
        TurnSample? rearSlipZero = null;
        TurnSample? rearSaturation = null;
        TurnSample? firstHealthySettle = null;
        TurnSample? frontForceFall = null;
        TurnSample? commandAtFrontSlipZero = null;
        int checkpointIndex = 0;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float previousFrontSlip = 0f;
        float previousRearSlip = 0f;
        float peakFrontForce = 0f;
        float? peakFrontForceTime = null;
        float maxBeta = 0f;
        float maxRearGrip = 0f;
        float startSpeed = simulator.State.SpeedMetersPerSecond * 3.6f;
        TurnSample finalSample = default;

        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, command, brakeAssistEnabled: true), Dt);
            TurnSample sample = BuildSample(i * Dt, command, simulator.State, geometry, previousBeta, previousYaw);
            finalSample = sample;
            maxBeta = MathF.Max(maxBeta, MathF.Abs(sample.BetaDegrees));
            maxRearGrip = MathF.Max(maxRearGrip, sample.RearGripUsage);

            if (frontSlipZero is null &&
                i > 1 &&
                MathF.Sign(previousFrontSlip) != 0f &&
                MathF.Sign(sample.FrontSlipDegrees) != 0f &&
                MathF.Sign(previousFrontSlip) != MathF.Sign(sample.FrontSlipDegrees))
            {
                frontSlipZero = sample;
                commandAtFrontSlipZero = sample;
            }

            if (rearSlipZero is null &&
                i > 1 &&
                MathF.Sign(previousRearSlip) != 0f &&
                MathF.Sign(sample.RearSlipDegrees) != 0f &&
                MathF.Sign(previousRearSlip) != MathF.Sign(sample.RearSlipDegrees))
            {
                rearSlipZero = sample;
            }

            if (rearSaturation is null && sample.RearGripUsage >= 0.98f)
            {
                rearSaturation = sample;
            }

            float frontAbsForce = MathF.Abs(sample.FrontLateralForceN);
            if (frontAbsForce > peakFrontForce)
            {
                peakFrontForce = frontAbsForce;
                peakFrontForceTime = sample.TimeSeconds;
            }
            else if (frontForceFall is null &&
                peakFrontForceTime.HasValue &&
                sample.TimeSeconds > peakFrontForceTime.Value &&
                frontAbsForce < peakFrontForce * 0.70f)
            {
                frontForceFall = sample;
            }

            if (firstHealthySettle is null &&
                sample.TimeSeconds >= 0.75f &&
                sample.FrontSlipDegrees > 0.35f &&
                sample.RearSlipDegrees > 0.20f &&
                sample.RearGripUsage < 0.90f &&
                MathF.Abs(sample.BetaDotDegreesPerSecond) <= 2.5f &&
                MathF.Abs(sample.CalculatedYawAccelerationDegreesPerSecondSquared) <= 20f)
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

        return new ProbeCase(
            command,
            startSpeed - finalSample.SpeedKmh,
            maxBeta,
            maxRearGrip,
            checkpoints,
            frontSlipZero,
            rearSlipZero,
            rearSaturation,
            frontForceFall,
            firstHealthySettle,
            commandAtFrontSlipZero,
            finalSample);
    }

    private static TurnSample BuildSample(
        float time,
        float command,
        VehicleState state,
        VehicleAxleGeometry geometry,
        float previousBetaDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
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
        float actualAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float normal = state.SteeringPhysicalNormalAngleDegrees;
        float overdrive = state.SteeringPhysicalOverdriveAngleDegrees;
        float boost = state.SteeringTransientBoostAngleDegrees;
        float slipAllowance = MathF.Max(0f, MathF.Abs(actualAngle) - normal - boost);

        return new TurnSample(
            time,
            command,
            state.SteeringNormalizedCommand,
            actualAngle,
            normal,
            overdrive,
            slipAllowance,
            boost,
            state.SpeedMetersPerSecond * 3.6f,
            state.ClassicBodySlipAngleDegrees,
            betaDot,
            yawRate,
            measuredYawAcceleration,
            calculatedYawAcceleration,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            frontLateral,
            rearLateral,
            frontMoment,
            rearMoment,
            frontMoment + rearMoment,
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            state.LateralAcceleration / 9.81f);
    }

    private static void PrintCase(ProbeCase probeCase)
    {
        Console.WriteLine();
        Console.WriteLine($"  command {probeCase.Command:F2}");
        Console.WriteLine("    t speed cmd/norm angle n/o/slip/boost beta betaDot yaw yawAcc(m/c) slipF/R latF/R yawM F/R/net rearGrip latG");
        foreach (TurnSample sample in probeCase.Checkpoints)
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,4:F2} {sample.SpeedKmh,6:F1} {sample.Command,4:F2}/{sample.NormalizedCommand,4:F2} " +
                $"{sample.RoadWheelAngleDegrees,5:F2} {sample.NormalAngleDegrees,4:F2}/{sample.OverdriveAngleDegrees,4:F2}/{sample.SlipAllowanceDegrees,4:F2}/{sample.TransientBoostAngleDegrees,4:F2} " +
                $"{sample.BetaDegrees,6:F2} {sample.BetaDotDegreesPerSecond,7:F1} {sample.YawRateDegreesPerSecond,7:F1} " +
                $"{sample.MeasuredYawAccelerationDegreesPerSecondSquared,7:F0}/{sample.CalculatedYawAccelerationDegreesPerSecondSquared,7:F0} " +
                $"{sample.FrontSlipDegrees,6:F2}/{sample.RearSlipDegrees,6:F2} " +
                $"{sample.FrontLateralForceN,7:F0}/{sample.RearLateralForceN,7:F0} " +
                $"{sample.FrontYawMomentNm,7:F0}/{sample.RearYawMomentNm,7:F0}/{sample.NetYawMomentNm,7:F0} " +
                $"{sample.RearGripUsage,5:F2} {sample.LateralG,5:F2}");
        }

        Console.WriteLine(
            $"    events: healthySettle={FormatEvent(probeCase.FirstHealthySettle)} " +
            $"frontForceFall={FormatEvent(probeCase.FirstFrontForceFall)} " +
            $"frontSlipZero={FormatEvent(probeCase.FirstFrontSlipZero)} " +
            $"rearSlipZero={FormatEvent(probeCase.FirstRearSlipZero)} " +
            $"rearSat={FormatEvent(probeCase.FirstRearSaturation)} " +
            $"maxBeta={probeCase.MaxBetaDegrees:F2} maxRearGrip={probeCase.MaxRearGripUsage:F2} speedDrop={probeCase.SpeedDropKmh:F1}km/h");

        if (probeCase.SampleAtFrontSlipZero is not null)
        {
            TurnSample z = probeCase.SampleAtFrontSlipZero.Value;
            Console.WriteLine(
                $"    front-slip zero angle components: actual={z.RoadWheelAngleDegrees:F2}deg " +
                $"normal={z.NormalAngleDegrees:F2} overdrive={z.OverdriveAngleDegrees:F2} " +
                $"slipAllowance={z.SlipAllowanceDegrees:F2} transientBoost={z.TransientBoostAngleDegrees:F2}");
        }

        Console.WriteLine($"    classification: {Classify(probeCase)}");
    }

    private static void PrintSummary(IReadOnlyList<ProbeCase> cases)
    {
        Console.WriteLine();
        Console.WriteLine("  sweep summary:");
        Console.WriteLine("    cmd angle@3s frontSlip@3s rearSlip@3s beta@3s betaDot@3s yaw@3s yawAcc@3s rearGripMax frontZero rearSat speedDrop result");
        foreach (ProbeCase probeCase in cases)
        {
            TurnSample end = probeCase.FinalSample;
            Console.WriteLine(
                $"    {probeCase.Command,4:F2} {end.RoadWheelAngleDegrees,7:F2} " +
                $"{end.FrontSlipDegrees,11:F2} {end.RearSlipDegrees,10:F2} {end.BetaDegrees,7:F2} " +
                $"{end.BetaDotDegreesPerSecond,10:F1} {end.YawRateDegreesPerSecond,7:F1} " +
                $"{end.CalculatedYawAccelerationDegreesPerSecondSquared,9:F0} {probeCase.MaxRearGripUsage,10:F2} " +
                $"{FormatTime(probeCase.FirstFrontSlipZero),9} {FormatTime(probeCase.FirstRearSaturation),7} " +
                $"{probeCase.SpeedDropKmh,9:F1} {ShortClassify(probeCase)}");
        }

        Console.WriteLine($"    root classification: {ClassifySweep(cases)}");
    }

    private static string Classify(ProbeCase probeCase)
    {
        if (probeCase.FirstHealthySettle is not null && probeCase.FirstFrontSlipZero is null && probeCase.FirstRearSaturation is null)
        {
            return "settles into positive-slip cornering without rear saturation";
        }

        if (probeCase.FirstFrontSlipZero is not null)
        {
            if (probeCase.SampleAtFrontSlipZero is { } zero &&
                zero.TransientBoostAngleDegrees > 0.20f)
            {
                return "front slip crosses zero while transient boost is still present";
            }

            if (probeCase.SampleAtFrontSlipZero is { } z &&
                z.SlipAllowanceDegrees > 0.20f)
            {
                return "front slip crosses zero in the overdrive/slip-allowance region";
            }

            return "front slip crosses zero before a healthy settle";
        }

        if (probeCase.FirstRearSaturation is not null)
        {
            return "rear saturates before a healthy settle";
        }

        if (MathF.Abs(probeCase.FinalSample.BetaDotDegreesPerSecond) > 2.5f ||
            MathF.Abs(probeCase.FinalSample.CalculatedYawAccelerationDegreesPerSecondSquared) > 20f)
        {
            return "no zero crossing, but beta/yaw are still not settled at 3s";
        }

        if (probeCase.FinalSample.FrontSlipDegrees <= 0.35f || probeCase.FinalSample.RearSlipDegrees <= 0.20f)
        {
            return "near steady but misses positive front/rear slip target";
        }

        return "appears usable but missed the stricter healthy-settle event window";
    }

    private static string ShortClassify(ProbeCase probeCase)
    {
        if (probeCase.FirstHealthySettle is not null && probeCase.FirstFrontSlipZero is null && probeCase.FirstRearSaturation is null)
        {
            return "settled";
        }

        if (probeCase.FirstFrontSlipZero is not null)
        {
            return "front-zero";
        }

        if (probeCase.FirstRearSaturation is not null)
        {
            return "rear-sat";
        }

        if (MathF.Abs(probeCase.FinalSample.BetaDotDegreesPerSecond) > 2.5f ||
            MathF.Abs(probeCase.FinalSample.CalculatedYawAccelerationDegreesPerSecondSquared) > 20f)
        {
            return "unsettled";
        }

        return "near";
    }

    private static string ClassifySweep(IReadOnlyList<ProbeCase> cases)
    {
        int settledLowMid = cases.Count(c => c.Command <= 0.60f && c.FirstHealthySettle is not null && c.FirstFrontSlipZero is null);
        int failedLowMid = cases.Count(c => c.Command <= 0.40f && c.FirstFrontSlipZero is not null);
        ProbeCase? firstFailure = cases.FirstOrDefault(c => c.FirstFrontSlipZero is not null || c.FirstRearSaturation is not null);

        if (failedLowMid > 0)
        {
            return "even low/moderate steering produces front-slip collapse; deeper base yaw/beta equilibrium issue remains";
        }

        if (settledLowMid >= 2 && firstFailure is { Command: >= 0.60f })
        {
            return $"healthy low/mid command with failure beginning around command {firstFailure.Value.Command:F2}; top-end steering/overdrive range is the prime suspect";
        }

        if (firstFailure is { } failure)
        {
            return $"clear command threshold around {failure.Command:F2}; inspect what changes at that command before tuning other systems";
        }

        return "no front-slip collapse or rear saturation in this sweep; remaining issue is quantitative feel/settle rate rather than the old inverted-slip failure";
    }

    private static string FormatEvent(TurnSample? sample)
    {
        return sample is null ? "none" : $"t{sample.Value.TimeSeconds:F3}s";
    }

    private static string FormatTime(TurnSample? sample)
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
        float Command,
        float SpeedDropKmh,
        float MaxBetaDegrees,
        float MaxRearGripUsage,
        IReadOnlyList<TurnSample> Checkpoints,
        TurnSample? FirstFrontSlipZero,
        TurnSample? FirstRearSlipZero,
        TurnSample? FirstRearSaturation,
        TurnSample? FirstFrontForceFall,
        TurnSample? FirstHealthySettle,
        TurnSample? SampleAtFrontSlipZero,
        TurnSample FinalSample);

    private readonly record struct TurnSample(
        float TimeSeconds,
        float Command,
        float NormalizedCommand,
        float RoadWheelAngleDegrees,
        float NormalAngleDegrees,
        float OverdriveAngleDegrees,
        float SlipAllowanceDegrees,
        float TransientBoostAngleDegrees,
        float SpeedKmh,
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
        float RearGripUsage,
        float FrontGripUsage,
        float LateralG);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
