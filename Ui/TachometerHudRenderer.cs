using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RType.Ui;

public sealed class TachometerHudRenderer : IDisposable
{
    private readonly Texture2D _pixel;
    private readonly Texture2D _background;
    private readonly Texture2D _needleTexture;
    private readonly Texture2D _pinUnderTexture;
    private readonly Texture2D _pinOverTexture;
    private readonly RuntimeFontTextureCache _fonts;

    public TachometerHudRenderer(GraphicsDevice graphicsDevice, TachometerConfig config)
    {
        Config = config;
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _background = LoadTexture(graphicsDevice, config.Artwork.BackgroundTexturePath);
        _needleTexture = LoadTexture(graphicsDevice, config.Needle.NeedleTexturePath);
        _pinUnderTexture = LoadTexture(graphicsDevice, config.Needle.PinUnderTexturePath);
        _pinOverTexture = LoadTexture(graphicsDevice, config.Needle.PinOverTexturePath);
        _fonts = new RuntimeFontTextureCache(graphicsDevice, config.Fonts);
        WarmUpFontCache();
    }

    public TachometerConfig Config { get; }

    private void WarmUpFontCache()
    {
        _fonts.MeasureTracked(
            TachometerFontRole.Exo2Medium,
            Config.RpmLabel.Text,
            Scale(Config.RpmLabel.FontSize),
            Config.RpmLabel.FontWeight,
            Scale(TrackingPixels(Config.RpmLabel)));
        _fonts.Measure(TachometerFontRole.Orbitron, "GEAR", Scale(Config.GearDisplay.GearLabelFontSize), Config.GearDisplay.GearLabelFontWeight);
        _fonts.Measure(TachometerFontRole.OrbitronSemiBold, Config.SpeedDisplay.SpeedUnit, Scale(Config.SpeedDisplay.SpeedUnitFontSize), Config.SpeedDisplay.SpeedUnitFontWeight);
        _fonts.Measure(TachometerFontRole.Oswald, new string('8', Math.Max(1, Config.SpeedDisplay.SpeedDigits)), Scale(Config.SpeedDisplay.SpeedFontSize), 700);
        _fonts.Measure(TachometerFontRole.Oswald, "8", Scale(Config.GearDisplay.GearFontSize), 700);
        _fonts.Measure(TachometerFontRole.Exo2BoldItalic, "10", Scale(Config.Numbers.RpmNumberFontSize), Config.Numbers.RpmNumberFontWeight);
    }

    public void Draw(SpriteBatch spriteBatch, TachometerHudState state)
    {
        TachometerScale scale = TachometerGeometry.ResolveScale(state.PowerRedlineRpm, Config);
        DrawArtwork(spriteBatch, _background, Vector2.Zero, origin: Vector2.Zero, rotationRadians: 0f);
        DrawWarningRedBands(spriteBatch, scale);
        DrawTicks(spriteBatch, scale);
        DrawRpmNumbers(spriteBatch, scale);
        DrawRpmLabel(spriteBatch);
        DrawNeedle(spriteBatch, state.Rpm, scale);
        DrawSpeedDisplay(spriteBatch, state.SpeedMetersPerSecond);
        DrawGearDisplay(spriteBatch, state.GearValue);
    }

    public void Dispose()
    {
        _fonts.Dispose();
        _pinOverTexture.Dispose();
        _pinUnderTexture.Dispose();
        _needleTexture.Dispose();
        _background.Dispose();
        _pixel.Dispose();
    }

    private void DrawTicks(SpriteBatch spriteBatch, TachometerScale scale)
    {
        foreach (float rpm in EnumerateRpmValues(Config.Rpm.RpmMin, scale.MaxGaugeRpm, Config.Rpm.RpmMinorStep))
        {
            if (IsMajorRpm(rpm))
            {
                continue;
            }

            DrawTick(spriteBatch, rpm, major: false, scale, ResolveRpmColour(rpm, scale, Config.Colours.MinorTickColor));
        }

        foreach (float rpm in EnumerateRpmValues(Config.Rpm.RpmMin, scale.MaxGaugeRpm, Config.Rpm.RpmMajorStep))
        {
            DrawTick(spriteBatch, rpm, major: true, scale, ResolveRpmColour(rpm, scale, Config.Colours.MajorTickColor));
        }
    }

