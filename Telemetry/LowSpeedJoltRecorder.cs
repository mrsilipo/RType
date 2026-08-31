using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using RType.Vehicle;

namespace RType.Telemetry;

public sealed class LowSpeedJoltRecorder
{
    private const float TriggerMinSpeedKmh = 6f;
    private const float TriggerMaxSpeedKmh = 14f;
    private const float PreTriggerSeconds = 0.5f;
    private const float PostTriggerSeconds = 0.5f;
    private const float ContactForceStepTriggerN = 3000f;
    private const float ContactYawStepTriggerNm = 2200f;
    private const float LateralForceStepTriggerN = 900f;
    private const float DriveSideForceStepTriggerN = 750f;
    private const float YawAccelerationStepTriggerDegreesPerSecondSquared = 90f;

    private readonly string _logDirectory = Path.Combine(Environment.CurrentDirectory, "Telemetry", "Jolts");
    private readonly Queue<JoltSample> _preTriggerSamples = new();
    private readonly List<JoltSample> _captureSamples = [];
    private JoltSample? _previous;
    private bool _capturing;
    private float _captureTimeRemaining;
    private int _captureIndex;

    public void Update(TimeSpan raceElapsed, float dt, VehicleInput input, VehicleState vehicle)
    {
        JoltSample sample = JoltSample.From(raceElapsed.TotalSeconds, dt, input, vehicle);

        while (_preTriggerSamples.Count > 0 &&
            sample.TimeSeconds - _preTriggerSamples.Peek().TimeSeconds > PreTriggerSeconds)
        {
            _preTriggerSamples.Dequeue();
        }

        if (_capturing)
        {
            _captureSamples.Add(sample);
            _captureTimeRemaining -= MathF.Max(0f, dt);
            if (_captureTimeRemaining <= 0f)
            {
                WriteCapture();
            }

            _previous = sample;
            return;
        }

        if (_previous is not null && IsTrigger(sample, _previous.Value))
        {
            _capturing = true;
            _captureTimeRemaining = PostTriggerSeconds;
            _captureSamples.Clear();
            _captureSamples.AddRange(_preTriggerSamples);
            _captureSamples.Add(sample);
        }

        _preTriggerSamples.Enqueue(sample);
        _previous = sample;
    }

    private static bool IsTrigger(JoltSample current, JoltSample previous)
    {
        if (current.SpeedKmh < TriggerMinSpeedKmh || current.SpeedKmh > TriggerMaxSpeedKmh)
        {
            return false;
        }

        if (MathF.Abs(current.SteerInput) < 0.55f && MathF.Abs(current.RoadWheelAngleDegrees) < 5f)
        {
            return false;
        }

        return
            MathF.Abs(current.RollingContactForceN - previous.RollingContactForceN) >= ContactForceStepTriggerN ||
            MathF.Abs(current.RollingContactYawMomentNm - previous.RollingContactYawMomentNm) >= ContactYawStepTriggerNm ||
            MathF.Abs(current.FinalLateralForceN - previous.FinalLateralForceN) >= LateralForceStepTriggerN ||
            MathF.Abs(current.FrontDriveSideForceN - previous.FrontDriveSideForceN) >= DriveSideForceStepTriggerN ||
            MathF.Abs(current.YawAccelerationDegreesPerSecondSquared - previous.YawAccelerationDegreesPerSecondSquared) >= YawAccelerationStepTriggerDegreesPerSecondSquared;
    }

