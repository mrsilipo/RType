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

    public TachometerArtworkConfig Artwork { get; init; } = new();

    public static TachometerConfig CreateEk9Preset()
    {
        return CreateEk9Preset(1f, 304f, 132f);
    }

    public static TachometerConfig CreateEk9Native1080Preset()
    {
        const float nativeScale = 1f;
        const float localRightEdge = 538f;
        const float localBottomEdge = 421f;
        return CreateEk9Preset(
            nativeScale,
            1920f - localRightEdge * nativeScale,
            1080f - localBottomEdge * nativeScale);
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
                RpmMinorStep = 200f
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
    public float DialCenterX { get; init; } = 329f;

    public float DialCenterY { get; init; } = 210f;

    public float DialRadius { get; init; } = 193.5f;

    public float DialStartAngle { get; init; } = 120f;

    public float DialEndAngle { get; init; } = 375f;

    public float BezelOuterOffset { get; init; } = 2.8f;

    public float BezelInnerOffset { get; init; } = 1.2f;

    public float RpmArcRadius { get; init; } = 68f;

    public float RpmArcWidth { get; init; } = 0f;
}

public sealed class TachometerTickConfig
{
    public float TickOuterDiameter { get; init; } = 387f;

    public float MajorTickInnerDiameter { get; init; } = 329f;

    public float MinorTickInnerDiameter { get; init; } = 358f;

    public float MajorTickWidth { get; init; } = 5f;

    public float MinorTickWidth { get; init; } = 3f;

    public int MinorTicksPerMajorInterval { get; init; } = 4;
}

public sealed class TachometerNumberConfig
{
    public float RpmNumberFontSize { get; init; } = 37f;

    public int RpmNumberFontWeight { get; init; } = 700;

    public float RpmNumberRadius { get; init; } = 140f;

    public float RpmNumberOffsetX { get; init; } = 0f;

    public float RpmNumberOffsetY { get; init; } = 0f;
}

public sealed class TachometerLabelConfig
{
    public string Text { get; init; } = "x1000";

    public float FontSize { get; init; } = 13f;

    public int FontWeight { get; init; } = 500;

    public float OffsetX { get; init; } = 0f;

    public float OffsetY { get; init; } = -103f;

    public int Tracking { get; init; } = 0;
}

public sealed class TachometerRedlineConfig
{
    public float RedlineStart { get; init; } = 8400f;

    public float RedlineEnd { get; init; } = 10000f;

    public float OuterBandDiameter { get; init; } = 214f;

    public float InnerBandDiameter { get; init; } = 145f;

    public float BandGap { get; init; } = 4f;

    public float SeamOverlayDegrees { get; init; } = 0.18f;

    public float ArcSegmentDegrees { get; init; } = 0.5f;

    public float ArcSegmentOverlapPixels { get; init; } = 2f;

    public float ColourLeadInRpm { get; init; } = 400f;

    public bool ReplaceNormalTickColor { get; init; } = true;
}

public sealed class TachometerNeedleConfig
{
    public float NeedlePivotX { get; init; } = 329f;

    public float NeedlePivotY { get; init; } = 210f;

    public float NeedleLength { get; init; } = 180f;

    public float NeedleWidth { get; init; } = 4.0f;

    public float NeedleTailLength { get; init; } = 29f;

    public float PinUnderOffsetX { get; init; } = 3f;

    public float PinUnderOffsetY { get; init; } = 3f;

    public string NeedleTexturePath { get; init; } = "Assets/Menus/Backgrounds/Racing/RacingPauseMenu_TachometerNeedle.png";

    public string PinUnderTexturePath { get; init; } = "Assets/Menus/Backgrounds/Racing/RacingPauseMenu_TachometerNeedlePinUnderNeedle.png";

    public string PinOverTexturePath { get; init; } = "Assets/Menus/Backgrounds/Racing/RacingPauseMenu_TachometerNeedlePinOverNeedle.png";
}

public sealed class TachometerPanelConfig
{
    public float PanelBorderWidth { get; init; } = 1.2f;

