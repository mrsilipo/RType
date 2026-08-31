namespace RType.Vehicle;

public sealed class SimulationEngineParameters
{
    public string HandlingModel { get; set; } = "rtypeClassic";

    public PhysicsTimingParameters Timing { get; set; } = new();

    public VehicleSafetyParameters VehicleSafety { get; set; } = new();

    public StabilityAssistParameters StabilityAssist { get; set; } = new();

    public DigitalThrottleAssistParameters DigitalThrottleAssist { get; set; } = new();

    public DigitalBrakeAssistParameters DigitalBrakeAssist { get; set; } = new();

    public BrakeThrottlePriorityParameters BrakeThrottlePriority { get; set; } = new();

    public SteeringAssistParameters SteeringAssist { get; set; } = new();

    public TyreForceTuningParameters TyreForce { get; set; } = new();

    public RpmResponseParameters RpmResponse { get; set; } = new();

    public ClassicBicycleParameters ClassicBicycle { get; set; } = new();

    public ClassicBicycleParameters ClassicFourWheel { get; set; } = new();
}

public sealed class PhysicsTimingParameters
{
    public float FixedTickRateHz { get; set; } = 120f;

    public float MaximumFrameTimeSeconds { get; set; } = 0.10f;

    public int MaximumTicksPerUpdate { get; set; } = 12;

    public float FixedDeltaSeconds => 1f / MathF.Max(1f, FixedTickRateHz);
}

public sealed class VehicleSafetyParameters
{
    public float MinimumSlipSpeedMetersPerSecond { get; set; } = 2.0f;

    public float MaximumReverseSpeedMetersPerSecond { get; set; } = 13.5f;

    public float MaximumForwardSpeedMetersPerSecond { get; set; } = 70.0f;
}

public sealed class StabilityAssistParameters
{
    public float MinimumSpeedMetersPerSecond { get; set; } = 5f;

    public float MinimumLateralSpeedMetersPerSecond { get; set; } = 0.05f;

    public float SpeedBlendStartMetersPerSecond { get; set; } = 8f;

    public float SpeedBlendEndMetersPerSecond { get; set; } = 30f;

    public float GripBlendStart { get; set; } = 0.84f;

    public float GripBlendEnd { get; set; } = 1.0f;

    public float ThrottleBlendStart { get; set; } = 0.55f;

    public float ThrottleBlendEnd { get; set; } = 1.0f;

    public float BrakeBlendStart { get; set; } = 0.12f;

    public float BrakeBlendEnd { get; set; } = 0.80f;

    public float LateralDampingMin { get; set; } = 0.35f;

    public float LateralDampingMax { get; set; } = 4.8f;

    public float LateralGripBoost { get; set; } = 0.45f;

    public float LateralThrottleBoost { get; set; } = 0.20f;

    public float LateralBrakeBoost { get; set; } = 0.25f;

    public float MaxLateralAccelerationMinG { get; set; } = 0.28f;

    public float MaxLateralAccelerationMaxG { get; set; } = 1.45f;

    public float YawDampingMin { get; set; } = 0.25f;

    public float YawDampingMax { get; set; } = 3.9f;

    public float YawGripBoost { get; set; } = 0.55f;

    public float YawRecoveryBoost { get; set; } = 0.75f;

    public float YawThrottleBoost { get; set; } = 0.15f;

    public float YawBrakeBoost { get; set; } = 0.20f;

    public float BodySlipStartDegrees { get; set; } = 3.0f;

    public float BodySlipEndDegrees { get; set; } = 12.0f;

    public float TyreSlipStartDegrees { get; set; } = 5.5f;

    public float TyreSlipEndDegrees { get; set; } = 16.0f;

    public float AssistGripStart { get; set; } = 0.90f;

    public float AssistGripEnd { get; set; } = 1.0f;

    public float BodyGripInfluenceMin { get; set; } = 0.25f;

    public float BodyGripInfluenceMax { get; set; } = 1.0f;

    public float TyreGripInfluenceMin { get; set; } = 0.45f;

    public float TyreGripInfluenceMax { get; set; } = 1.0f;

    public float CounterSteerInputStart { get; set; } = 0.12f;

    public float CounterSteerInputEnd { get; set; } = 0.85f;

    public float CounterSteerGripAllowance { get; set; } = 0.34f;

    public float CounterSteerSlipRelaxationMultiplier { get; set; } = 0.34f;

    public float CounterSteerSlidingFrictionRecovery { get; set; } = 0.52f;

    public float NeutralRecoveryInputStart { get; set; } = 0.04f;

    public float NeutralRecoveryInputEnd { get; set; } = 0.30f;

    public float NeutralRecoveryMultiplier { get; set; } = 0.45f;

    public float CommittedTurnInputStart { get; set; } = 0.22f;

