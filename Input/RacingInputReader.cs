using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RType.Vehicle;

namespace RType.Input;

public sealed class RacingInputReader
{
    private readonly ControlScheme _scheme;
    private readonly PlayerIndex _playerIndex;
    private readonly GamePadAxis _gamePadSteeringAxis;
    private readonly float _gamePadSteeringDeadZone;
    private readonly float _triggerPressedThreshold;
    private KeyboardState _previousKeyboard;
    private GamePadState _previousGamePad;

    public RacingInputReader(ControlScheme scheme)
    {
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        ValidateScheme(_scheme);
        _playerIndex = ParsePlayerIndex(_scheme.GamePad.PlayerIndex);
        _gamePadSteeringAxis = ParseAxis(_scheme.GamePad.Steering.Axis);
        _gamePadSteeringDeadZone = MathHelper.Clamp(_scheme.GamePad.Steering.DeadZone, 0f, 0.95f);
        _triggerPressedThreshold = MathHelper.Clamp(_scheme.GamePad.TriggerPressedThreshold, 0f, 1f);
    }

    public RacingControls Read()
    {
        KeyboardState keyboard = Keyboard.GetState();
        GamePadState gamePad = GamePad.GetState(_playerIndex);

        float steer = ReadSteering(keyboard, gamePad);
        float throttle = ReadActionValue(keyboard, gamePad, actions => actions.Accelerate, out bool digitalThrottleActive);
        float brake = ReadActionValue(keyboard, gamePad, actions => actions.Brake, out bool digitalBrakeActive);
        float handbrake = ReadActionValue(keyboard, gamePad, actions => actions.Handbrake, out _);
        float reverse = ReadActionValue(keyboard, gamePad, actions => actions.Reverse, out _);

        VehicleInput vehicle = new(
            throttle,
            brake,
            steer,
            handbrake,
            reverse,
            WasActionPressed(keyboard, gamePad, actions => actions.ShiftUp),
            WasActionPressed(keyboard, gamePad, actions => actions.ShiftDown),
            brakeAssistEnabled: digitalBrakeActive,
            throttleAssistEnabled: digitalThrottleActive);

        int menuHorizontal = ReadMenuAxis(
            keyboard,
            gamePad,
            _scheme.Menu.Navigation.LeftKeys,
            _scheme.Menu.Navigation.RightKeys,
            _scheme.Menu.Navigation.LeftButtons,
            _scheme.Menu.Navigation.RightButtons,
            _scheme.Menu.Navigation.HorizontalAxis,
            false);
        int menuVertical = ReadMenuAxis(
            keyboard,
            gamePad,
            _scheme.Menu.Navigation.UpKeys,
            _scheme.Menu.Navigation.DownKeys,
            _scheme.Menu.Navigation.UpButtons,
            _scheme.Menu.Navigation.DownButtons,
            _scheme.Menu.Navigation.VerticalAxis,
            true);

        RacingControls controls = new(
            vehicle,
            WasActionPressed(keyboard, gamePad, actions => actions.ToggleView),
            IsActionDown(keyboard, gamePad, actions => actions.LookBehind),
            WasActionPressed(keyboard, gamePad, actions => actions.Pause),
            IsActionDown(keyboard, gamePad, actions => actions.Exit),
            WasActionPressed(keyboard, gamePad, actions => actions.ToggleDebug),
            WasActionPressed(keyboard, gamePad, actions => actions.ToggleTransmissionMode),
            gamePad.IsConnected,
            WasBindingPressed(keyboard, gamePad, _scheme.Menu.Confirm),
            WasBindingPressed(keyboard, gamePad, _scheme.Menu.Cancel),
            menuHorizontal,
            menuVertical);

        _previousKeyboard = keyboard;
        _previousGamePad = gamePad;
        return controls;
    }

    private float ReadSteering(KeyboardState keyboard, GamePadState gamePad)
    {
        float steer = ReadAxis(gamePad, _gamePadSteeringAxis);
        if (_scheme.GamePad.Steering.InvertAxis)
        {
            steer = -steer;
        }

        steer = ApplyDeadZone(steer, _gamePadSteeringDeadZone);

        if (AnyKeyDown(keyboard, _scheme.Keyboard.Steering.Negative) ||
            AnyButtonDown(gamePad, _scheme.GamePad.Steering.NegativeButtons))
        {
            steer -= 1f;
        }

        if (AnyKeyDown(keyboard, _scheme.Keyboard.Steering.Positive) ||
            AnyButtonDown(gamePad, _scheme.GamePad.Steering.PositiveButtons))
        {
            steer += 1f;
        }

        return MathHelper.Clamp(steer, -1f, 1f);
    }