    private void DrawTick(SpriteBatch spriteBatch, float rpm, bool major, TachometerScale scale, Color color)
    {
        Vector2 center = DialCenter();
        Vector2 direction = AngleToVector(RpmToAngle(rpm, scale.MaxGaugeRpm));
        float outerRadius = Scale(Config.Ticks.TickOuterDiameter * 0.5f);
        float innerRadius = Scale((major ? Config.Ticks.MajorTickInnerDiameter : Config.Ticks.MinorTickInnerDiameter) * 0.5f);
        Vector2 outer = center + direction * outerRadius;
        Vector2 inner = center + direction * innerRadius;
        DrawLine(spriteBatch, inner, outer, Scale(major ? Config.Ticks.MajorTickWidth : Config.Ticks.MinorTickWidth), color);
    }

    private void DrawWarningRedBands(SpriteBatch spriteBatch, TachometerScale scale)
    {
        float separatorDegrees = ResolveBandSeparatorDegrees();
        DrawBandRange(
            spriteBatch,
            scale.WarningStartRpm,
            scale.RedStartRpm,
            0f,
            -separatorDegrees * 0.5f,
            scale,
            Config.Colours.WarningColor);
        DrawBandRange(
            spriteBatch,
            scale.RedStartRpm,
            scale.MaxGaugeRpm,
            separatorDegrees * 0.5f,
            0f,
            scale,
            Config.Colours.RedlineTickColor);
    }

    private void DrawBandRange(
        SpriteBatch spriteBatch,
        float startRpm,
        float endRpm,
        float startAngleOffset,
        float endAngleOffset,
        TachometerScale scale,
        Color color)
    {
        startRpm = MathHelper.Clamp(startRpm, Config.Rpm.RpmMin, scale.MaxGaugeRpm);
        endRpm = MathHelper.Clamp(endRpm, Config.Rpm.RpmMin, scale.MaxGaugeRpm);
        if (endRpm <= startRpm)
        {
            return;
        }

        float outerRadius = Scale(Config.Redline.OuterBandDiameter * 0.5f);
        float innerRadius = Scale(Config.Redline.InnerBandDiameter * 0.5f);
        float totalBandWidth = MathF.Max(1f, outerRadius - innerRadius);
        float bandRadius = innerRadius + totalBandWidth * 0.5f;
        float startAngle = RpmToAngle(startRpm, scale.MaxGaugeRpm) + startAngleOffset;
        float endAngle = RpmToAngle(endRpm, scale.MaxGaugeRpm) + endAngleOffset;
        Vector2 center = DialCenter();
        DrawArc(spriteBatch, center, bandRadius, startAngle, endAngle, totalBandWidth, color);
        float overlayOffset = Config.Redline.SeamOverlayDegrees;
        if (overlayOffset > 0f)
        {
            DrawArc(
                spriteBatch,
                center,
                bandRadius,
                startAngle - overlayOffset,
                endAngle + overlayOffset,
                totalBandWidth,
                color);
        }
    }

    private void DrawRpmNumbers(SpriteBatch spriteBatch, TachometerScale scale)
    {
        Vector2 center = DialCenter();
        foreach (float rpm in EnumerateRpmValues(Config.Rpm.RpmMin, scale.MaxGaugeRpm, Config.Rpm.RpmMajorStep))
        {
            Vector2 direction = AngleToVector(RpmToAngle(rpm, scale.MaxGaugeRpm));
            Vector2 numberCenter = center +
                                   direction * Scale(Config.Numbers.RpmNumberRadius) +
                                   ScaleVector(Config.Numbers.RpmNumberOffsetX, Config.Numbers.RpmNumberOffsetY);
            _fonts.DrawCentered(
                spriteBatch,
                TachometerFontRole.Exo2BoldItalic,
                FormatRpmNumber(rpm),
                numberCenter,
                Scale(Config.Numbers.RpmNumberFontSize),
                Config.Numbers.RpmNumberFontWeight,
                ResolveRpmColour(rpm, scale, Config.Colours.RpmNumberColor));
        }
    }

    private void DrawRpmLabel(SpriteBatch spriteBatch)
    {
        Vector2 center = DialCenter() + ScaleVector(Config.RpmLabel.OffsetX, Config.RpmLabel.OffsetY);
        if (Config.RpmLabel.Tracking <= 0)
        {
            _fonts.DrawCentered(
                spriteBatch,
                TachometerFontRole.Exo2Medium,
                Config.RpmLabel.Text,
                center,
                Scale(Config.RpmLabel.FontSize),
                Config.RpmLabel.FontWeight,
                Config.Colours.RpmLabelColor);
            return;
        }

        _fonts.DrawTrackedCentered(
            spriteBatch,
            TachometerFontRole.Exo2Medium,
            Config.RpmLabel.Text,
            center,
            Scale(Config.RpmLabel.FontSize),
            Config.RpmLabel.FontWeight,
            Scale(TrackingPixels(Config.RpmLabel)),
            Config.Colours.RpmLabelColor);
    }

