using RetroRacer.Vehicle;

namespace RetroRacer.Input;

public readonly struct RacingControls
{
    public RacingControls(
        VehicleInput vehicle,
        bool toggleViewRequested,
        bool lookBehind,
        bool pauseRequested,
        bool exitRequested,
        bool toggleDebugRequested,
        bool toggleTransmissionModeRequested,
        bool controllerConnected,
        bool menuConfirmRequested,
        bool menuCancelRequested,
        int menuHorizontal,
        int menuVertical)
    {
        Vehicle = vehicle;
        ToggleViewRequested = toggleViewRequested;
        LookBehind = lookBehind;
        PauseRequested = pauseRequested;
        ExitRequested = exitRequested;
        ToggleDebugRequested = toggleDebugRequested;
        ToggleTransmissionModeRequested = toggleTransmissionModeRequested;
        ControllerConnected = controllerConnected;
        MenuConfirmRequested = menuConfirmRequested;
        MenuCancelRequested = menuCancelRequested;
        MenuHorizontal = Math.Clamp(menuHorizontal, -1, 1);
        MenuVertical = Math.Clamp(menuVertical, -1, 1);
    }

    public VehicleInput Vehicle { get; }

    public bool ToggleViewRequested { get; }

    public bool LookBehind { get; }

    public bool PauseRequested { get; }

    public bool ExitRequested { get; }

    public bool ToggleDebugRequested { get; }

    public bool ToggleTransmissionModeRequested { get; }

    public bool ControllerConnected { get; }

    public bool MenuConfirmRequested { get; }

    public bool MenuCancelRequested { get; }

    public int MenuHorizontal { get; }

    public int MenuVertical { get; }
}
