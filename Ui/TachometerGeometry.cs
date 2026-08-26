using Microsoft.Xna.Framework;

namespace RType.Ui;

internal enum TachometerRpmZone
{
    Normal,
    Warning,
    Red
}

internal readonly record struct TachometerScale(
    float MaxGaugeRpm,
    float FirstRedMajorRpm,
    float WarningMajorRpm,
    float WarningStartRpm,
    float RedStartRpm);

internal static class TachometerGeometry
{
    public static TachometerScale ResolveScale(float redlineRpm, TachometerConfig config)
    {
        float majorStep = MathF.Max(1000f, config.Rpm.RpmMajorStep);
        float firstRedMajorRpm = MathF.Ceiling(MathF.Max(0f, redlineRpm) / majorStep) * majorStep;
        firstRedMajorRpm = MathF.Max(majorStep, firstRedMajorRpm);
        float maxGaugeRpm = firstRedMajorRpm + majorStep;
        float warningMajorRpm = MathF.Max(config.Rpm.RpmMin, firstRedMajorRpm - majorStep);
        float leadInRpm = MathF.Max(0f, config.Redline.ColourLeadInRpm);
        float warningStartRpm = MathF.Max(config.Rpm.RpmMin, warningMajorRpm - leadInRpm);
        float redStartRpm = MathF.Max(config.Rpm.RpmMin, firstRedMajorRpm - leadInRpm);
        return new TachometerScale(maxGaugeRpm, firstRedMajorRpm, warningMajorRpm, warningStartRpm, redStartRpm);
    }

    public static float RpmToAngle(float rpm, float maxGaugeRpm, TachometerConfig config)
    {
        float range = MathF.Max(1f, maxGaugeRpm - config.Rpm.RpmMin);
        float t = MathHelper.Clamp((rpm - config.Rpm.RpmMin) / range, 0f, 1f);
        return MathHelper.Lerp(config.Dial.DialStartAngle, config.Dial.DialEndAngle, t);
    }

    public static TachometerRpmZone ResolveZone(float rpm, TachometerScale scale)
    {
        if (rpm >= scale.RedStartRpm - 0.5f)
        {
            return TachometerRpmZone.Red;
        }

        if (rpm >= scale.WarningStartRpm - 0.5f)
        {
            return TachometerRpmZone.Warning;
        }

        return TachometerRpmZone.Normal;
    }

    public static int CountMajorTicks(TachometerScale scale, TachometerConfig config)
    {
        return CountTicks(config.Rpm.RpmMin, scale.MaxGaugeRpm, config.Rpm.RpmMajorStep);
    }

    public static int CountMinorTicks(TachometerScale scale, TachometerConfig config)
    {
        int allTicks = CountTicks(config.Rpm.RpmMin, scale.MaxGaugeRpm, config.Rpm.RpmMinorStep);
        return allTicks - CountMajorTicks(scale, config);
    }

    private static int CountTicks(float minRpm, float maxRpm, float step)
    {
        step = MathF.Max(1f, step);
        int count = 0;
        float start = MathF.Ceiling(minRpm / step) * step;
        for (float rpm = start; rpm <= maxRpm + 0.5f; rpm += step)
        {
            count++;
        }

        return count;
    }
}