    private static float ApplyDeadZone(float value, float deadZone)
    {
        if (MathF.Abs(value) < deadZone)
        {
            return 0f;
        }

        float sign = MathF.Sign(value);
        float normalized = (MathF.Abs(value) - deadZone) / (1f - deadZone);
        return sign * normalized;
    }

    private bool IsActionDown(KeyboardState keyboard, GamePadState gamePad, Func<RacingActionMap, InputBinding> actionSelector)
    {
        InputBinding keyboardBinding = actionSelector(_scheme.Keyboard.Actions);
        InputBinding gamePadBinding = actionSelector(_scheme.GamePad.Actions);
        return AnyKeyDown(keyboard, keyboardBinding.Keys) ||
               AnyButtonDown(gamePad, gamePadBinding.Buttons) ||
               AnyTriggerDown(gamePad, gamePadBinding.Triggers) ||
               AnyAxisDown(gamePad, gamePadBinding.PositiveAxes, positive: true) ||
               AnyAxisDown(gamePad, gamePadBinding.NegativeAxes, positive: false);
    }

    private float ReadActionValue(
        KeyboardState keyboard,
        GamePadState gamePad,
        Func<RacingActionMap, InputBinding> actionSelector,
        out bool digitalActive)
    {
        InputBinding keyboardBinding = actionSelector(_scheme.Keyboard.Actions);
        InputBinding gamePadBinding = actionSelector(_scheme.GamePad.Actions);
        digitalActive = AnyKeyDown(keyboard, keyboardBinding.Keys) || AnyButtonDown(gamePad, gamePadBinding.Buttons);
        float value = digitalActive
            ? 1f
            : 0f;

        foreach (string triggerName in gamePadBinding.Triggers)
        {
            value = MathF.Max(value, ReadTrigger(gamePad, ParseTrigger(triggerName)));
        }

        return MathHelper.Clamp(value, 0f, 1f);
    }

    private bool WasActionPressed(KeyboardState keyboard, GamePadState gamePad, Func<RacingActionMap, InputBinding> actionSelector)
    {
        InputBinding keyboardBinding = actionSelector(_scheme.Keyboard.Actions);
        InputBinding gamePadBinding = actionSelector(_scheme.GamePad.Actions);
        return WasBindingPressed(keyboard, gamePad, keyboardBinding) ||
               WasBindingPressed(keyboard, gamePad, gamePadBinding);
    }

    private bool WasBindingPressed(KeyboardState keyboard, GamePadState gamePad, InputBinding binding)
    {
        return AnyKeyPressed(keyboard, binding.Keys) ||
               AnyButtonPressed(gamePad, binding.Buttons) ||
               AnyTriggerPressed(gamePad, binding.Triggers) ||
               AnyAxisPressed(gamePad, binding.PositiveAxes, positive: true) ||
               AnyAxisPressed(gamePad, binding.NegativeAxes, positive: false);
    }

    private int ReadMenuAxis(
        KeyboardState keyboard,
        GamePadState gamePad,
        IEnumerable<string> negativeKeys,
        IEnumerable<string> positiveKeys,
        IEnumerable<string> negativeButtons,
        IEnumerable<string> positiveButtons,
        string axisName,
        bool vertical)
    {
        bool negativePressed = AnyKeyPressed(keyboard, negativeKeys) || AnyButtonPressed(gamePad, negativeButtons);
        bool positivePressed = AnyKeyPressed(keyboard, positiveKeys) || AnyButtonPressed(gamePad, positiveButtons);
        GamePadAxis axis = ParseAxis(axisName);
        float deadZone = MathHelper.Clamp(_scheme.Menu.Navigation.DeadZone, 0f, 0.95f);
        float currentAxis = ReadAxis(gamePad, axis);
        float previousAxis = ReadAxis(_previousGamePad, axis);

        if (vertical)
        {
            negativePressed |= currentAxis >= deadZone && previousAxis < deadZone;
            positivePressed |= currentAxis <= -deadZone && previousAxis > -deadZone;
        }
        else
        {
            negativePressed |= currentAxis <= -deadZone && previousAxis > -deadZone;
            positivePressed |= currentAxis >= deadZone && previousAxis < deadZone;
        }

        if (negativePressed == positivePressed)
        {
            return 0;
        }

        return negativePressed ? -1 : 1;
    }

