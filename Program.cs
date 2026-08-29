using RType.Core;

if (args.Any(arg => arg.Equals("--physics-smoke-test", StringComparison.OrdinalIgnoreCase)))
{
    PhysicsSmokeTest.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--handling-probe", StringComparison.OrdinalIgnoreCase)))
{
    HandlingProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-bicycle-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBicycleProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-deceleration-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicDecelerationProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-four-wheel-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicFourWheelProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--power-balance-probe", StringComparison.OrdinalIgnoreCase)))
{
    PowerBalanceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--steering-authority-probe", StringComparison.OrdinalIgnoreCase)))
{
    SteeringAuthorityProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--surface-probe", StringComparison.OrdinalIgnoreCase)))
{
    SurfaceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--tire-relaxation-probe", StringComparison.OrdinalIgnoreCase)))
{
    TireRelaxationProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--friction-ellipse-probe", StringComparison.OrdinalIgnoreCase)))
{
    FrictionEllipseProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--universal-tyre-force-probe", StringComparison.OrdinalIgnoreCase)))
{
    UniversalTyreForceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--drivetrain-layout-probe", StringComparison.OrdinalIgnoreCase)))
{
    DrivetrainLayoutProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--weight-transfer-probe", StringComparison.OrdinalIgnoreCase)))
{
    WeightTransferProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--launch-probe", StringComparison.OrdinalIgnoreCase)))
{
    LaunchProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--throttle-release-probe", StringComparison.OrdinalIgnoreCase)))
{
    ThrottleReleaseProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--free-rev-probe", StringComparison.OrdinalIgnoreCase)))
{
    FreeRevProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--shift-probe", StringComparison.OrdinalIgnoreCase)))
{
    ShiftProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--track-geometry-probe", StringComparison.OrdinalIgnoreCase)))
{
    TrackGeometryProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--tachometer-geometry-probe", StringComparison.OrdinalIgnoreCase)))
{
    TachometerGeometryProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--track-editor", StringComparison.OrdinalIgnoreCase)))
{
    using var editor = TrackEditorGame.CreateFromArgs(args);
    editor.Run();
    return;
}

if (args.Any(arg => arg.Equals("--track-editor-probe", StringComparison.OrdinalIgnoreCase)))
{
    TrackEditorTool.Run();
    return;
}

if (args.Any(arg => arg.Equals("--audio-probe", StringComparison.OrdinalIgnoreCase)))
{
    AudioProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-audio-profile-catalog-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineAudioProfileCatalogProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-audio-coverage-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineAudioCoverageProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-audio-generation-target-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineAudioGenerationTargetProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--vehicle-assembly-probe", StringComparison.OrdinalIgnoreCase)))
{
    VehicleAssemblyProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--vehicle-modification-comparison-probe", StringComparison.OrdinalIgnoreCase)))
{
    VehicleModificationComparisonProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--vehicle-catalog-probe", StringComparison.OrdinalIgnoreCase)))
{
    VehicleCatalogProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--vehicle-engine-swap-probe", StringComparison.OrdinalIgnoreCase)))
{
    VehicleEngineSwapProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--catalog-inheritance-probe", StringComparison.OrdinalIgnoreCase)))
{
    CatalogInheritanceProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--part-catalog-integrity-probe", StringComparison.OrdinalIgnoreCase)))
{
    PartCatalogIntegrityProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--runtime-data-isolation-probe", StringComparison.OrdinalIgnoreCase)))
{
    RuntimeDataIsolationProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-assembly-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineAssemblyProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-compatibility-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineCompatibilityProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-power-composer-probe", StringComparison.OrdinalIgnoreCase)))
{
    EnginePowerComposerProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--engine-mod-path-probe", StringComparison.OrdinalIgnoreCase)))
{
    EngineModPathProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--vehicle-mod-path-probe", StringComparison.OrdinalIgnoreCase)))
{
    VehicleModPathProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-vehicle-factory-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageVehicleFactoryProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-mod-installer-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageModInstallerProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-inventory-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageInventoryProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-profile-integrity-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageProfileIntegrityProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-vehicle-purchase-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageVehiclePurchaseProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-saved-setup-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageSavedSetupProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-saved-setup-editor-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageSavedSetupEditorProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-saved-setup-creation-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageSavedSetupCreationProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-active-setup-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageActiveSetupProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--garage-active-vehicle-probe", StringComparison.OrdinalIgnoreCase)))
{
    GarageActiveVehicleProbe.Run();
    return;
}

if (args.Any(arg => arg.Equals("--rtype-engine-room", StringComparison.OrdinalIgnoreCase)))
{
    using var engineRoom = RTypeEngineRoomGame.CreateFromArgs(args);
    engineRoom.Run();
    return;
}

if (args.Any(arg => arg.Equals("--audio-diagnostics-smoke", StringComparison.OrdinalIgnoreCase)))
{
    AudioDiagnosticsSmoke.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--race-condition-probe", StringComparison.OrdinalIgnoreCase)))
{
    RaceConditionProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--chase-camera-probe", StringComparison.OrdinalIgnoreCase)))
{
    ChaseCameraProbe.Run();
    return;
}

using var game = new RacingGame(GameLaunchOptions.FromArgs(args));
game.Run();
