using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Ui;

public sealed class TachometerHudRenderer : IDisposable
{
    private readonly Texture2D _pixel;
    private readonly RuntimeFontTextureCache _fonts;

    public TachometerHudRenderer(GraphicsDevice graphicsDevice, TachometerConfig config)
    {
        Config = config;
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _fonts = new RuntimeFontTextureCache(graphicsDevice, config.Fonts);
        WarmUpFontCache();
    }

    public TachometerConfig Config { get; }

    private void WarmUpFontCache()
    {
        _fonts.Measure(TachometerFontRole.Orbitron, "x1000 rpm", Scale(Config.RpmLabel.FontSize), Config.RpmLabel.FontWeight);
        _fonts.Measure(TachometerFontRole.Orbitron, "GEAR", Scale(Config.GearDisplay.GearLabelFontSize), Config.GearDisplay.GearLabelFontWeight);
        _fonts.Measure(TachometerFontRole.Orbitron, Config.SpeedDisplay.SpeedUnit, Scale(Config.SpeedDisplay.SpeedUnitFontSize), Config.SpeedDisplay.SpeedUnitFontWeight);
        _fonts.Measure(TachometerFontRole.Dseg7ClassicBold, new string('8', Math.Max(1, Config.SpeedDisplay.SpeedDigits)), Scale(Config.SpeedDisplay.SpeedFontSize), 700);
        _fonts.Measure(TachometerFontRole.Dseg7ClassicBold, "8", Scale(Config.GearDisplay.GearFontSize), 700);
    }

    public void Draw(SpriteBatch spriteBatch, TachometerHudState state)
    {
        float maxGaugeRpm = ResolveGaugeMaxRpm(state.MaxGaugeRpm, state.LimiterHardCutRpm);
        DrawDial(spriteBatch);
        DrawRpmArc(spriteBatch);
        DrawRedline(spriteBatch, state.PowerRedlineRpm, maxGaugeRpm);
        DrawTicks(spriteBatch, state.PowerRedlineRpm, maxGaugeRpm);
        DrawRevLimiterMarker(spriteBatch, state.LimiterHardCutRpm, maxGaugeRpm);
        DrawRpmNumbers(spriteBatch, maxGaugeRpm);
        DrawRpmLabel(spriteBatch);
        DrawNeedle(spriteBatch, state.Rpm, state.LimiterHardCutRpm, maxGaugeRpm, state.RevLimiterActive, state.MechanicalOverRevActive);
        DrawSpeedDisplay(spriteBatch, state.SpeedMetersPerSecond);
        DrawGearDisplay(spriteBatch, state.GearValue);
    }

    public void Dispose()
    {
        _fonts.Dispose();
        _pixel.Dispose();
    }

    private void DrawDial(SpriteBatch spriteBatch)
    {
        Vector2 center = HudPoint(Config.Dial.DialCenterX, Config.Dial.DialCenterY);
        float radius = Scale(Config.Dial.DialRadius);
        DrawFilledCircle(spriteBatch, center, radius + Scale(Config.Dial.BezelOuterOffset), Config.Colours.BezelOuter);
        DrawFilledCircle(spriteBatch, center, radius + Scale(Config.Dial.BezelInnerOffset), Config.Colours.BezelInner);
        DrawFilledCircle(spriteBatch, center, radius, Config.Colours.DialBackground);
    }

    private void DrawRpmArc(SpriteBatch spriteBatch)
    {
        if (Config.Dial.RpmArcWidth <= 0f || Config.Colours.RpmArc.A == 0)
        {
            return;
        }

        Vector2 center = DialCenter();
        DrawArc(
            spriteBatch,
            center,
            Scale(Config.Dial.RpmArcRadius),
            Config.Dial.DialStartAngle,
            Config.Dial.DialEndAngle,
            Scale(Config.Dial.RpmArcWidth),
            Config.Colours.RpmArc);
    }

    private void DrawRedline(SpriteBatch spriteBatch, float powerRedlineRpm, float maxGaugeRpm)
    {
        float start = ResolvePowerRedlineStart(powerRedlineRpm, maxGaugeRpm);
        float end = maxGaugeRpm;
        if (end <= start)
        {
            return;
        }

        Vector2 center = DialCenter();
        DrawArc(
            spriteBatch,
            center,
            Scale(Config.Redline.RedlineArcRadius),
            RpmToAngle(start, maxGaugeRpm),
            RpmToAngle(end, maxGaugeRpm),
            Scale(Config.Redline.RedlineArcWidth),
            Config.Colours.RedlineColor);
    }

