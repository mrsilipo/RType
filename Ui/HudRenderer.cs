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
    private static readonly Color HudTimingLabelColor = new(242, 242, 242, 255);
    private static readonly Color HudTimingValueColor = new(217, 163, 0, 255);
    private static readonly Color HudTimingOutlineColor = new(0, 0, 0, 168);

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
        bool controllerConnected,
        TimeSpan raceElapsed,
        RaceSessionState? raceSession = null)
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
            const int pauseWidth = 420;
            const int pauseHeight = 144;
            int pauseX = (UiLayout.Width - pauseWidth) / 2;
            int pauseY = (UiLayout.Height - pauseHeight) / 2;
            DrawPanel(spriteBatch, pauseX, pauseY, pauseWidth, pauseHeight, new Color(0, 0, 0, 190));
            _font.Draw(spriteBatch, "PAUSED", pauseX + 120, pauseY + 52, 5, new Color(238, 238, 214));
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
