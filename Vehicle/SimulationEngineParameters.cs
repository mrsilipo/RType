namespace RetroRacer.Vehicle;

public sealed class SimulationEngineParameters
{
    public PhysicsTimingParameters Timing { get; init; } = new();

    public VehicleSafetyParameters VehicleSafety { get; init; } = new();

    public StabilityAssistParameters StabilityAssist { get; init; } = new();

    public DigitalThrottleAssistParameters DigitalThrottleAssist { get; init; } = new();

    public DigitalBrakeAssistParameters DigitalBrakeAssist { get; init; } = new();

    public BrakeThrottlePriorityParameters BrakeThrottlePriority { get; init; } = new();

    public SteeringAssistParameters SteeringAssist { get; init; } = new();

    public RpmResponseParameters RpmResponse { get; init; } = new();
}

public sealed class PhysicsTimingParameters
{
    public float FixedTickRateHz { get; init; } = 120f;

    public float MaximumFrameTimeSeconds { get; init; } = 0.10f;

    public int MaximumTicksPerUpdate { get; init; } = 12;

    public float FixedDeltaSeconds => 1f / MathF.Max(1f, FixedTickRateHz);
}

public sealed class VehicleSafetyParameters
{
    public float MinimumSlipSpeedMetersPerSecond { get; init; } = 2.0f;

    public float MaximumReverseSpeedMetersPerSecond { get; init; } = 13.5f;

    public float MaximumForwardSpeedMetersPerSecond { get; init; } = 70.0f;
}

public sealed class StabilityAssistParameters
{
    public float MinimumSpeedMetersPerSecond { get; init; } = 5f;

    public float MinimumLateralSpeedMetersPerSecond { get; init; } = 0.05f;

    public float SpeedBlendStartMetersPerSecond { get; init; } = 8f;

    public float SpeedBlendEndMetersPerSecond { get; init; } = 30f;

    public float GripBlendStart { get; init; } = 0.84f;

    public float GripBlendEnd { get; init; } = 1.0f;

    public float ThrottleBlendStart { get; init; } = 0.55f;

    public float ThrottleBlendEnd { get; init; } = 1.0f;

    public float BrakeBlendStart { get; init; } = 0.12f;

    public float BrakeBlendEnd { get; init; } = 0.80f;

    public float LateralDampingMin { get; init; } = 0.35f;

    public float LateralDampingMax { get; init; } = 4.8f;

    public float LateralGripBoost { get; init; } = 0.45f;

    public float LateralThrottleBoost { get; init; } = 0.20f;

    public float LateralBrakeBoost { get; init; } = 0.25f;

    public float MaxLateralAccelerationMinG { get; init; } = 0.28f;

    public float MaxLateralAccelerationMaxG { get; init; } = 1.45f;

    public float YawDampingMin { get; init; } = 0.25f;

    public float YawDampingMax { get; init; } = 3.9f;

    public float YawGripBoost { get; init; } = 0.55f;

    public float YawRecoveryBoost { get; init; } = 0.75f;

    public float YawThrottleBoost { get; init; } = 0.15f;

    public float YawBrakeBoost { get; init; } = 0.20f;

    public float BodySlipStartDegrees { get; init; } = 3.0f;

    public float BodySlipEndDegrees { get; init; } = 12.0f;

    public float TyreSlipStartDegrees { get; init; } = 5.5f;

    public float TyreSlipEndDegrees { get; init; } = 16.0f;

    public float AssistGripStart { get; init; } = 0.90f;

    public float AssistGripEnd { get; init; } = 1.0f;

    public float BodyGripInfluenceMin { get; init; } = 0.25f;

    public float BodyGripInfluenceMax { get; init; } = 1.0f;

    public float TyreGripInfluenceMin { get; init; } = 0.45f;

    public float TyreGripInfluenceMax { get; init; } = 1.0f;

    public float CounterSteerInputStart { get; init; } = 0.12f;

    public float CounterSteerInputEnd { get; init; } = 0.85f;

    public float CounterSteerGripAllowance { get; init; } = 0.34f;

    public float CounterSteerSlipRelaxationMultiplier { get; init; } = 0.34f;

    public float CounterSteerSlidingFrictionRecovery { get; init; } = 0.52f;

    public float NeutralRecoveryInputStart { get; init; } = 0.04f;

    public float NeutralRecoveryInputEnd { get; init; } = 0.30f;

    public float NeutralRecoveryMultiplier { get; init; } = 0.45f;

    public float CommittedTurnInputStart { get; init; } = 0.22f;

    public float CommittedTurnInputEnd { get; init; } = 0.85f;

    public float CommittedTurnBrakeDampingMultiplier { get; init; } = 0.72f;

    public float MinimumYawRateDegreesPerSecond { get; init; } = 2.0f;
}