    private void DrawNeedle(SpriteBatch spriteBatch, float rpm, TachometerScale scale)
    {
        TachometerNeedleConfig needle = Config.Needle;
        Vector2 pivot = HudPoint(needle.NeedlePivotX, needle.NeedlePivotY);
        DrawArtwork(
            spriteBatch,
            _pinUnderTexture,
            new Vector2(
                needle.NeedlePivotX + needle.PinUnderOffsetX,
                needle.NeedlePivotY + needle.PinUnderOffsetY),
            TextureCenter(_pinUnderTexture),
            0f);
        float angleRadians = MathHelper.ToRadians(RpmToAngle(rpm, scale.MaxGaugeRpm));
        Vector2 needleOrigin = new(needle.NeedleTailLength, _needleTexture.Height * 0.5f);
        spriteBatch.Draw(
            _needleTexture,
            pivot,
            null,
            Color.White,
            angleRadians,
            needleOrigin,
            Config.HudScale,
            SpriteEffects.None,
            0f);
        DrawArtwork(spriteBatch, _pinOverTexture, new Vector2(needle.NeedlePivotX, needle.NeedlePivotY), TextureCenter(_pinOverTexture), 0f);
    }

    private void DrawSpeedDisplay(SpriteBatch spriteBatch, float speedMetersPerSecond)
    {
        TachometerSpeedDisplayConfig speed = Config.SpeedDisplay;
        string speedText = FormatSpeed(speedMetersPerSecond);
        _fonts.DrawRightAligned(
            spriteBatch,
            TachometerFontRole.Oswald,
            speedText,
            HudPoint(speed.SpeedValueRightX, speed.SpeedValueTopY),
            Scale(speed.SpeedFontSize),
            700,
            Config.Colours.DigitalColor);

        _fonts.DrawRightAligned(
            spriteBatch,
            TachometerFontRole.OrbitronSemiBold,
            speed.SpeedUnit,
            HudPoint(speed.SpeedUnitRightX, speed.SpeedUnitTopY),
            Scale(speed.SpeedUnitFontSize),
            speed.SpeedUnitFontWeight,
            Config.Colours.SpeedUnitColor);
    }

    private void DrawGearDisplay(SpriteBatch spriteBatch, string gearValue)
    {
        TachometerGearDisplayConfig gear = Config.GearDisplay;
        float gearLabelSize = Scale(gear.GearLabelFontSize);
        Vector2 gearLabelSizePixels = _fonts.Measure(
            TachometerFontRole.Orbitron,
            "GEAR",
            gearLabelSize,
            gear.GearLabelFontWeight);
        Vector2 gearLabelPosition = HudPoint(gear.GearLabelCenterX, gear.GearLabelTopY) -
                                    new Vector2(gearLabelSizePixels.X * 0.5f, 0f);
        _fonts.Draw(
            spriteBatch,
            TachometerFontRole.Orbitron,
            "GEAR",
            gearLabelPosition,
            gearLabelSize,
            gear.GearLabelFontWeight,
            Config.Colours.RpmLabelColor);

        TachometerSpeedDisplayConfig speed = Config.SpeedDisplay;
        float speedUnitSize = Scale(speed.SpeedUnitFontSize);
        float speedUnitBottomY = HudPoint(speed.SpeedUnitRightX, speed.SpeedUnitTopY).Y +
                                 _fonts.Measure(
                                     TachometerFontRole.OrbitronSemiBold,
                                     speed.SpeedUnit,
                                     speedUnitSize,
                                     speed.SpeedUnitFontWeight).Y;
        float gearValueSize = Scale(gear.GearFontSize);
        Vector2 gearValueSizePixels = _fonts.Measure(TachometerFontRole.Oswald, gearValue, gearValueSize, 700);
        Vector2 gearValuePosition = new(
            HudPoint(gear.GearValueCenterX, 0f).X - gearValueSizePixels.X * 0.5f,
            speedUnitBottomY - gearValueSizePixels.Y + Scale(gear.GearValueOffsetY));
        _fonts.Draw(
            spriteBatch,
            TachometerFontRole.Oswald,
            gearValue,
            gearValuePosition,
            gearValueSize,
            700,
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
        float segmentDegrees = MathF.Max(0.1f, Config.Redline.ArcSegmentDegrees);
        int steps = Math.Max(6, (int)MathF.Ceiling(MathF.Abs(sweep) / segmentDegrees));
        Vector2 previous = center + AngleToVector(startAngle) * radius;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 current = center + AngleToVector(MathHelper.Lerp(startAngle, endAngle, t)) * radius;
            DrawOverlappedLine(
                spriteBatch,
                previous,
                current,
                width + Scale(1f),
                Scale(Config.Redline.ArcSegmentOverlapPixels),
                color);
            previous = current;
        }
    }