    private void WriteCapture()
    {
        Directory.CreateDirectory(_logDirectory);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string path = Path.Combine(_logDirectory, $"{timestamp}_low_speed_jolt_{_captureIndex++:000}.csv");
        using StreamWriter writer = new(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(',',
        [
            "time_s",
            "dt_s",
            "speed_kmh",
            "signed_forward_speed_mps",
            "steer_input",
            "throttle_input",
            "brake_input",
            "reverse_input",
            "gear",
            "rpm",
            "displayed_rpm",
            "clutch_engagement",
            "clutch_is_locked",
            "classic_drive_force_request_n",
            "classic_engine_brake_force_request_n",
            "classic_service_brake_force_request_n",
            "limiter_torque_multiplier",
            "rev_limiter_active",
            "road_wheel_deg",
            "yaw_rate_deg_s",
            "yaw_accel_deg_s2",
            "beta_deg",
            "fl_local_u",
            "fr_local_u",
            "rl_local_u",
            "rr_local_u",
            "fl_local_v",
            "fr_local_v",
            "rl_local_v",
            "rr_local_v",
            "fl_slip_deg",
            "fr_slip_deg",
            "rl_slip_deg",
            "rr_slip_deg",
            "fl_slip_target_fy_n",
            "fr_slip_target_fy_n",
            "rl_slip_target_fy_n",
            "rr_slip_target_fy_n",
            "fl_rolling_constraint_fy_n",
            "fr_rolling_constraint_fy_n",
            "rl_rolling_constraint_fy_n",
            "rr_rolling_constraint_fy_n",
            "fl_rolling_blend",
            "fr_rolling_blend",
            "rl_rolling_blend",
            "rr_rolling_blend",
            "fl_low_speed_scale",
            "fr_low_speed_scale",
            "rl_low_speed_scale",
            "rr_low_speed_scale",
            "fl_target_fy_n",
            "fr_target_fy_n",
            "rl_target_fy_n",
            "rr_target_fy_n",
            "fl_relaxed_fy_n",
            "fr_relaxed_fy_n",
            "rl_relaxed_fy_n",
            "rr_relaxed_fy_n",
            "fl_final_fy_n",
            "fr_final_fy_n",
            "rl_final_fy_n",
            "rr_final_fy_n",
            "fl_local_right_force_n",
            "fr_local_right_force_n",
            "rl_local_right_force_n",
            "rr_local_right_force_n",
            "fl_drive_side_force_n",
            "fr_drive_side_force_n",
            "rl_drive_side_force_n",
            "rr_drive_side_force_n",
            "fl_requested_longitudinal_force_n",
            "fr_requested_longitudinal_force_n",
            "rl_requested_longitudinal_force_n",
            "rr_requested_longitudinal_force_n",
            "fl_rolling_contact_fy_n",
            "fr_rolling_contact_fy_n",
            "rl_rolling_contact_fy_n",
            "rr_rolling_contact_fy_n",
            "fl_rolling_contact_yaw_nm",
            "fr_rolling_contact_yaw_nm",
            "rl_rolling_contact_yaw_nm",
            "rr_rolling_contact_yaw_nm",
            "front_yaw_accel_deg_s2",
            "rear_yaw_accel_deg_s2",
            "natural_yaw_accel_deg_s2"
        ]));

        foreach (JoltSample sample in _captureSamples)
        {
            writer.WriteLine(sample.ToCsv());
        }

        _captureSamples.Clear();
        _capturing = false;
    }