    private void DrawTicks(SpriteBatch spriteBatch, float powerRedlineRpm, float maxGaugeRpm)
    {
        float minorStep = CalculateReadableMinorStep();
        foreach (float rpm in EnumerateRpmValues(Config.Rpm.RpmMin, maxGaugeRpm, minorStep))
        {
            if (IsMajorRpm(rpm))
            {
                continue;
            }

            Color color = IsRedlineRpm(rpm, powerRedlineRpm, maxGaugeRpm) && Config.Redline.ReplaceNormalTickColor
                ? Config.Colours.RedlineTickColor
                : Config.Colours.MinorTickColor;
            bool halfMajor = IsHalfMajorRpm(rpm);
            float length = halfMajor ? Config.Ticks.HalfMajorTickLength : Config.Ticks.MinorTickLength;
            float width = halfMajor ? Config.Ticks.HalfMajorTickWidth : Config.Ticks.MinorTickWidth;
            DrawTick(spriteBatch, rpm, length, width, maxGaugeRpm, color);
        }

        foreach (float rpm in EnumerateRpmValues(Config.Rpm.RpmMin, maxGaugeRpm, Config.Rpm.RpmMajorStep))
        {
            Color color = IsRedlineRpm(rpm, powerRedlineRpm, maxGaugeRpm) && Config.Redline.ReplaceNormalTickColor
                ? Config.Colours.RedlineTickColor
                : Config.Colours.MajorTickColor;
            DrawTick(spriteBatch, rpm, Config.Ticks.MajorTickLength, Config.Ticks.MajorTickWidth, maxGaugeRpm, color);
        }
    }

    private void DrawTick(
        SpriteBatch spriteBatch,
        float rpm,
        float localLength,
        float localWidth,
        float maxGaugeRpm,
        Color color)
    {
        Vector2 center = DialCenter();
        Vector2 direction = AngleToVector(RpmToAngle(rpm, maxGaugeRpm));
        float outerRadius = Scale(Config.Dial.DialRadius + Config.Ticks.TickOuterRadiusOffset);
        Vector2 outer = center + direction * outerRadius;
        Vector2 inner = outer - direction * Scale(localLength);
        DrawLine(spriteBatch, inner, outer, Scale(localWidth), color);
    }

    private void DrawRevLimiterMarker(SpriteBatch spriteBatch, float limiterHardCutRpm, float maxGaugeRpm)
    {
        float rpm = MathHelper.Clamp(limiterHardCutRpm, Config.Rpm.RpmMin, maxGaugeRpm);
        DrawTick(
            spriteBatch,
            rpm,
            Config.Redline.RedlineTickLength,
            Config.Redline.RedlineTickWidth,
            maxGaugeRpm,
            Config.Colours.RedlineTickColor);
    }

    private void DrawRpmNumbers(SpriteBatch spriteBatch, float maxGaugeRpm)
    {
        Vector2 center = DialCenter();
        foreach (float rpm in EnumerateRpmValues(Config.Rpm.RpmMin, maxGaugeRpm, Config.Rpm.RpmMajorStep))
        {
            Vector2 direction = AngleToVector(RpmToAngle(rpm, maxGaugeRpm));
            Vector2 numberCenter = center +
                                   direction * Scale(Config.Numbers.RpmNumberRadius) +
                                   ScaleVector(Config.Numbers.RpmNumberOffsetX, Config.Numbers.RpmNumberOffsetY);
            _fonts.DrawCentered(
                spriteBatch,
                TachometerFontRole.Orbitron,
                FormatRpmNumber(rpm),
                numberCenter,
                Scale(Config.Numbers.RpmNumberFontSize),
                Config.Numbers.RpmNumberFontWeight,
                Config.Colours.RpmNumberColor);
        }
    }

    private void DrawRpmLabel(SpriteBatch spriteBatch)
    {
        Vector2 center = DialCenter() + ScaleVector(Config.RpmLabel.OffsetX, Config.RpmLabel.OffsetY);
        _fonts.DrawCentered(
            spriteBatch,
            TachometerFontRole.Orbitron,
            Config.RpmLabel.Text,
            center,
            Scale(Config.RpmLabel.FontSize),
            Config.RpmLabel.FontWeight,
            Config.Colours.RpmLabelColor);
    }

