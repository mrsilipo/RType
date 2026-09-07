using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicRearYawAssistSuppressionAuditProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float Gravity = 9.81f;

    private static readonly AuditCase[] Cases =
    [
        new("lift-mid", 120f, 2.2f, time => new VehicleInput(time < 0.80f ? 0.35f : 0f, 0f, 0.65f, brakeAssistEnabled: true), 0.65f),
        new("left-right", 100f, 2.2f, time => new VehicleInput(0.25f, 0f, time < 0.85f ? 0.75f : -0.75f, brakeAssistEnabled: true), 0.75f),
        new("brake-turn", 120f, 1.4f, _ => new VehicleInput(0f, 0.85f, 0.65f, brakeAssistEnabled: true), 0.65f),
        new("trail-release", 120f, 2.2f, time => new VehicleInput(0f, TrailBrake(time), 0.65f, brakeAssistEnabled: true), 0.65f)
    ];

    private static readonly AssistProfile[] Profiles =
    [
        new("conditional", new ClassicFourWheelAssistOptions(), false),
        new("legacy-latVel", new ClassicFourWheelAssistOptions(), true),
        new("yawRec-off", new ClassicFourWheelAssistOptions { YawRecoveryEnabled = false }, false),
        new("latVel-off", new ClassicFourWheelAssistOptions { LateralVelocityDampingEnabled = false }, false),
        new("bodySlip-off", new ClassicFourWheelAssistOptions { BodySlipDampingEnabled = false }, false),
        new("rearFollow-off", new ClassicFourWheelAssistOptions { RearFollowEnabled = false }, false),
        new("speedRet-off", new ClassicFourWheelAssistOptions { SpeedRetentionEnabled = false }, false),
        new("all-cleanup-off", new ClassicFourWheelAssistOptions
        {
            BodySlipDampingEnabled = false,
            LateralVelocityDampingEnabled = false,
            RearFollowEnabled = false,
            YawRecoveryEnabled = false,
            SpeedRetentionEnabled = false
        }, false)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engine.ClassicFourWheel.Yaw.InertiaScale));

        Console.WriteLine($"Classic rear-yaw / assist-suppression audit: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  frozen baseline: assist options are probe-only counterfactuals.");
        Console.WriteLine("  columns: profile case latGmax decelGmax rearSlipMax betaMax betaDotMax yawRateFinal yawAccMax rearLoadMin rearCapLoss yawF/Rmax netYawMax rearLatMax frontLatMax yawDampMax yawRecMax rearFollowMax bodySlipForceMax latVelDampMax latVelActMax latVelIntentMin speedRetMax brakeRatioMin verdict");

        foreach (AuditCase auditCase in Cases)
        {
            Console.WriteLine();
            Console.WriteLine($"  case {auditCase.Label}");
            Result baseline = RunCase(parameters, engine, yawInertia, auditCase, Profiles[0]);
            PrintResult(Profiles[0], auditCase, baseline, baseline);

            for (int i = 1; i < Profiles.Length; i++)
            {
                Result result = RunCase(parameters, engine, yawInertia, auditCase, Profiles[i]);
                PrintResult(Profiles[i], auditCase, result, baseline);
            }
        }

        Console.WriteLine("Classic rear-yaw / assist-suppression audit complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float yawInertia,
        AuditCase auditCase,
        AssistProfile profile)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            AssistOptions = profile.Options
        };
        simulator.UseLegacyLateralVelocityDampingForProbe = profile.UseLegacyLateralVelocityDamping;
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, auditCase.StartSpeedKmh / 3.6f);

        float staticRearCapacity = 0f;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        Accumulator acc = new();
        int ticks = Math.Max(1, (int)MathF.Round(auditCase.DurationSeconds / Dt));
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            simulator.Update(auditCase.Input(time), Dt);
            VehicleState state = simulator.State;
            if (staticRearCapacity <= 0f && state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN > 1f)
            {
                staticRearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
            }

            acc.Add(BuildSample(state, previousBeta, staticRearCapacity, yawInertia, auditCase.ReferenceSteerSign, parameters, engine, profile.Options));
            previousBeta = state.ClassicBodySlipAngleDegrees;
        }

        return acc.ToResult(simulator.State, auditCase.ReferenceSteerSign);
    }

    private static Sample BuildSample(
        VehicleState state,
        float previousBetaDegrees,
        float staticRearCapacityN,
        float yawInertia,
        float referenceSteerSign,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ClassicFourWheelAssistOptions assistOptions)
    {
        float turnSign = MathF.Sign(referenceSteerSign);
        if (turnSign == 0f)
        {
            turnSign = 1f;
        }

        float rearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
        float yawDampingMoment = MathHelper.ToRadians(state.ClassicYawDampingAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;
        float yawRecoveryMoment = MathHelper.ToRadians(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;
        float rearFollowMoment = MathHelper.ToRadians(state.ClassicRearFollowAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;
        float lateralVelocityDampingForce = state.ClassicLateralVelocityDampingForceN;
        float brakePressureRatio = Minimum(
            state.FrontLeftBrakePressureRatio,
            state.FrontRightBrakePressureRatio,
            state.RearLeftBrakePressureRatio,
            state.RearRightBrakePressureRatio);
        float netYawMoment = MathHelper.ToRadians(
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared) * yawInertia * turnSign;

        return new Sample(
            MathF.Abs(state.LateralAcceleration) / Gravity,
            state.LongitudinalAcceleration / Gravity,
            MathF.Abs(Average(state.RearLeftSlipAngleDegrees, state.RearRightSlipAngleDegrees) * turnSign),
            MathF.Abs(Average(state.FrontLeftSlipAngleDegrees, state.FrontRightSlipAngleDegrees) * turnSign),
            MathF.Abs(state.ClassicBodySlipAngleDegrees * turnSign),
            MathF.Abs((state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnSign),
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond) * turnSign,
            MathF.Abs(state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
                state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
                state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
                state.ClassicRearFollowAccelerationDegreesPerSecondSquared),
            MathF.Min(state.RearLeftLoadN, state.RearRightLoadN),
            staticRearCapacityN > 1f ? MathHelper.Clamp((staticRearCapacityN - rearCapacity) / staticRearCapacityN, 0f, 1f) : 0f,
            MathF.Abs(MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * yawInertia),
            MathF.Abs(MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * yawInertia),
            MathF.Abs(netYawMoment),
            MathF.Abs(state.RearLeftLateralForceN + state.RearRightLateralForceN),
            MathF.Abs(state.FrontLeftLateralForceN + state.FrontRightLateralForceN),
            MathF.Abs(yawDampingMoment),
            MathF.Abs(yawRecoveryMoment),
            MathF.Abs(rearFollowMoment),
            MathF.Abs(state.ClassicBodySlipDampingForceN),
            MathF.Abs(lateralVelocityDampingForce),
            state.ClassicLateralVelocityDampingActivation,
            state.ClassicLateralVelocityDampingDriverIntentFactor,
            MathF.Abs(state.ClassicCorneringCleanupSpeedRetentionForceN),
            brakePressureRatio);
    }

    private static void PrintResult(AssistProfile profile, AuditCase auditCase, Result result, Result baseline)
    {
        Console.WriteLine(
            $"    {profile.Label,-15} {auditCase.Label,-10} {result.PeakLateralG,7:F2} {result.PeakDecelerationG,9:F2} " +
            $"{result.PeakRearSlipDegrees,11:F2} {result.PeakAbsBetaDegrees,7:F2} {result.PeakAbsBetaDotDegreesPerSecond,10:F1} {result.FinalYawRateDegreesPerSecond,12:F1} " +
            $"{result.PeakAbsYawAccelerationDegreesPerSecondSquared,9:F0} {result.MinimumRearLoadN,11:F0} {result.PeakRearCapacityLoss * 100f,10:F1}% " +
            $"{result.PeakAbsFrontYawMomentNm,6:F0}/{result.PeakAbsRearYawMomentNm,6:F0} {result.PeakAbsNetYawMomentNm,9:F0} " +
            $"{result.PeakRearLateralForceN,10:F0} {result.PeakFrontLateralForceN,11:F0} " +
            $"{result.PeakYawDampingMomentNm,10:F0} {result.PeakYawRecoveryMomentNm,9:F0} {result.PeakRearFollowMomentNm,12:F0} " +
            $"{result.PeakBodySlipDampingForceN,16:F0} {result.PeakLateralVelocityDampingForceN,13:F0} " +
            $"{result.PeakLateralVelocityDampingActivation,12:F2} {result.MinimumLateralVelocityDampingDriverIntentFactor,15:F2} " +
            $"{result.PeakSpeedRetentionForceN,11:F0} {result.MinimumBrakePressureRatio,13:F2} " +
            $"{Classify(result, baseline)}");
    }

    private static string Classify(Result result, Result baseline)
    {
        float betaGain = result.PeakAbsBetaDegrees - baseline.PeakAbsBetaDegrees;
        float rearSlipGain = result.PeakRearSlipDegrees - baseline.PeakRearSlipDegrees;
        float yawGain = MathF.Abs(result.FinalYawRateDegreesPerSecond) - MathF.Abs(baseline.FinalYawRateDegreesPerSecond);

        if (betaGain > 1.0f || rearSlipGain > 1.0f || yawGain > 4.0f)
        {
            return "assist-suppression-dominates";
        }

        if (result.PeakAbsRearYawMomentNm > result.PeakAbsFrontYawMomentNm * 0.65f &&
            result.PeakRearSlipDegrees < 6f)
        {
            return "rear-stabilising-moment-dominates";
        }

        if (result.PeakRearCapacityLoss < 0.10f && result.PeakRearSlipDegrees < 6f)
        {
            return "physical-rear-state-too-small";
        }

        return "mixed";
    }

    private static float Average(float a, float b) => (a + b) * 0.5f;

    private static float Minimum(float a, float b, float c, float d) => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));

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

    private readonly record struct AuditCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        Func<float, VehicleInput> Input,
        float ReferenceSteerSign);

    private readonly record struct AssistProfile(
        string Label,
        ClassicFourWheelAssistOptions Options,
        bool UseLegacyLateralVelocityDamping);

    private readonly record struct Sample(
        float LateralG,
        float LongitudinalAccelerationG,
        float RearSlipDegrees,
        float FrontSlipDegrees,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float RearLoadN,
        float RearCapacityLoss,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float RearLateralForceN,
        float FrontLateralForceN,
        float YawDampingMomentNm,
        float YawRecoveryMomentNm,
        float RearFollowMomentNm,
        float BodySlipDampingForceN,
        float LateralVelocityDampingForceN,
        float LateralVelocityDampingActivation,
        float LateralVelocityDampingDriverIntentFactor,
        float SpeedRetentionForceN,
        float BrakePressureRatio);

    private readonly record struct Result(
        float PeakLateralG,
        float PeakRearSlipDegrees,
        float PeakFrontSlipDegrees,
        float PeakAbsBetaDegrees,
        float PeakAbsBetaDotDegreesPerSecond,
        float FinalYawRateDegreesPerSecond,
        float PeakAbsYawAccelerationDegreesPerSecondSquared,
        float MinimumRearLoadN,
        float PeakRearCapacityLoss,
        float PeakAbsFrontYawMomentNm,
        float PeakAbsRearYawMomentNm,
        float PeakAbsNetYawMomentNm,
        float PeakRearLateralForceN,
        float PeakFrontLateralForceN,
        float PeakYawDampingMomentNm,
        float PeakYawRecoveryMomentNm,
        float PeakRearFollowMomentNm,
        float PeakBodySlipDampingForceN,
        float PeakLateralVelocityDampingForceN,
        float PeakLateralVelocityDampingActivation,
        float MinimumLateralVelocityDampingDriverIntentFactor,
        float PeakSpeedRetentionForceN,
        float MinimumBrakePressureRatio,
        float PeakDecelerationG);

    private sealed class Accumulator
    {
        private float _peakLateralG;
        private float _peakRearSlip;
        private float _peakFrontSlip;
        private float _peakBeta;
        private float _peakBetaDot;
        private float _peakYawAcceleration;
        private float _minimumRearLoad = float.PositiveInfinity;
        private float _peakRearCapacityLoss;
        private float _peakFrontYawMoment;
        private float _peakRearYawMoment;
        private float _peakNetYawMoment;
        private float _peakRearLateralForce;
        private float _peakFrontLateralForce;
        private float _peakYawDampingMoment;
        private float _peakYawRecoveryMoment;
        private float _peakRearFollowMoment;
        private float _peakBodySlipDampingForce;
        private float _peakLateralVelocityDampingForce;
        private float _peakLateralVelocityDampingActivation;
        private float _minimumLateralVelocityDampingDriverIntentFactor = 1f;
        private float _peakSpeedRetentionForce;
        private float _minimumBrakePressureRatio = 1f;
        private float _peakDecelerationG;

        public void Add(Sample sample)
        {
            _peakLateralG = MathF.Max(_peakLateralG, sample.LateralG);
            _peakRearSlip = MathF.Max(_peakRearSlip, sample.RearSlipDegrees);
            _peakFrontSlip = MathF.Max(_peakFrontSlip, sample.FrontSlipDegrees);
            _peakBeta = MathF.Max(_peakBeta, sample.BetaDegrees);
            _peakBetaDot = MathF.Max(_peakBetaDot, sample.BetaDotDegreesPerSecond);
            _peakYawAcceleration = MathF.Max(_peakYawAcceleration, sample.YawAccelerationDegreesPerSecondSquared);
            _minimumRearLoad = MathF.Min(_minimumRearLoad, sample.RearLoadN);
            _peakRearCapacityLoss = MathF.Max(_peakRearCapacityLoss, sample.RearCapacityLoss);
            _peakFrontYawMoment = MathF.Max(_peakFrontYawMoment, sample.FrontYawMomentNm);
            _peakRearYawMoment = MathF.Max(_peakRearYawMoment, sample.RearYawMomentNm);
            _peakNetYawMoment = MathF.Max(_peakNetYawMoment, sample.NetYawMomentNm);
            _peakRearLateralForce = MathF.Max(_peakRearLateralForce, sample.RearLateralForceN);
            _peakFrontLateralForce = MathF.Max(_peakFrontLateralForce, sample.FrontLateralForceN);
            _peakYawDampingMoment = MathF.Max(_peakYawDampingMoment, sample.YawDampingMomentNm);
            _peakYawRecoveryMoment = MathF.Max(_peakYawRecoveryMoment, sample.YawRecoveryMomentNm);
            _peakRearFollowMoment = MathF.Max(_peakRearFollowMoment, sample.RearFollowMomentNm);
            _peakBodySlipDampingForce = MathF.Max(_peakBodySlipDampingForce, sample.BodySlipDampingForceN);
            _peakLateralVelocityDampingForce = MathF.Max(_peakLateralVelocityDampingForce, sample.LateralVelocityDampingForceN);
            _peakLateralVelocityDampingActivation = MathF.Max(_peakLateralVelocityDampingActivation, sample.LateralVelocityDampingActivation);
            _minimumLateralVelocityDampingDriverIntentFactor = MathF.Min(_minimumLateralVelocityDampingDriverIntentFactor, sample.LateralVelocityDampingDriverIntentFactor);
            _peakSpeedRetentionForce = MathF.Max(_peakSpeedRetentionForce, sample.SpeedRetentionForceN);
            _minimumBrakePressureRatio = MathF.Min(_minimumBrakePressureRatio, sample.BrakePressureRatio);
            _peakDecelerationG = MathF.Max(_peakDecelerationG, MathF.Max(0f, -sample.LongitudinalAccelerationG));
        }

        public Result ToResult(VehicleState final, float referenceSteerSign)
        {
            float turnSign = MathF.Sign(referenceSteerSign);
            if (turnSign == 0f)
            {
                turnSign = 1f;
            }

            return new Result(
                _peakLateralG,
                _peakRearSlip,
                _peakFrontSlip,
                _peakBeta,
                _peakBetaDot,
                MathHelper.ToDegrees(final.YawRateRadiansPerSecond) * turnSign,
                _peakYawAcceleration,
                _minimumRearLoad,
                _peakRearCapacityLoss,
                _peakFrontYawMoment,
                _peakRearYawMoment,
                _peakNetYawMoment,
                _peakRearLateralForce,
                _peakFrontLateralForce,
                _peakYawDampingMoment,
                _peakYawRecoveryMoment,
                _peakRearFollowMoment,
                _peakBodySlipDampingForce,
                _peakLateralVelocityDampingForce,
                _peakLateralVelocityDampingActivation,
                _minimumLateralVelocityDampingDriverIntentFactor,
                _peakSpeedRetentionForce,
                _minimumBrakePressureRatio,
                _peakDecelerationG);
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