    public float CommittedTurnInputEnd { get; set; } = 0.85f;

    public float CommittedTurnBrakeDampingMultiplier { get; set; } = 0.0f;

    public float CommittedTurnCoastDampingMultiplier { get; set; } = 0.0f;

    public float CommittedTurnCoastThrottleEnd { get; set; } = 0.12f;

    public float MinimumYawRateDegreesPerSecond { get; set; } = 2.0f;
}

public sealed class DigitalThrottleAssistParameters
{
    public float FullThrottleBelowSpeedMetersPerSecond { get; set; } = 3.0f;

    public float SpeedBlendStartMetersPerSecond { get; set; } = 4.5f;

    public float SpeedBlendEndMetersPerSecond { get; set; } = 28f;

    public float SteeringBlendStart { get; set; } = 0.06f;

    public float SteeringBlendEnd { get; set; } = 0.64f;

    public float StraightLaunchBypassSpeedMetersPerSecond { get; set; } = 9.0f;

    public float GripUsageBlendStart { get; set; } = 0.94f;

    public float GripUsageBlendEnd { get; set; } = 1.14f;

    public float SlipRatioBlendStart { get; set; } = 0.09f;

    public float SlipRatioBlendEnd { get; set; } = 0.30f;

    public float CornerLimitLowSpeed { get; set; } = 0.90f;

    public float CornerLimitHighSpeed { get; set; } = 0.94f;

    public float TractionDemandGripScale { get; set; } = 0.35f;

    public float TractionLimitFloor { get; set; } = 0.90f;

    public float MinimumAssistLimit { get; set; } = 0.56f;
}

public sealed class DigitalBrakeAssistParameters
{
    public float FullBrakeBelowSpeedMetersPerSecond { get; set; } = 1.5f;

    public float SpeedBlendStartMetersPerSecond { get; set; } = 5f;

    public float SpeedBlendEndMetersPerSecond { get; set; } = 34f;

    public float SteeringBlendStart { get; set; } = 0.12f;

    public float SteeringBlendEnd { get; set; } = 0.82f;

    public float HighSpeedBrakeLimit { get; set; } = 0.975f;

    public float SteeringReductionLowSpeed { get; set; } = 0.02f;

    public float SteeringReductionHighSpeed { get; set; } = 0.09f;

    public float MinimumAssistLimit { get; set; } = 0.88f;

    public float MaximumAssistLimit { get; set; } = 0.975f;

    public float TrailBrakeFrontTorqueMultiplier { get; set; } = 0.70f;

    public float TrailBrakeRearTorqueMultiplier { get; set; } = 1.28f;

    public float AbsTargetSlipRatio { get; set; } = -0.09f;

    public float AbsReleaseSlipRatio { get; set; } = -0.125f;

    public float AbsApplyRatePerSecond { get; set; } = 14f;

    public float AbsReleaseRatePerSecond { get; set; } = 38f;

    public float AbsMinimumSpeedMetersPerSecond { get; set; } = 2.0f;

    public float AbsMinimumPressureRatio { get; set; } = 0.10f;
}

public sealed class BrakeThrottlePriorityParameters
{
    public float BrakeBlendStart { get; set; } = 0.06f;

    public float BrakeBlendEnd { get; set; } = 0.65f;

    public float FullBrakeThrottleMultiplier { get; set; } = 0.0f;
}

public sealed class SteeringAssistParameters
{
    public bool DirectRackInput { get; set; } = true;

    public float BrakeAngleBoostBrakeStart { get; set; } = 0.12f;

    public float BrakeAngleBoostBrakeEnd { get; set; } = 0.85f;

    public float BrakeAngleBoostSpeedStartMetersPerSecond { get; set; } = 14f;

    public float BrakeAngleBoostSpeedEndMetersPerSecond { get; set; } = 38f;

    public float BrakeAngleBoostMultiplier { get; set; } = 1.30f;

    public float SpeedMatchedSlipStartMetersPerSecond { get; set; } = 32f;

    public float SpeedMatchedSlipEndMetersPerSecond { get; set; } = 68f;

    public float LowSpeedSlipAllowanceMultiplier { get; set; } = 1.16f;

    public float HighSpeedSlipAllowanceMultiplier { get; set; } = 0.44f;

    public float HighSpeedMinimumRoadWheelAngleDegrees { get; set; } = 2.25f;

    public float CommittedTurnInputStart { get; set; } = 0.25f;

    public float CommittedTurnInputEnd { get; set; } = 0.85f;

    public float CommittedTurnMinimumRoadWheelAngleDegrees { get; set; } = 24.0f;

    public float GripReserveAngleBoost { get; set; } = 0.65f;

    public float HighSpeedInputCurveExponent { get; set; } = 0.82f;

