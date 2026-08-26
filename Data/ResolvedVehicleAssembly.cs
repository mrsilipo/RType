using RType.Vehicle;

namespace RType.Data;

internal sealed class ResolvedVehicleAssembly
{
    public string BuildId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string BuildPath { get; init; } = string.Empty;
    public string VehicleDefinitionPath { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public string SourcePurchaseCarPath { get; init; } = string.Empty;
    public string PurchaseCarId { get; init; } = string.Empty;
    public bool PlayerOwned { get; init; }
    public string OwnerProfileId { get; init; } = string.Empty;
    public int GarageSlot { get; init; }
    public string ChassisCode { get; init; } = string.Empty;
    public string DrivetrainLayout { get; init; } = string.Empty;
    public string BodyShellId { get; init; } = string.Empty;
    public ResolvedEngineAssembly Engine { get; init; } = new();
    public ResolvedVehicleMass Mass { get; init; } = new();
    public ResolvedMassProperties MassProperties { get; init; } = new();
    public ResolvedVehicleBuild RuntimeBuild { get; init; } = new();
    public IReadOnlyList<VehicleAssemblyValidationMessage> Validation { get; init; } = [];
}

internal sealed class ResolvedEngineAssembly
{
    public string EngineId { get; init; } = string.Empty;
    public string EngineCombinationId { get; init; } = string.Empty;
    public string EngineCombinationDisplayName { get; init; } = string.Empty;
    public string EngineCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string BlockId { get; init; } = string.Empty;
    public string BlockFamily { get; init; } = string.Empty;
    public string HeadId { get; init; } = string.Empty;
    public string HeadFamily { get; init; } = string.Empty;
    public string Valvetrain { get; init; } = string.Empty;
    public string TuneId { get; init; } = string.Empty;
    public string TuneTier { get; init; } = string.Empty;
    public string FuelId { get; init; } = string.Empty;
    public string FuelDisplayName { get; init; } = string.Empty;
    public float FuelOctaneRon { get; init; }
    public float FuelEthanolContent { get; init; }
    public float FuelSafeCompressionRatio { get; init; }
    public float FuelEffectivePowerMultiplier { get; init; } = 1f;
    public bool FuelRequiresRetune { get; init; }
    public IReadOnlyList<ResolvedEngineMassComponent> MassComponents { get; init; } = [];
    public IReadOnlyDictionary<string, string> InstalledParts { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public float EstimatedAssemblyMassKg { get; init; }
    public float DisplacementCc { get; init; }
    public float BoreMm { get; init; }
    public float StrokeMm { get; init; }
    public float RodLengthMm { get; init; }
    public float CompressionRatio { get; init; }
    public float IdleRpm { get; init; }
    public float PowerRedlineRpm { get; init; }
    public float LimiterHardCutRpm { get; init; }
    public float LimiterResumeRpm { get; init; }
    public float MaxGaugeRpm { get; init; }
    public float LimiterFuelCutSeconds { get; init; } = 0.34f;
    public float LimiterRestoreSeconds { get; init; } = 0.41f;
    public float LimiterCutTorqueMultiplier { get; init; }
    public float RotationalInertiaKgM2 { get; init; }
    public bool VtecEnabled { get; init; }
    public float VtecActivationRpm { get; init; }
    public float VtecTransitionWidthRpm { get; init; }
    public float LowCamFlowMultiplier { get; init; } = 1f;
    public float HighCamFlowMultiplier { get; init; } = 1f;
    public float IntakeFlowScale { get; init; } = 1f;
    public float ExhaustFlowScale { get; init; } = 1f;
    public float ThrottleGamma { get; init; } = 2f;
    public float ClutchTorqueCapacityNm { get; init; }
    public float ClutchBitePoint { get; init; }
    public float ClutchCouplingRate { get; init; }
    public float ClutchEngagementSharpness { get; init; } = 1f;
    public float ClutchSlipDamping { get; init; } = 1f;
    public float ClutchLowSpeedAssistStrength { get; init; } = 0.65f;
    public float ClutchBiteInputStartMultiplier { get; init; } = 0.35f;
    public float ClutchLaunchAssistExponent { get; init; } = 0.55f;
    public float ClutchLowSpeedThrottleGamma { get; init; } = 0.65f;
    public float ClutchLowSpeedThrottleAssist { get; init; } = 0.45f;
    public float ClutchLowSpeedTorqueAssistNm { get; init; } = 55f;
    public float ClutchRollingLockSpeedMetersPerSecond { get; init; } = 0.85f;
    public float ClutchRollingLockSlipRadiansPerSecond { get; init; } = 115f;
    public float ValveSpringFloatStartRpm { get; init; }
    public float ValveSpringSafeContinuousRpm { get; init; }
    public string EngineAudioDspId { get; init; } = string.Empty;
    public string EngineAudioDspDisplayName { get; init; } = string.Empty;
    public string EngineAudioProfilePath { get; init; } = string.Empty;
    public string EngineAudioProfileEngineId { get; init; } = string.Empty;
    public string EngineAudioProfileEngineFamily { get; init; } = string.Empty;
    public bool EngineAudioFallbackAllowed { get; init; }
    public string EngineAudioSourceRecordingPath { get; init; } = string.Empty;
    public string EngineAudioGenerationMethod { get; init; } = string.Empty;
    public string EngineAudioGeneratedSampleSetPath { get; init; } = string.Empty;
    public TorqueCurvePoint[] TorqueCurve { get; init; } = [];
    public TorqueCurvePoint[] EngineBrakeTorqueCurve { get; init; } = [];
    public EnginePowerCompositionTrace PowerComposition { get; init; } = EnginePowerCompositionTrace.Empty;
    public IReadOnlyList<EngineAssemblyValidationMessage> Validation { get; init; } = [];

    public IEnumerable<string> ValidationMessages => Validation.Select(message => message.Message);
}

internal sealed record EnginePowerCompositionTrace(
    float BaselineDisplacementCc,
    float ResolvedDisplacementCc,
    float BaseCompressionRatio,
    float ResolvedCompressionRatio,
    float DisplacementScale,
    float CompressionScale,
    float LowCamScale,
    float HighCamScale,
    float IntakeScale,
    float ExhaustScale,
    float LowFlowScale,
    float HighFlowScale,
    float FuelEffectivePowerMultiplier,
    bool VtecEnabled,
    float VtecActivationRpm,
    float VtecTransitionWidthRpm,
    float BaselinePeakTorqueNm,
    float ResolvedPeakTorqueNm,
    float BaselinePeakEngineBrakeTorqueNm,
    float ResolvedPeakEngineBrakeTorqueNm,
    float EngineBrakeDisplacementScale,
    float EngineBrakeCompressionScale,
    float EngineBrakeInertiaScale,
    float EngineBrakeScale)
{
    public static EnginePowerCompositionTrace Empty { get; } = new(
        0f,
        0f,
        0f,
        0f,
        1f,
        1f,
        1f,
        1f,
        1f,
        1f,
        1f,
        1f,
        1f,
        false,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        1f,
        1f,
        1f,
        1f);
}

internal sealed record EngineAssemblyValidationMessage(
    EngineAssemblyValidationSeverity Severity,
    string Code,
    string Message);

internal sealed record ResolvedEngineMassComponent(
    string Id,
    string Role,
    float MassKg,
    float LocalX,
    float LocalY,
    float LocalZ);

internal enum EngineAssemblyValidationSeverity
{
    Info,
    Warning
}

internal sealed record VehicleAssemblyValidationMessage(
    VehicleAssemblyValidationSeverity Severity,
    string Code,
    string Message);

internal enum VehicleAssemblyValidationSeverity
{
    Info,
    Warning
}

internal sealed class ResolvedMassProperties
{
    public float TotalMassKg { get; init; }
    public float FrontWeightDistribution { get; init; }
    public float CenterOfGravityHeightMeters { get; init; }
    public float CenterOfGravityLongitudinalMeters { get; init; }
    public float YawInertiaKgM2 { get; init; }
    public float CatalogMassKg { get; init; }
    public float CalibrationResidualMassKg { get; init; }
    public MassResolutionTrace Trace { get; init; } = MassResolutionTrace.Empty;
    public IReadOnlyList<ResolvedMassComponent> Components { get; init; } = [];
}

internal sealed record MassResolutionTrace(
    float BodyShellMassKg,
    float BoltOnMassKg,
    float CatalogMassKg,
    float CalibrationResidualMassKg,
    float TotalMassKg,
    int ComponentCount,
    float MassMomentY,
    float MassMomentZ,
    float CenterOfGravityHeightMeters,
    float CenterOfGravityLongitudinalMeters,
    float FrontWeightDistribution,
    float RawYawInertiaKgM2,
    float YawInertiaCalibrationScale,
    float CalibratedYawInertiaKgM2,
    float FinalYawInertiaKgM2)
{
    public static MassResolutionTrace Empty { get; } = new(
        0f,
        0f,
        0f,
        0f,
        0f,
        0,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        1f,
        0f,
        0f);
}

internal sealed record ResolvedMassComponent(
    string Id,
    string Role,
    float MassKg,
    float X,
    float Y,
    float Z);
