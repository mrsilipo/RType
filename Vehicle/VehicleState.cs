using Microsoft.Xna.Framework;

namespace RetroRacer.Vehicle;

public sealed class VehicleState
{
    public string VehicleName { get; set; } = "Prototype Car";

    public float RedlineRpm { get; set; } = 6800f;

    public Vector3 Position { get; set; }

    public float HeadingRadians { get; set; }

    public Vector2 Velocity { get; set; }

    public float YawRateRadiansPerSecond { get; set; }

    public int Gear { get; set; } = 1;

    public float Rpm { get; set; } = 900f;

    public float PreviousPhysicsRpm { get; set; } = 900f;

    public float PhysicsTickAlpha { get; set; }

    public float DisplayedRpm { get; set; } = 900f;

    public float DisplayedRpmTarget { get; set; } = 900f;

    public float DisplayedRpmVelocity { get; set; }

    public bool RevLimiterActive { get; set; }

    public float RevLimiterBounceIntensity { get; set; }

    public float LimiterTorqueMultiplier { get; set; } = 1f;

    public bool IsShifting { get; set; }

    public float ShiftTimeRemainingSeconds { get; set; }

    public float ShiftKickIntensity { get; set; }

    public int LastCompletedShiftFromGear { get; set; }

    public int LastCompletedShiftToGear { get; set; }

    public float LastCompletedShiftKickSeverity { get; set; }

    public float ClutchSlipRpm { get; set; }

    public bool MechanicalOverRevActive { get; set; }

    public float MechanicalOverRevRpm { get; set; }

    public float MechanicalOverRevSeverity { get; set; }

    public float PowertrainShockIntensity { get; set; }

    public float CounterSteerRecoveryIntensity { get; set; }

    public float SignedForwardSpeed { get; set; }

    public float LateralSpeed { get; set; }

    public float LongitudinalAcceleration { get; set; }

    public float LateralAcceleration { get; set; }

    public float SurfaceGrip { get; set; } = 1f;

    public string SurfaceName { get; set; } = "ROAD";

    public float Throttle { get; set; }

    public float EffectiveThrottle { get; set; }

    public float Brake { get; set; }

    public float Handbrake { get; set; }

    public float Steer { get; set; }

    public float FrontLeftSteerAngleDegrees { get; set; }

    public float FrontRightSteerAngleDegrees { get; set; }

    public float DriveForce { get; set; }

    public float BrakeForce { get; set; }

    public float FrontBrakeTorqueNm { get; set; }

    public float RearBrakeTorqueNm { get; set; }

    public float EngineBrakeTorqueNm { get; set; }

    public bool EngineSimulatorPowerActive { get; set; }

    public float EngineSimulatorDriveTorqueNm { get; set; }

    public float EngineSimulatorEngineDriveTorqueNm { get; set; }

    public float EngineSimulatorRawTorqueNm { get; set; }

    public float EngineSimulatorVtecBlend { get; set; }

    public float EngineSimulatorVtecKickIntensity { get; set; }

    public float EngineSimulatorLoad { get; set; }

    public float EngineSimulatorFuelCutBlend { get; set; }

    public float EngineSimulatorCrankRpm { get; set; }

    public float EngineSimulatorTransmissionRpm { get; set; }

    public float EngineSimulatorClutchTorqueNm { get; set; }

    public float EngineSimulatorCrankFrictionTorqueNm { get; set; }

    public bool AbsActive { get; set; }

    public int LockedWheelCount { get; set; }

    public float BodyPitchRadians { get; set; }

    public float BodyRollRadians { get; set; }

    public float GroundPitchRadians { get; set; }

    public float GroundRollRadians { get; set; }

    public float WheelContactCenterHeightMeters { get; set; }

    public float BodyPivotHeightMeters { get; set; } = 0.48f;

    public float FrontLeftVisualSuspensionCompressionMeters { get; set; }

    public float FrontRightVisualSuspensionCompressionMeters { get; set; }

    public float RearLeftVisualSuspensionCompressionMeters { get; set; }

    public float RearRightVisualSuspensionCompressionMeters { get; set; }

    public float FrontLeftSupportHeightMeters { get; set; }

    public float FrontRightSupportHeightMeters { get; set; }

    public float RearLeftSupportHeightMeters { get; set; }

    public float RearRightSupportHeightMeters { get; set; }

    public float AverageSlipRatio { get; set; }

    public float AverageSlipAngleDegrees { get; set; }

    public float FrontLeftLoadN { get; set; }

    public float FrontRightLoadN { get; set; }

    public float RearLeftLoadN { get; set; }

    public float RearRightLoadN { get; set; }

    public float FrontLeftGripUsage { get; set; }

    public float FrontRightGripUsage { get; set; }

    public float RearLeftGripUsage { get; set; }

    public float RearRightGripUsage { get; set; }

    public float FrontLeftSlipRatio { get; set; }

    public float FrontRightSlipRatio { get; set; }

    public float RearLeftSlipRatio { get; set; }

    public float RearRightSlipRatio { get; set; }

    public float FrontLeftSlipAngleDegrees { get; set; }

    public float FrontRightSlipAngleDegrees { get; set; }

    public float RearLeftSlipAngleDegrees { get; set; }

    public float RearRightSlipAngleDegrees { get; set; }

    public float FrontLeftLongitudinalForceN { get; set; }

    public float FrontRightLongitudinalForceN { get; set; }

    public float RearLeftLongitudinalForceN { get; set; }

    public float RearRightLongitudinalForceN { get; set; }

    public float FrontLeftLateralForceN { get; set; }

    public float FrontRightLateralForceN { get; set; }

    public float RearLeftLateralForceN { get; set; }

    public float RearRightLateralForceN { get; set; }

    public float FrontLeftSurfaceGrip { get; set; }

    public float FrontRightSurfaceGrip { get; set; }

    public float RearLeftSurfaceGrip { get; set; }

    public float RearRightSurfaceGrip { get; set; }

    public string FrontLeftSurfaceName { get; set; } = "ROAD";

    public string FrontRightSurfaceName { get; set; } = "ROAD";

    public string RearLeftSurfaceName { get; set; } = "ROAD";

    public string RearRightSurfaceName { get; set; } = "ROAD";

    public float FrontLeftCamberDegrees { get; set; }

    public float FrontRightCamberDegrees { get; set; }

    public float RearLeftCamberDegrees { get; set; }

    public float RearRightCamberDegrees { get; set; }

    public float FrontLeftToeDegrees { get; set; }

    public float FrontRightToeDegrees { get; set; }

    public float RearLeftToeDegrees { get; set; }

    public float RearRightToeDegrees { get; set; }

    public bool CollisionActive { get; set; }

    public int WallContactCount { get; set; }

    public float LastImpactSpeedKph { get; set; }

    public float CrashSeverity { get; set; }

    public float CrashFlashSeconds { get; set; }

    public bool IsManualTransmission { get; set; }

    public string TransmissionModeName => IsManualTransmission ? "M" : "A";

    public float SpeedMetersPerSecond => Velocity.Length();

    public Vector3 Forward => new(MathF.Sin(HeadingRadians), 0f, MathF.Cos(HeadingRadians));

    public Vector3 Right => new(MathF.Cos(HeadingRadians), 0f, -MathF.Sin(HeadingRadians));
}
