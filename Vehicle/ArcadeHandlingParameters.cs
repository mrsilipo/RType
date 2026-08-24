namespace RType.Vehicle;

public sealed class ArcadeHandlingParameters
{
    public float PseudoLateralTransferScale { get; init; } = 0.014f;

    public float PseudoLateralTransferBlend { get; init; } = 0.72f;

    public float DrivenGripAllowance { get; init; } = 0.46f;

    public float GenericGripAllowance { get; init; } = 0.05f;

    public float BrakingGripAllowance { get; init; } = 0.46f;

    public float BrakingSlidingFrictionFloor { get; init; } = 0.82f;

    public float PassiveSlideRecoveryLateralSpeedMetersPerSecond { get; init; } = 1.2f;

    public float PassiveSlideRecoveryYawRateDegreesPerSecond { get; init; } = 8.0f;

    public float WallImpactVelocityMultiplier { get; init; } = 0.35f;

    public float WallDirectImpactBlendStart { get; init; } = 0.55f;

    public float WallDirectImpactBlendEnd { get; init; } = 0.92f;

    public float VisualSuspensionMotionScale { get; init; } = 5.60f;

    public float VisualSuspensionHeavePitchScale { get; init; } = 0.35f;

    public float VisualSuspensionLoadTransferMeters { get; init; } = 0.40f;

    public float VisualSuspensionSpringRate { get; init; } = 118f;

    public float VisualSuspensionDampingRate { get; init; } = 26f;

    public float FrontVisualSuspensionMultiplier { get; init; } = 1.12f;

    public float RearVisualSuspensionMultiplier { get; init; } = 0.72f;

    public float VisualBodyPitchScale { get; init; } = 0.82f;

    public float VisualBodyRollScale { get; init; } = 0.55f;

    public float VisualBodyPitchLimitRadians { get; init; } = 0.13f;

    public float VisualBodyRollLimitRadians { get; init; } = 0.06f;
}