    private readonly record struct JoltSample(
        double TimeSeconds,
        float DtSeconds,
        float SpeedKmh,
        float SignedForwardSpeedMetersPerSecond,
        float SteerInput,
        float ThrottleInput,
        float BrakeInput,
        float ReverseInput,
        int Gear,
        float Rpm,
        float DisplayedRpm,
        float ClutchEngagement,
        bool ClutchIsLocked,
        float ClassicDriveForceRequestN,
        float ClassicEngineBrakeForceRequestN,
        float ClassicServiceBrakeForceRequestN,
        float LimiterTorqueMultiplier,
        bool RevLimiterActive,
        float RoadWheelAngleDegrees,
        float YawRateDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float BetaDegrees,
        float FlLocalU,
        float FrLocalU,
        float RlLocalU,
        float RrLocalU,
        float FlLocalV,
        float FrLocalV,
        float RlLocalV,
        float RrLocalV,
        float FlSlipDegrees,
        float FrSlipDegrees,
        float RlSlipDegrees,
        float RrSlipDegrees,
        float FlSlipTargetFyN,
        float FrSlipTargetFyN,
        float RlSlipTargetFyN,
        float RrSlipTargetFyN,
        float FlRollingConstraintFyN,
        float FrRollingConstraintFyN,
        float RlRollingConstraintFyN,
        float RrRollingConstraintFyN,
        float FlRollingBlend,
        float FrRollingBlend,
        float RlRollingBlend,
        float RrRollingBlend,
        float FlLowSpeedScale,
        float FrLowSpeedScale,
        float RlLowSpeedScale,
        float RrLowSpeedScale,
        float FlTargetFyN,
        float FrTargetFyN,
        float RlTargetFyN,
        float RrTargetFyN,
        float FlRelaxedFyN,
        float FrRelaxedFyN,
        float RlRelaxedFyN,
        float RrRelaxedFyN,
        float FlFinalFyN,
        float FrFinalFyN,
        float RlFinalFyN,
        float RrFinalFyN,
        float FlLocalRightForceN,
        float FrLocalRightForceN,
        float RlLocalRightForceN,
        float RrLocalRightForceN,
        float FlDriveSideForceN,
        float FrDriveSideForceN,
        float RlDriveSideForceN,
        float RrDriveSideForceN,
        float FlRequestedLongitudinalForceN,
        float FrRequestedLongitudinalForceN,
        float RlRequestedLongitudinalForceN,
        float RrRequestedLongitudinalForceN,
        float FlRollingContactFyN,
        float FrRollingContactFyN,
        float RlRollingContactFyN,
        float RrRollingContactFyN,
        float FlRollingContactYawNm,
        float FrRollingContactYawNm,
        float RlRollingContactYawNm,
        float RrRollingContactYawNm,
        float FrontYawAccelerationDegreesPerSecondSquared,
        float RearYawAccelerationDegreesPerSecondSquared,
        float NaturalYawAccelerationDegreesPerSecondSquared)
    {
        public float RollingContactForceN =>
            MathF.Abs(FlRollingContactFyN) + MathF.Abs(FrRollingContactFyN) +
            MathF.Abs(RlRollingContactFyN) + MathF.Abs(RrRollingContactFyN);

        public float RollingContactYawMomentNm =>
            MathF.Abs(FlRollingContactYawNm) + MathF.Abs(FrRollingContactYawNm) +
            MathF.Abs(RlRollingContactYawNm) + MathF.Abs(RrRollingContactYawNm);

        public float FinalLateralForceN =>
            MathF.Abs(FlFinalFyN) + MathF.Abs(FrFinalFyN) +
            MathF.Abs(RlFinalFyN) + MathF.Abs(RrFinalFyN);

        public float FrontDriveSideForceN => MathF.Abs(FlDriveSideForceN) + MathF.Abs(FrDriveSideForceN);

        public static JoltSample From(double timeSeconds, float dt, VehicleInput input, VehicleState vehicle)
        {
            return new JoltSample(
                timeSeconds,
                dt,
                vehicle.SpeedMetersPerSecond * 3.6f,
                vehicle.SignedForwardSpeed,
                input.Steer,
                input.Throttle,
                input.Brake,
                input.Reverse,
                vehicle.Gear,
                vehicle.Rpm,
                vehicle.DisplayedRpm,
                vehicle.ClutchEngagement,
                vehicle.ClutchIsLocked,
                vehicle.ClassicDriveForceRequestN,
                vehicle.ClassicEngineBrakeForceRequestN,
                vehicle.ClassicServiceBrakeForceRequestN,
                vehicle.LimiterTorqueMultiplier,
                vehicle.RevLimiterActive,
                (vehicle.FrontLeftSteerAngleDegrees + vehicle.FrontRightSteerAngleDegrees) * 0.5f,
                MathHelper.ToDegrees(vehicle.YawRateRadiansPerSecond),
                vehicle.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
                vehicle.ClassicBodySlipAngleDegrees,
                vehicle.FrontLeftLocalForwardSpeedMetersPerSecond,
                vehicle.FrontRightLocalForwardSpeedMetersPerSecond,
                vehicle.RearLeftLocalForwardSpeedMetersPerSecond,
                vehicle.RearRightLocalForwardSpeedMetersPerSecond,
                vehicle.FrontLeftLocalLateralSpeedMetersPerSecond,
                vehicle.FrontRightLocalLateralSpeedMetersPerSecond,
                vehicle.RearLeftLocalLateralSpeedMetersPerSecond,
                vehicle.RearRightLocalLateralSpeedMetersPerSecond,
                vehicle.FrontLeftSlipAngleDegrees,
                vehicle.FrontRightSlipAngleDegrees,
                vehicle.RearLeftSlipAngleDegrees,
                vehicle.RearRightSlipAngleDegrees,
                vehicle.FrontLeftLowSpeedSlipLateralForceN,
                vehicle.FrontRightLowSpeedSlipLateralForceN,
                vehicle.RearLeftLowSpeedSlipLateralForceN,
                vehicle.RearRightLowSpeedSlipLateralForceN,
                vehicle.FrontLeftLowSpeedRollingConstraintForceN,
                vehicle.FrontRightLowSpeedRollingConstraintForceN,
                vehicle.RearLeftLowSpeedRollingConstraintForceN,
                vehicle.RearRightLowSpeedRollingConstraintForceN,
                vehicle.FrontLeftLowSpeedRollingBlend,
                vehicle.FrontRightLowSpeedRollingBlend,
                vehicle.RearLeftLowSpeedRollingBlend,
                vehicle.RearRightLowSpeedRollingBlend,
                vehicle.FrontLeftLowSpeedLateralForceScale,
                vehicle.FrontRightLowSpeedLateralForceScale,
                vehicle.RearLeftLowSpeedLateralForceScale,
                vehicle.RearRightLowSpeedLateralForceScale,
                vehicle.FrontLeftRequestedLateralForceN,
                vehicle.FrontRightRequestedLateralForceN,
                vehicle.RearLeftRequestedLateralForceN,
                vehicle.RearRightRequestedLateralForceN,
                vehicle.FrontLeftRelaxedLateralForceN,
                vehicle.FrontRightRelaxedLateralForceN,
                vehicle.RearLeftRelaxedLateralForceN,
                vehicle.RearRightRelaxedLateralForceN,
                vehicle.FrontLeftLowSpeedFinalLateralForceN,
                vehicle.FrontRightLowSpeedFinalLateralForceN,
                vehicle.RearLeftLowSpeedFinalLateralForceN,
                vehicle.RearRightLowSpeedFinalLateralForceN,
                vehicle.FrontLeftLateralForceN,
                vehicle.FrontRightLateralForceN,
                vehicle.RearLeftLateralForceN,
                vehicle.RearRightLateralForceN,
                CalculateDriveSideForce(vehicle.FrontLeftLateralForceN, vehicle.FrontLeftLowSpeedFinalLateralForceN, vehicle.FrontLeftSteerAngleDegrees),
                CalculateDriveSideForce(vehicle.FrontRightLateralForceN, vehicle.FrontRightLowSpeedFinalLateralForceN, vehicle.FrontRightSteerAngleDegrees),
                vehicle.RearLeftLateralForceN - vehicle.RearLeftLowSpeedFinalLateralForceN,
                vehicle.RearRightLateralForceN - vehicle.RearRightLowSpeedFinalLateralForceN,
                vehicle.FrontLeftRequestedLongitudinalForceN,
                vehicle.FrontRightRequestedLongitudinalForceN,
                vehicle.RearLeftRequestedLongitudinalForceN,
                vehicle.RearRightRequestedLongitudinalForceN,
                vehicle.FrontLeftLowSpeedRollingContactForceN,
                vehicle.FrontRightLowSpeedRollingContactForceN,
                vehicle.RearLeftLowSpeedRollingContactForceN,
                vehicle.RearRightLowSpeedRollingContactForceN,
                vehicle.FrontLeftLowSpeedRollingContactYawMomentNm,
                vehicle.FrontRightLowSpeedRollingContactYawMomentNm,
                vehicle.RearLeftLowSpeedRollingContactYawMomentNm,
                vehicle.RearRightLowSpeedRollingContactYawMomentNm,
                vehicle.ClassicFrontYawAccelerationDegreesPerSecondSquared,
                vehicle.ClassicRearYawAccelerationDegreesPerSecondSquared,
                vehicle.ClassicNaturalYawAccelerationDegreesPerSecondSquared);
        }

        public string ToCsv()
        {
            return string.Join(',',
            [
                F(TimeSeconds),
                F(DtSeconds),
                F(SpeedKmh),
                F(SignedForwardSpeedMetersPerSecond),
                F(SteerInput),
                F(ThrottleInput),
                F(BrakeInput),
                F(ReverseInput),
                Gear.ToString(CultureInfo.InvariantCulture),
                F(Rpm),
                F(DisplayedRpm),
                F(ClutchEngagement),
                ClutchIsLocked ? "1" : "0",
                F(ClassicDriveForceRequestN),
                F(ClassicEngineBrakeForceRequestN),
                F(ClassicServiceBrakeForceRequestN),
                F(LimiterTorqueMultiplier),
                RevLimiterActive ? "1" : "0",
                F(RoadWheelAngleDegrees),
                F(YawRateDegreesPerSecond),
                F(YawAccelerationDegreesPerSecondSquared),
                F(BetaDegrees),
                F(FlLocalU),
                F(FrLocalU),
                F(RlLocalU),
                F(RrLocalU),
                F(FlLocalV),
                F(FrLocalV),
                F(RlLocalV),
                F(RrLocalV),
                F(FlSlipDegrees),
                F(FrSlipDegrees),
                F(RlSlipDegrees),
                F(RrSlipDegrees),
                F(FlSlipTargetFyN),
                F(FrSlipTargetFyN),
                F(RlSlipTargetFyN),
                F(RrSlipTargetFyN),
                F(FlRollingConstraintFyN),
                F(FrRollingConstraintFyN),
                F(RlRollingConstraintFyN),
                F(RrRollingConstraintFyN),
                F(FlRollingBlend),
                F(FrRollingBlend),
                F(RlRollingBlend),
                F(RrRollingBlend),
                F(FlLowSpeedScale),
                F(FrLowSpeedScale),
                F(RlLowSpeedScale),
                F(RrLowSpeedScale),
                F(FlTargetFyN),
                F(FrTargetFyN),
                F(RlTargetFyN),
                F(RrTargetFyN),
                F(FlRelaxedFyN),
                F(FrRelaxedFyN),
                F(RlRelaxedFyN),
                F(RrRelaxedFyN),
                F(FlFinalFyN),
                F(FrFinalFyN),
                F(RlFinalFyN),
                F(RrFinalFyN),
                F(FlLocalRightForceN),
                F(FrLocalRightForceN),
                F(RlLocalRightForceN),
                F(RrLocalRightForceN),
                F(FlDriveSideForceN),
                F(FrDriveSideForceN),
                F(RlDriveSideForceN),
                F(RrDriveSideForceN),
                F(FlRequestedLongitudinalForceN),
                F(FrRequestedLongitudinalForceN),
                F(RlRequestedLongitudinalForceN),
                F(RrRequestedLongitudinalForceN),
                F(FlRollingContactFyN),
                F(FrRollingContactFyN),
                F(RlRollingContactFyN),
                F(RrRollingContactFyN),
                F(FlRollingContactYawNm),
                F(FrRollingContactYawNm),
                F(RlRollingContactYawNm),
                F(RrRollingContactYawNm),
                F(FrontYawAccelerationDegreesPerSecondSquared),
                F(RearYawAccelerationDegreesPerSecondSquared),
                F(NaturalYawAccelerationDegreesPerSecondSquared)
            ]);
        }

        private static float CalculateDriveSideForce(float localRightForceN, float wheelLateralForceN, float steerAngleDegrees)
        {
            float steerRadians = MathHelper.ToRadians(steerAngleDegrees);
            return localRightForceN - wheelLateralForceN * MathF.Cos(steerRadians);
        }

        private static string F(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static string F(double value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }
}
