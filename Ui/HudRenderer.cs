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

    private readonly Texture2D _pixel;
    private readonly PixelFont _font;
    private readonly TachometerHudRenderer _tachometer;

    public HudRenderer(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);
        _tachometer = new TachometerHudRenderer(graphicsDevice, TachometerConfig.CreateEk9Native1080Preset());
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

        TimeSpan displayedTime = raceSession?.RaceTime ?? raceElapsed;
        const int timePanelWidth = 390;
        int timePanelX = UiLayout.Width - timePanelWidth - 32;
        DrawPanel(spriteBatch, timePanelX, 24, timePanelWidth, raceSession is null ? 58 : 102);
        _font.Draw(spriteBatch, $"TIME {FormatTime(displayedTime)}", timePanelX + 24, 40, MainTextScale, new Color(220, 244, 206));
        if (raceSession is not null)
        {
            _font.Draw(spriteBatch, $"LAP  {FormatTime(raceSession.CurrentLapTime)}", timePanelX + 24, 82, MainTextScale, new Color(220, 244, 206));
            DrawRaceStatus(spriteBatch, raceSession);
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
        _pixel.Dispose();
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
        return $"{(int)time.TotalMinutes:0}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }
}
