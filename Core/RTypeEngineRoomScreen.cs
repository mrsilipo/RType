using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RType.Audio;
using RType.Camera;
using RType.Data;
using RType.Ui;
using RType.Vehicle;

namespace RType.Core;

public sealed class RTypeEngineRoomScreen : IDisposable
{
    private const int Width = UiLayout.Width;
    private const int Height = UiLayout.Height;
    private const string VehicleBuildPath = "Data/VehicleBuilds/ek9_showroom_stock.json";

    private static readonly Color BackgroundColor = new(7, 8, 9);
    private static readonly Color PanelColor = new(18, 20, 22);
    private static readonly Color PanelEdgeColor = new(60, 65, 70);
    private static readonly Color BrandRed = new(227, 0, 0);
    private static readonly Color TextColor = new(226, 232, 236);
    private static readonly Color MutedTextColor = new(128, 138, 146);
    private static readonly Color GaugeColor = new(28, 32, 35);

    private readonly Texture2D _pixel;
    private readonly PixelFont _font;
    private readonly VehicleSimulationParameters _parameters;
    private readonly VehicleAudioSystem _audio = new();
    private readonly VehicleState _vehicle = new();
    private GamePadState _previousGamePad;
    private KeyboardState _previousKeyboard;
    private int _gear = 1;
    private float _rpm;
    private float _speedMetersPerSecond;
    private float _throttle;
    private float _load;
    private bool _dynoLoadEngaged;
    private float _shiftKick;
    private float _crankPhase;
    private float _limiterVisualPhase;
    private bool _wasLimiter;

