namespace RType.Core;

public sealed record GameLaunchOptions(
    int? AutoExitMilliseconds,
    string VehiclePath,
    string GarageProfilePath,
    string GarageVehicleIdOrPath,
    string GarageSetupIdOrPath,
    bool StartInManualTransmission,
    string ControlSchemePath,
    string SurfaceDefinitionPath,
    string SimulationEngineDefinitionPath)
{
    public const string DefaultVehiclePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
    public const string DefaultControlSchemePath = "Data/Controls/racing_xbox360_default.json";
    public const string DefaultSurfaceDefinitionPath = "Data/Surfaces/default_surfaces.json";
    public const string DefaultSimulationEngineDefinitionPath = "Data/Simulation/arcade_physics.json";

    public static GameLaunchOptions FromArgs(string[] args)
    {
        int? autoExitMilliseconds = null;
        string vehiclePath = DefaultVehiclePath;
        string garageProfilePath = string.Empty;
        string garageVehicleIdOrPath = string.Empty;
        string garageSetupIdOrPath = "active";
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
                vehiclePath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--manual-transmission", StringComparison.OrdinalIgnoreCase))
            {
                startInManualTransmission = true;
            }
            else if (args[i].Equals("--garage-profile", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                garageProfilePath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--garage-vehicle", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                garageVehicleIdOrPath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--garage-setup", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                garageSetupIdOrPath = args[i + 1];
                i++;
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
            vehiclePath,
            garageProfilePath,
            garageVehicleIdOrPath,
            garageSetupIdOrPath,
            startInManualTransmission,
            controlSchemePath,
            surfaceDefinitionPath,
            simulationEngineDefinitionPath);
    }
}