    public float DecelInputCurveExponent { get; set; } = 0.82f;

    public float DecelAuthorityThrottleEnd { get; set; } = 0.12f;

    public float DecelInputRateBoost { get; set; } = 1.55f;

    public float LateralForceForwardProjectionScale { get; set; } = 0.18f;

    public float PoweredLateralForceForwardProjectionScale { get; set; } = 0.09f;

    public float LowSpeedPivotSpeedEndMetersPerSecond { get; set; } = 14.0f;

    public float LowSpeedPivotSteerStart { get; set; } = 0.60f;

    public float LowSpeedPivotRearLateralMultiplier { get; set; } = 1.0f;

    public float LowSpeedPivotYawResponse { get; set; } = 5.5f;

    public float LowSpeedPivotMaxYawRateDegreesPerSecond { get; set; } = 145f;

    public float InputBrakeAuthorityStart { get; set; } = 0.10f;

    public float InputBrakeAuthorityEnd { get; set; } = 0.70f;

    public float InputBrakeAuthoritySpeedStartMetersPerSecond { get; set; } = 12f;

    public float InputBrakeAuthoritySpeedEndMetersPerSecond { get; set; } = 36f;

    public float BrakingInputMultiplierFloor { get; set; } = 0.95f;

    public float BrakingReturnMultiplierFloor { get; set; } = 1.05f;

    public float BrakingInputRateBoost { get; set; } = 1.32f;

    public float RecentBrakeBoostThreshold { get; set; } = 0.12f;

    public float RecentBrakeBoostSeconds { get; set; } = 0.42f;

    public float RecentBrakeAuthority { get; set; } = 0.58f;
}

public sealed class TyreForceTuningParameters
{
    public float SlidingForceFloor { get; set; } = 0.95f;

    public float ScrubDragLimitMultiplier { get; set; } = 0.25f;

    public float LateralLongitudinalGripCoupling { get; set; } = 0.45f;

    public float CorneringSpeedRetention { get; set; } = 0.55f;

    public float CorneringSpeedRetentionSteerStart { get; set; } = 0.04f;

    public float CorneringSpeedRetentionSteerEnd { get; set; } = 0.35f;

    public float ScrubRpmIsolationSlipStart { get; set; } = 0.55f;

    public float ScrubRpmIsolationSlipEnd { get; set; } = 1.30f;

    public float ScrubRpmIsolationMaximumSpeedDropMetersPerSecond { get; set; } = 2.0f;
}

public sealed class RpmResponseParameters
{
    public float PoweredAntiDipWindowRpm { get; set; } = 6500f;

    public float PoweredAntiDipFallRateRpmPerSecond { get; set; } = 520f;
}

public sealed class ClassicBicycleParameters
{
    public ClassicBicycleSteeringParameters Steering { get; set; } = new();

    public ClassicBicycleTyreParameters FrontTyres { get; set; } = new()
    {
        CorneringStiffness = 7.2f,
        PeakSlipAngleDegrees = 7.0f,
        FalloffSlipAngleDegrees = 22.0f,
        MaxGrip = 1.05f,
        SlidingGrip = 0.78f
    };

    public ClassicBicycleTyreParameters RearTyres { get; set; } = new()
    {
        CorneringStiffness = 7.8f,
        PeakSlipAngleDegrees = 8.0f,
        FalloffSlipAngleDegrees = 24.0f,
        MaxGrip = 1.08f,
        SlidingGrip = 0.82f
    };

    public ClassicBicycleYawParameters Yaw { get; set; } = new();

    public ClassicBicycleGripBudgetParameters GripBudget { get; set; } = new();

    public ClassicChassisLoadTransferParameters ChassisLoadTransfer { get; set; } = new();

    public ClassicBicycleLowSpeedParameters LowSpeed { get; set; } = new();

    public ClassicBicycleResistanceParameters Resistance { get; set; } = new();
}

public sealed class ClassicBicycleSteeringParameters
{
    public float ZeroKmhAngleDegrees { get; set; } = 32.0f;

    public float SixtyKmhAngleDegrees { get; set; } = 24.0f;

    public float OneTwentyKmhAngleDegrees { get; set; } = 15.0f;

    public float TwoHundredKmhAngleDegrees { get; set; } = 8.0f;

    public float SteerSpeedDegreesPerSecond { get; set; } = 170.0f;

    public float ReturnSpeedDegreesPerSecond { get; set; } = 230.0f;

    public float PhysicalEnvelopeBlendStartKmh { get; set; } = 40.0f;

    public float PhysicalEnvelopeFullKmh { get; set; } = 95.0f;

    public float NormalLateralAccelerationG { get; set; } = 1.15f;

    public float OverdriveLateralAccelerationG { get; set; } = 1.40f;

    public float NormalCommand { get; set; } = 0.75f;

