namespace RType.Vehicle;

public sealed class DifferentialParameters
{
    public float TorqueBiasRatio { get; init; } = 1f;

    public float PreloadTorqueNm { get; init; }

    public float PowerRampAngleDegrees { get; init; } = 45f;

    public float CoastRampAngleDegrees { get; init; } = 70f;

    public float ClutchFrictionCoefficient { get; init; } = 0.32f;

    public float ClutchPressureScale { get; init; } = 0.14f;

    public static DifferentialParameters Open { get; } = new();
}

public sealed class DrivetrainConfiguration
{
    public DrivetrainLayout Layout { get; init; } = DrivetrainLayout.FF;

    public float FrontTorqueShare { get; init; } = 1f;

    public DifferentialParameters FrontDifferential { get; init; } = DifferentialParameters.Open;

    public DifferentialParameters RearDifferential { get; init; } = DifferentialParameters.Open;
}
