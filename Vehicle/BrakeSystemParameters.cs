namespace RType.Vehicle;

public sealed class BrakeSystemParameters
{
    public float MaxLinePressurePa { get; init; } = 8_500_000f;

    public float BrakeBiasFront { get; init; } = 0.67f;

    public float HandbrakeRearTorqueNm { get; init; } = 950f;

    public float PressureRiseRatePerSecond { get; init; } = 3.5f;

    public float PressureReleaseRatePerSecond { get; init; } = 10f;

    public BrakeAxleParameters Front { get; init; } = new();

    public BrakeAxleParameters Rear { get; init; } = new();

    public AbsParameters Abs { get; init; } = new();
}

public sealed class BrakeAxleParameters
{
    public float DiscDiameterMeters { get; init; } = 0.280f;

    public float EffectiveRadiusRatio { get; init; } = 0.42f;

    public float TotalPistonAreaSquareMeters { get; init; } = 0.0022f;

    public float ClampForceMultiplier { get; init; } = 2.0f;

    public float PadFrictionCoefficient { get; init; } = 0.40f;

    public float TorqueAtPressure(float pressurePa)
    {
        float effectiveRadius = DiscDiameterMeters * EffectiveRadiusRatio;
        return pressurePa * TotalPistonAreaSquareMeters * ClampForceMultiplier * PadFrictionCoefficient * effectiveRadius;
    }
}

public sealed class AbsParameters
{
    public bool Enabled { get; init; }

    public float TargetSlipRatio { get; init; } = -0.14f;

    public float ReleaseSlipRatio { get; init; } = -0.22f;

    public float ApplyRatePerSecond { get; init; } = 8f;

    public float ReleaseRatePerSecond { get; init; } = 18f;

    public float MinimumSpeedMetersPerSecond { get; init; } = 2.2f;

    public float MinimumPressureRatio { get; init; } = 0.18f;
}