    private void DrawOverlappedLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, float width, float overlap, Color color)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length <= 0.001f)
        {
            DrawLine(spriteBatch, start, end, width, color);
            return;
        }

        Vector2 direction = delta / length;
        DrawLine(spriteBatch, start - direction * overlap, end + direction * overlap, width, color);
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
        return speed.ToString("0", CultureInfo.InvariantCulture);
    }

    private string FormatRpmNumber(float rpm)
    {
        float thousands = rpm / 1000f;
        float rounded = MathF.Round(thousands);
        return MathF.Abs(thousands - rounded) < 0.01f
            ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
            : thousands.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private bool IsMajorRpm(float rpm)
    {
        float majorStep = MathF.Max(1f, Config.Rpm.RpmMajorStep);
        float offset = rpm - Config.Rpm.RpmMin;
        float nearest = MathF.Round(offset / majorStep) * majorStep;
        return MathF.Abs(offset - nearest) < 0.5f;
    }

    private Color ResolveRpmColour(float rpm, TachometerScale scale, Color normal)
    {
        if (TachometerGeometry.ResolveZone(rpm, scale) == TachometerRpmZone.Red)
        {
            return Config.Colours.RedlineTickColor;
        }

        if (TachometerGeometry.ResolveZone(rpm, scale) == TachometerRpmZone.Warning)
        {
            return Config.Colours.WarningColor;
        }

        return normal;
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
        float range = MathF.Max(1f, maxGaugeRpm - Config.Rpm.RpmMin);
        float t = MathHelper.Clamp((rpm - Config.Rpm.RpmMin) / range, 0f, 1f);
        return MathHelper.Lerp(Config.Dial.DialStartAngle, Config.Dial.DialEndAngle, t);
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

    private float ResolveBandSeparatorDegrees()
    {
        float outerRadius = Scale(Config.Redline.OuterBandDiameter * 0.5f);
        float innerRadius = Scale(Config.Redline.InnerBandDiameter * 0.5f);
        float centerRadius = MathF.Max(1f, (outerRadius + innerRadius) * 0.5f);
        float radians = Scale(Config.Redline.BandGap) / centerRadius;
        return MathHelper.ToDegrees(radians);
    }

    private static float TrackingPixels(TachometerLabelConfig label)
    {
        return label.FontSize * label.Tracking / 1000f;
    }

    private static Vector2 AngleToVector(float degrees)
    {
        float radians = MathHelper.ToRadians(degrees);
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    private void DrawArtwork(SpriteBatch spriteBatch, Texture2D texture, Vector2 localPosition, Vector2 origin, float rotationRadians)
    {
        spriteBatch.Draw(
            texture,
            HudPoint(localPosition.X, localPosition.Y),
            null,
            Color.White,
            rotationRadians,
            origin,
            Config.HudScale,
            SpriteEffects.None,
            0f);
    }

    private static Vector2 TextureCenter(Texture2D texture)
    {
        return new Vector2(texture.Width * 0.5f, texture.Height * 0.5f);
    }

    private static Texture2D LoadTexture(GraphicsDevice graphicsDevice, string relativePath)
    {
        foreach (string path in GetCandidateAssetPaths(relativePath))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using FileStream stream = File.OpenRead(path);
            return Texture2D.FromStream(graphicsDevice, stream, DefaultColorProcessors.PremultiplyAlpha);
        }

        throw new FileNotFoundException($"Tachometer asset file not found: {relativePath}", relativePath);
    }

    private static IEnumerable<string> GetCandidateAssetPaths(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            yield return relativePath;
            yield break;
        }

        yield return Path.Combine(Environment.CurrentDirectory, relativePath);
        yield return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private readonly record struct RectangleF(float X, float Y, float Width, float Height);
}