    private void DrawNeedle(
        SpriteBatch spriteBatch,
        float rpm,
        float limiterHardCutRpm,
        float maxGaugeRpm,
        bool revLimiterActive,
        bool mechanicalOverRevActive)
    {
        TachometerNeedleConfig needle = Config.Needle;
        Vector2 pivot = HudPoint(needle.NeedlePivotX, needle.NeedlePivotY);
        float needleRpm = revLimiterActive || mechanicalOverRevActive
            ? MathF.Min(rpm, limiterHardCutRpm)
            : rpm;
        Vector2 direction = AngleToVector(RpmToAngle(needleRpm, maxGaugeRpm));
        Vector2 tip = pivot + direction * Scale(needle.NeedleLength);
        Vector2 tail = pivot - direction * Scale(needle.NeedleTailLength);
        Color needleColor = revLimiterActive || mechanicalOverRevActive
            ? Config.Colours.RedlineTickColor
            : Config.Colours.NeedleColor;

        DrawLine(spriteBatch, tail, tip, Scale(needle.NeedleWidth), needleColor);
        DrawFilledCircle(spriteBatch, pivot, Scale(needle.HubRadius), Config.Colours.HubColor);
        DrawFilledCircle(spriteBatch, pivot, Scale(needle.HubInnerRadius), Config.Colours.HubInnerColor);
    }

    private void DrawSpeedDisplay(SpriteBatch spriteBatch, float speedMetersPerSecond)
    {
        TachometerSpeedDisplayConfig speed = Config.SpeedDisplay;
        RectangleF panel = new(
            speed.SpeedPanelPositionX,
            speed.SpeedPanelPositionY,
            speed.SpeedPanelWidth,
            speed.SpeedPanelHeight);
        DrawClippedPanel(spriteBatch, panel);

        Vector2 panelCenter = HudPoint(panel.X + panel.Width * 0.5f, panel.Y + panel.Height * 0.5f);
        string speedText = FormatSpeed(speedMetersPerSecond);
        string inactiveText = new('8', Math.Max(1, speed.SpeedDigits));
        Vector2 numberCenter = panelCenter + ScaleVector(speed.SpeedNumberOffsetX, speed.SpeedNumberOffsetY);
        _fonts.DrawCentered(
            spriteBatch,
            TachometerFontRole.Dseg7ClassicBold,
            inactiveText,
            numberCenter,
            Scale(speed.SpeedFontSize),
            700,
            Config.Colours.DigitalInactiveColor);
        _fonts.DrawCentered(
            spriteBatch,
            TachometerFontRole.Dseg7ClassicBold,
            speedText,
            numberCenter,
            Scale(speed.SpeedFontSize),
            700,
            Config.Colours.DigitalColor);

        _fonts.DrawCentered(
            spriteBatch,
            TachometerFontRole.Orbitron,
            speed.SpeedUnit,
            panelCenter + ScaleVector(speed.SpeedUnitOffsetX, speed.SpeedUnitOffsetY),
            Scale(speed.SpeedUnitFontSize),
            speed.SpeedUnitFontWeight,
            Config.Colours.DigitalColor);
    }

    private void DrawGearDisplay(SpriteBatch spriteBatch, string gearValue)
    {
        TachometerGearDisplayConfig gear = Config.GearDisplay;
        RectangleF panel = new(
            gear.GearPanelPositionX,
            gear.GearPanelPositionY,
            gear.GearPanelWidth,
            gear.GearPanelHeight);
        DrawClippedPanel(spriteBatch, panel);

        Vector2 panelCenter = HudPoint(panel.X + panel.Width * 0.5f, panel.Y + panel.Height * 0.5f);
        _fonts.DrawCentered(
            spriteBatch,
            TachometerFontRole.Orbitron,
            "GEAR",
            new Vector2(panelCenter.X, HudPoint(0f, panel.Y + gear.GearLabelOffsetY).Y),
            Scale(gear.GearLabelFontSize),
            gear.GearLabelFontWeight,
            Config.Colours.RpmLabelColor);

        bool dsegSupported = gearValue.Length == 1 && gearValue[0] >= '0' && gearValue[0] <= '8';
        TachometerFontRole role = dsegSupported ? TachometerFontRole.Dseg7ClassicBold : TachometerFontRole.Orbitron;
        int weight = dsegSupported ? 700 : 800;
        _fonts.DrawCentered(
            spriteBatch,
            role,
            gearValue,
            new Vector2(panelCenter.X, HudPoint(0f, panel.Y + gear.GearValueOffsetY + panel.Height * 0.5f).Y),
            Scale(gear.GearFontSize),
            weight,
            Config.Colours.GearColor);
    }

