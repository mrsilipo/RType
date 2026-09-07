using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicFfRotationChainAuditProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float Gravity = 9.81f;

    private static readonly AuditCase[] Cases =
    [
        new("lift-mid", 120f, 2.2f, time => new VehicleInput(time < 0.80f ? 0.35f : 0f, 0f, 0.65f, brakeAssistEnabled: true), 0.65f),
        new("trail-release", 120f, 2.2f, time => new VehicleInput(0f, TrailBrake(time), 0.65f, brakeAssistEnabled: true), 0.65f),
        new("left-right", 100f, 2.2f, time => new VehicleInput(0.25f, 0f, time < 0.85f ? 0.75f : -0.75f, brakeAssistEnabled: true), 0.75f)
    ];

    private static readonly float[] CheckpointsSeconds = [0.50f, 0.80f, 1.05f, 1.35f, 1.80f, 2.20f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);
        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engine.ClassicFourWheel.Yaw.InertiaScale));

        Console.WriteLine($"Classic FF rotation-chain audit: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  frozen baseline: audit only; no production tuning changed.");
        Console.WriteLine("  columns: t speed throttle brake steer latG rearLoad in/out rearDiff target/actual unloadRate capRear capLoss slipR fyR stiffR muR yawF/R/net beta betaDot yawRec cleanup brakeReg weak");

        foreach (AuditCase auditCase in Cases)
        {
            RunCase(parameters, engine, tyres, yawInertia, auditCase);
        }

        Console.WriteLine("Classic FF rotation-chain audit complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ClassicFourWheelTyres tyres,
        float yawInertia,
        AuditCase auditCase)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, auditCase.StartSpeedKmh / 3.6f);

        float staticRearCapacity = 0f;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        float previousInsideRearLoad = float.NaN;
        int checkpointIndex = 0;
        Summary summary = new();

        Console.WriteLine();
        Console.WriteLine($"  case {auditCase.Label}");

        int ticks = Math.Max(1, (int)MathF.Round(auditCase.DurationSeconds / Dt));
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            VehicleInput input = auditCase.Input(time);
            simulator.Update(input, Dt);
            VehicleState state = simulator.State;

            if (staticRearCapacity <= 0f && state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN > 1f)
            {
                staticRearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
            }

            Sample sample = BuildSample(state, previousBeta, previousInsideRearLoad, staticRearCapacity, yawInertia, auditCase.ReferenceSteerSign, parameters, engine, tyres);
            previousBeta = state.ClassicBodySlipAngleDegrees;
            previousInsideRearLoad = sample.InsideRearLoadN;
            summary.Add(sample);

            if (checkpointIndex < CheckpointsSeconds.Length &&
                time + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(CheckpointsSeconds[checkpointIndex], state, sample);
                checkpointIndex++;
            }
        }

        Console.WriteLine(
            $"  summary {auditCase.Label,-13} " +
            $"minRear={summary.MinInsideRearLoadN,5:F0}N maxRearDiff={summary.MaxRearLoadDifferenceN,5:F0}N " +
            $"maxUnloadRate={summary.MaxRearUnloadRateNPerSecond,6:F0}N/s maxCapLoss={summary.MaxRearCapacityLoss * 100f,4:F1}% " +
            $"maxRearSlip={summary.MaxRearSlipDegrees,4:F1}deg maxBeta={summary.MaxAbsBetaDegrees,4:F1}deg " +
            $"yawF/R={summary.MaxAbsFrontYawMomentNm,6:F0}/{summary.MaxAbsRearYawMomentNm,6:F0}Nm " +
            $"yawRec={summary.MaxYawRecoveryDegreesPerSecondSquared,5:F0} cleanup={summary.MaxCleanupForceN,5:F0}N " +
            $"classification={summary.Classify()}");
    }

    private static Sample BuildSample(
        VehicleState state,
        float previousBetaDegrees,
        float previousInsideRearLoadN,
        float staticRearCapacityN,
        float yawInertia,
        float referenceSteerSign,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ClassicFourWheelTyres tyres)
    {
        float turnSign = MathF.Sign(referenceSteerSign);
        if (turnSign == 0f)
        {
            turnSign = 1f;
        }

        float rlLoad = state.RearLeftLoadN;
        float rrLoad = state.RearRightLoadN;
        float insideRearLoad = MathF.Min(rlLoad, rrLoad);
        float outsideRearLoad = MathF.Max(rlLoad, rrLoad);
        float rearLoadDifference = outsideRearLoad - insideRearLoad;
        float targetRearDifference = MathF.Abs(state.RearRightSuspensionTargetLoadN - state.RearLeftSuspensionTargetLoadN);
        float actualRearDifference = MathF.Abs(state.RearRightSuspensionNormalLoadN - state.RearLeftSuspensionNormalLoadN);
        float unloadRate = float.IsFinite(previousInsideRearLoadN)
            ? MathF.Max(0f, (previousInsideRearLoadN - insideRearLoad) / Dt)
            : 0f;

        float rearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
        float rearCapacityLoss = staticRearCapacityN > 1f
            ? MathHelper.Clamp((staticRearCapacityN - rearCapacity) / staticRearCapacityN, 0f, 1f)
            : 0f;

        float rearSlip = Average(state.RearLeftSlipAngleDegrees, state.RearRightSlipAngleDegrees) * turnSign;
        float rearFy = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float rearEffectiveMu = rearCapacity / MathF.Max(1f, rlLoad + rrLoad);
        float rearStiffness = Average(
            CalculateTyreSlopeAtSlip(state.RearLeftSlipAngleDegrees, state.RearLeftFrictionEllipseGripBudgetN, tyres.Rear),
            CalculateTyreSlopeAtSlip(state.RearRightSlipAngleDegrees, state.RearRightFrictionEllipseGripBudgetN, tyres.Rear));

        float frontYawMoment = MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;
        float rearYawMoment = MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;
        float netYawMoment = MathHelper.ToRadians(
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;
        float beta = state.ClassicBodySlipAngleDegrees * turnSign;
        float betaDot = (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnSign;
        float cleanupForce = MathF.Abs(state.ClassicBodySlipDampingForceN + state.ClassicLateralVelocityDampingForceN);
        float brakeRatio = Minimum(
            state.FrontLeftBrakePressureRatio,
            state.FrontRightBrakePressureRatio,
            state.RearLeftBrakePressureRatio,
            state.RearRightBrakePressureRatio);

        return new Sample(
            insideRearLoad,
            outsideRearLoad,
            rearLoadDifference,
            targetRearDifference,
            actualRearDifference,
            unloadRate,
            rearCapacity,
            rearCapacityLoss,
            rearSlip,
            rearFy,
            rearStiffness,
            rearEffectiveMu,
            frontYawMoment,
            rearYawMoment,
            netYawMoment,
            beta,
            betaDot,
            MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared),
            cleanupForce,
            brakeRatio);
    }

    private static void PrintSample(float time, VehicleState state, Sample sample)
    {
        Console.WriteLine(
            $"    {time,4:F2} {state.SpeedMetersPerSecond * 3.6f,6:F1} {state.Throttle,5:F2} {state.Brake,5:F2} {state.Steer,5:F2} " +
            $"{MathF.Abs(state.LateralAcceleration) / Gravity,4:F2} " +
            $"{sample.InsideRearLoadN,5:F0}/{sample.OutsideRearLoadN,5:F0} {sample.TargetRearLoadDifferenceN,5:F0}/{sample.ActualRearLoadDifferenceN,5:F0} " +
            $"{sample.RearUnloadRateNPerSecond,6:F0} {sample.RearCapacityN,6:F0} {sample.RearCapacityLoss * 100f,5:F1}% " +
            $"{sample.RearSlipDegrees,5:F2} {sample.RearLateralForceN,6:F0} {sample.RearCorneringStiffnessNPerDegree,6:F0} {sample.RearEffectiveMu,4:F2} " +
            $"{sample.FrontYawMomentNm,7:F0}/{sample.RearYawMomentNm,7:F0}/{sample.NetYawMomentNm,7:F0} " +
            $"{sample.BetaDegrees,5:F2} {sample.BetaDotDegreesPerSecond,6:F1} " +
            $"{sample.YawRecoveryDegreesPerSecondSquared,5:F0} {sample.CleanupForceN,5:F0} {sample.BrakePressureRatio,4:F2} {ClassifySample(sample)}");
    }

    private static string ClassifySample(Sample sample)
    {
        if (sample.RearCapacityLoss < 0.10f && sample.InsideRearLoadN < 1000f)
        {
            return "load-no-cap-loss";
        }

        if (sample.RearCapacityLoss >= 0.10f && MathF.Abs(sample.BetaDegrees) < 3f)
        {
            return "cap-no-attitude";
        }

        if (sample.YawRecoveryDegreesPerSecondSquared > 15f || sample.CleanupForceN > 3000f)
        {
            return "assist-active";
        }

        return "chain-moving";
    }

    private static float CalculateTyreSlopeAtSlip(float slipDegrees, float maxForceN, ClassicBicycleTyreParameters tyre)
    {
        const float DeltaDegrees = 0.1f;
        float lower = slipDegrees - DeltaDegrees;
        float upper = slipDegrees + DeltaDegrees;
        float lowerForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(lower), maxForceN, tyre);
        float upperForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(upper), maxForceN, tyre);
        return (upperForce - lowerForce) / MathF.Max(0.001f, upper - lower);
    }

    private static float TrailBrake(float time)
    {
        if (time < 0.35f)
        {
            return 0.85f;
        }

        return MathHelper.Lerp(0.85f, 0.10f, SmoothStep01((time - 0.35f) / 0.90f));
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Average(float a, float b) => (a + b) * 0.5f;

    private static float Minimum(float a, float b, float c, float d) => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));

    private readonly record struct AuditCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        Func<float, VehicleInput> Input,
        float ReferenceSteerSign);

    private readonly record struct Sample(
        float InsideRearLoadN,
        float OutsideRearLoadN,
        float RearLoadDifferenceN,
        float TargetRearLoadDifferenceN,
        float ActualRearLoadDifferenceN,
        float RearUnloadRateNPerSecond,
        float RearCapacityN,
        float RearCapacityLoss,
        float RearSlipDegrees,
        float RearLateralForceN,
        float RearCorneringStiffnessNPerDegree,
        float RearEffectiveMu,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRecoveryDegreesPerSecondSquared,
        float CleanupForceN,
        float BrakePressureRatio);

    private sealed class Summary
    {
        public float MinInsideRearLoadN { get; private set; } = float.PositiveInfinity;
        public float MaxRearLoadDifferenceN { get; private set; }
        public float MaxRearUnloadRateNPerSecond { get; private set; }
        public float MaxRearCapacityLoss { get; private set; }
        public float MaxRearSlipDegrees { get; private set; }
        public float MaxAbsBetaDegrees { get; private set; }
        public float MaxAbsFrontYawMomentNm { get; private set; }
        public float MaxAbsRearYawMomentNm { get; private set; }
        public float MaxYawRecoveryDegreesPerSecondSquared { get; private set; }
        public float MaxCleanupForceN { get; private set; }

        public void Add(Sample sample)
        {
            MinInsideRearLoadN = MathF.Min(MinInsideRearLoadN, sample.InsideRearLoadN);
            MaxRearLoadDifferenceN = MathF.Max(MaxRearLoadDifferenceN, sample.RearLoadDifferenceN);
            MaxRearUnloadRateNPerSecond = MathF.Max(MaxRearUnloadRateNPerSecond, sample.RearUnloadRateNPerSecond);
            MaxRearCapacityLoss = MathF.Max(MaxRearCapacityLoss, sample.RearCapacityLoss);
            MaxRearSlipDegrees = MathF.Max(MaxRearSlipDegrees, MathF.Abs(sample.RearSlipDegrees));
            MaxAbsBetaDegrees = MathF.Max(MaxAbsBetaDegrees, MathF.Abs(sample.BetaDegrees));
            MaxAbsFrontYawMomentNm = MathF.Max(MaxAbsFrontYawMomentNm, MathF.Abs(sample.FrontYawMomentNm));
            MaxAbsRearYawMomentNm = MathF.Max(MaxAbsRearYawMomentNm, MathF.Abs(sample.RearYawMomentNm));
            MaxYawRecoveryDegreesPerSecondSquared = MathF.Max(MaxYawRecoveryDegreesPerSecondSquared, sample.YawRecoveryDegreesPerSecondSquared);
            MaxCleanupForceN = MathF.Max(MaxCleanupForceN, sample.CleanupForceN);
        }

        public string Classify()
        {
            if (MinInsideRearLoadN < 1000f && MaxRearCapacityLoss < 0.10f)
            {
                return "rear-unloads-but-capacity-barely-falls";
            }

            if (MaxRearCapacityLoss >= 0.10f && MaxAbsBetaDegrees < 3f)
            {
                return "capacity-falls-but-attitude-barely-moves";
            }

            if (MaxYawRecoveryDegreesPerSecondSquared > 15f || MaxCleanupForceN > 3000f)
            {
                return "natural-state-may-be-assist-leashed";
            }

            if (MaxRearSlipDegrees < 6f)
            {
                return "rear-slip-stays-pre-limit";
            }

            return "rotation-chain-active";
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
