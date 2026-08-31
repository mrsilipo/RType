namespace RType.Vehicle;

public sealed class ClassicFourWheelAssistOptions
{
    public static ClassicFourWheelAssistOptions Default { get; } = new();

    public bool BodySlipDampingEnabled { get; init; } = true;

    public bool LateralVelocityDampingEnabled { get; init; } = true;

    public bool RearFollowEnabled { get; init; } = true;

    public bool YawRecoveryEnabled { get; init; } = true;

    public bool SpeedRetentionEnabled { get; init; } = true;
}

