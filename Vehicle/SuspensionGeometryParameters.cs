namespace RType.Vehicle;

public sealed class SuspensionGeometryParameters
{
    public float StaticCamberRadians { get; init; }

    public float StaticToeRadians { get; init; }

    public float CasterRadians { get; init; }

    public float CamberGainRadiansPerMeter { get; init; }

    public float ToeGainRadiansPerMeter { get; init; }

    public float BodyRollCamberMultiplier { get; init; } = 1f;

    public float CasterCamberGain { get; init; } = 0.6f;

    public float MaxCompressionMeters { get; init; } = 0.085f;

    public float MaxDroopMeters { get; init; } = 0.075f;
}
