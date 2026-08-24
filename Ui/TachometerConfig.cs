using Microsoft.Xna.Framework;

namespace RType.Ui;

public sealed class TachometerConfig
{
    public float HudScale { get; init; } = 1f;

    public float HudPositionX { get; init; } = 304f;

    public float HudPositionY { get; init; } = 132f;

    public TachometerRpmConfig Rpm { get; init; } = new();

    public TachometerDialConfig Dial { get; init; } = new();

    public TachometerTickConfig Ticks { get; init; } = new();

    public TachometerNumberConfig Numbers { get; init; } = new();

    public TachometerLabelConfig RpmLabel { get; init; } = new();

    public TachometerRedlineConfig Redline { get; init; } = new();

    public TachometerNeedleConfig Needle { get; init; } = new();

    public TachometerPanelConfig Panels { get; init; } = new();

    public TachometerSpeedDisplayConfig SpeedDisplay { get; init; } = new();

    public TachometerGearDisplayConfig GearDisplay { get; init; } = new();

    public TachometerFontConfig Fonts { get; init; } = new();

    public TachometerColours Colours { get; init; } = new();

    public static TachometerConfig CreateEk9Preset()
    {
        return CreateEk9Preset(1f, 304f, 132f);
    }

    public static TachometerConfig CreateEk9Native1080Preset()
    {
        const float nativeScale = 2.95f;
        const float localRightEdge = 174f;
        const float localBottomEdge = 162f;
        const float rightMargin = 30f;
        const float bottomMargin = 28f;
        return CreateEk9Preset(
            nativeScale,
            1920f - rightMargin - localRightEdge * nativeScale,
            1080f - bottomMargin - localBottomEdge * nativeScale);
    }

    private static TachometerConfig CreateEk9Preset(float hudScale, float hudPositionX, float hudPositionY)
    {
        return new TachometerConfig
        {
            HudScale = hudScale,
            HudPositionX = hudPositionX,
            HudPositionY = hudPositionY,
            Rpm = new TachometerRpmConfig
            {
                RpmMin = 0f,
                RpmMax = 10000f,
                RpmMajorStep = 1000f,
                RpmMinorStep = 100f
            },
            Redline = new TachometerRedlineConfig
            {
                RedlineStart = 8400f,
                RedlineEnd = 10000f
            }
        };
    }
}

public sealed class TachometerRpmConfig
{
    public float RpmMin { get; init; } = 0f;

    public float RpmMax { get; init; } = 10000f;

    public float RpmMajorStep { get; init; } = 1000f;

    public float RpmMinorStep { get; init; } = 100f;
}

public sealed class TachometerDialConfig
{
    public float DialCenterX { get; init; } = 92f;

    public float DialCenterY { get; init; } = 78f;

    public float DialRadius { get; init; } = 74f;

    public float DialStartAngle { get; init; } = 152f;

    public float DialEndAngle { get; init; } = 378f;

    public float BezelOuterOffset { get; init; } = 2.8f;

    public float BezelInnerOffset { get; init; } = 1.2f;

    public float RpmArcRadius { get; init; } = 68f;

    public float RpmArcWidth { get; init; } = 0f;
}

public sealed class TachometerTickConfig
{
    public float MajorTickLength { get; init; } = 10.5f;

    public float MajorTickWidth { get; init; } = 2.1f;

    public float MinorTickLength { get; init; } = 4.4f;

    public float MinorTickWidth { get; init; } = 0.75f;

    public float HalfMajorTickLength { get; init; } = 5.8f;

    public float HalfMajorTickWidth { get; init; } = 0.95f;

    public float TickOuterRadiusOffset { get; init; } = -5f;

    public int MaximumMinorTicks { get; init; } = 180;
}

public sealed class TachometerNumberConfig
{
    public float RpmNumberFontSize { get; init; } = 7.4f;

    public int RpmNumberFontWeight { get; init; } = 800;

    public float RpmNumberRadius { get; init; } = 50f;

    public float RpmNumberOffsetX { get; init; } = 0f;

    public float RpmNumberOffsetY { get; init; } = 0f;
}

public sealed class TachometerLabelConfig
{
    public string Text { get; init; } = "x1000 rpm";

    public float FontSize { get; init; } = 5f;

    public int FontWeight { get; init; } = 700;

    public float OffsetX { get; init; } = 0f;

    public float OffsetY { get; init; } = -26f;
}

public sealed class TachometerRedlineConfig
{
    public float RedlineStart { get; init; } = 8400f;

