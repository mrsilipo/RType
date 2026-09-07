using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicFrontPathAuthorityAuditProbe
{
    private const float Dt = 1f / 120f;
    private const float Gravity = 9.81f;
    private const int Gear = 4;

    private static readonly AuditCase[] Cases =
    [
        new("steady-near", 120f, 1.4f, _ => new VehicleInput(0.25f, 0f, 0.75f, brakeAssistEnabled: true), 0.75f),
        new("brake-turn", 120f, 1.4f, _ => new VehicleInput(0f, 0.85f, 0.65f, brakeAssistEnabled: true), 0.65f),
        new("trail-release", 120f, 1.8f, time => new VehicleInput(0f, TrailBrake(time), 0.65f, brakeAssistEnabled: true), 0.65f),
        new("power-exit", 100f, 1.8f, time => new VehicleInput(time < 0.65f ? 0.20f : 1.0f, 0f, 0.60f, brakeAssistEnabled: true), 0.60f)
    ];

    private static readonly AuditProfile[] Profiles =
    [
        new("current", false, float.NaN, float.NaN),
        new("shape-stiff", false, 0.22f, float.NaN),
        new("shape-peak", false, float.NaN, 0.20f),
        new("shape-both", false, 0.18f, 0.16f),
        new("legacy-latVel", true, float.NaN, float.NaN)
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

        Console.WriteLine($"Classic front path-authority / brake-bite audit: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  production physics frozen. Combined-slip shape variants are probe-only and keep nominal peak mu unchanged.");
        Console.WriteLine("  summary columns: profile case latGpeak@t decelGpeak@t resultantGpeak@t overlapG t025/t050/t080/t100 jerkGmax steerDeg frontSlip rearSlip beta yawRate yawAcc frontFx/Fy rearFx/Fy frontYaw rearYaw frontGrip rearGrip yawRec bodySlip latVel latVelAct brakeRatio loadTransferT/A suspPitchTravel");

        foreach (AuditCase auditCase in Cases)
        {
            Console.WriteLine();
            Console.WriteLine($"  case {auditCase.Label}");
            foreach (AuditProfile profile in Profiles)
            {
                Result result = RunCase(parameters, engine, yawInertia, auditCase, profile);
                Print(profile, auditCase, result);
            }
        }

        Console.WriteLine("Classic front path-authority / brake-bite audit complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float yawInertia,
        AuditCase auditCase,
        AuditProfile profile)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            UseLegacyLateralVelocityDampingForProbe = profile.UseLegacyLateralVelocityDamping
        };
        simulator.CombinedSlipLateralStiffnessReductionForProbe = profile.LateralStiffnessReduction;
        simulator.CombinedSlipLateralPeakReductionForProbe = profile.LateralPeakReduction;
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, auditCase.StartSpeedKmh / 3.6f);

        Accumulator acc = new(yawInertia, auditCase.ReferenceSteerSign);
        int ticks = Math.Max(1, (int)MathF.Round(auditCase.DurationSeconds / Dt));
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            simulator.Update(auditCase.Input(time), Dt);
            acc.Add(time, simulator.State);
        }

        return acc.ToResult(simulator.State);
    }

    private static void Print(AuditProfile profile, AuditCase auditCase, Result result)
    {
        Console.WriteLine(
            $"    {profile.Label,-14} {auditCase.Label,-13} " +
            $"{result.PeakLateralG,5:F2}@{result.TimeOfPeakLateralG,4:F2} {result.PeakDecelerationG,5:F2}@{result.TimeOfPeakDecelerationG,4:F2} " +
            $"{result.PeakResultantG,5:F2}@{result.TimeOfPeakResultantG,4:F2} {result.PeakBrakeTurnOverlapG,8:F2} " +
            $"{FormatTime(result.TimeTo025G),5}/{FormatTime(result.TimeTo050G),5}/{FormatTime(result.TimeTo080G),5}/{FormatTime(result.TimeTo100G),5} " +
            $"{result.PeakJerkGPerSecond,8:F1} {result.PeakRoadWheelAngleDegrees,8:F2} " +
            $"{result.FinalFrontSlipDegrees,9:F2} {result.FinalRearSlipDegrees,8:F2} {result.PeakAbsBetaDegrees,5:F2} " +
            $"{result.FinalYawRateDegreesPerSecond,7:F1} {result.PeakYawAccelerationDegreesPerSecondSquared,6:F0} " +
            $"{result.PeakFrontLongitudinalForceN,6:F0}/{result.PeakFrontLateralForceN,6:F0} {result.PeakRearLongitudinalForceN,5:F0}/{result.PeakRearLateralForceN,5:F0} " +
            $"{result.PeakFrontYawMomentNm,8:F0} {result.PeakRearYawMomentNm,7:F0} " +
            $"{result.PeakFrontGripUsage,8:F2} {result.PeakRearGripUsage,7:F2} " +
            $"{result.PeakYawRecoveryDegreesPerSecondSquared,6:F0} {result.PeakBodySlipDampingForceN,8:F0} " +
            $"{result.PeakLateralVelocityDampingForceN,6:F0} {result.PeakLateralVelocityDampingActivation,9:F2} " +
            $"{result.MinimumBrakePressureRatio,10:F2} " +
            $"{result.PeakTargetLongitudinalTransferN,6:F0}/{result.PeakActualLongitudinalTransferN,6:F0} " +
            $"{result.PeakFrontSuspensionTravelDeltaMeters * 1000f,7:F1}mm");
    }

    private static string FormatTime(float time) => float.IsFinite(time) ? time.ToString("F2") : "--";

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

    private readonly record struct AuditProfile(
        string Label,
        bool UseLegacyLateralVelocityDamping,
        float LateralStiffnessReduction,
        float LateralPeakReduction);

    private readonly record struct Result(
        float PeakLateralG,
        float PeakDecelerationG,
        float PeakResultantG,
        float PeakBrakeTurnOverlapG,
        float TimeOfPeakLateralG,
        float TimeOfPeakDecelerationG,
        float TimeOfPeakResultantG,
        float TimeTo025G,
        float TimeTo050G,
        float TimeTo080G,
        float TimeTo100G,
        float PeakJerkGPerSecond,
        float PeakRoadWheelAngleDegrees,
        float FinalFrontSlipDegrees,
        float FinalRearSlipDegrees,
        float PeakAbsBetaDegrees,
        float FinalYawRateDegreesPerSecond,
        float PeakYawAccelerationDegreesPerSecondSquared,
        float PeakFrontLongitudinalForceN,
        float PeakFrontLateralForceN,
        float PeakRearLongitudinalForceN,
        float PeakRearLateralForceN,
        float PeakFrontYawMomentNm,
        float PeakRearYawMomentNm,
        float PeakFrontGripUsage,
        float PeakRearGripUsage,
        float PeakYawRecoveryDegreesPerSecondSquared,
        float PeakBodySlipDampingForceN,
        float PeakLateralVelocityDampingForceN,
        float PeakLateralVelocityDampingActivation,
        float MinimumBrakePressureRatio,
        float PeakTargetLongitudinalTransferN,
        float PeakActualLongitudinalTransferN,
        float PeakFrontSuspensionTravelDeltaMeters);

    private sealed class Accumulator
    {
        private readonly float _yawInertia;
        private readonly float _turnSign;
        private float _previousLongitudinalAccelerationG;
        private float _peakLateralG;
        private float _peakDecelerationG;
        private float _peakResultantG;
        private float _peakBrakeTurnOverlapG;
        private float _timeOfPeakLateralG;
        private float _timeOfPeakDecelerationG;
        private float _timeOfPeakResultantG;
        private float _timeTo025G = float.NaN;
        private float _timeTo050G = float.NaN;
        private float _timeTo080G = float.NaN;
        private float _timeTo100G = float.NaN;
        private float _peakJerkGPerSecond;
        private float _peakRoadWheelAngleDegrees;
        private float _finalFrontSlipDegrees;
        private float _finalRearSlipDegrees;
        private float _peakAbsBetaDegrees;
        private float _finalYawRateDegreesPerSecond;
        private float _peakYawAcceleration;
        private float _peakFrontLongitudinalForceN;
        private float _peakFrontLateralForceN;
        private float _peakRearLongitudinalForceN;
        private float _peakRearLateralForceN;
        private float _peakFrontYawMomentNm;
        private float _peakRearYawMomentNm;
        private float _peakFrontGripUsage;
        private float _peakRearGripUsage;
        private float _peakYawRecovery;
        private float _peakBodySlipDampingForceN;
        private float _peakLateralVelocityDampingForceN;
        private float _peakLateralVelocityDampingActivation;
        private float _minimumBrakePressureRatio = 1f;
        private float _peakTargetLongitudinalTransferN;
        private float _peakActualLongitudinalTransferN;
        private float _peakFrontSuspensionTravelDeltaMeters;

        public Accumulator(float yawInertia, float referenceSteerSign)
        {
            _yawInertia = yawInertia;
            _turnSign = MathF.Sign(referenceSteerSign);
            if (_turnSign == 0f)
            {
                _turnSign = 1f;
            }
        }

        public void Add(float time, VehicleState state)
        {
            float decelerationG = MathF.Max(0f, -state.LongitudinalAcceleration / Gravity);
            float lateralG = MathF.Abs(state.LateralAcceleration) / Gravity;
            float resultantG = MathF.Sqrt(
                state.LongitudinalAcceleration * state.LongitudinalAcceleration +
                state.LateralAcceleration * state.LateralAcceleration) / Gravity;
            float longitudinalAccelerationG = state.LongitudinalAcceleration / Gravity;
            if (lateralG > _peakLateralG)
            {
                _peakLateralG = lateralG;
                _timeOfPeakLateralG = time;
            }

            if (decelerationG > _peakDecelerationG)
            {
                _peakDecelerationG = decelerationG;
                _timeOfPeakDecelerationG = time;
            }

            if (resultantG > _peakResultantG)
            {
                _peakResultantG = resultantG;
                _timeOfPeakResultantG = time;
            }

            if (decelerationG >= 0.50f)
            {
                _peakBrakeTurnOverlapG = MathF.Max(_peakBrakeTurnOverlapG, resultantG);
            }

            if (!float.IsFinite(_timeTo025G) && decelerationG >= 0.25f) _timeTo025G = time;
            if (!float.IsFinite(_timeTo050G) && decelerationG >= 0.50f) _timeTo050G = time;
            if (!float.IsFinite(_timeTo080G) && decelerationG >= 0.80f) _timeTo080G = time;
            if (!float.IsFinite(_timeTo100G) && decelerationG >= 1.00f) _timeTo100G = time;
            _peakJerkGPerSecond = MathF.Max(_peakJerkGPerSecond, MathF.Abs((longitudinalAccelerationG - _previousLongitudinalAccelerationG) / Dt));
            _previousLongitudinalAccelerationG = longitudinalAccelerationG;

            float roadWheelAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
            _peakRoadWheelAngleDegrees = MathF.Max(_peakRoadWheelAngleDegrees, MathF.Abs(roadWheelAngle));
            _finalFrontSlipDegrees = Average(state.FrontLeftSlipAngleDegrees, state.FrontRightSlipAngleDegrees) * _turnSign;
            _finalRearSlipDegrees = Average(state.RearLeftSlipAngleDegrees, state.RearRightSlipAngleDegrees) * _turnSign;
            _peakAbsBetaDegrees = MathF.Max(_peakAbsBetaDegrees, MathF.Abs(state.ClassicBodySlipAngleDegrees * _turnSign));
            _finalYawRateDegreesPerSecond = MathHelper.ToDegrees(state.YawRateRadiansPerSecond) * _turnSign;
            float yawAcceleration = state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
                state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
                state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
                state.ClassicRearFollowAccelerationDegreesPerSecondSquared;
            _peakYawAcceleration = MathF.Max(_peakYawAcceleration, MathF.Abs(yawAcceleration));

            _peakFrontLongitudinalForceN = MathF.Max(_peakFrontLongitudinalForceN, MathF.Abs(state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN));
            _peakFrontLateralForceN = MathF.Max(_peakFrontLateralForceN, MathF.Abs(state.FrontLeftLateralForceN + state.FrontRightLateralForceN));
            _peakRearLongitudinalForceN = MathF.Max(_peakRearLongitudinalForceN, MathF.Abs(state.RearLeftLongitudinalForceN + state.RearRightLongitudinalForceN));
            _peakRearLateralForceN = MathF.Max(_peakRearLateralForceN, MathF.Abs(state.RearLeftLateralForceN + state.RearRightLateralForceN));
            _peakFrontYawMomentNm = MathF.Max(
                _peakFrontYawMomentNm,
                MathF.Abs(MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * _yawInertia));
            _peakRearYawMomentNm = MathF.Max(
                _peakRearYawMomentNm,
                MathF.Abs(MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * _yawInertia));
            _peakFrontGripUsage = MathF.Max(_peakFrontGripUsage, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage));
            _peakRearGripUsage = MathF.Max(_peakRearGripUsage, MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));
            _peakYawRecovery = MathF.Max(_peakYawRecovery, MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared));
            _peakBodySlipDampingForceN = MathF.Max(_peakBodySlipDampingForceN, MathF.Abs(state.ClassicBodySlipDampingForceN));
            _peakLateralVelocityDampingForceN = MathF.Max(_peakLateralVelocityDampingForceN, MathF.Abs(state.ClassicLateralVelocityDampingForceN));
            _peakLateralVelocityDampingActivation = MathF.Max(_peakLateralVelocityDampingActivation, state.ClassicLateralVelocityDampingActivation);
            _minimumBrakePressureRatio = MathF.Min(
                _minimumBrakePressureRatio,
                Minimum(state.FrontLeftBrakePressureRatio, state.FrontRightBrakePressureRatio, state.RearLeftBrakePressureRatio, state.RearRightBrakePressureRatio));
            _peakTargetLongitudinalTransferN = MathF.Max(_peakTargetLongitudinalTransferN, MathF.Abs(state.ClassicTargetLongitudinalLoadTransferN));
            _peakActualLongitudinalTransferN = MathF.Max(_peakActualLongitudinalTransferN, MathF.Abs(state.ClassicActualLongitudinalLoadTransferN));
            _peakFrontSuspensionTravelDeltaMeters = MathF.Max(
                _peakFrontSuspensionTravelDeltaMeters,
                MathF.Abs(state.FrontLeftSuspensionTravelMeters - state.FrontRightSuspensionTravelMeters));
        }

        public Result ToResult(VehicleState final)
        {
            return new Result(
                _peakLateralG,
                _peakDecelerationG,
                _peakResultantG,
                _peakBrakeTurnOverlapG,
                _timeOfPeakLateralG,
                _timeOfPeakDecelerationG,
                _timeOfPeakResultantG,
                _timeTo025G,
                _timeTo050G,
                _timeTo080G,
                _timeTo100G,
                _peakJerkGPerSecond,
                _peakRoadWheelAngleDegrees,
                _finalFrontSlipDegrees,
                _finalRearSlipDegrees,
                _peakAbsBetaDegrees,
                _finalYawRateDegreesPerSecond,
                _peakYawAcceleration,
                _peakFrontLongitudinalForceN,
                _peakFrontLateralForceN,
                _peakRearLongitudinalForceN,
                _peakRearLateralForceN,
                _peakFrontYawMomentNm,
                _peakRearYawMomentNm,
                _peakFrontGripUsage,
                _peakRearGripUsage,
                _peakYawRecovery,
                _peakBodySlipDampingForceN,
                _peakLateralVelocityDampingForceN,
                _peakLateralVelocityDampingActivation,
                _minimumBrakePressureRatio,
                _peakTargetLongitudinalTransferN,
                _peakActualLongitudinalTransferN,
                _peakFrontSuspensionTravelDeltaMeters);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);
    }
}