    public float MinimumHighSpeedAngleDegrees { get; set; } = 0.35f;

    public float NormalPeakSlipFraction { get; set; } = 0.08f;

    public float OverdrivePeakSlipFraction { get; set; } = 0.30f;

    public float TransientPeakSlipFraction { get; set; } = 0.32f;

    public float TransientBoostSeconds { get; set; } = 0.42f;

    public float DigitalInitialCommandRatePerSecond { get; set; } = 5.2f;

    public float DigitalSustainedCommandRatePerSecond { get; set; } = 7.8f;

    public float DigitalRiseAccelerationSeconds { get; set; } = 0.14f;

    public float DigitalReleaseCommandRatePerSecond { get; set; } = 8.0f;

    public float DigitalCounterSteerRateMultiplier { get; set; } = 3.0f;
}

public sealed class ClassicBicycleTyreParameters
{
    public float CorneringStiffness { get; set; } = 7.5f;

    public float PeakSlipAngleDegrees { get; set; } = 7.5f;

    public float FalloffSlipAngleDegrees { get; set; } = 23.0f;

    public float MaxGrip { get; set; } = 1.06f;

    public float SlidingGrip { get; set; } = 0.80f;

    public float LoadSensitivity { get; set; } = 0f;

    public float ReferenceLoadN { get; set; } = 0f;

    public float RelaxationLengthMeters { get; set; } = 0f;
}

public sealed class ClassicBicycleYawParameters
{
    public float InertiaScale { get; set; } = 1.0f;

    public float Damping { get; set; } = 0.18f;

    public float LateralVelocityDamping { get; set; } = 0.0f;
}

public sealed class ClassicBicycleGripBudgetParameters
{
    public float CombinedGripExponent { get; set; } = 2.0f;

    public float BrakingSteeringLateralPriority { get; set; } = 0.35f;

    public float BrakingSteeringPrioritySteerStart { get; set; } = 0.20f;

    public float BrakingSteeringPrioritySteerEnd { get; set; } = 0.85f;

    public float BrakingSteeringPriorityBrakeStart { get; set; } = 0.35f;

    public float BrakingSteeringPriorityBrakeEnd { get; set; } = 1.0f;

    public float BrakingSteeringFrontBrakeMultiplier { get; set; } = 0.88f;

    public float BrakingSteeringRearBrakeMultiplier { get; set; } = 0.42f;

    public float BrakePressureFrontTargetGripUsage { get; set; } = 0.94f;

    public float BrakePressureRearTargetGripUsage { get; set; } = 0.82f;

    public float BrakePressureApplyRatePerSecond { get; set; } = 14f;

    public float BrakePressureReleaseRatePerSecond { get; set; } = 38f;

    public float BrakePressureMinimumRatio { get; set; } = 0.10f;

    public float BrakePressureMinimumSpeedMetersPerSecond { get; set; } = 2.0f;
}

public sealed class ClassicChassisLoadTransferParameters
{
    public bool Enabled { get; set; } = true;

    public float LongitudinalNaturalFrequencyHz { get; set; } = 5.5f;

    public float LongitudinalDampingRatio { get; set; } = 0.72f;

    public float LateralNaturalFrequencyHz { get; set; } = 4.2f;

    public float LateralDampingRatio { get; set; } = 0.70f;
}

public sealed class ClassicBicycleLowSpeedParameters
{
    public float SlipSpeedFloorMetersPerSecond { get; set; } = 3.0f;

    public float RollingDominantEndMetersPerSecond { get; set; } = 5.0f / 3.6f;

    public float DynamicBlendEndMetersPerSecond { get; set; } = 10.0f / 3.6f;

    public float RollingDominantMaximumLateralScale { get; set; } = 0.02f;

    public float RollingDominantRearLateralScale { get; set; } = 0.55f;

    public float RollingConstraintLateralSpeedMetersPerSecond { get; set; } = 0.75f;

    public float RollingConstraintGripFraction { get; set; } = 0.80f;

    public float KinematicYawBlend { get; set; } = 0f;

    public float KinematicBlendEndSpeedMetersPerSecond { get; set; } = 2.5f;

    public float KinematicYawAccelerationLimitDegreesPerSecondSquared { get; set; } = 45f;

    public bool SlipRateLimitEnabled { get; set; }

    public float MaxSlipRateDegreesPerSecond { get; set; } = 120f;

    public float SlipRateLimitFadeStartMetersPerSecond { get; set; } = 2.0f;

    public float SlipRateLimitFadeEndMetersPerSecond { get; set; } = 5.0f;
}

public sealed class ClassicBicycleResistanceParameters
{
    public float RollingResistanceMultiplier { get; set; } = 1.0f;

    public float AeroDragMultiplier { get; set; } = 1.0f;
}