    private void DrawClippedPanel(SpriteBatch spriteBatch, RectangleF panel)
    {
        float clip = MathF.Min(
            MathF.Min(panel.Width, panel.Height) * 0.35f,
            Config.Panels.PanelCornerRadius);
        RectangleF shadow = new(panel.X + 2f, panel.Y + 2f, panel.Width, panel.Height);
        FillClippedPanel(spriteBatch, shadow, clip, Config.Colours.PanelShadowColor);
        FillClippedPanel(spriteBatch, panel, clip, Config.Colours.PanelBackgroundColor);

        Vector2[] points =
        [
            HudPoint(panel.X + clip, panel.Y),
            HudPoint(panel.X + panel.Width - clip, panel.Y),
            HudPoint(panel.X + panel.Width, panel.Y + clip),
            HudPoint(panel.X + panel.Width, panel.Y + panel.Height - clip),
            HudPoint(panel.X + panel.Width - clip, panel.Y + panel.Height),
            HudPoint(panel.X + clip, panel.Y + panel.Height),
            HudPoint(panel.X, panel.Y + panel.Height - clip),
            HudPoint(panel.X, panel.Y + clip)
        ];

        for (int i = 0; i < points.Length; i++)
        {
            DrawLine(
                spriteBatch,
                points[i],
                points[(i + 1) % points.Length],
                Scale(Config.Panels.PanelBorderWidth),
                Config.Colours.PanelBorderColor);
        }
    }

    private void FillClippedPanel(SpriteBatch spriteBatch, RectangleF panel, float clip, Color color)
    {
        FillRectangle(spriteBatch, panel.X + clip, panel.Y, panel.Width - clip * 2f, panel.Height, color);
        FillRectangle(spriteBatch, panel.X, panel.Y + clip, panel.Width, panel.Height - clip * 2f, color);
    }

    private void FillRectangle(SpriteBatch spriteBatch, float x, float y, float width, float height, Color color)
    {
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        Vector2 position = HudPoint(x, y);
        spriteBatch.Draw(
            _pixel,
            new Rectangle(
                (int)MathF.Round(position.X),
                (int)MathF.Round(position.Y),
                Math.Max(1, (int)MathF.Round(Scale(width))),
                Math.Max(1, (int)MathF.Round(Scale(height)))),
            color);
    }

    private void DrawFilledCircle(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
    {
        int r = Math.Max(1, (int)MathF.Ceiling(radius));
        for (int y = -r; y <= r; y++)
        {
            float span = MathF.Sqrt(MathF.Max(0f, radius * radius - y * y));
            int x = (int)MathF.Round(center.X - span);
            int width = Math.Max(1, (int)MathF.Round(span * 2f));
            spriteBatch.Draw(_pixel, new Rectangle(x, (int)MathF.Round(center.Y + y), width, 1), color);
        }
    }

    private void DrawArc(
        SpriteBatch spriteBatch,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        float width,
        Color color)
    {
        float sweep = endAngle - startAngle;
        int steps = Math.Max(6, (int)MathF.Ceiling(MathF.Abs(sweep) / 3f));
        Vector2 previous = center + AngleToVector(startAngle) * radius;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 current = center + AngleToVector(MathHelper.Lerp(startAngle, endAngle, t)) * radius;
            DrawLine(spriteBatch, previous, current, width, color);
            previous = current;
        }
    }

