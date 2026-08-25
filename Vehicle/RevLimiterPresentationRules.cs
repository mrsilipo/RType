using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public static class RevLimiterPresentationRules
{
    private const float HighestReferenceRedlineRpm = 12000f;
    private const float SlowestBounceSeconds = 1.00f;
    private const float FastestBounceSeconds = 0.25f;

    public static float CalculateBounceDepthRpm(float redlineRpm)
    {
        return MathF.Max(80f, MathF.Max(450f, redlineRpm) * 0.08f);
    }

    public static float CalculateBounceSeconds(float redlineRpm)
    {
        float t = MathHelper.Clamp((redlineRpm - 4500f) / (HighestReferenceRedlineRpm - 4500f), 0f, 1f);
        return MathHelper.Lerp(SlowestBounceSeconds, FastestBounceSeconds, t);
    }

    public static float AdvanceBouncePhase(float phase, float redlineRpm, float deltaSeconds)
    {
        float bounceSeconds = MathF.Max(0.001f, CalculateBounceSeconds(redlineRpm));
        float next = phase + MathHelper.Clamp(deltaSeconds, 0f, 1f / 20f) / bounceSeconds;
        return next - MathF.Floor(next);
    }

    public static float CalculateBouncedRpm(float redlineRpm, float phase)
    {
        float redline = MathF.Max(450f, redlineRpm);
        float wave = 0.5f - 0.5f * MathF.Cos((phase - MathF.Floor(phase)) * MathF.Tau);
        return MathHelper.Clamp(redline - CalculateBounceDepthRpm(redline) * wave, 450f, redline);
    }
}
