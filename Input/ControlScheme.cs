namespace RetroRacer.Input;

public sealed class ControlScheme
{
    public int SchemaVersion { get; init; } = 1;

    public string Id { get; init; } = "racing_xbox360_default";

    public string Name { get; init; } = "Xbox 360 Racing Controls";

    public KeyboardControlMap Keyboard { get; init; } = new();

    public GamePadControlMap GamePad { get; init; } = new();

    public MenuControlMap Menu { get; init; } = new();
}

public sealed class KeyboardControlMap
{
    public DigitalAxisBinding Steering { get; init; } = new();

    public RacingActionMap Actions { get; init; } = new();
}

public sealed class GamePadControlMap
{
    public string PlayerIndex { get; init; } = "One";

    public float TriggerPressedThreshold { get; init; } = 0.55f;

    public GamePadSteeringBinding Steering { get; init; } = new();

    public RacingActionMap Actions { get; init; } = new();

    public string[] UnmappedWhileRacing { get; init; } = [];
}

public sealed class DigitalAxisBinding
{
    public string[] Negative { get; init; } = [];

    public string[] Positive { get; init; } = [];
}

public sealed class GamePadSteeringBinding
{
    public string Axis { get; init; } = "LeftThumbstickX";

    public bool InvertAxis { get; init; }

    public float DeadZone { get; init; } = 0.16f;

    public string[] NegativeButtons { get; init; } = [];

    public string[] PositiveButtons { get; init; } = [];
}

public sealed class RacingActionMap
{
    public InputBinding Accelerate { get; init; } = new();

    public InputBinding Brake { get; init; } = new();

    public InputBinding Handbrake { get; init; } = new();

    public InputBinding Reverse { get; init; } = new();

    public InputBinding LookBehind { get; init; } = new();

    public InputBinding ToggleView { get; init; } = new();

    public InputBinding ShiftDown { get; init; } = new();

    public InputBinding ShiftUp { get; init; } = new();

    public InputBinding Pause { get; init; } = new();

    public InputBinding Exit { get; init; } = new();

    public InputBinding ToggleDebug { get; init; } = new();

    public InputBinding ToggleTransmissionMode { get; init; } = new();
}

public sealed class InputBinding
{
    public string[] Keys { get; init; } = [];

    public string[] Buttons { get; init; } = [];

    public string[] Triggers { get; init; } = [];
}

public sealed class MenuControlMap
{
    public InputBinding Confirm { get; init; } = new();

    public InputBinding Cancel { get; init; } = new();

    public MenuNavigationBinding Navigation { get; init; } = new();
}

public sealed class MenuNavigationBinding
{
    public string HorizontalAxis { get; init; } = "LeftThumbstickX";

    public string VerticalAxis { get; init; } = "LeftThumbstickY";

    public float DeadZone { get; init; } = 0.45f;

    public string[] LeftKeys { get; init; } = [];

    public string[] RightKeys { get; init; } = [];

    public string[] UpKeys { get; init; } = [];

    public string[] DownKeys { get; init; } = [];

    public string[] LeftButtons { get; init; } = [];

    public string[] RightButtons { get; init; } = [];

    public string[] UpButtons { get; init; } = [];

    public string[] DownButtons { get; init; } = [];
}