    internal RTypeEngineRoomScreen(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);
        _parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(VehicleBuildPath);
        _rpm = _parameters.IdleRpm;
        _vehicle.VehicleName = _parameters.DisplayName;
        _vehicle.RedlineRpm = _parameters.RedlineRpm;
        _vehicle.Gear = _gear;
        _vehicle.Rpm = _rpm;
        _vehicle.DisplayedRpm = _rpm;
        _audio.SetVehicle(_parameters.Audio);
    }

    public bool ExitRequested { get; private set; }

    public void Activate()
    {
        ExitRequested = false;
        _previousGamePad = GamePad.GetState(PlayerIndex.One);
        _previousKeyboard = Keyboard.GetState();
    }

    public void ClearExitRequest()
    {
        ExitRequested = false;
    }

    public void Update(GameTime gameTime)
    {
        float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 1f / 20f);
        GamePadState gamePad = GamePad.GetState(PlayerIndex.One);
        KeyboardState keyboard = Keyboard.GetState();

        if (Pressed(gamePad, keyboard, Buttons.Y, Keys.Back) ||
            (keyboard.IsKeyDown(Keys.Escape) && !_previousKeyboard.IsKeyDown(Keys.Escape)))
        {
            ExitRequested = true;
            StopAudio();
            StorePreviousInput(gamePad, keyboard);
            return;
        }

        if (Pressed(gamePad, keyboard, Buttons.X, Keys.R))
        {
            ResetBench();
        }

        if (Pressed(gamePad, keyboard, Buttons.A, Keys.Enter))
        {
            _dynoLoadEngaged = !_dynoLoadEngaged;
        }

        if (Pressed(gamePad, keyboard, Buttons.RightShoulder, Keys.Up))
        {
            ShiftTo(Math.Clamp(_gear + 1, -1, _parameters.ForwardGearRatios.Length));
        }

        if (Pressed(gamePad, keyboard, Buttons.LeftShoulder, Keys.Down))
        {
            ShiftTo(Math.Clamp(_gear - 1, -1, _parameters.ForwardGearRatios.Length));
        }

        float keyboardThrottle = keyboard.IsKeyDown(Keys.Space) ? 1f : 0f;
        float keyboardLoad = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 1f : 0f;
        _throttle = MathHelper.Clamp(MathF.Max(gamePad.Triggers.Right, keyboardThrottle), 0f, 1f);
        _load = MathHelper.Clamp(_dynoLoadEngaged ? MathF.Max(gamePad.Triggers.Left, keyboardLoad) : 0f, 0f, 1f);
        UpdateRaceBench(dt);
        StorePreviousInput(gamePad, keyboard);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        _audio.TryGetRaceEngineState(out RaceEngineAudioState state);
        DrawRect(spriteBatch, new Rectangle(0, 0, Width, Height), BackgroundColor);
        DrawHeader(spriteBatch);
        DrawTachometer(spriteBatch, state);
        DrawControlPanel(spriteBatch, state);
        DrawSamplePanel(spriteBatch);
        DrawReadoutPanel(spriteBatch, state);
    }

    public void StopAudio()
    {
        _audio.Stop();
    }

    public void Dispose()
    {
        StopAudio();
        _audio.Dispose();
        _pixel.Dispose();
    }

    private void UpdateRaceBench(float dt)
    {
        float redline = _parameters.RedlineRpm;
        float vtec = SmoothStep(_parameters.VtecActivationRpm, _parameters.VtecActivationRpm + _parameters.VtecTransitionWidthRpm, _rpm);
        float loadDrag = _dynoLoadEngaged
            ? MathHelper.Lerp(0.18f, 0.82f, _load)
            : 0f;
        float targetRpm = _throttle > 0.02f
            ? MathHelper.Lerp(_parameters.IdleRpm + 600f, redline + _parameters.RevLimiterBounceRpm, MathF.Pow(_throttle, 0.72f))
            : CalculateIdleCycleRpm();
        targetRpm -= loadDrag * MathHelper.Lerp(280f, 1850f, SmoothStep(1800f, redline, _rpm));
        float response = _throttle > 0.02f
            ? MathHelper.Lerp(5.6f, 9.4f, _throttle) * MathHelper.Lerp(1f, 1.16f, vtec)
            : 7.2f;
        response *= MathHelper.Lerp(1f, 0.62f, loadDrag);
        _rpm = MathHelper.Lerp(_rpm, targetRpm, 1f - MathF.Exp(-response * dt));

        bool limiter = _rpm >= redline;
        if (limiter)
        {
            if (!_wasLimiter)
            {
                _limiterVisualPhase = 0f;
            }

            _limiterVisualPhase = RevLimiterPresentationRules.AdvanceBouncePhase(_limiterVisualPhase, redline, dt);
        }
        else
        {
            _limiterVisualPhase = 0f;
        }

        _wasLimiter = limiter;

        float gearRatio = ResolveGearRatio(_gear);
        if (_gear != 0 && gearRatio > 0f)
        {
            float wheelRpm = _rpm / MathF.Max(0.001f, gearRatio * _parameters.FinalDriveRatio);
            float signed = _gear < 0 ? -1f : 1f;
            _speedMetersPerSecond = signed * wheelRpm / 60f * MathHelper.TwoPi * _parameters.WheelRadiusMeters;
        }
        else
        {
            _speedMetersPerSecond = MathHelper.Lerp(_speedMetersPerSecond, 0f, 1f - MathF.Exp(-1.8f * dt));
        }

        _shiftKick = MathHelper.Lerp(_shiftKick, 0f, 1f - MathF.Exp(-18f * dt));
        _crankPhase = (_crankPhase + _rpm / 60f * 360f * dt) % 720f;
        PopulateVehicleState(vtec, limiter);
        RpmPresentationSmoother.Update(_vehicle, dt);
        RaceEnginePresentationBridge.ApplyAudioState(_vehicle, _parameters, dt, _crankPhase);
        _vehicle.EnginePowerUnitLoad = MathHelper.Clamp(_vehicle.EnginePowerUnitLoad + _load * 0.28f, 0f, 1f);

        _audio.Update(_vehicle, CameraMode.Chase1, active: true, paused: false, dt);

    }

    private float CalculateIdleCycleRpm()
    {
        float phase = _crankPhase % 720f;
        if (phase < 0f)
        {
            phase += 720f;
        }

        if (phase < 270f)
        {
            return MathHelper.Lerp(900f, 950f, SmoothStep(0f, 1f, phase / 270f));
        }

        if (phase < 540f)
        {
            return MathHelper.Lerp(950f, 900f, SmoothStep(0f, 1f, (phase - 270f) / 270f));
        }

        return 900f;
    }

    private void PopulateVehicleState(float vtec, bool limiter)
    {
        _vehicle.RedlineRpm = _parameters.RedlineRpm;
        _vehicle.Gear = _gear;
        _vehicle.Rpm = _rpm;
        _vehicle.Throttle = _throttle;
        _vehicle.EffectiveThrottle = _throttle;
        _vehicle.Brake = _load;
        _vehicle.SignedForwardSpeed = _speedMetersPerSecond;
        _vehicle.Velocity = new Vector2(0f, _speedMetersPerSecond);
        _vehicle.RevLimiterActive = limiter;
        _vehicle.RevLimiterBounceIntensity = limiter ? 1f : 0f;
        _vehicle.RevLimiterBouncePhase = limiter ? _limiterVisualPhase : 0f;
        _vehicle.ShiftKickIntensity = _shiftKick;
        _vehicle.PowertrainShockIntensity = _shiftKick * 0.65f;
        _vehicle.EngineBrakeTorqueNm = (1f - _throttle) * SmoothStep(2600f, _parameters.RedlineRpm, _rpm) * 46f;
        _vehicle.DriveForce = _throttle * MathHelper.Lerp(220f, 2550f, SmoothStep(_parameters.IdleRpm, _parameters.RedlineRpm, _rpm));
    }

    private void ShiftTo(int gear)
    {
        if (gear == 0)
        {
            gear = _gear > 0 ? -1 : 1;
        }

        int previous = _gear;
        _gear = gear;
        _vehicle.LastCompletedShiftFromGear = previous;
        _vehicle.LastCompletedShiftToGear = _gear;
        _shiftKick = 1f;
        if (previous != 0 && _gear != 0)
        {
            float previousRatio = ResolveGearRatio(previous);
            float nextRatio = ResolveGearRatio(_gear);
            if (previousRatio > 0f && nextRatio > 0f)
            {
                _rpm = MathHelper.Clamp(_rpm * nextRatio / previousRatio, _parameters.IdleRpm, _parameters.RedlineRpm * 1.08f);
            }
        }
    }

    private float ResolveGearRatio(int gear)
    {
        if (gear < 0)
        {
            return _parameters.ReverseGearRatio;
        }

        if (gear == 0 || _parameters.ForwardGearRatios.Length == 0)
        {
            return 0f;
        }

        return _parameters.ForwardGearRatios[Math.Clamp(gear, 1, _parameters.ForwardGearRatios.Length) - 1];
    }

    private void ResetBench()
    {
        _gear = 1;
        _rpm = _parameters.IdleRpm;
        _vehicle.Rpm = _rpm;
        _vehicle.DisplayedRpm = _rpm;
        _vehicle.DisplayedRpmTarget = _rpm;
        _vehicle.DisplayedRpmVelocity = 0f;
        _speedMetersPerSecond = 0f;
        _shiftKick = 0f;
        _crankPhase = 0f;
        _limiterVisualPhase = 0f;
        _wasLimiter = false;
        _audio.Stop();
    }

    private bool Pressed(GamePadState gamePad, KeyboardState keyboard, Buttons button, Keys key)
    {
        return (gamePad.IsButtonDown(button) && !_previousGamePad.IsButtonDown(button)) ||
               (keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key));
    }

    private void StorePreviousInput(GamePadState gamePad, KeyboardState keyboard)
    {
        _previousGamePad = gamePad;
        _previousKeyboard = keyboard;
    }

    private void DrawHeader(SpriteBatch spriteBatch)
    {
        DrawRect(spriteBatch, new Rectangle(0, 0, Width, 112), new Color(12, 14, 16));
        DrawRect(spriteBatch, new Rectangle(0, 106, Width, 6), BrandRed);
        _font.Draw(spriteBatch, "R TYPE RACE ENGINE ROOM", 52, 32, 6, TextColor);
        _font.Draw(spriteBatch, _parameters.DisplayName, 1140, 28, 3, MutedTextColor);
        _font.Draw(spriteBatch, "RT THROTTLE  LT LOAD  A LOAD ON/OFF  LB/RB GEAR  Y BACK", 1140, 66, 3, MutedTextColor);
    }

    private void DrawTachometer(SpriteBatch spriteBatch, RaceEngineAudioState state)
    {
        Rectangle panel = new(52, 158, 820, 576);
        DrawPanel(spriteBatch, panel);
        _font.Draw(spriteBatch, "TACHOMETER", panel.X + 34, panel.Y + 32, 5, TextColor);
        Rectangle bar = new(panel.X + 60, panel.Y + 320, panel.Width - 120, 52);
        DrawRect(spriteBatch, bar, GaugeColor);
        float displayedRpm = MathF.Max(300f, _vehicle.DisplayedRpm);
        float rpmNorm = MathHelper.Clamp(displayedRpm / MathF.Max(1f, _parameters.RedlineRpm), 0f, 1f);
        DrawRect(spriteBatch, new Rectangle(bar.X, bar.Y, (int)MathF.Round(bar.Width * rpmNorm), bar.Height), rpmNorm > 0.92f ? BrandRed : Color.White);
        int vtecX = bar.X + (int)MathF.Round(bar.Width * MathHelper.Clamp(_parameters.VtecActivationRpm / _parameters.RedlineRpm, 0f, 1f));
        DrawRect(spriteBatch, new Rectangle(vtecX, bar.Y - 24, 6, bar.Height + 48), BrandRed);
        _font.Draw(spriteBatch, "VTEC", vtecX - 42, bar.Y - 56, 3, BrandRed);
        _font.Draw(spriteBatch, $"{displayedRpm:0000} RPM", panel.X + 92, panel.Y + 138, 12, TextColor);
        _font.Draw(spriteBatch, $"LIMIT {_parameters.RedlineRpm:0}", panel.X + 66, panel.Y + 414, 5, MutedTextColor);
        _font.Draw(spriteBatch, $"CUT {(state.LimiterCut ? "ON" : "OFF")}", panel.X + 486, panel.Y + 414, 5, state.LimiterCut ? BrandRed : MutedTextColor);
    }

    private void DrawControlPanel(SpriteBatch spriteBatch, RaceEngineAudioState state)
    {
        Rectangle panel = new(924, 158, 944, 334);
        DrawPanel(spriteBatch, panel);
        _font.Draw(spriteBatch, "RACE AUDIO CONTROLS", panel.X + 34, panel.Y + 32, 5, TextColor);
        DrawBar(spriteBatch, "THROTTLE", _throttle, panel.X + 50, panel.Y + 114, 604, BrandRed);
        DrawBar(spriteBatch, "DYNO LOAD", _load, panel.X + 50, panel.Y + 176, 604, Color.White);
        DrawBar(spriteBatch, "VTEC", state.VtecBlend, panel.X + 50, panel.Y + 238, 604, new Color(122, 195, 255));
        _font.Draw(spriteBatch, $"GEAR {(_gear < 0 ? "R" : _gear.ToString())}", panel.X + 726, panel.Y + 122, 6, TextColor);
        _font.Draw(spriteBatch, _dynoLoadEngaged ? "LOAD ON" : "FREE REV", panel.X + 726, panel.Y + 198, 4, _dynoLoadEngaged ? BrandRed : MutedTextColor);
        _font.Draw(spriteBatch, "SAMPLE RACE", panel.X + 726, panel.Y + 260, 3, BrandRed);
    }

    private void DrawSamplePanel(SpriteBatch spriteBatch)
    {
        Rectangle panel = new(924, 534, 944, 384);
        DrawPanel(spriteBatch, panel);
        _font.Draw(spriteBatch, "ACTIVE SAMPLE SET", panel.X + 34, panel.Y + 32, 5, TextColor);
        int y = panel.Y + 104;
        foreach (EngineAudioSampleParameters sample in _parameters.Audio.EngineSamples)
        {
            string flags = IsRole(sample.Role, "idle") ? "ADD 10%"
                : sample.Limiter ? "LIMITER"
                : sample.HighRpm ? "VTEC"
                : "NORMAL";
            _font.Draw(spriteBatch, $"{flags,-7} {sample.Rpm,4:0}  {Path.GetFileName(sample.Path)}", panel.X + 50, y, 3, TextColor);
            y += 48;
        }
    }

    private void DrawReadoutPanel(SpriteBatch spriteBatch, RaceEngineAudioState state)
    {
        Rectangle panel = new(52, 954, 1816, 84);
        DrawPanel(spriteBatch, panel);
        _font.Draw(
            spriteBatch,
            $"MODE RACE SAMPLE ENGINE   SPD {_vehicle.DisplayedSpeedMetersPerSecond * 3.6f,5:0} KPH   LOAD {_load:0.00}   VOL {state.LastOutputRms:0.000}   PROFILE {state.ProfileId}   HEAVY SIM ENGINE ROOM DISCONNECTED",
            panel.X + 34,
            panel.Y + 30,
            3,
            TextColor);
    }

    private void DrawBar(SpriteBatch spriteBatch, string label, float value, int x, int y, int width, Color fillColor)
    {
        _font.Draw(spriteBatch, label, x, y - 6, 3, MutedTextColor);
        Rectangle bar = new(x + 178, y, width, 36);
        DrawRect(spriteBatch, bar, GaugeColor);
        DrawRect(spriteBatch, new Rectangle(bar.X, bar.Y, (int)MathF.Round(bar.Width * MathHelper.Clamp(value, 0f, 1f)), bar.Height), fillColor);
        _font.Draw(spriteBatch, $"{value * 100f:000}%", bar.Right + 26, y + 4, 3, TextColor);
    }

    private void DrawPanel(SpriteBatch spriteBatch, Rectangle rectangle)
    {
        DrawRect(spriteBatch, rectangle, PanelColor);
        DrawRect(spriteBatch, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, 3), PanelEdgeColor);
        DrawRect(spriteBatch, new Rectangle(rectangle.X, rectangle.Bottom - 3, rectangle.Width, 3), PanelEdgeColor);
        DrawRect(spriteBatch, new Rectangle(rectangle.X, rectangle.Y, 3, rectangle.Height), PanelEdgeColor);
        DrawRect(spriteBatch, new Rectangle(rectangle.Right - 3, rectangle.Y, 3, rectangle.Height), PanelEdgeColor);
    }

    private void DrawRect(SpriteBatch spriteBatch, Rectangle rectangle, Color color)
    {
        spriteBatch.Draw(_pixel, rectangle, color);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static bool IsRole(string role, string expected)
    {
        return role.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