    public float PanelCornerRadius { get; init; } = 5.5f;
}

public sealed class TachometerSpeedDisplayConfig
{
    public int SpeedDigits { get; init; } = 3;

    public float SpeedFontSize { get; init; } = 84f;

    public float SpeedValueRightX { get; init; } = 518f;

    public float SpeedValueTopY { get; init; } = 261f;

    public string SpeedUnit { get; init; } = "km/h";

    public float SpeedUnitFontSize { get; init; } = 23f;

    public int SpeedUnitFontWeight { get; init; } = 600;

    public float SpeedUnitRightX { get; init; } = 518f;

    public float SpeedUnitTopY { get; init; } = 365f;
}

public sealed class TachometerGearDisplayConfig
{
    public float GearFontSize { get; init; } = 58f;

    public float GearValueCenterX { get; init; } = 82f;

    public float GearValueTopY { get; init; } = 332f;

    public float GearValueOffsetY { get; init; } = 8f;

    public float GearLabelFontSize { get; init; } = 28f;

    public int GearLabelFontWeight { get; init; } = 800;

    public float GearLabelCenterX { get; init; } = 81f;

    public float GearLabelTopY { get; init; } = 278f;
}

public sealed class TachometerFontConfig
{
    public string OrbitronPath { get; init; } = "Assets/Fonts/Orbitron/Orbitron-VariableFont_wght.ttf";

    public string OrbitronSemiBoldPath { get; init; } = "Assets/Fonts/Orbitron/static/Orbitron-SemiBold.ttf";

    public string Dseg7ClassicBoldPath { get; init; } = "Assets/Fonts/fonts-DSEG_v046/DSEG7-Classic/DSEG7Classic-Bold.ttf";

    public string OswaldBoldPath { get; init; } = "Assets/Fonts/Oswald/Oswald-Bold.ttf";

    public string Exo2MediumPath { get; init; } = "Assets/Fonts/Exo2/Exo2-VariableFont_wght.ttf";

    public string Exo2BoldItalicPath { get; init; } = "Assets/Fonts/Exo2/Exo2-Italic-VariableFont_wght.ttf";
}

public sealed class TachometerArtworkConfig
{
    public string BackgroundTexturePath { get; init; } = "Assets/Menus/Backgrounds/Racing/RacingPauseMenu_TachometerBackground.png";
}

public sealed class TachometerColours
{
    public Color DialBackground { get; init; } = new(15, 16, 17, 235);

    public Color BezelOuter { get; init; } = new(5, 5, 6, 245);

    public Color BezelInner { get; init; } = new(45, 47, 48, 245);

    public Color RpmArc { get; init; } = new(230, 235, 232, 120);

    public Color MajorTickColor { get; init; } = new(160, 152, 149, 245);

    public Color MinorTickColor { get; init; } = new(160, 152, 149, 245);

    public Color RpmNumberColor { get; init; } = new(160, 152, 149, 245);

    public Color RpmLabelColor { get; init; } = new(160, 152, 149, 245);

    public Color NeedleColor { get; init; } = new(250, 37, 46, 245);

    public Color HubColor { get; init; } = new(68, 70, 72, 245);

    public Color HubInnerColor { get; init; } = new(22, 24, 26, 245);

    public Color RedlineColor { get; init; } = new(255, 42, 48, 235);

    public Color RedlineTickColor { get; init; } = new(221, 37, 28, 245);

    public Color WarningColor { get; init; } = new(216, 127, 23, 245);

    public Color DigitalColor { get; init; } = new(212, 159, 3, 245);

    public Color DigitalInactiveColor { get; init; } = new(48, 30, 8, 34);

    public Color GearColor { get; init; } = new(212, 159, 3, 245);

    public Color SpeedUnitColor { get; init; } = new(160, 152, 149, 245);

    public Color PanelBackgroundColor { get; init; } = new(10, 12, 15, 225);

    public Color PanelBorderColor { get; init; } = new(110, 114, 116, 215);

    public Color PanelShadowColor { get; init; } = new(0, 0, 0, 155);
}
