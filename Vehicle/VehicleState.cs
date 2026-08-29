using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public sealed class VehicleState
{
    public string VehicleName { get; set; } = "Prototype Car";

    public float PowerRedlineRpm { get; set; } = 6500f;

    public float LimiterHardCutRpm { get; set; } = 6800f;

    public float LimiterResumeRpm { get; set; } = 6620f;

    public float MaxGaugeRpm { get; set; } = 8000f;

    public DrivetrainLimits DrivetrainLimits => new(
        Rpm,
        PowerRedlineRpm,
        LimiterHardCutRpm,
        LimiterResumeRpm,
        MaxGaugeRpm);

    [Obsolete("Use PowerRedlineRpm for power/HUD redline and LimiterHardCutRpm for mechanical cut logic.")]
    public float RedlineRpm => LimiterHardCutRpm;

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

    public float RevLimiterBouncePhase { get; set; }

    public float LimiterTorqueMultiplier { get; set; } = 1f;

    public bool IsShifting { get; set; }

    public float ShiftTimeRemainingSeconds { get; set; }

    public float ShiftKickIntensity { get; set; }

    public int LastCompletedShiftFromGear { get; set; }

    public int LastCompletedShiftToGear { get; set; }

    public float LastCompletedShiftKickSeverity { get; set; }

    public float ClutchSlipRpm { get; set; }

    public float EngineOmegaRadiansPerSecond { get; set; }

    public float GearboxInputOmegaRadiansPerSecond { get; set; }

    public float ClutchSlipDeltaRadiansPerSecond { get; set; }

    public bool ClutchIsLocked { get; set; }

    public float ActiveClutchTorqueNm { get; set; }

    public float ClutchEngagement { get; set; }

    public bool MechanicalOverRevActive { get; set; }

    public float MechanicalOverRevRpm { get; set; }

    public float MechanicalOverRevSeverity { get; set; }

    public float PowertrainShockIntensity { get; set; }

    public float CounterSteerRecoveryIntensity { get; set; }

    public float SignedForwardSpeed { get; set; }

    public float DisplayedSpeedMetersPerSecond { get; set; }

    public float LateralSpeed { get; set; }

    public float LongitudinalAcceleration { get; set; }

    public float LateralAcceleration { get; set; }

    public float PhysicalLoadTransferLongitudinalAcceleration { get; set; }

    public float PhysicalLoadTransferLateralAcceleration { get; set; }

    public float TrackPitchRadians { get; set; }

    public float TrackRollRadians { get; set; }

    public float TrackLongitudinalGravityForceN { get; set; }

    public float TrackLateralGravityForceN { get; set; }

    public float VisualLoadTransferLateralAcceleration { get; set; }

    public float LongitudinalLoadTransferN { get; set; }

    public float FrontLateralLoadTransferN { get; set; }

    public float RearLateralLoadTransferN { get; set; }

    public float FrontStaticAxleLoadN { get; set; }

    public float RearStaticAxleLoadN { get; set; }

    public float ClassicStaticFrontAxleLoadN { get; set; }

    public float ClassicStaticRearAxleLoadN { get; set; }

    public float ClassicDynamicFrontAxleLoadN { get; set; }

    public float ClassicDynamicRearAxleLoadN { get; set; }

    public float ClassicLongitudinalLoadTransferN { get; set; }

    public float ClassicDriveForceRequestN { get; set; }

    public float ClassicEngineBrakeForceRequestN { get; set; }

    public float ClassicServiceBrakeForceRequestN { get; set; }

    public float ClassicHandbrakeForceRequestN { get; set; }

    public float ClassicRollingResistanceForceN { get; set; }

    public float ClassicAeroDragForceN { get; set; }

    public float ClassicFrontLongitudinalGripUsage { get; set; }

    public float ClassicRearLongitudinalGripUsage { get; set; }

    public float ClassicFrontLateralGripUsage { get; set; }

    public float ClassicRearLateralGripUsage { get; set; }

    public float ClassicBodySlipAngleDegrees { get; set; }

    public float ClassicNaturalYawAccelerationDegreesPerSecondSquared { get; set; }

    public float ClassicFrontYawAccelerationDegreesPerSecondSquared { get; set; }

    public float ClassicRearYawAccelerationDegreesPerSecondSquared { get; set; }

    public float ClassicYawDampingAccelerationDegreesPerSecondSquared { get; set; }

    public float ClassicYawRecoveryAccelerationDegreesPerSecondSquared { get; set; }

    public float ClassicRearFollowAccelerationDegreesPerSecondSquared { get; set; }

    public float ClassicRearFollowForceDeficitN { get; set; }

    public float ClassicBodySlipDampingForceN { get; set; }

    public float ClassicCorneringCleanupSpeedRetentionForceN { get; set; }

    public float FrontAeroLoadN { get; set; }

    public float RearAeroLoadN { get; set; }

    public float FrontRollShare { get; set; } = 0.5f;

    public float SurfaceGrip { get; set; } = 1f;

    public string SurfaceName { get; set; } = "ROAD";

    public float Throttle { get; set; }

    public float EffectiveThrottle { get; set; }

    public float Brake { get; set; }

    public float Handbrake { get; set; }

    public float Steer { get; set; }

    public float FrontLeftSteerAngleDegrees { get; set; }

    public float FrontRightSteerAngleDegrees { get; set; }

    public float SteeringFrontGripReserve { get; set; } = 1f;

    public float SteeringCommittedTurnAuthority { get; set; }

    public float SteeringSpeedMatchedMaxAngleDegrees { get; set; }

    public float SteeringForwardForceClampN { get; set; }

    public float FrontLeftTyreScrubForceN { get; set; }

    public float FrontRightTyreScrubForceN { get; set; }

    public float RearLeftTyreScrubForceN { get; set; }

    public float RearRightTyreScrubForceN { get; set; }

    public float FrontLeftSteeringProjectionForceN { get; set; }

    public float FrontRightSteeringProjectionForceN { get; set; }

    public float RearLeftSteeringProjectionForceN { get; set; }

    public float RearRightSteeringProjectionForceN { get; set; }

    public float PeakTyreScrubForceN { get; set; }

    public float PeakSteeringProjectionForceN { get; set; }

    public float RpmScrubIsolationIntensity { get; set; }

    public float DriveForce { get; set; }

    public float BrakeForce { get; set; }

    public float FrontBrakeTorqueNm { get; set; }

    public float RearBrakeTorqueNm { get; set; }

    public float RearHandbrakeLockAmount { get; set; }

    public float RearHandbrakeSlideIntensity { get; set; }

    public float RearHandbrakeScreechFactor { get; set; } = 1f;

    public float EngineBrakeTorqueNm { get; set; }

    public bool EnginePowerUnitActive { get; set; }

    public float EnginePowerUnitDriveTorqueNm { get; set; }

    public float EnginePowerUnitEngineDriveTorqueNm { get; set; }

    public float EnginePowerUnitRawTorqueNm { get; set; }

    public float EnginePowerUnitVtecBlend { get; set; }

    public float EnginePowerUnitVtecKickIntensity { get; set; }

    public float EnginePowerUnitLoad { get; set; }

    public float EnginePowerUnitFuelCutBlend { get; set; }

    public float EnginePowerUnitCrankRpm { get; set; }

    public float EnginePowerUnitCrankPhaseDegrees { get; set; }

    public float EnginePowerUnitAfterfireBlend { get; set; }

    public float EnginePowerUnitTransmissionRpm { get; set; }

    public float EnginePowerUnitClutchTorqueNm { get; set; }

    public float EnginePowerUnitCrankFrictionTorqueNm { get; set; }

    public float EnginePowerUnitReferenceDriveTorqueNm { get; set; }

    public float EnginePowerUnitCalibratedDriveTorqueNm { get; set; }

    public float EnginePowerUnitGasAuthority { get; set; }

    public float EnginePowerUnitFullThrottleGasTorqueNm { get; set; }

    public bool RTypeEngineActive { get; set; }

    public string RTypeEngineProfileId { get; set; } = string.Empty;

    public float RTypeEngineRpm { get; set; }

    public float RTypeEngineCrankPhaseDegrees { get; set; }

    public float RTypeEngineVtecBlend { get; set; }

    public bool RTypeEngineLimiterCut { get; set; }

    public float RTypeEngineRevLimitTimerSeconds { get; set; }

    public int RTypeEngineLastIgnitedCylinder { get; set; } = -1;

    public float RTypeEngineThrottle { get; set; }

    public float RTypeEngineOutputPeak { get; set; }

    public float RTypeEngineOutputRms { get; set; }

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

    public float FrontLeftRelaxedLongitudinalSlipRatio { get; set; }

    public float FrontRightRelaxedLongitudinalSlipRatio { get; set; }

    public float RearLeftRelaxedLongitudinalSlipRatio { get; set; }

    public float RearRightRelaxedLongitudinalSlipRatio { get; set; }

    public float FrontLeftRelaxedLateralSlip { get; set; }

    public float FrontRightRelaxedLateralSlip { get; set; }

    public float RearLeftRelaxedLateralSlip { get; set; }

    public float RearRightRelaxedLateralSlip { get; set; }

    public float PeakRawSlipRatio { get; set; }

    public float PeakRelaxedLongitudinalSlipRatio { get; set; }

    public float PeakRelaxedLateralSlip { get; set; }

    public float FrontLeftWheelOmegaRadiansPerSecond { get; set; }

    public float FrontRightWheelOmegaRadiansPerSecond { get; set; }

    public float RearLeftWheelOmegaRadiansPerSecond { get; set; }

    public float RearRightWheelOmegaRadiansPerSecond { get; set; }

    public float FrontLeftFrictionEllipseTotalSlip { get; set; }

    public float FrontRightFrictionEllipseTotalSlip { get; set; }

    public float RearLeftFrictionEllipseTotalSlip { get; set; }

    public float RearRightFrictionEllipseTotalSlip { get; set; }

    public float FrontLeftFrictionEllipseGripBudgetN { get; set; }

    public float FrontRightFrictionEllipseGripBudgetN { get; set; }

    public float RearLeftFrictionEllipseGripBudgetN { get; set; }

    public float RearRightFrictionEllipseGripBudgetN { get; set; }

    public float FrontLeftFrictionEllipseLongitudinalShare { get; set; }

    public float FrontRightFrictionEllipseLongitudinalShare { get; set; }

    public float RearLeftFrictionEllipseLongitudinalShare { get; set; }

    public float RearRightFrictionEllipseLongitudinalShare { get; set; }

    public float FrontLeftFrictionEllipseLateralShare { get; set; }

    public float FrontRightFrictionEllipseLateralShare { get; set; }

    public float RearLeftFrictionEllipseLateralShare { get; set; }

    public float RearRightFrictionEllipseLateralShare { get; set; }

    public float FrontLeftFrictionEllipseLongitudinalForceN { get; set; }

    public float FrontRightFrictionEllipseLongitudinalForceN { get; set; }

    public float RearLeftFrictionEllipseLongitudinalForceN { get; set; }

    public float RearRightFrictionEllipseLongitudinalForceN { get; set; }

    public float FrontLeftFrictionEllipseLateralForceN { get; set; }

    public float FrontRightFrictionEllipseLateralForceN { get; set; }

    public float RearLeftFrictionEllipseLateralForceN { get; set; }

    public float RearRightFrictionEllipseLateralForceN { get; set; }

    public float FrontLeftFrictionEllipseTotalForceN { get; set; }

    public float FrontRightFrictionEllipseTotalForceN { get; set; }

    public float RearLeftFrictionEllipseTotalForceN { get; set; }

    public float RearRightFrictionEllipseTotalForceN { get; set; }

    public float FrontLeftFrictionEllipseGripUsage { get; set; }

    public float FrontRightFrictionEllipseGripUsage { get; set; }

    public float RearLeftFrictionEllipseGripUsage { get; set; }

    public float RearRightFrictionEllipseGripUsage { get; set; }

    public float PeakFrictionEllipseTotalSlip { get; set; }

    public float PeakFrictionEllipseGripUsage { get; set; }

    public float FrontLeftSlipAngleDegrees { get; set; }

    public float FrontRightSlipAngleDegrees { get; set; }

    public float RearLeftSlipAngleDegrees { get; set; }

    public float RearRightSlipAngleDegrees { get; set; }

    public float FrontLeftLongitudinalForceN { get; set; }

    public float FrontRightLongitudinalForceN { get; set; }

    public float RearLeftLongitudinalForceN { get; set; }

    public float RearRightLongitudinalForceN { get; set; }

    public float FrontLeftRequestedLongitudinalForceN { get; set; }

    public float FrontRightRequestedLongitudinalForceN { get; set; }

    public float RearLeftRequestedLongitudinalForceN { get; set; }

    public float RearRightRequestedLongitudinalForceN { get; set; }

    public float FfLsdCornerExitBite { get; set; }

    public float FfLsdInsideFrontMaxTorqueNm { get; set; }

    public float FfLsdOutsideFrontMaxTorqueNm { get; set; }

    public float FfLsdManagedFrontAxleTorqueNm { get; set; }

    public float FfLsdFrontLeftActualTorqueNm { get; set; }

    public float FfLsdFrontRightActualTorqueNm { get; set; }

    public string FfLsdLowGripAnchor { get; set; } = string.Empty;

    public float FrontDriveTorqueSteerYawMomentNm { get; set; }

    public float FrontDifferentialCornerExitBite { get; set; }

    public float FrontDifferentialManagedAxleTorqueNm { get; set; }

    public float FrontDifferentialLeftActualTorqueNm { get; set; }

    public float FrontDifferentialRightActualTorqueNm { get; set; }

    public string FrontDifferentialLowGripAnchor { get; set; } = string.Empty;

    public float RearDifferentialManagedAxleTorqueNm { get; set; }

    public float RearDifferentialLeftActualTorqueNm { get; set; }

    public float RearDifferentialRightActualTorqueNm { get; set; }

    public string RearDifferentialLowGripAnchor { get; set; } = string.Empty;

    public float FrontLeftDriveTorqueNm { get; set; }

    public float FrontRightDriveTorqueNm { get; set; }

    public float RearLeftDriveTorqueNm { get; set; }

    public float RearRightDriveTorqueNm { get; set; }

    public float FrontLeftLateralForceN { get; set; }

    public float FrontRightLateralForceN { get; set; }

    public float RearLeftLateralForceN { get; set; }

    public float RearRightLateralForceN { get; set; }

    public float FrontLeftSurfaceGrip { get; set; }

    public float FrontRightSurfaceGrip { get; set; }

    public float RearLeftSurfaceGrip { get; set; }

    public float RearRightSurfaceGrip { get; set; }

    public float FrontLeftSurfaceMu { get; set; }

    public float FrontRightSurfaceMu { get; set; }

    public float RearLeftSurfaceMu { get; set; }

    public float RearRightSurfaceMu { get; set; }

    public float FrontLeftDisplacementDragForceN { get; set; }

    public float FrontRightDisplacementDragForceN { get; set; }

    public float RearLeftDisplacementDragForceN { get; set; }

    public float RearRightDisplacementDragForceN { get; set; }

    public float FrontLeftCurbLoadMultiplier { get; set; } = 1f;

    public float FrontRightCurbLoadMultiplier { get; set; } = 1f;

    public float RearLeftCurbLoadMultiplier { get; set; } = 1f;

    public float RearRightCurbLoadMultiplier { get; set; } = 1f;

    public int CurbContactWheelCount { get; set; }

    public float FrontLeftSurfaceLoadMultiplier { get; set; } = 1f;

    public float FrontRightSurfaceLoadMultiplier { get; set; } = 1f;

    public float RearLeftSurfaceLoadMultiplier { get; set; } = 1f;

    public float RearRightSurfaceLoadMultiplier { get; set; } = 1f;

    public int SurfaceVibrationContactWheelCount { get; set; }

    public float SurfaceRumbleLeft { get; set; }

    public float SurfaceRumbleRight { get; set; }

    public float FrontLeftSurfaceBlend { get; set; }

    public float FrontRightSurfaceBlend { get; set; }

    public float RearLeftSurfaceBlend { get; set; }

    public float RearRightSurfaceBlend { get; set; }

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
