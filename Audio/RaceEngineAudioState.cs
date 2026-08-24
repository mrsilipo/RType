namespace RType.Audio;

public readonly record struct RaceEngineAudioState(
    bool Active,
    string ProfileId,
    float Rpm,
    float CrankPhaseDegrees,
    float VtecBlend,
    bool LimiterCut,
    float RevLimitTimerSeconds,
    int LastIgnitedCylinder,
    float LastThrottle,
    float LastOutputPeak,
    float LastOutputRms);
