namespace RType.Vehicle;

public sealed class TyreAxleParameters
{
    public float LoadedRadiusMeters { get; init; } = 0.33f;

    public float PeakFriction { get; init; } = 1.05f;

    public float RollingResistanceCoefficient { get; init; } = 0.013f;

    public float LoadSensitivity { get; init; } = 0.12f;

    public float CorneringStiffnessNPerRad { get; init; } = 72000f;

    public float LongitudinalStiffnessN { get; init; } = 90000f;

    public float LateralPeakSlipAngleRadians { get; init; } = 0.12f;

    public float LateralSlideSlipAngleRadians { get; init; } = 0.32f;

    public float LateralForceRiseShape { get; init; } = 3.0f;

    public float SlidingLateralFrictionMultiplier { get; init; } = 0.88f;

    public float RelaxationLengthMeters { get; init; } = 0.45f;

    public float LateralScrubDragCoefficient { get; init; } = 0.08f;

    public float IdealCamberRadians { get; init; } = -0.02f;

    public float CamberGripLossPerDegree { get; init; } = 0.025f;

    public float MinimumCamberGripMultiplier { get; init; } = 0.78f;

    public float CamberThrustStiffnessNPerRad { get; init; } = 1200f;

    public float LongitudinalPeakSlipRatio { get; init; } = 0.15f;

    public float LongitudinalForceRiseShape { get; init; } = 3.0f;

    public float LongitudinalSlideSlipRatio { get; init; } = 1.0f;

    public float SlidingFrictionMultiplier { get; init; } = 0.62f;
}