public sealed class DigitalThrottleAssistParameters
{
    public float FullThrottleBelowSpeedMetersPerSecond { get; init; } = 3.0f;

    public float SpeedBlendStartMetersPerSecond { get; init; } = 4.5f;

    public float SpeedBlendEndMetersPerSecond { get; init; } = 28f;

    public float SteeringBlendStart { get; init; } = 0.06f;

    public float SteeringBlendEnd { get; init; } = 0.64f;

    public float StraightLaunchBypassSpeedMetersPerSecond { get; init; } = 9.0f;

    public float GripUsageBlendStart { get; init; } = 0.94f;

    public float GripUsageBlendEnd { get; init; } = 1.14f;

    public float SlipRatioBlendStart { get; init; } = 0.09f;

    public float SlipRatioBlendEnd { get; init; } = 0.30f;

    public float CornerLimitLowSpeed { get; init; } = 0.90f;

    public float CornerLimitHighSpeed { get; init; } = 0.94f;

    public float TractionDemandGripScale { get; init; } = 0.35f;

    public float TractionLimitFloor { get; init; } = 0.90f;

    public float MinimumAssistLimit { get; init; } = 0.56f;
}

public sealed class DigitalBrakeAssistParameters
{
    public float FullBrakeBelowSpeedMetersPerSecond { get; init; } = 1.5f;

    public float SpeedBlendStartMetersPerSecond { get; init; } = 5f;

    public float SpeedBlendEndMetersPerSecond { get; init; } = 34f;

    public float SteeringBlendStart { get; init; } = 0.12f;

    public float SteeringBlendEnd { get; init; } = 0.82f;

    public float HighSpeedBrakeLimit { get; init; } = 0.975f;

    public float SteeringReductionLowSpeed { get; init; } = 0.02f;

    public float SteeringReductionHighSpeed { get; init; } = 0.09f;

    public float MinimumAssistLimit { get; init; } = 0.88f;

    public float MaximumAssistLimit { get; init; } = 0.975f;

    public float TrailBrakeFrontTorqueMultiplier { get; init; } = 0.92f;

    public float TrailBrakeRearTorqueMultiplier { get; init; } = 0.96f;

    public float AbsTargetSlipRatio { get; init; } = -0.09f;

    public float AbsReleaseSlipRatio { get; init; } = -0.125f;

    public float AbsApplyRatePerSecond { get; init; } = 14f;

    public float AbsReleaseRatePerSecond { get; init; } = 38f;

    public float AbsMinimumSpeedMetersPerSecond { get; init; } = 2.0f;

    public float AbsMinimumPressureRatio { get; init; } = 0.10f;
}

public sealed class BrakeThrottlePriorityParameters
{
    public float BrakeBlendStart { get; init; } = 0.06f;

    public float BrakeBlendEnd { get; init; } = 0.65f;

    public float FullBrakeThrottleMultiplier { get; init; } = 0.0f;
}

public sealed class SteeringAssistParameters
{
    public float BrakeAngleBoostBrakeStart { get; init; } = 0.12f;

    public float BrakeAngleBoostBrakeEnd { get; init; } = 0.85f;

    public float BrakeAngleBoostSpeedStartMetersPerSecond { get; init; } = 14f;

    public float BrakeAngleBoostSpeedEndMetersPerSecond { get; init; } = 38f;

    public float BrakeAngleBoostMultiplier { get; init; } = 1.30f;

    public float SpeedMatchedSlipStartMetersPerSecond { get; init; } = 32f;

    public float SpeedMatchedSlipEndMetersPerSecond { get; init; } = 68f;

    public float LowSpeedSlipAllowanceMultiplier { get; init; } = 1.16f;

    public float HighSpeedSlipAllowanceMultiplier { get; init; } = 0.44f;

    public float HighSpeedMinimumRoadWheelAngleDegrees { get; init; } = 2.25f;

    public float InputBrakeAuthorityStart { get; init; } = 0.10f;

    public float InputBrakeAuthorityEnd { get; init; } = 0.70f;

    public float InputBrakeAuthoritySpeedStartMetersPerSecond { get; init; } = 12f;

    public float InputBrakeAuthoritySpeedEndMetersPerSecond { get; init; } = 36f;

    public float BrakingInputMultiplierFloor { get; init; } = 0.95f;

    public float BrakingReturnMultiplierFloor { get; init; } = 1.05f;

    public float BrakingInputRateBoost { get; init; } = 1.32f;

    public float RecentBrakeBoostThreshold { get; init; } = 0.12f;

    public float RecentBrakeBoostSeconds { get; init; } = 0.42f;

    public float RecentBrakeAuthority { get; init; } = 0.58f;
}

public sealed class RpmResponseParameters
{
    public float PoweredAntiDipWindowRpm { get; init; } = 6500f;

    public float PoweredAntiDipFallRateRpmPerSecond { get; init; } = 520f;
}
