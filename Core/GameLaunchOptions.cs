namespace RType.Core;

public sealed record GameLaunchOptions(
    int? AutoExitMilliseconds,
    string VehicleDefinitionPath,
    bool StartInManualTransmission,
    string ControlSchemePath,
    string SurfaceDefinitionPath,
    string SimulationEngineDefinitionPath)
{
    public const string DefaultVehicleDefinitionPath = "Data/Vehicles/ek9_reference_2000.json";
    public const string DefaultControlSchemePath = "Data/Controls/racing_xbox360_default.json";
    public const string DefaultSurfaceDefinitionPath = "Data/Surfaces/default_surfaces.json";
    public const string DefaultSimulationEngineDefinitionPath = "Data/Simulation/arcade_physics.json";

    public static GameLaunchOptions FromArgs(string[] args)
    {
        int? autoExitMilliseconds = null;
        string vehicleDefinitionPath = DefaultVehicleDefinitionPath;
        string controlSchemePath = DefaultControlSchemePath;
        string surfaceDefinitionPath = DefaultSurfaceDefinitionPath;
        string simulationEngineDefinitionPath = DefaultSimulationEngineDefinitionPath;
        bool startInManualTransmission = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--auto-exit-ms", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                int.TryParse(args[i + 1], out int parsed))
            {
                autoExitMilliseconds = Math.Max(1, parsed);
                i++;
            }
            else if (args[i].Equals("--vehicle", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                vehicleDefinitionPath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--manual-transmission", StringComparison.OrdinalIgnoreCase))
            {
                startInManualTransmission = true;
            }
            else if (args[i].Equals("--controls", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                controlSchemePath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--surfaces", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                surfaceDefinitionPath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--simulation-engine", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                simulationEngineDefinitionPath = args[i + 1];
                i++;
            }
        }

        return new GameLaunchOptions(
            autoExitMilliseconds,
            vehicleDefinitionPath,
            startInManualTransmission,
            controlSchemePath,
            surfaceDefinitionPath,
            simulationEngineDefinitionPath);
    }
}
