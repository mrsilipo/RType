using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using RType.Vehicle;
using RType.World;

namespace RType.Telemetry;

public sealed class RaceRunTelemetryLogger : IDisposable
{
    private const double SampleIntervalSeconds = 1.0 / 60.0;
    private const int WriterBufferBytes = 1024 * 1024;
    private const int FlushIntervalSamples = 0;
    private static readonly char[] CsvSpecialChars = [',', '"', '\r', '\n'];
    private readonly string _logDirectory;
    private readonly StringBuilder _rowBuilder = new(4096);
    private StreamWriter? _writer;
    private double _nextSampleSeconds;
    private int _sampleCount;

    public RaceRunTelemetryLogger()
    {
        _logDirectory = Path.Combine(Environment.CurrentDirectory, "Telemetry", "RaceRuns");
    }

    public string? CurrentPath { get; private set; }

    public void Start(
        string vehiclePath,
        VehicleState vehicle,
        TrackDefinition track,
        bool reverse,
        bool rtypeEnginePowerEnabled = false)
    {
        Stop();

        Directory.CreateDirectory(_logDirectory);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string car = SanitizeFilePart(vehicle.VehicleName);
        string trackName = SanitizeFilePart(track.DisplayName);
        string direction = reverse ? "reverse" : "normal";
        string transmission = vehicle.IsManualTransmission ? "manual" : "automatic";
        CurrentPath = Path.Combine(_logDirectory, $"{timestamp}_{car}_{trackName}_{direction}_{transmission}.csv");

        _writer = new StreamWriter(CurrentPath, false, Encoding.UTF8, WriterBufferBytes);
        _writer.WriteLine($"# vehiclePath,{Escape(vehiclePath)}");
        _writer.WriteLine($"# vehicle,{Escape(vehicle.VehicleName)}");
        _writer.WriteLine($"# track,{Escape(track.DisplayName)}");
        _writer.WriteLine($"# direction,{direction}");
        _writer.WriteLine($"# transmission,{transmission}");
        _writer.WriteLine($"# rtypeEnginePower,{(rtypeEnginePowerEnabled ? 1 : 0)}");
        _writer.WriteLine("# sampleRateHz,60");
        _writer.WriteLine(string.Join(',',
        [
            "time_s",
            "dt_s",
            "vehicle",
            "transmission",
            "gear",
            "rpm",
            "displayed_rpm",
            "previous_physics_rpm",
            "physics_tick_alpha",
            "rev_limiter_active",
            "clutch_slip_rpm",
            "speed_kph",
            "forward_speed_ms",
            "lateral_speed_ms",
            "longitudinal_accel_ms2",
            "lateral_accel_ms2",
            "yaw_rate_deg_s",
            "heading_deg",
            "position_x_m",
            "position_z_m",
            "throttle_input",
            "effective_throttle",
            "brake_input",
            "brake_applied",
            "handbrake_input",
            "steer_input",
            "steer_applied",
            "steering_speed_matched_max_angle_deg",
            "front_left_steer_angle_deg",
            "front_right_steer_angle_deg",
            "surface",
            "surface_grip",
            "drive_force_n",
            "brake_force_n",
            "front_brake_torque_nm",
            "rear_brake_torque_nm",
            "engine_brake_torque_nm",
            "classic_engine_brake_force_n",
            "classic_service_brake_force_n",
            "classic_front_longitudinal_grip_usage",
            "classic_front_lateral_grip_usage",
            "classic_rear_longitudinal_grip_usage",
            "classic_rear_lateral_grip_usage",
            "classic_body_slip_angle_deg",
            "classic_front_yaw_accel_deg_s2",
            "classic_rear_yaw_accel_deg_s2",
            "classic_natural_yaw_accel_deg_s2",
            "classic_yaw_damping_accel_deg_s2",
            "classic_yaw_recovery_accel_deg_s2",
            "classic_rear_follow_accel_deg_s2",
            "classic_body_slip_damping_force_n",
            "classic_cornering_cleanup_speed_retention_force_n",
            "engine_power_unit_active",
            "engine_power_unit_drive_torque_nm",
            "engine_power_unit_engine_drive_torque_nm",
            "engine_power_unit_raw_torque_nm",
            "engine_power_unit_vtec_blend",
            "engine_power_unit_vtec_kick",
            "engine_power_unit_load",
            "engine_power_unit_fuel_cut",
            "engine_power_unit_crank_rpm",
            "engine_power_unit_crank_phase_deg",
            "engine_power_unit_transmission_rpm",
            "engine_power_unit_clutch_torque_nm",
            "engine_power_unit_crank_friction_nm",
            "engine_power_unit_reference_drive_torque_nm",
            "engine_power_unit_calibrated_drive_torque_nm",
            "engine_power_unit_gas_authority",
            "engine_power_unit_full_throttle_gas_torque_nm",
            "abs_active",
            "locked_wheels",
            "fl_surface",
            "fr_surface",
            "rl_surface",
            "rr_surface",
            "fl_surface_grip",
            "fr_surface_grip",
            "rl_surface_grip",
            "rr_surface_grip",
            "fl_load_n",
            "fr_load_n",
            "rl_load_n",
            "rr_load_n",
            "fl_grip_usage",
            "fr_grip_usage",
            "rl_grip_usage",
            "rr_grip_usage",
            "fl_slip_ratio",
            "fr_slip_ratio",
            "rl_slip_ratio",
            "rr_slip_ratio",
            "fl_slip_angle_deg",
            "fr_slip_angle_deg",
            "rl_slip_angle_deg",
            "rr_slip_angle_deg",
            "fl_long_force_n",
            "fr_long_force_n",
            "rl_long_force_n",
            "rr_long_force_n",
            "fl_requested_long_force_n",
            "fr_requested_long_force_n",
            "rl_requested_long_force_n",
            "rr_requested_long_force_n",
            "fl_lat_force_n",
            "fr_lat_force_n",
            "rl_lat_force_n",
            "rr_lat_force_n",
            "fl_camber_deg",
            "fr_camber_deg",
            "rl_camber_deg",
            "rr_camber_deg",
            "fl_toe_deg",
            "fr_toe_deg",
            "rl_toe_deg",
            "rr_toe_deg",
            "body_pitch_deg",
            "body_roll_deg",
            "wall_contacts",
            "last_impact_kph",
            "crash_severity"
        ]));

        _nextSampleSeconds = 0.0;
        _sampleCount = 0;
    }

