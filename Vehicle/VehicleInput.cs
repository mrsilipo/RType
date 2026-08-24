namespace RType.Vehicle;

public readonly struct VehicleInput
{
    public VehicleInput(
        float throttle,
        float brake,
        float steer,
        float handbrake = 0f,
        float reverse = 0f,
        bool shiftUpRequested = false,
        bool shiftDownRequested = false,
        bool brakeAssistEnabled = false,
        bool throttleAssistEnabled = false)
    {
        Throttle = Math.Clamp(throttle, 0f, 1f);
        Brake = Math.Clamp(brake, 0f, 1f);
        Steer = Math.Clamp(steer, -1f, 1f);
        Handbrake = Math.Clamp(handbrake, 0f, 1f);
        Reverse = Math.Clamp(reverse, 0f, 1f);
        ShiftUpRequested = shiftUpRequested;
        ShiftDownRequested = shiftDownRequested;
        BrakeAssistEnabled = brakeAssistEnabled;
        ThrottleAssistEnabled = throttleAssistEnabled;
    }

    public float Throttle { get; }

    public float Brake { get; }

    public float Steer { get; }

    public float Handbrake { get; }

    public float Reverse { get; }

    public bool ShiftUpRequested { get; }

    public bool ShiftDownRequested { get; }

    public bool BrakeAssistEnabled { get; }

    public bool ThrottleAssistEnabled { get; }
}