    private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, float width, Color color)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length <= 0.001f || width <= 0f)
        {
            return;
        }

        float angle = MathF.Atan2(delta.Y, delta.X);
        spriteBatch.Draw(
            _pixel,
            start,
            null,
            color,
            angle,
            new Vector2(0f, 0.5f),
            new Vector2(length, MathF.Max(1f, width)),
            SpriteEffects.None,
            0f);
    }

    private string FormatSpeed(float speedMetersPerSecond)
    {
        int digits = Math.Max(1, Config.SpeedDisplay.SpeedDigits);
        float multiplier = Config.SpeedDisplay.SpeedUnit.Equals("mi/h", StringComparison.OrdinalIgnoreCase)
            ? 2.2369363f
            : 3.6f;
        int maxValue = (int)MathF.Pow(10f, Math.Min(6, digits)) - 1;
        int speed = Math.Clamp((int)MathF.Round(speedMetersPerSecond * multiplier), 0, maxValue);
        return speed.ToString($"D{digits}", CultureInfo.InvariantCulture);
    }

    private string FormatRpmNumber(float rpm)
    {
        float thousands = rpm / 1000f;
        float rounded = MathF.Round(thousands);
        return MathF.Abs(thousands - rounded) < 0.01f
            ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
            : thousands.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private float CalculateReadableMinorStep()
    {
        float minorStep = MathF.Max(1f, Config.Rpm.RpmMinorStep);
        float range = MathF.Max(1f, Config.Rpm.RpmMax - Config.Rpm.RpmMin);
        int maximumTicks = Math.Max(1, Config.Ticks.MaximumMinorTicks);
        if (range / minorStep <= maximumTicks)
        {
            return minorStep;
        }

        float multiplier = MathF.Ceiling(range / maximumTicks / minorStep);
        return minorStep * multiplier;
    }

    private bool IsMajorRpm(float rpm)
    {
        float majorStep = MathF.Max(1f, Config.Rpm.RpmMajorStep);
        float offset = rpm - Config.Rpm.RpmMin;
        float nearest = MathF.Round(offset / majorStep) * majorStep;
        return MathF.Abs(offset - nearest) < 0.5f;
    }

    private bool IsHalfMajorRpm(float rpm)
    {
        float majorStep = MathF.Max(1f, Config.Rpm.RpmMajorStep);
        float halfStep = majorStep * 0.5f;
        float offset = rpm - Config.Rpm.RpmMin;
        float nearest = MathF.Round((offset - halfStep) / majorStep) * majorStep + halfStep;
        return MathF.Abs(offset - nearest) < 0.5f;
    }

    private bool IsRedlineRpm(float rpm, float powerRedlineRpm, float maxGaugeRpm)
    {
        return rpm >= ResolvePowerRedlineStart(powerRedlineRpm, maxGaugeRpm) && rpm <= maxGaugeRpm;
    }

    private float ResolvePowerRedlineStart(float powerRedlineRpm, float maxGaugeRpm)
    {
        float start = powerRedlineRpm > Config.Rpm.RpmMin
            ? powerRedlineRpm
            : Config.Redline.RedlineStart;
        return MathHelper.Clamp(start, Config.Rpm.RpmMin, maxGaugeRpm);
    }

    private IEnumerable<float> EnumerateRpmValues(float minRpm, float maxRpm, float step)
    {
        step = MathF.Max(1f, step);
        float start = MathF.Ceiling(minRpm / step) * step;
        for (float rpm = start; rpm <= maxRpm + 0.5f; rpm += step)
        {
            if (rpm >= minRpm - 0.5f)
            {
                yield return MathHelper.Clamp(rpm, Config.Rpm.RpmMin, maxRpm);
            }
        }
    }

    private float RpmToAngle(float rpm, float maxGaugeRpm)
    {
        float gaugeMax = ResolveGaugeMaxRpm(maxGaugeRpm, maxGaugeRpm);
        float range = MathF.Max(1f, gaugeMax - Config.Rpm.RpmMin);
        float t = MathHelper.Clamp((rpm - Config.Rpm.RpmMin) / range, 0f, 1f);
        return MathHelper.Lerp(Config.Dial.DialStartAngle, Config.Dial.DialEndAngle, t);
    }

    private float ResolveGaugeMaxRpm(float requestedGaugeMaxRpm, float limiterHardCutRpm)
    {
        float minimum = MathF.Max(Config.Rpm.RpmMin + 1000f, limiterHardCutRpm + 250f);
        return MathF.Max(minimum, requestedGaugeMaxRpm > 0f ? requestedGaugeMaxRpm : Config.Rpm.RpmMax);
    }

    private Vector2 DialCenter()
    {
        return HudPoint(Config.Dial.DialCenterX, Config.Dial.DialCenterY);
    }

    private Vector2 HudPoint(float x, float y)
    {
        return new Vector2(
            Config.HudPositionX + x * Config.HudScale,
            Config.HudPositionY + y * Config.HudScale);
    }

    private Vector2 ScaleVector(float x, float y)
    {
        return new Vector2(x * Config.HudScale, y * Config.HudScale);
    }

    private float Scale(float value)
    {
        return value * Config.HudScale;
    }

    private static Vector2 AngleToVector(float degrees)
    {
        float radians = MathHelper.ToRadians(degrees);
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    private readonly record struct RectangleF(float X, float Y, float Width, float Height);
}