    public float RedlineEnd { get; init; } = 10000f;

    public float RedlineArcRadius { get; init; } = 71f;

    public float RedlineArcWidth { get; init; } = 3f;

    public float RedlineTickWidth { get; init; } = 1.35f;

    public float RedlineTickLength { get; init; } = 10f;

    public bool ReplaceNormalTickColor { get; init; } = true;
}

public sealed class TachometerNeedleConfig
{
    public float NeedlePivotX { get; init; } = 92f;

    public float NeedlePivotY { get; init; } = 78f;

    public float NeedleLength { get; init; } = 61f;

    public float NeedleWidth { get; init; } = 2.0f;

    public float NeedleTailLength { get; init; } = 5f;

    public float HubRadius { get; init; } = 4.0f;

    public float HubInnerRadius { get; init; } = 1.7f;
}

public sealed class TachometerPanelConfig
{
    public float PanelBorderWidth { get; init; } = 1.2f;

    public float PanelCornerRadius { get; init; } = 5.5f;
}

public sealed class TachometerSpeedDisplayConfig
{
    public int SpeedDigits { get; init; } = 3;

    public float SpeedFontSize { get; init; } = 26f;

    public float SpeedPanelWidth { get; init; } = 78f;

    public float SpeedPanelHeight { get; init; } = 44f;

    public float SpeedPanelPositionX { get; init; } = 54f;

    public float SpeedPanelPositionY { get; init; } = 110f;

    public string SpeedUnit { get; init; } = "km/h";

    public float SpeedUnitFontSize { get; init; } = 7.2f;

    public int SpeedUnitFontWeight { get; init; } = 600;

    public float SpeedUnitOffsetX { get; init; } = 0f;

    public float SpeedUnitOffsetY { get; init; } = 16.5f;

    public float SpeedNumberOffsetX { get; init; } = 0f;

    public float SpeedNumberOffsetY { get; init; } = -2.5f;
}

public sealed class TachometerGearDisplayConfig
{
    public float GearFontSize { get; init; } = 22f;

    public float GearPanelWidth { get; init; } = 26f;

    public float GearPanelHeight { get; init; } = 44f;

    public float GearPanelPositionX { get; init; } = 145f;

    public float GearPanelPositionY { get; init; } = 110f;

    public float GearLabelFontSize { get; init; } = 6.6f;

    public int GearLabelFontWeight { get; init; } = 800;

    public float GearLabelOffsetY { get; init; } = 8.2f;

    public float GearValueOffsetY { get; init; } = 6f;
}

public sealed class TachometerFontConfig
{
    public string OrbitronPath { get; init; } = "Assets/Fonts/Orbitron/Orbitron-VariableFont_wght.ttf";

    public string Dseg7ClassicBoldPath { get; init; } = "Assets/Fonts/fonts-DSEG_v046/DSEG7-Classic/DSEG7Classic-Bold.ttf";
}

public sealed class TachometerColours
{
    public Color DialBackground { get; init; } = new(15, 16, 17, 235);

    public Color BezelOuter { get; init; } = new(5, 5, 6, 245);

    public Color BezelInner { get; init; } = new(45, 47, 48, 245);

    public Color RpmArc { get; init; } = new(230, 235, 232, 120);

    public Color MajorTickColor { get; init; } = new(240, 242, 238, 245);

    public Color MinorTickColor { get; init; } = new(218, 224, 222, 230);

    public Color RpmNumberColor { get; init; } = new(238, 240, 236, 245);

    public Color RpmLabelColor { get; init; } = new(226, 230, 228, 235);

    public Color NeedleColor { get; init; } = new(250, 37, 46, 245);

    public Color HubColor { get; init; } = new(68, 70, 72, 245);

    public Color HubInnerColor { get; init; } = new(22, 24, 26, 245);

    public Color RedlineColor { get; init; } = new(255, 42, 48, 235);

    public Color RedlineTickColor { get; init; } = new(255, 52, 54, 245);

    public Color DigitalColor { get; init; } = new(255, 145, 8, 245);

    public Color DigitalInactiveColor { get; init; } = new(48, 30, 8, 34);

    public Color GearColor { get; init; } = new(255, 145, 8, 245);

    public Color PanelBackgroundColor { get; init; } = new(10, 12, 15, 225);

    public Color PanelBorderColor { get; init; } = new(110, 114, 116, 215);

    public Color PanelShadowColor { get; init; } = new(0, 0, 0, 155);
}
