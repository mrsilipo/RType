namespace RType.Vehicle;

public readonly record struct DrivetrainLimits(
    float IdleRpm,
    float PowerRedlineRpm,
    float LimiterHardCutRpm,
    float LimiterResumeRpm,
    float MaxGaugeRpm);
