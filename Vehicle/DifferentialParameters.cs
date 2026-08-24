namespace RType.Vehicle;

public sealed class DifferentialParameters
{
    public float TorqueBiasRatio { get; init; } = 1f;

    public float PreloadTorqueNm { get; init; }

    public float PowerRampAngleDegrees { get; init; } = 45f;

    public float CoastRampAngleDegrees { get; init; } = 70f;

    public float ClutchFrictionCoefficient { get; init; } = 0.32f;

    public float ClutchPressureScale { get; init; } = 0.14f;
}
