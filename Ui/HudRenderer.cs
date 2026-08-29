using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RType.Core;
using RType.Vehicle;

namespace RType.Ui;

public sealed class HudRenderer : IDisposable
{
    private const int MainTextScale = 4;
    private const int SecondaryTextScale = 3;
    private const int DebugTextScale = 2;
    private const int TimingOutlinePixels = 3;
    private const float TimingLabelFontSize = 29f;
    private const float TimingValueFontSize = 44f;
    private const int TimingLabelWeight = 600;
    private const int TimingValueWeight = 700;
    private const int PauseMenuWidth = 520;
    private const int PauseMenuHeight = 260;
    private const int PauseMenuRowHeight = 62;
    private const int PauseMenuFirstRowY = 466;
    private const int PauseMenuRowStep = 76;
    private const float PauseMenuTitleSize = 42f;
    private const float PauseMenuOptionSize = 36f;
    private const int PauseMenuTitleWeight = 700;
    private const int PauseMenuOptionWeight = 700;
    private static readonly Color HudTimingLabelColor = new(242, 242, 242, 255);
    private static readonly Color HudTimingValueColor = new(217, 163, 0, 255);
    private static readonly Color HudTimingOutlineColor = new(0, 0, 0, 168);
    private static readonly Color PauseMenuFill = new(0, 0, 0, 196);
    private static readonly Color PauseMenuSelectedFill = new(227, 0, 0, 220);
    private static readonly Color PauseMenuText = new(242, 242, 242, 255);
    private static readonly Color PauseMenuMutedText = new(160, 152, 149, 255);
    private static readonly Color TuningPanelFill = new(0, 0, 0, 204);
    private static readonly Color TuningRowFill = new(255, 255, 255, 18);
    private static readonly Color TuningSelectedFill = new(217, 163, 0, 62);
    private static readonly Color TuningHeadingText = new(217, 163, 0, 255);
    private static readonly Color TuningPathText = new(160, 152, 149, 255);
    private static readonly Color TuningValueText = new(242, 242, 242, 255);
    private static readonly Color TuningImpactText = new(221, 37, 28, 255);
    private static readonly Color TuningInfoFill = new(244, 244, 238, 238);
    private static readonly Color TuningInfoText = new(8, 8, 8, 255);
    private static readonly Color TuningInfoMutedText = new(42, 42, 42, 255);
    private static readonly string[] RacePauseItems = ["Continue", "Exit"];

    private readonly Texture2D _pixel;
    private readonly PixelFont _font;
    private readonly RuntimeFontTextureCache _timingFonts;
    private readonly TachometerHudRenderer _tachometer;

