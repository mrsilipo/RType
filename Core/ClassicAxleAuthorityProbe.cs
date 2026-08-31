using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicAxleAuthorityProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float RunSeconds = 3.0f;

    private static readonly float[] Commands = [0.80f, 1.00f];
    private static readonly float[] CheckpointsSeconds = [0.10f, 0.25f, 0.50f, 1.00f, 2.00f, 3.00f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic axle authority probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: current production config, coast-turn, 150km/h, gear=4");
        Console.WriteLine("  turn-normalized values: into the commanded turn is positive");
        Console.WriteLine("  purpose: preserve steering authority, identify whether front force, rear force, beta, or moment balance dictates the corner");

        List<ProbeCase> cases = [];
        foreach (float command in Commands)
        {
            ProbeCase probeCase = RunCase(parameters, engine, geometry, command);
            cases.Add(probeCase);
            PrintCase(probeCase);
        }

        PrintComparison(cases);
        Console.WriteLine("Classic axle authority probe complete.");
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

        List<AuthoritySample> checkpoints = [];
        int checkpointIndex = 0;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousYaw = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float peakFrontYaw = 0f;
        AuthoritySample? firstFrontYawFall = null;
        AuthoritySample? firstRearYawDominance = null;
        AuthoritySample? firstBetaRunaway = null;
        AuthoritySample finalSample = default;

        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, command, brakeAssistEnabled: true), Dt);
            AuthoritySample sample = BuildSample(i * Dt, command, simulator.State, geometry, previousBeta, previousYaw);
            finalSample = sample;

            float frontYawAbs = MathF.Abs(sample.NormalizedFrontYawMomentNm);
            if (frontYawAbs > peakFrontYaw)
            {
                peakFrontYaw = frontYawAbs;
            }
            else if (firstFrontYawFall is null &&
                sample.TimeSeconds >= 0.15f &&
                frontYawAbs < peakFrontYaw * 0.70f)
            {
                firstFrontYawFall = sample;
            }

            if (firstRearYawDominance is null &&
                sample.TimeSeconds >= 0.15f &&
                MathF.Abs(sample.NormalizedRearYawMomentNm) > MathF.Abs(sample.NormalizedFrontYawMomentNm) * 1.25f)
            {
                firstRearYawDominance = sample;
            }

            if (firstBetaRunaway is null &&
                sample.TimeSeconds >= 0.20f &&
                MathF.Abs(sample.BetaDotDegreesPerSecond) > 4f &&
                MathF.Abs(sample.NormalizedBetaDegrees) > 3f)
            {
                firstBetaRunaway = sample;
            }

            if (checkpointIndex < CheckpointsSeconds.Length &&
                sample.TimeSeconds + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                checkpoints.Add(sample with { TimeSeconds = CheckpointsSeconds[checkpointIndex] });
                checkpointIndex++;
            }

            previousBeta = sample.RawBetaDegrees;
            previousYaw = sample.RawYawRateDegreesPerSecond;
        }

        return new ProbeCase(command, checkpoints, finalSample, firstFrontYawFall, firstRearYawDominance, firstBetaRunaway);
    }

    private static AuthoritySample BuildSample(
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

        float rawYawRate = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float measuredYawAcceleration = (rawYawRate - previousYawRateDegreesPerSecond) / Dt;
        float calculatedYawAcceleration =
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared;
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float normalizedFrontLateral = frontLateral * turnMultiplier;
        float normalizedRearLateral = rearLateral * turnMultiplier;
        float normalizedFrontYaw = frontMoment * turnMultiplier;
        float normalizedRearYaw = rearMoment * turnMultiplier;
        float totalLatAbs = MathF.Abs(normalizedFrontLateral) + MathF.Abs(normalizedRearLateral);
        float totalYawAbs = MathF.Abs(normalizedFrontYaw) + MathF.Abs(normalizedRearYaw);

        return new AuthoritySample(
            time,
            command,
            state.SpeedMetersPerSecond * 3.6f,
            roadAngle,
            state.SteeringPhysicalNormalAngleDegrees,
            state.SteeringPhysicalOverdriveAngleDegrees,
            MathF.Max(0f, MathF.Abs(roadAngle) -
                state.SteeringPhysicalNormalAngleDegrees -
                state.SteeringTransientBoostAngleDegrees),
            state.SteeringTransientBoostAngleDegrees,
            state.ClassicBodySlipAngleDegrees,
            state.ClassicBodySlipAngleDegrees * turnMultiplier,
            (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnMultiplier,
            rawYawRate,
            rawYawRate * turnMultiplier,
            measuredYawAcceleration * turnMultiplier,
            calculatedYawAcceleration * turnMultiplier,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f * turnMultiplier,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f * turnMultiplier,
            normalizedFrontLateral,
            normalizedRearLateral,
            normalizedFrontYaw,
            normalizedRearYaw,
            (frontMoment + rearMoment) * turnMultiplier,
            totalLatAbs > 1f ? MathF.Abs(normalizedFrontLateral) / totalLatAbs : 0f,
            totalLatAbs > 1f ? MathF.Abs(normalizedRearLateral) / totalLatAbs : 0f,
            totalYawAbs > 1f ? MathF.Abs(normalizedFrontYaw) / totalYawAbs : 0f,
            totalYawAbs > 1f ? MathF.Abs(normalizedRearYaw) / totalYawAbs : 0f,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            state.LateralAcceleration / 9.81f);
    }

    private static void PrintCase(ProbeCase probeCase)
    {
        Console.WriteLine();
        Console.WriteLine($"  command {probeCase.Command:F2}");
        Console.WriteLine("    t speed angle n/o/slip/boost beta betaDot yaw yawAcc slipF/R latF/R latShareF/R yawM F/R/net yawShareF/R gripF/R latG");
        foreach (AuthoritySample sample in probeCase.Checkpoints)
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,4:F2} {sample.SpeedKmh,6:F1} {sample.RoadWheelAngleDegrees,5:F2} " +
                $"{sample.NormalAngleDegrees:F2}/{sample.OverdriveAngleDegrees:F2}/{sample.SlipAllowanceDegrees:F2}/{sample.TransientBoostAngleDegrees:F2} " +
                $"{sample.NormalizedBetaDegrees,6:F2} {sample.BetaDotDegreesPerSecond,7:F1} " +
                $"{sample.NormalizedYawRateDegreesPerSecond,6:F1} {sample.CalculatedYawAccelerationDegreesPerSecondSquared,7:F0} " +
                $"{sample.NormalizedFrontSlipDegrees,6:F2}/{sample.NormalizedRearSlipDegrees,6:F2} " +
                $"{sample.NormalizedFrontLateralForceN,7:F0}/{sample.NormalizedRearLateralForceN,7:F0} " +
                $"{sample.FrontLateralShare,4:P0}/{sample.RearLateralShare,4:P0} " +
                $"{sample.NormalizedFrontYawMomentNm,7:F0}/{sample.NormalizedRearYawMomentNm,7:F0}/{sample.NormalizedNetYawMomentNm,7:F0} " +
                $"{sample.FrontYawShare,4:P0}/{sample.RearYawShare,4:P0} " +
                $"{sample.FrontGripUsage,4:F2}/{sample.RearGripUsage,4:F2} {sample.LateralG,5:F2}");
        }

        Console.WriteLine(
            $"    events: frontYawFall={FormatEvent(probeCase.FirstFrontYawFall)} " +
            $"rearYawDominance={FormatEvent(probeCase.FirstRearYawDominance)} " +
            $"betaRunaway={FormatEvent(probeCase.FirstBetaRunaway)}");
        Console.WriteLine($"    classification: {Classify(probeCase)}");
    }

    private static void PrintComparison(IReadOnlyList<ProbeCase> cases)
    {
        Console.WriteLine();
        Console.WriteLine("  comparison:");
        foreach (ProbeCase probeCase in cases)
        {
            AuthoritySample end = probeCase.FinalSample;
            Console.WriteLine(
                $"    cmd={probeCase.Command:F2} final: angle={end.RoadWheelAngleDegrees:F2} beta={end.NormalizedBetaDegrees:F2} " +
                $"yaw={end.NormalizedYawRateDegreesPerSecond:F1} slipF/R={end.NormalizedFrontSlipDegrees:F2}/{end.NormalizedRearSlipDegrees:F2} " +
                $"latShareF/R={end.FrontLateralShare:P0}/{end.RearLateralShare:P0} " +
                $"yawShareF/R={end.FrontYawShare:P0}/{end.RearYawShare:P0} " +
                $"gripF/R={end.FrontGripUsage:F2}/{end.RearGripUsage:F2}");
        }
    }

    private static string Classify(ProbeCase probeCase)
    {
        AuthoritySample end = probeCase.FinalSample;
        if (probeCase.FirstFrontYawFall is not null && probeCase.FirstRearYawDominance is not null)
        {
            return "front yaw contribution falls away, then rear yaw/moment share dictates the settled attitude";
        }

        if (end.RearYawShare > 0.65f && end.NormalizedRearSlipDegrees > end.NormalizedFrontSlipDegrees * 1.25f)
        {
            return "rear axle dominates settled path/attitude despite front still contributing lateral force";
        }

        if (end.FrontLateralShare < 0.45f)
        {
            return "front lateral force share is low relative to rear";
        }

        if (MathF.Abs(end.NormalizedBetaDegrees) > 4f)
        {
            return "force shares are not extreme, but beta remains large enough to make chassis attitude feel dominant";
        }

        return "front/rear authority is not obviously pathological in this window";
    }

    private static string FormatEvent(AuthoritySample? sample)
    {
        return sample is null ? "none" : $"t{sample.Value.TimeSeconds:F3}s";
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
        IReadOnlyList<AuthoritySample> Checkpoints,
        AuthoritySample FinalSample,
        AuthoritySample? FirstFrontYawFall,
        AuthoritySample? FirstRearYawDominance,
        AuthoritySample? FirstBetaRunaway);

    private readonly record struct AuthoritySample(
        float TimeSeconds,
        float Command,
        float SpeedKmh,
        float RoadWheelAngleDegrees,
        float NormalAngleDegrees,
        float OverdriveAngleDegrees,
        float SlipAllowanceDegrees,
        float TransientBoostAngleDegrees,
        float RawBetaDegrees,
        float NormalizedBetaDegrees,
        float BetaDotDegreesPerSecond,
        float RawYawRateDegreesPerSecond,
        float NormalizedYawRateDegreesPerSecond,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float CalculatedYawAccelerationDegreesPerSecondSquared,
        float NormalizedFrontSlipDegrees,
        float NormalizedRearSlipDegrees,
        float NormalizedFrontLateralForceN,
        float NormalizedRearLateralForceN,
        float NormalizedFrontYawMomentNm,
        float NormalizedRearYawMomentNm,
        float NormalizedNetYawMomentNm,
        float FrontLateralShare,
        float RearLateralShare,
        float FrontYawShare,
        float RearYawShare,
        float FrontGripUsage,
        float RearGripUsage,
        float LateralG);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
