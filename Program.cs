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

if (args.Any(arg => arg.Equals("--cornering-speed-loss-probe", StringComparison.OrdinalIgnoreCase)))
{
    CorneringSpeedLossProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-slip-gap-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSlipGapProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-kinematic-audit-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicKinematicAuditProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-assist-matrix-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicAssistMatrixProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-base-force-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBaseForceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-corner-causal-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicCornerCausalProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-yaw-moment-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicYawMomentProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-yaw-damping-experiment-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicYawDampingExperimentProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-body-dynamics-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBodyDynamicsProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-tyre-response-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTyreResponseProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-steering-path-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSteeringPathProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-equilibrium-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicEquilibriumProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-equilibrium-matrix-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicEquilibriumMatrixProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-lateral-balance-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicLateralBalanceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-slip-kinematics-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSlipKinematicsProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-transient-force-balance-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTransientForceBalanceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-steering-envelope-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSteeringEnvelopeProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-steering-architecture-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSteeringArchitectureProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-steering-envelope-matrix-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSteeringEnvelopeMatrixProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-turn-radius-budget-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTurnRadiusBudgetProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-ek9-ad09-validation-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicEk9Ad09ValidationProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-digital-steering-feel-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicDigitalSteeringFeelProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-brake-turn-authority-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBrakeTurnAuthorityProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-trail-brake-dynamics-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTrailBrakeDynamicsProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-transient-load-transfer-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTransientLoadTransferProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-roll-stiffness-transfer-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicRollStiffnessTransferProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-suspension-state-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSuspensionStateProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-low-speed-caster-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicLowSpeedCasterProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-zero-crawl-force-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicZeroCrawlForceProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-low-speed-handoff-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicLowSpeedHandoffProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-steering-unwind-continuity-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSteeringUnwindContinuityProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-low-speed-drive-side-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicLowSpeedDriveSideProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-forklift-invariant-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicForkliftInvariantProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-rpm-speedo-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicRpmSpeedoProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-limiter-state-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicLimiterStateProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-tyre-load-front-axle-audit-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTyreLoadFrontAxleAuditProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-front-brake-combined-grip-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicFrontBrakeCombinedGripProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-brake-turn-steering-state-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBrakeTurnSteeringStateProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-brake-turn-yaw-beta-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBrakeTurnYawBetaProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-base-turn-in-equilibrium-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicBaseTurnInEquilibriumProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-steering-command-equilibrium-sweep", StringComparison.OrdinalIgnoreCase)))
{
    ClassicSteeringCommandEquilibriumSweep.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-turn-normalized-steady-state-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTurnNormalizedSteadyStateProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-top-end-steering-mapping-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTopEndSteeringMappingProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-axle-authority-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicAxleAuthorityProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-front-yaw-authority-audit-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicFrontYawAuthorityAuditProbe.Run(GameLaunchOptions.FromArgs(args));
    return;
}

if (args.Any(arg => arg.Equals("--classic-tyre-relaxation-architecture-probe", StringComparison.OrdinalIgnoreCase)))
{
    ClassicTyreRelaxationArchitectureProbe.Run(GameLaunchOptions.FromArgs(args));
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
