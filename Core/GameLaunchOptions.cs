using RType.Vehicle;

namespace RType.Core;

public sealed record GameLaunchOptions(
    int? AutoExitMilliseconds,
    string SceneryScreenshotPath,
    string SceneryScreenshotSetPrefix,
    int SceneryScreenshotDelayMilliseconds,
    string SceneryScreenshotView,
    string SceneryVisibilityLogPath,
    bool SceneryInventory,
    string VehiclePath,
    string GarageProfilePath,
    string GarageVehicleIdOrPath,
    string GarageSetupIdOrPath,
    bool StartInManualTransmission,
    string ControlSchemePath,
    string SurfaceDefinitionPath,
    string SimulationEngineDefinitionPath,
    TyreLoadAuthorityMode TyreLoadAuthority,
    PhysicalLoadFilterRecontactMode PhysicalLoadFilterRecontact,
    float PhysicalLoadFilterTauSeconds)
{
    public const string DefaultVehiclePath = "Data/PurchaseCars/2000_Ek9_Stock.json";
    public const string DefaultControlSchemePath = "Data/Controls/racing_xbox360_default.json";
    public const string DefaultSurfaceDefinitionPath = "Data/Surfaces/default_surfaces.json";
    public const string DefaultSimulationEngineDefinitionPath = "Data/Simulation/classic_four_wheel_physics.json";

    public static GameLaunchOptions FromArgs(string[] args)
    {
        int? autoExitMilliseconds = null;
        string sceneryScreenshotPath = string.Empty;
        string sceneryScreenshotSetPrefix = string.Empty;
        int sceneryScreenshotDelayMilliseconds = 0;
        string sceneryScreenshotView = "chase";
        string sceneryVisibilityLogPath = string.Empty;
        bool sceneryInventory = false;
        string vehiclePath = DefaultVehiclePath;
        string garageProfilePath = string.Empty;
        string garageVehicleIdOrPath = string.Empty;
        string garageSetupIdOrPath = "active";
        string controlSchemePath = DefaultControlSchemePath;
        string surfaceDefinitionPath = DefaultSurfaceDefinitionPath;
        string simulationEngineDefinitionPath = DefaultSimulationEngineDefinitionPath;
        bool startInManualTransmission = false;
        TyreLoadAuthorityMode tyreLoadAuthority = TyreLoadAuthorityMode.Tuned;
        PhysicalLoadFilterRecontactMode physicalLoadFilterRecontact = PhysicalLoadFilterRecontactMode.SeedFromRaw;
        float physicalLoadFilterTauSeconds = 0.05f;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--auto-exit-ms", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                int.TryParse(args[i + 1], out int parsed))
            {
                autoExitMilliseconds = Math.Max(1, parsed);
                i++;
            }
            else if (args[i].Equals("--scenery-screenshot", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                sceneryScreenshotPath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--scenery-screenshot-delay-ms", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length &&
                     int.TryParse(args[i + 1], out int screenshotDelayMs))
            {
                sceneryScreenshotDelayMilliseconds = Math.Max(0, screenshotDelayMs);
                i++;
            }
            else if (args[i].Equals("--scenery-screenshot-set", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                sceneryScreenshotSetPrefix = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--scenery-screenshot-view", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                sceneryScreenshotView = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--scenery-visibility-log", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length)
            {
                sceneryVisibilityLogPath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--scenery-inventory", StringComparison.OrdinalIgnoreCase))
            {
                sceneryInventory = true;
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
            else if (args[i].Equals("--tyre-load-authority", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--tyre-load-authority requires one of: Tuned, PhysicalRaw, PhysicalFiltered.");
                }

                string value = args[++i];
                if (value.Equals(nameof(TyreLoadAuthorityMode.Hybrid), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("--tyre-load-authority Hybrid is diagnostic-only and is not exposed for normal runtime testing.");
                }

                if (!Enum.TryParse(value, ignoreCase: true, out tyreLoadAuthority) ||
                    tyreLoadAuthority == TyreLoadAuthorityMode.Hybrid)
                {
                    throw new ArgumentException($"Invalid --tyre-load-authority '{value}'. Valid values: Tuned, PhysicalRaw, PhysicalFiltered.");
                }
            }
            else if (args[i].Equals("--physical-load-filter-recontact", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--physical-load-filter-recontact requires one of: SeedFromRaw, ResumeFromZero.");
                }

                string value = args[++i];
                if (!Enum.TryParse(value, ignoreCase: true, out physicalLoadFilterRecontact))
                {
                    throw new ArgumentException($"Invalid --physical-load-filter-recontact '{value}'. Valid values: SeedFromRaw, ResumeFromZero.");
                }
            }
            else if (args[i].Equals("--physical-load-filter-tau", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--physical-load-filter-tau requires a positive finite number of seconds.");
                }

                string value = args[++i];
                if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out physicalLoadFilterTauSeconds) ||
                    !float.IsFinite(physicalLoadFilterTauSeconds) ||
                    physicalLoadFilterTauSeconds <= 0f)
                {
                    throw new ArgumentException($"Invalid --physical-load-filter-tau '{value}'. Value must be positive and finite.");
                }
            }
        }

        return new GameLaunchOptions(
            autoExitMilliseconds,
            sceneryScreenshotPath,
            sceneryScreenshotSetPrefix,
            sceneryScreenshotDelayMilliseconds,
            sceneryScreenshotView,
            sceneryVisibilityLogPath,
            sceneryInventory,
            vehiclePath,
            garageProfilePath,
            garageVehicleIdOrPath,
            garageSetupIdOrPath,
            startInManualTransmission,
            controlSchemePath,
            surfaceDefinitionPath,
            simulationEngineDefinitionPath,
            tyreLoadAuthority,
            physicalLoadFilterRecontact,
            physicalLoadFilterTauSeconds);
    }
}