    public void Log(TimeSpan raceElapsed, float dt, VehicleInput input, VehicleState vehicle)
    {
        if (_writer is null)
        {
            return;
        }

        double timeSeconds = raceElapsed.TotalSeconds;
        if (timeSeconds + 0.0001 < _nextSampleSeconds)
        {
            return;
        }

        _nextSampleSeconds = timeSeconds + SampleIntervalSeconds;
        _sampleCount++;

        WriteDataRow(timeSeconds, dt, input, vehicle);

        if (FlushIntervalSamples > 0 && _sampleCount % FlushIntervalSamples == 0)
        {
            _writer.Flush();
        }
    }

    public void Stop()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _nextSampleSeconds = 0.0;
        _sampleCount = 0;
    }

    public void Dispose()
    {
        Stop();
    }

    private void WriteDataRow(double timeSeconds, float dt, VehicleInput input, VehicleState vehicle)
    {
        _rowBuilder.Clear();
        Append(timeSeconds);
        Append(dt);
        Append(vehicle.VehicleName);
        Append(vehicle.TransmissionModeName);
        Append(vehicle.Gear);
        Append(vehicle.Rpm);
        Append(vehicle.DisplayedRpm);
        Append(vehicle.PreviousPhysicsRpm);
        Append(vehicle.PhysicsTickAlpha);
        Append(vehicle.RevLimiterActive);
        Append(vehicle.ClutchSlipRpm);
        Append(vehicle.SpeedMetersPerSecond * 3.6f);
        Append(vehicle.SignedForwardSpeed);
        Append(vehicle.LateralSpeed);
        Append(vehicle.LongitudinalAcceleration);
        Append(vehicle.LateralAcceleration);
        Append(MathHelper.ToDegrees(vehicle.YawRateRadiansPerSecond));
        Append(MathHelper.ToDegrees(vehicle.HeadingRadians));
        Append(vehicle.Position.X);
        Append(vehicle.Position.Z);
        Append(input.Throttle);
        Append(vehicle.EffectiveThrottle);
        Append(input.Brake);
        Append(vehicle.Brake);
        Append(input.Handbrake);
        Append(input.Steer);
        Append(vehicle.Steer);
        Append(vehicle.SteeringSpeedMatchedMaxAngleDegrees);
        Append(vehicle.FrontLeftSteerAngleDegrees);
        Append(vehicle.FrontRightSteerAngleDegrees);
        Append(vehicle.SurfaceName);
        Append(vehicle.SurfaceGrip);
        Append(vehicle.DriveForce);
        Append(vehicle.BrakeForce);
        Append(vehicle.FrontBrakeTorqueNm);
        Append(vehicle.RearBrakeTorqueNm);
        Append(vehicle.EngineBrakeTorqueNm);
        Append(vehicle.ClassicEngineBrakeForceRequestN);
        Append(vehicle.ClassicServiceBrakeForceRequestN);
        Append(vehicle.ClassicFrontLongitudinalGripUsage);
        Append(vehicle.ClassicFrontLateralGripUsage);
        Append(vehicle.ClassicRearLongitudinalGripUsage);
        Append(vehicle.ClassicRearLateralGripUsage);
        Append(vehicle.ClassicBodySlipAngleDegrees);
        Append(vehicle.ClassicFrontYawAccelerationDegreesPerSecondSquared);
        Append(vehicle.ClassicRearYawAccelerationDegreesPerSecondSquared);
        Append(vehicle.ClassicNaturalYawAccelerationDegreesPerSecondSquared);
        Append(vehicle.ClassicYawDampingAccelerationDegreesPerSecondSquared);
        Append(vehicle.ClassicYawRecoveryAccelerationDegreesPerSecondSquared);
        Append(vehicle.ClassicRearFollowAccelerationDegreesPerSecondSquared);
        Append(vehicle.ClassicBodySlipDampingForceN);
        Append(vehicle.ClassicCorneringCleanupSpeedRetentionForceN);
        Append(vehicle.EnginePowerUnitActive);
        Append(vehicle.EnginePowerUnitDriveTorqueNm);
        Append(vehicle.EnginePowerUnitEngineDriveTorqueNm);
        Append(vehicle.EnginePowerUnitRawTorqueNm);
        Append(vehicle.EnginePowerUnitVtecBlend);
        Append(vehicle.EnginePowerUnitVtecKickIntensity);
        Append(vehicle.EnginePowerUnitLoad);
        Append(vehicle.EnginePowerUnitFuelCutBlend);
        Append(vehicle.EnginePowerUnitCrankRpm);
        Append(vehicle.EnginePowerUnitCrankPhaseDegrees);
        Append(vehicle.EnginePowerUnitTransmissionRpm);
        Append(vehicle.EnginePowerUnitClutchTorqueNm);
        Append(vehicle.EnginePowerUnitCrankFrictionTorqueNm);
        Append(vehicle.EnginePowerUnitReferenceDriveTorqueNm);
        Append(vehicle.EnginePowerUnitCalibratedDriveTorqueNm);
        Append(vehicle.EnginePowerUnitGasAuthority);
        Append(vehicle.EnginePowerUnitFullThrottleGasTorqueNm);
        Append(vehicle.AbsActive);
        Append(vehicle.LockedWheelCount);
        Append(vehicle.FrontLeftSurfaceName);
        Append(vehicle.FrontRightSurfaceName);
        Append(vehicle.RearLeftSurfaceName);
        Append(vehicle.RearRightSurfaceName);
        Append(vehicle.FrontLeftSurfaceGrip);
        Append(vehicle.FrontRightSurfaceGrip);
        Append(vehicle.RearLeftSurfaceGrip);
        Append(vehicle.RearRightSurfaceGrip);
        Append(vehicle.FrontLeftLoadN);
        Append(vehicle.FrontRightLoadN);
        Append(vehicle.RearLeftLoadN);
        Append(vehicle.RearRightLoadN);
        Append(vehicle.FrontLeftGripUsage);
        Append(vehicle.FrontRightGripUsage);
        Append(vehicle.RearLeftGripUsage);
        Append(vehicle.RearRightGripUsage);
        Append(vehicle.FrontLeftSlipRatio);
        Append(vehicle.FrontRightSlipRatio);
        Append(vehicle.RearLeftSlipRatio);
        Append(vehicle.RearRightSlipRatio);
        Append(vehicle.FrontLeftSlipAngleDegrees);
        Append(vehicle.FrontRightSlipAngleDegrees);
        Append(vehicle.RearLeftSlipAngleDegrees);
        Append(vehicle.RearRightSlipAngleDegrees);
        Append(vehicle.FrontLeftLongitudinalForceN);
        Append(vehicle.FrontRightLongitudinalForceN);
        Append(vehicle.RearLeftLongitudinalForceN);
        Append(vehicle.RearRightLongitudinalForceN);
        Append(vehicle.FrontLeftRequestedLongitudinalForceN);
        Append(vehicle.FrontRightRequestedLongitudinalForceN);
        Append(vehicle.RearLeftRequestedLongitudinalForceN);
        Append(vehicle.RearRightRequestedLongitudinalForceN);
        Append(vehicle.FrontLeftLateralForceN);
        Append(vehicle.FrontRightLateralForceN);
        Append(vehicle.RearLeftLateralForceN);
        Append(vehicle.RearRightLateralForceN);
        Append(vehicle.FrontLeftCamberDegrees);
        Append(vehicle.FrontRightCamberDegrees);
        Append(vehicle.RearLeftCamberDegrees);
        Append(vehicle.RearRightCamberDegrees);
        Append(vehicle.FrontLeftToeDegrees);
        Append(vehicle.FrontRightToeDegrees);
        Append(vehicle.RearLeftToeDegrees);
        Append(vehicle.RearRightToeDegrees);
        Append(MathHelper.ToDegrees(vehicle.BodyPitchRadians));
        Append(MathHelper.ToDegrees(vehicle.BodyRollRadians));
        Append(vehicle.WallContactCount);
        Append(vehicle.LastImpactSpeedKph);
        Append(vehicle.CrashSeverity);

        _writer!.Write(_rowBuilder);
        _writer.WriteLine();
    }

    private void AppendSeparator()
    {
        if (_rowBuilder.Length > 0)
        {
            _rowBuilder.Append(',');
        }
    }

    private void Append(float value)
    {
        AppendSeparator();
        Span<char> chars = stackalloc char[32];
        if (value.TryFormat(chars, out int written, "0.#####", CultureInfo.InvariantCulture))
        {
            _rowBuilder.Append(chars[..written]);
            return;
        }

        _rowBuilder.Append(value.ToString("0.#####", CultureInfo.InvariantCulture));
    }

    private void Append(double value)
    {
        AppendSeparator();
        Span<char> chars = stackalloc char[32];
        if (value.TryFormat(chars, out int written, "0.#####", CultureInfo.InvariantCulture))
        {
            _rowBuilder.Append(chars[..written]);
            return;
        }

        _rowBuilder.Append(value.ToString("0.#####", CultureInfo.InvariantCulture));
    }

    private void Append(int value)
    {
        AppendSeparator();
        _rowBuilder.Append(value);
    }

    private void Append(bool value)
    {
        AppendSeparator();
        _rowBuilder.Append(value ? '1' : '0');
    }

    private void Append(string value)
    {
        AppendSeparator();
        if (value.IndexOfAny(CsvSpecialChars) < 0)
        {
            _rowBuilder.Append(value);
            return;
        }

        _rowBuilder.Append('"');
        foreach (char character in value)
        {
            if (character == '"')
            {
                _rowBuilder.Append('"');
            }

            _rowBuilder.Append(character);
        }

        _rowBuilder.Append('"');
    }

    private static string F(float value)
    {
        return value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    private static string F(double value)
    {
        return value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    private static string B(bool value)
    {
        return value ? "1" : "0";
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(CsvSpecialChars) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string SanitizeFilePart(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]) || char.IsWhiteSpace(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return chars.Length == 0 ? "run" : new string(chars);
    }
}
