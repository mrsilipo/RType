using RType.Ui;

namespace RType.Core;

public static class TachometerGeometryProbe
{
    public static void Run()
    {
        TachometerConfig config = TachometerConfig.CreateEk9Native1080Preset();
        VerifyScale(config, 8200f, 10000f, 9000f, 8000f, 7600f, 8600f);
        VerifyScale(config, 7600f, 9000f, 8000f, 7000f, 6600f, 7600f);
        VerifyAngles(config);
        VerifyTickCounts(config);
        Console.WriteLine("Tachometer geometry probe passed.");
    }

    private static void VerifyScale(
        TachometerConfig config,
        float redlineRpm,
        float expectedMaxRpm,
        float expectedFirstRedMajorRpm,
        float expectedWarningMajorRpm,
        float expectedWarningStartRpm,
        float expectedRedStartRpm)
    {
        TachometerScale scale = TachometerGeometry.ResolveScale(redlineRpm, config);
        RequireNear(scale.MaxGaugeRpm, expectedMaxRpm, "max gauge");
        RequireNear(scale.FirstRedMajorRpm, expectedFirstRedMajorRpm, "first red major");
        RequireNear(scale.WarningMajorRpm, expectedWarningMajorRpm, "warning major");
        RequireNear(scale.WarningStartRpm, expectedWarningStartRpm, "warning start");
        RequireNear(scale.RedStartRpm, expectedRedStartRpm, "red start");
        Require(TachometerGeometry.ResolveZone(expectedWarningStartRpm, scale) == TachometerRpmZone.Warning, "warning lead-in did not resolve as warning");
        Require(TachometerGeometry.ResolveZone(expectedRedStartRpm, scale) == TachometerRpmZone.Red, "red lead-in did not resolve as red");
        Require(TachometerGeometry.ResolveZone(expectedRedStartRpm + 200f, scale) == TachometerRpmZone.Red, "red should override any orange region");
    }

    private static void VerifyAngles(TachometerConfig config)
    {
        TachometerScale scale = TachometerGeometry.ResolveScale(8200f, config);
        RequireNear(TachometerGeometry.RpmToAngle(0f, scale.MaxGaugeRpm, config), 120f, "zero angle");
        RequireNear(TachometerGeometry.RpmToAngle(scale.MaxGaugeRpm, scale.MaxGaugeRpm, config), 375f, "max angle");
        RequireNear(TachometerGeometry.RpmToAngle(scale.MaxGaugeRpm * 0.5f, scale.MaxGaugeRpm, config), 247.5f, "middle angle");
    }

    private static void VerifyTickCounts(TachometerConfig config)
    {
        TachometerScale scale = TachometerGeometry.ResolveScale(8200f, config);
        Require(TachometerGeometry.CountMajorTicks(scale, config) == 11, "10000rpm scale should have 11 major ticks");
        Require(TachometerGeometry.CountMinorTicks(scale, config) == 40, "10000rpm scale should have 40 minor ticks");
    }

    private static void RequireNear(float actual, float expected, string label)
    {
        if (MathF.Abs(actual - expected) > 0.01f)
        {
            throw new InvalidOperationException($"Tachometer geometry probe failed: {label} expected {expected:0.##}, got {actual:0.##}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Tachometer geometry probe failed: {message}.");
        }
    }
}