    public HudRenderer(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);
        _timingFonts = new RuntimeFontTextureCache(graphicsDevice, new TachometerFontConfig());
        _tachometer = new TachometerHudRenderer(graphicsDevice, TachometerConfig.CreateEk9Native1080Preset());
        WarmUpTimingFontCache();
    }

    public void DrawTachometer(SpriteBatch spriteBatch, VehicleState vehicle)
    {
        _tachometer.Draw(spriteBatch, TachometerHudState.FromVehicle(vehicle));
    }

    public void Draw(
        SpriteBatch spriteBatch,
        VehicleState vehicle,
        VehicleInput input,
        bool showDebug,
        int fps,
        string viewName,
        bool paused,
        int pauseSelectedIndex,
        bool controllerConnected,
        TimeSpan raceElapsed,
        RaceSessionState? raceSession = null,
        DrivabilityTuningOverlayView? tuning = null)
    {
        string gear = vehicle.Gear < 0 ? "R" : vehicle.Gear == 0 ? "N" : vehicle.Gear.ToString();

        if (raceSession is not null)
        {
            DrawTimingHud(spriteBatch, raceSession);
        }

        if (raceSession?.WrongWay == true)
        {
            DrawCenterNotice(spriteBatch, "WRONG WAY", new Color(106, 20, 18, 220), new Color(255, 226, 192));
        }
        else if (raceSession?.CurrentLapInvalid == true)
        {
            DrawCenterNotice(spriteBatch, "LAP INVALID", new Color(82, 64, 20, 206), new Color(250, 235, 142));
        }

        if (paused)
        {
            DrawRacePauseMenu(spriteBatch, pauseSelectedIndex);
        }

        if (tuning?.Visible == true)
        {
            DrawDrivabilityTuningOverlay(spriteBatch, tuning);
        }

        if (vehicle.CrashFlashSeconds > 0f)
        {
            const int impactWidth = 330;
            int impactX = (UiLayout.Width - impactWidth) / 2;
            DrawPanel(spriteBatch, impactX, 250, impactWidth, 78, new Color(82, 18, 16, 190));
            _font.Draw(spriteBatch, "IMPACT", impactX + 94, 276, MainTextScale, new Color(252, 224, 184));
        }

        if (!showDebug)
        {
            return;
        }

        DrawPanel(spriteBatch, 22, 212, 1120, UiLayout.Height - 238, new Color(0, 0, 0, 172));
        int y = 230;
        DrawDebugLine(spriteBatch, $"CAR {vehicle.VehicleName}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"DEBUG FPS {fps:00} VIEW {viewName}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"INPUT {(controllerConnected ? "PAD" : "KEY")} TRANS {vehicle.TransmissionModeName}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"GEAR {gear} SHIFT {vehicle.ShiftTimeRemainingSeconds:0.00} LIM {vehicle.LimiterTorqueMultiplier:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"SURFACE {vehicle.SurfaceName} GRIP {vehicle.SurfaceGrip:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"VEL F{vehicle.SignedForwardSpeed:0.0} L{vehicle.LateralSpeed:0.0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"ACC F{vehicle.LongitudinalAcceleration:0.0} L{vehicle.LateralAcceleration:0.0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"YAW {MathHelper.ToDegrees(vehicle.YawRateRadiansPerSecond):0.0} DEG/S", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"SLIP R{vehicle.AverageSlipRatio:0.00} A{vehicle.AverageSlipAngleDegrees:0.0} CTR {vehicle.CounterSteerRecoveryIntensity:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"LOAD F {vehicle.FrontLeftLoadN / 1000f:0.0}/{vehicle.FrontRightLoadN / 1000f:0.0}K", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"LOAD R {vehicle.RearLeftLoadN / 1000f:0.0}/{vehicle.RearRightLoadN / 1000f:0.0}K", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"GRIP F {vehicle.FrontLeftGripUsage:0.00}/{vehicle.FrontRightGripUsage:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"GRIP R {vehicle.RearLeftGripUsage:0.00}/{vehicle.RearRightGripUsage:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"THR {vehicle.Throttle:0.0}/{vehicle.EffectiveThrottle:0.0} BRK {vehicle.Brake:0.0} HB {vehicle.Handbrake:0.0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"RPM {vehicle.Rpm:0}/{vehicle.DisplayedRpm:0} RED {vehicle.PowerRedlineRpm:0} CUT {vehicle.LimiterHardCutRpm:0} CLUTCH SLIP {vehicle.ClutchSlipRpm:0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"RTYPE {(vehicle.RTypeEngineActive ? "ON" : "OFF")} {vehicle.RTypeEngineRpm:0}RPM VTEC {vehicle.RTypeEngineVtecBlend:0.00} CUT {(vehicle.RTypeEngineLimiterCut ? "ON" : "OFF")} PK {vehicle.RTypeEngineOutputPeak:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"OVERREV {(vehicle.MechanicalOverRevActive ? "ON" : "OFF")} +{vehicle.MechanicalOverRevRpm:0} SEV {vehicle.MechanicalOverRevSeverity:0.00} SHK {vehicle.PowertrainShockIntensity:0.00} KICK {vehicle.ShiftKickIntensity:0.00} LIM {vehicle.RevLimiterBounceIntensity:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"STR {input.Steer:0.0} FORCE D{vehicle.DriveForce / 1000f:0.0} B{vehicle.BrakeForce / 1000f:0.0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"BTQ F{vehicle.FrontBrakeTorqueNm:0} R{vehicle.RearBrakeTorqueNm:0} EB{vehicle.EngineBrakeTorqueNm:0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"ABS {(vehicle.AbsActive ? "ON" : "OFF")} LOCK {vehicle.LockedWheelCount}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"CAM F {vehicle.FrontLeftCamberDegrees:0.0}/{vehicle.FrontRightCamberDegrees:0.0} R {vehicle.RearLeftCamberDegrees:0.0}/{vehicle.RearRightCamberDegrees:0.0}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"TOE F {vehicle.FrontLeftToeDegrees:0.00}/{vehicle.FrontRightToeDegrees:0.00} R {vehicle.RearLeftToeDegrees:0.00}/{vehicle.RearRightToeDegrees:0.00}", y);
        y += 24;
        DrawDebugLine(spriteBatch, $"WALL {vehicle.WallContactCount} HIT {vehicle.LastImpactSpeedKph:0}KPH DMG {vehicle.CrashSeverity:0.00}", y);
    }

    public void Dispose()
    {
        _tachometer.Dispose();
        _timingFonts.Dispose();
        _pixel.Dispose();
    }

    public static bool TryHitRacePauseItem(Vector2 position, out int index)
    {
        int panelX = (UiLayout.Width - PauseMenuWidth) / 2;
        for (int i = 0; i < RacePauseItems.Length; i++)
        {
            Rectangle row = new(panelX + 42, PauseMenuFirstRowY + i * PauseMenuRowStep, PauseMenuWidth - 84, PauseMenuRowHeight);
            if (row.Contains(position))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private void WarmUpTimingFontCache()
    {
        _timingFonts.Measure(TachometerFontRole.OrbitronSemiBold, "Total Record", TimingLabelFontSize, TimingLabelWeight);
        _timingFonts.Measure(TachometerFontRole.Oswald, "--:--.---", TimingValueFontSize, TimingValueWeight);
        _timingFonts.Measure(TachometerFontRole.Oswald, "99/99", TimingValueFontSize, TimingValueWeight);
    }

    private void DrawPanel(SpriteBatch spriteBatch, int x, int y, int width, int height)
    {
        DrawPanel(spriteBatch, x, y, width, height, new Color(0, 0, 0, 150));
    }

    private void DrawPanel(SpriteBatch spriteBatch, int x, int y, int width, int height, Color fill)
    {
        spriteBatch.Draw(_pixel, new Rectangle(x, y, width, height), fill);
        spriteBatch.Draw(_pixel, new Rectangle(x, y, width, 1), new Color(210, 210, 190, 150));
        spriteBatch.Draw(_pixel, new Rectangle(x, y + height - 1, width, 1), new Color(20, 20, 24, 180));
    }

    private void DrawRacePauseMenu(SpriteBatch spriteBatch, int selectedIndex)
    {
        int safeSelectedIndex = Math.Clamp(selectedIndex, 0, RacePauseItems.Length - 1);
        int panelX = (UiLayout.Width - PauseMenuWidth) / 2;
        int panelY = (UiLayout.Height - PauseMenuHeight) / 2;

        DrawPanel(spriteBatch, panelX, panelY, PauseMenuWidth, PauseMenuHeight, PauseMenuFill);
        DrawCenteredFontText(
            spriteBatch,
            "Paused",
            panelX + PauseMenuWidth / 2f,
            panelY + 34f,
            PauseMenuTitleSize,
            PauseMenuTitleWeight,
            PauseMenuText);

        for (int i = 0; i < RacePauseItems.Length; i++)
        {
            Rectangle row = new(panelX + 42, PauseMenuFirstRowY + i * PauseMenuRowStep, PauseMenuWidth - 84, PauseMenuRowHeight);
            bool selected = i == safeSelectedIndex;
            if (selected)
            {
                spriteBatch.Draw(_pixel, row, PauseMenuSelectedFill);
            }
            else
            {
                spriteBatch.Draw(_pixel, row, new Color(255, 255, 255, 24));
            }

            DrawCenteredFontText(
                spriteBatch,
                RacePauseItems[i],
                row.Center.X,
                row.Y + 10f,
                PauseMenuOptionSize,
                PauseMenuOptionWeight,
                selected ? PauseMenuText : PauseMenuMutedText);
        }
    }

    private void DrawDrivabilityTuningOverlay(SpriteBatch spriteBatch, DrivabilityTuningOverlayView view)
    {
        const int panelX = 32;
        const int panelY = 128;
        const int panelWidth = 980;
        const int panelHeight = 720;
        DrawPanel(spriteBatch, panelX, panelY, panelWidth, panelHeight, TuningPanelFill);
        _font.Draw(spriteBatch, "DRIVABILITY TUNING", panelX + 26, panelY + 24, 4, TuningHeadingText);
        _font.Draw(spriteBatch, $"PAGE {view.Page}/{view.PageCount}   KEYBOARD ONLY   TAB CLOSE   UP/DOWN SELECT   LEFT/RIGHT VALUE   SHIFT INFO   ` DEFAULTS   1 SAVE   2 LOAD", panelX + 26, panelY + 72, 2, TuningPathText);

        int y = panelY + 112;
        string? currentGroup = null;
        foreach (DrivabilityTuningRow row in view.Rows)
        {
            if (!string.Equals(currentGroup, row.Group, StringComparison.Ordinal))
            {
                currentGroup = row.Group;
                _font.Draw(spriteBatch, currentGroup.ToUpperInvariant(), panelX + 26, y, 2, TuningHeadingText);
                y += 24;
            }

            Rectangle rowBounds = new(panelX + 20, y - 5, panelWidth - 40, 28);
            spriteBatch.Draw(_pixel, rowBounds, row.Selected ? TuningSelectedFill : TuningRowFill);
            string marker = row.Selected ? ">" : " ";
            string impact = row.HighImpact ? "*" : " ";
            _font.Draw(spriteBatch, $"{marker}{impact} {row.DisplayName}", panelX + 34, y, 2, row.HighImpact ? TuningImpactText : TuningValueText);
            _font.Draw(spriteBatch, row.Value, panelX + 520, y, 2, TuningValueText);
            _font.Draw(spriteBatch, row.Limits, panelX + 640, y, 2, TuningPathText);
            y += 32;
        }

        if (view.ShowExplanation)
        {
            DrawTuningExplanation(spriteBatch, view, panelX + 26, panelY + 540, panelWidth - 52);
        }

        if (view.LoadListVisible)
        {
            DrawTuningSaveList(spriteBatch, view, panelX + panelWidth + 24, panelY);
        }

        int messageY = panelY + panelHeight - 54;
        foreach (string message in view.Messages)
        {
            _font.Draw(spriteBatch, message.ToUpperInvariant(), panelX + 26, messageY, 2, TuningPathText);
            messageY += 22;
        }
    }

    private void DrawTuningExplanation(SpriteBatch spriteBatch, DrivabilityTuningOverlayView view, int x, int y, int width)
    {
        DrawPanel(spriteBatch, x, y, width, 118, TuningInfoFill);
        _font.Draw(spriteBatch, view.SelectedName.ToUpperInvariant(), x + 18, y + 14, 2, TuningInfoText);
        _font.Draw(spriteBatch, view.SelectedPath, x + 18, y + 38, 2, TuningInfoMutedText);
        _font.Draw(spriteBatch, view.Explanation, x + 18, y + 60, 2, TuningInfoText);
        _font.Draw(spriteBatch, $"HIGHER: {view.HigherText}", x + 18, y + 82, 2, TuningInfoMutedText);
        _font.Draw(spriteBatch, $"LOWER: {view.LowerText}", x + 18, y + 100, 2, TuningInfoMutedText);
    }

    private void DrawTuningSaveList(SpriteBatch spriteBatch, DrivabilityTuningOverlayView view, int x, int y)
    {
        const int panelWidth = 560;
        DrawPanel(spriteBatch, x, y, panelWidth, 420, new Color(0, 0, 0, 218));
        _font.Draw(spriteBatch, "LOAD TUNING", x + 24, y + 24, 3, TuningHeadingText);
        _font.Draw(spriteBatch, "KEYBOARD ONLY   UP/DOWN SELECT   ENTER OR 2 LOAD   ESC BACK", x + 24, y + 64, 2, TuningPathText);
        if (view.Saves.Count == 0)
        {
            _font.Draw(spriteBatch, "NO SAVED VALUES", x + 24, y + 116, 2, TuningValueText);
            return;
        }

        int rowY = y + 112;
        foreach (DrivabilityTuningSaveRow save in view.Saves)
        {
            Rectangle rowBounds = new(x + 18, rowY - 5, panelWidth - 36, 32);
            spriteBatch.Draw(_pixel, rowBounds, save.Selected ? TuningSelectedFill : TuningRowFill);
            _font.Draw(spriteBatch, $"{(save.Selected ? ">" : " ")} {save.Name}", x + 30, rowY, 2, TuningValueText);
            rowY += 38;
        }
    }

    private void DrawCenteredFontText(
        SpriteBatch spriteBatch,
        string text,
        float centerX,
        float y,
        float size,
        int weight,
        Color color)
    {
        Vector2 measured = _timingFonts.Measure(TachometerFontRole.OrbitronSemiBold, text, size, weight);
        _timingFonts.DrawOutlined(
            spriteBatch,
            TachometerFontRole.OrbitronSemiBold,
            text,
            new Vector2(centerX - measured.X * 0.5f, y),
            size,
            weight,
            color,
            HudTimingOutlineColor,
            2);
    }

    private void DrawDebugLine(SpriteBatch spriteBatch, string text, int y)
    {
        _font.Draw(spriteBatch, text, 42, y, DebugTextScale, new Color(222, 232, 222));
    }

    private void DrawRaceStatus(SpriteBatch spriteBatch, RaceSessionState session)
    {
        DrawPanel(spriteBatch, 24, 24, 376, 132);
        _font.Draw(spriteBatch, $"LAP {session.CurrentLap}/{session.TargetLaps}", 48, 42, SecondaryTextScale, new Color(238, 238, 214));
        _font.Draw(spriteBatch, $"SECTOR {session.CurrentSector}", 48, 82, SecondaryTextScale, new Color(184, 196, 190));
        string best = session.BestLapTime is TimeSpan bestLap ? FormatTime(bestLap) : "--:--.--";
        _font.Draw(spriteBatch, $"BEST {best}", 48, 122, SecondaryTextScale, new Color(184, 196, 190));

        if (session.LastSectorTime is TimeSpan sectorTime && session.LastSectorIndex > 0)
        {
            const int splitWidth = 360;
            int splitX = (UiLayout.Width - splitWidth) / 2;
            DrawPanel(spriteBatch, splitX, 24, splitWidth, 62);
            _font.Draw(spriteBatch, $"S{session.LastSectorIndex} {FormatTime(sectorTime)}", splitX + 38, 42, SecondaryTextScale, new Color(250, 235, 142));
        }
    }

    private void DrawTimingHud(SpriteBatch spriteBatch, RaceSessionState session)
    {
        DrawLeftTimingBlock(spriteBatch, "Lap", $"{session.CurrentLap}/{session.TargetLaps}", 16f, 47f);
        DrawLeftTimingBlock(spriteBatch, "Total Time", FormatTime(session.RaceTime), 131f, 162f);
        DrawLeftTimingLabel(spriteBatch, "Lap Time", 251f);

        int visibleLapRows = Math.Clamp(session.TargetLaps, 1, 5);
        int firstVisibleLap = session.TargetLaps <= visibleLapRows
            ? 1
            : Math.Clamp(session.CurrentLap - visibleLapRows + 1, 1, Math.Max(1, session.TargetLaps - visibleLapRows + 1));
        for (int row = 0; row < visibleLapRows; row++)
        {
            int lapNumber = firstVisibleLap + row;
            string value = ResolveLapSlotText(session, lapNumber);
            DrawLeftTimingValue(spriteBatch, value, 282f + row * 50f);
        }

        DrawRightTimingBlock(spriteBatch, "Total Record", "--:--.---", 16f, 47f);
        DrawRightTimingBlock(
            spriteBatch,
            "Fastest Lap",
            session.BestLapTime is TimeSpan bestLap ? FormatTime(bestLap) : "--:--.---",
            133f,
            164f);
    }

    private void DrawLeftTimingBlock(SpriteBatch spriteBatch, string label, string value, float labelY, float valueY)
    {
        DrawLeftTimingLabel(spriteBatch, label, labelY);
        DrawLeftTimingValue(spriteBatch, value, valueY);
    }

    private static string ResolveLapSlotText(RaceSessionState session, int lapNumber)
    {
        if (!session.Finished && lapNumber == session.CurrentLap)
        {
            return FormatTime(session.CurrentLapTime);
        }

        if (lapNumber <= 0 || lapNumber > session.CompletedLaps)
        {
            return "--:--.---";
        }

        int completedIndex = session.CompletedLaps - lapNumber;
        return completedIndex >= 0 && completedIndex < session.CompletedLapTimes.Count
            ? FormatTime(session.CompletedLapTimes[completedIndex])
            : "--:--.---";
    }

    private void DrawLeftTimingLabel(SpriteBatch spriteBatch, string text, float y)
    {
        _timingFonts.DrawOutlined(
            spriteBatch,
            TachometerFontRole.OrbitronSemiBold,
            text,
            new Vector2(20f, y),
            TimingLabelFontSize,
            TimingLabelWeight,
            HudTimingLabelColor,
            HudTimingOutlineColor,
            TimingOutlinePixels);
    }

    private void DrawLeftTimingValue(SpriteBatch spriteBatch, string text, float y)
    {
        _timingFonts.DrawOutlined(
            spriteBatch,
            TachometerFontRole.Oswald,
            text,
            new Vector2(52f, y),
            TimingValueFontSize,
            TimingValueWeight,
            HudTimingValueColor,
            HudTimingOutlineColor,
            TimingOutlinePixels);
    }

    private void DrawRightTimingBlock(SpriteBatch spriteBatch, string label, string value, float labelY, float valueY)
    {
        _timingFonts.DrawRightAlignedOutlined(
            spriteBatch,
            TachometerFontRole.OrbitronSemiBold,
            label,
            new Vector2(1900f, labelY),
            TimingLabelFontSize,
            TimingLabelWeight,
            HudTimingLabelColor,
            HudTimingOutlineColor,
            TimingOutlinePixels);
        _timingFonts.DrawRightAlignedOutlined(
            spriteBatch,
            TachometerFontRole.Oswald,
            value,
            new Vector2(1900f, valueY),
            TimingValueFontSize,
            TimingValueWeight,
            HudTimingValueColor,
            HudTimingOutlineColor,
            TimingOutlinePixels);
    }

    private void DrawCenterNotice(SpriteBatch spriteBatch, string text, Color fill, Color textColor)
    {
        const int scale = 5;
        int width = Math.Max(420, text.Length * 6 * scale + 96);
        int x = (UiLayout.Width - width) / 2;
        DrawPanel(spriteBatch, x, 118, width, 88, fill);
        _font.Draw(spriteBatch, text, x + 48, 146, scale, textColor);
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:0}:{time.Seconds:00}.{time.Milliseconds:000}";
    }
}