    private bool AnyKeyPressed(KeyboardState keyboard, IEnumerable<string> keyNames)
    {
        foreach (string keyName in keyNames)
        {
            Keys key = ParseKey(keyName);
            if (keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyKeyDown(KeyboardState keyboard, IEnumerable<string> keyNames)
    {
        foreach (string keyName in keyNames)
        {
            if (keyboard.IsKeyDown(ParseKey(keyName)))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyButtonPressed(GamePadState gamePad, IEnumerable<string> buttonNames)
    {
        foreach (string buttonName in buttonNames)
        {
            Buttons button = ParseButton(buttonName);
            if (gamePad.IsButtonDown(button) && !_previousGamePad.IsButtonDown(button))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyButtonDown(GamePadState gamePad, IEnumerable<string> buttonNames)
    {
        foreach (string buttonName in buttonNames)
        {
            if (gamePad.IsButtonDown(ParseButton(buttonName)))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyTriggerPressed(GamePadState gamePad, IEnumerable<string> triggerNames)
    {
        foreach (string triggerName in triggerNames)
        {
            GamePadTrigger trigger = ParseTrigger(triggerName);
            if (ReadTrigger(gamePad, trigger) >= _triggerPressedThreshold &&
                ReadTrigger(_previousGamePad, trigger) < _triggerPressedThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyTriggerDown(GamePadState gamePad, IEnumerable<string> triggerNames)
    {
        foreach (string triggerName in triggerNames)
        {
            if (ReadTrigger(gamePad, ParseTrigger(triggerName)) >= _triggerPressedThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyAxisPressed(GamePadState gamePad, IEnumerable<string> axisNames, bool positive)
    {
        foreach (string axisName in axisNames)
        {
            GamePadAxis axis = ParseAxis(axisName);
            if (AxisDirectionDown(gamePad, axis, positive) &&
                !AxisDirectionDown(_previousGamePad, axis, positive))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyAxisDown(GamePadState gamePad, IEnumerable<string> axisNames, bool positive)
    {
        foreach (string axisName in axisNames)
        {
            if (AxisDirectionDown(gamePad, ParseAxis(axisName), positive))
            {
                return true;
            }
        }

        return false;
    }

    private bool AxisDirectionDown(GamePadState gamePad, GamePadAxis axis, bool positive)
    {
        float value = ReadAxis(gamePad, axis);
        return positive
            ? value >= _triggerPressedThreshold
            : value <= -_triggerPressedThreshold;
    }

    private static float ReadAxis(GamePadState gamePad, GamePadAxis axis)
    {
        return axis switch
        {
            GamePadAxis.LeftThumbstickX => gamePad.ThumbSticks.Left.X,
            GamePadAxis.LeftThumbstickY => gamePad.ThumbSticks.Left.Y,
            GamePadAxis.RightThumbstickX => gamePad.ThumbSticks.Right.X,
            GamePadAxis.RightThumbstickY => gamePad.ThumbSticks.Right.Y,
            GamePadAxis.LeftTrigger => gamePad.Triggers.Left,
            GamePadAxis.RightTrigger => gamePad.Triggers.Right,
            _ => 0f
        };
    }

    private static float ReadTrigger(GamePadState gamePad, GamePadTrigger trigger)
    {
        return trigger == GamePadTrigger.Left ? gamePad.Triggers.Left : gamePad.Triggers.Right;
    }

    private static Keys ParseKey(string name)
    {
        return Normalize(name) switch
        {
            "esc" => Keys.Escape,
            "return" => Keys.Enter,
            "spacebar" => Keys.Space,
            "leftarrow" => Keys.Left,
            "rightarrow" => Keys.Right,
            "uparrow" => Keys.Up,
            "downarrow" => Keys.Down,
            _ => Enum.TryParse(name, true, out Keys key)
                ? key
                : throw new InvalidOperationException($"Unknown keyboard key '{name}' in control scheme.")
        };
    }

    private static Buttons ParseButton(string name)
    {
        return Normalize(name) switch
        {
            "l1" or "lb" or "leftbumper" => Buttons.LeftShoulder,
            "r1" or "rb" or "rightbumper" => Buttons.RightShoulder,
            "select" => Buttons.Back,
            _ => Enum.TryParse(name, true, out Buttons button)
                ? button
                : throw new InvalidOperationException($"Unknown gamepad button '{name}' in control scheme.")
        };
    }

    private static GamePadTrigger ParseTrigger(string name)
    {
        return Normalize(name) switch
        {
            "left" or "lefttrigger" or "l2" or "lt" => GamePadTrigger.Left,
            "right" or "righttrigger" or "r2" or "rt" => GamePadTrigger.Right,
            _ => throw new InvalidOperationException($"Unknown gamepad trigger '{name}' in control scheme.")
        };
    }

    private static GamePadAxis ParseAxis(string name)
    {
        return Normalize(name) switch
        {
            "leftthumbstickx" or "leftstickx" or "lstickx" or "lx" => GamePadAxis.LeftThumbstickX,
            "leftthumbsticky" or "leftsticky" or "lsticky" or "ly" => GamePadAxis.LeftThumbstickY,
            "rightthumbstickx" or "rightstickx" or "rstickx" or "rx" => GamePadAxis.RightThumbstickX,
            "rightthumbsticky" or "rightsticky" or "rsticky" or "ry" => GamePadAxis.RightThumbstickY,
            "lefttrigger" or "l2" or "lt" => GamePadAxis.LeftTrigger,
            "righttrigger" or "r2" or "rt" => GamePadAxis.RightTrigger,
            _ => throw new InvalidOperationException($"Unknown gamepad axis '{name}' in control scheme.")
        };
    }

    private static PlayerIndex ParsePlayerIndex(string name)
    {
        return Enum.TryParse(name, true, out PlayerIndex playerIndex)
            ? playerIndex
            : throw new InvalidOperationException($"Unknown gamepad player index '{name}' in control scheme.");
    }

    private static string Normalize(string value)
    {
        return value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static void ValidateScheme(ControlScheme scheme)
    {
        _ = ParsePlayerIndex(scheme.GamePad.PlayerIndex);
        _ = ParseAxis(scheme.GamePad.Steering.Axis);

        foreach (string key in scheme.Keyboard.Steering.Negative.Concat(scheme.Keyboard.Steering.Positive))
        {
            _ = ParseKey(key);
        }

        foreach (string button in scheme.GamePad.Steering.NegativeButtons.Concat(scheme.GamePad.Steering.PositiveButtons))
        {
            _ = ParseButton(button);
        }

        foreach (InputBinding binding in EnumerateBindings(scheme.Keyboard.Actions))
        {
            foreach (string key in binding.Keys)
            {
                _ = ParseKey(key);
            }
        }

        foreach (InputBinding binding in EnumerateBindings(scheme.GamePad.Actions))
        {
            ValidateGamePadBinding(binding);
        }

        ValidateKeyboardBinding(scheme.Menu.Confirm);
        ValidateKeyboardBinding(scheme.Menu.Cancel);
        ValidateGamePadBinding(scheme.Menu.Confirm);
        ValidateGamePadBinding(scheme.Menu.Cancel);
        _ = ParseAxis(scheme.Menu.Navigation.HorizontalAxis);
        _ = ParseAxis(scheme.Menu.Navigation.VerticalAxis);

        foreach (string key in scheme.Menu.Navigation.LeftKeys
                     .Concat(scheme.Menu.Navigation.RightKeys)
                     .Concat(scheme.Menu.Navigation.UpKeys)
                     .Concat(scheme.Menu.Navigation.DownKeys))
        {
            _ = ParseKey(key);
        }

        foreach (string button in scheme.Menu.Navigation.LeftButtons
                     .Concat(scheme.Menu.Navigation.RightButtons)
                     .Concat(scheme.Menu.Navigation.UpButtons)
                     .Concat(scheme.Menu.Navigation.DownButtons))
        {
            _ = ParseButton(button);
        }
    }

    private static void ValidateKeyboardBinding(InputBinding binding)
    {
        foreach (string key in binding.Keys)
        {
            _ = ParseKey(key);
        }
    }

    private static void ValidateGamePadBinding(InputBinding binding)
    {
        foreach (string button in binding.Buttons)
        {
            _ = ParseButton(button);
        }

        foreach (string trigger in binding.Triggers)
        {
            _ = ParseTrigger(trigger);
        }

        foreach (string axis in binding.PositiveAxes.Concat(binding.NegativeAxes))
        {
            _ = ParseAxis(axis);
        }
    }

    private static IEnumerable<InputBinding> EnumerateBindings(RacingActionMap actions)
    {
        yield return actions.Accelerate;
        yield return actions.Brake;
        yield return actions.Handbrake;
        yield return actions.Reverse;
        yield return actions.LookBehind;
        yield return actions.ToggleView;
        yield return actions.ShiftDown;
        yield return actions.ShiftUp;
        yield return actions.Pause;
        yield return actions.Exit;
        yield return actions.ToggleDebug;
        yield return actions.ToggleTransmissionMode;
    }

    private enum GamePadAxis
    {
        LeftThumbstickX,
        LeftThumbstickY,
        RightThumbstickX,
        RightThumbstickY,
        LeftTrigger,
        RightTrigger
    }

    private enum GamePadTrigger
    {
        Left,
        Right
    }
}
