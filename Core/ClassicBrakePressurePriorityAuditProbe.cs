using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBrakePressurePriorityAuditProbe
{
    private const float Dt = 1f / 120f;
    private const float Gravity = 9.81f;
    private const int Gear = 4;

    private static readonly AuditCase[] Cases =
    [
        new("brake-turn", 120f, 1.20f, 0f, _ => new VehicleInput(0f, 0.85f, 0.65f, brakeAssistEnabled: true)),
        new("brake-then-steer", 120f, 1.20f, 0.35f, time => new VehicleInput(0f, 0.85f, time < 0.35f ? 0f : 0.65f, brakeAssistEnabled: true)),
        new("trail-release", 120f, 1.60f, 0f, time => new VehicleInput(0f, TrailBrake(time), 0.65f, brakeAssistEnabled: true))
    ];

    private static readonly AuditProfile[] Profiles =
    [
        new("current", false, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN),
        new("slower-release", false, 14f, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN),
        new("higher-min", false, float.NaN, 0.28f, float.NaN, float.NaN, float.NaN, float.NaN),
        new("less-priority", false, float.NaN, float.NaN, 0.20f, 0.88f, 0.42f, float.NaN),
        new("no-reg-cap", true, float.NaN, float.NaN, 0f, 1f, 1f, 0.36f)
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

        Console.WriteLine($"Classic brake-pressure / brake-steer priority audit: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  production values frozen. Profiles are diagnostic-only overrides.");
        Console.WriteLine("  current params: releaseRate={0:F1}/s minPressure={1:F2} lateralPriority={2:F2} front/rearBrakeMult={3:F2}/{4:F2}",
            engine.ClassicFourWheel.GripBudget.BrakePressureReleaseRatePerSecond,
            engine.ClassicFourWheel.GripBudget.BrakePressureMinimumRatio,
            engine.ClassicFourWheel.GripBudget.BrakingSteeringLateralPriority,
            engine.ClassicFourWheel.GripBudget.BrakingSteeringFrontBrakeMultiplier,
            engine.ClassicFourWheel.GripBudget.BrakingSteeringRearBrakeMultiplier);
        Console.WriteLine("  summary columns: profile case decelPeak resultantPeak latPeak tActF/R tMinF/R removedReq% brakeForceAt0.5s pressF/R@0.5s fxF/fyF@0.5s usageF@0.5s yawRate/beta@0.5s yawMomentF/R pressureRecovered");

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

        Console.WriteLine("Classic brake-pressure / brake-steer priority audit complete.");
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
            DisableBrakePressureRegulatorForProbe = profile.DisableRegulator
        };
        simulator.BrakePressureReleaseRateOverrideForProbe = profile.ReleaseRate;
        simulator.BrakePressureMinimumRatioOverrideForProbe = profile.MinimumPressure;
        simulator.BrakingSteeringLateralPriorityOverrideForProbe = profile.LateralPriority;
        simulator.BrakingSteeringFrontBrakeMultiplierOverrideForProbe = profile.FrontBrakeMultiplier;
        simulator.BrakingSteeringRearBrakeMultiplierOverrideForProbe = profile.RearBrakeMultiplier;
        simulator.ServiceBrakeForceScaleForProbe = profile.ServiceBrakeForceScale;
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, auditCase.StartSpeedKmh / 3.6f);

        Accumulator acc = new(parameters, yawInertia, auditCase.SteeringOnsetSeconds);
        int ticks = Math.Max(1, (int)MathF.Round(auditCase.DurationSeconds / Dt));
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            VehicleInput input = auditCase.Input(time);
            simulator.Update(input, Dt);
            acc.Add(time, input, simulator.State);
        }

        return acc.ToResult();
    }

    private static void Print(AuditProfile profile, AuditCase auditCase, Result result)
    {
        Console.WriteLine(
            $"    {profile.Label,-14} {auditCase.Label,-15} " +
            $"{result.PeakDecelG,8:F2} {result.PeakResultantG,13:F2} {result.PeakLateralG,7:F2} " +
            $"{FormatTime(result.TimeFromSteerToFrontRegulatorActive),5}/{FormatTime(result.TimeFromSteerToRearRegulatorActive),5} " +
            $"{FormatTime(result.TimeFromSteerToFrontMinPressure),5}/{FormatTime(result.TimeFromSteerToRearMinPressure),5} " +
            $"{result.PeakRequestedBrakeRemovedFraction * 100f,10:F1}% {result.BrakeForceAtHalfSecondN,14:F0} " +
            $"{result.FrontPressureAtHalfSecond,5:F2}/{result.RearPressureAtHalfSecond,5:F2} " +
            $"{result.FrontLongitudinalForceAtHalfSecondN,6:F0}/{result.FrontLateralForceAtHalfSecondN,6:F0} " +
            $"{result.FrontUsageAtHalfSecond,6:F2} " +
            $"{result.YawRateAtHalfSecondDegreesPerSecond,6:F1}/{result.BetaAtHalfSecondDegrees,5:F2} " +
            $"{result.FrontYawMomentAtHalfSecondNm,7:F0}/{result.RearYawMomentAtHalfSecondNm,7:F0} " +
            $"{result.PressureRecovered}");
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

    private readonly record struct AuditCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        float SteeringOnsetSeconds,
        Func<float, VehicleInput> Input);

    private readonly record struct AuditProfile(
        string Label,
        bool DisableRegulator,
        float ReleaseRate,
        float MinimumPressure,
        float LateralPriority,
        float FrontBrakeMultiplier,
        float RearBrakeMultiplier,
        float ServiceBrakeForceScale);

    private readonly record struct Result(
        float PeakDecelG,
        float PeakResultantG,
        float PeakLateralG,
        float TimeFromSteerToFrontRegulatorActive,
        float TimeFromSteerToRearRegulatorActive,
        float TimeFromSteerToFrontMinPressure,
        float TimeFromSteerToRearMinPressure,
        float PeakRequestedBrakeRemovedFraction,
        float BrakeForceAtHalfSecondN,
        float FrontPressureAtHalfSecond,
        float RearPressureAtHalfSecond,
        float FrontLongitudinalForceAtHalfSecondN,
        float FrontLateralForceAtHalfSecondN,
        float FrontUsageAtHalfSecond,
        float YawRateAtHalfSecondDegreesPerSecond,
        float BetaAtHalfSecondDegrees,
        float FrontYawMomentAtHalfSecondNm,
        float RearYawMomentAtHalfSecondNm,
        bool PressureRecovered);

    private sealed class Accumulator
    {
        private readonly VehicleSimulationParameters _parameters;
        private readonly float _yawInertia;
        private readonly float _steeringOnsetSeconds;
        private float _peakDecelG;
        private float _peakResultantG;
        private float _peakLateralG;
        private float _frontActiveTime = float.NaN;
        private float _rearActiveTime = float.NaN;
        private float _frontMinTime = float.NaN;
        private float _rearMinTime = float.NaN;
        private float _peakRemovedFraction;
        private float _highestFrontPressureAfterMinimum;
        private bool _frontReachedMinimum;
        private Snapshot _halfSecond = new();

        public Accumulator(VehicleSimulationParameters parameters, float yawInertia, float steeringOnsetSeconds)
        {
            _parameters = parameters;
            _yawInertia = yawInertia;
            _steeringOnsetSeconds = steeringOnsetSeconds;
        }

        public void Add(float time, VehicleInput input, VehicleState state)
        {
            float decelG = MathF.Max(0f, -state.LongitudinalAcceleration / Gravity);
            float lateralG = MathF.Abs(state.LateralAcceleration) / Gravity;
            float resultantG = MathF.Sqrt(
                state.LongitudinalAcceleration * state.LongitudinalAcceleration +
                state.LateralAcceleration * state.LateralAcceleration) / Gravity;
            _peakDecelG = MathF.Max(_peakDecelG, decelG);
            _peakLateralG = MathF.Max(_peakLateralG, lateralG);
            _peakResultantG = MathF.Max(_peakResultantG, resultantG);

            float frontPressure = Average(state.FrontLeftBrakePressureRatio, state.FrontRightBrakePressureRatio);
            float rearPressure = Average(state.RearLeftBrakePressureRatio, state.RearRightBrakePressureRatio);
            bool frontActive = state.FrontLeftBrakePressureRegulatorActive || state.FrontRightBrakePressureRegulatorActive;
            bool rearActive = state.RearLeftBrakePressureRegulatorActive || state.RearRightBrakePressureRegulatorActive;
            if (time >= _steeringOnsetSeconds)
            {
                float elapsed = time - _steeringOnsetSeconds;
                if (!float.IsFinite(_frontActiveTime) && frontActive)
                {
                    _frontActiveTime = elapsed;
                }

                if (!float.IsFinite(_rearActiveTime) && rearActive)
                {
                    _rearActiveTime = elapsed;
                }

                if (!float.IsFinite(_frontMinTime) && frontPressure <= 0.105f)
                {
                    _frontMinTime = elapsed;
                    _frontReachedMinimum = true;
                }

                if (!float.IsFinite(_rearMinTime) && rearPressure <= 0.105f)
                {
                    _rearMinTime = elapsed;
                }

                if (_frontReachedMinimum)
                {
                    _highestFrontPressureAfterMinimum = MathF.Max(_highestFrontPressureAfterMinimum, frontPressure);
                }
            }

            float requestedBrakeForce = input.Brake * MathF.Max(0f, _parameters.MaxBrakeForceN);
            float actualBrakeForce = MathF.Abs(state.ClassicServiceBrakeForceRequestN);
            if (requestedBrakeForce > 1f)
            {
                _peakRemovedFraction = MathF.Max(
                    _peakRemovedFraction,
                    MathHelper.Clamp((requestedBrakeForce - actualBrakeForce) / requestedBrakeForce, 0f, 1f));
            }

            if (time <= 0.5f + Dt * 0.5f)
            {
                _halfSecond = Snapshot.From(state, frontPressure, rearPressure, _yawInertia);
            }
        }

        public Result ToResult()
        {
            return new Result(
                _peakDecelG,
                _peakResultantG,
                _peakLateralG,
                _frontActiveTime,
                _rearActiveTime,
                _frontMinTime,
                _rearMinTime,
                _peakRemovedFraction,
                _halfSecond.BrakeForceN,
                _halfSecond.FrontPressure,
                _halfSecond.RearPressure,
                _halfSecond.FrontLongitudinalForceN,
                _halfSecond.FrontLateralForceN,
                _halfSecond.FrontUsage,
                _halfSecond.YawRateDegreesPerSecond,
                _halfSecond.BetaDegrees,
                _halfSecond.FrontYawMomentNm,
                _halfSecond.RearYawMomentNm,
                _highestFrontPressureAfterMinimum > 0.2f);
        }
    }

    private readonly record struct Snapshot(
        float BrakeForceN,
        float FrontPressure,
        float RearPressure,
        float FrontLongitudinalForceN,
        float FrontLateralForceN,
        float FrontUsage,
        float YawRateDegreesPerSecond,
        float BetaDegrees,
        float FrontYawMomentNm,
        float RearYawMomentNm)
    {
        public static Snapshot From(VehicleState state, float frontPressure, float rearPressure, float yawInertia)
        {
            return new Snapshot(
                state.BrakeForce,
                frontPressure,
                rearPressure,
                state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN,
                state.FrontLeftLateralForceN + state.FrontRightLateralForceN,
                MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
                MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
                state.ClassicBodySlipAngleDegrees,
                MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * yawInertia,
                MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * yawInertia);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);
    }
}
