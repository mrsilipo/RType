namespace RType.Vehicle;

public sealed record EngineAudioSampleParameters(
    string Path,
    float Rpm,
    bool HighRpm = false,
    bool Limiter = false,
    float Volume = 1f,
    string Role = "normal",
    float LoopStartRatio = 0f,
    float LoopEndRatio = 1f);

public sealed class VehicleAudioParameters
{
    public string EngineLoopPath { get; init; } = string.Empty;

    public string HighRpmLoopPath { get; init; } = string.Empty;

    public EngineAudioSampleParameters[] EngineSamples { get; init; } = [];

    public string EngineAudioProfilePath { get; init; } = string.Empty;

    public string EngineAudioProfileId { get; init; } = string.Empty;

    public string EngineAudioProfileEngineId { get; init; } = string.Empty;

    public string EngineAudioProfileEngineFamily { get; init; } = string.Empty;

    public bool EngineAudioFallbackAllowed { get; init; }

    public string EngineAudioSourceRecordingPath { get; init; } = string.Empty;

    public string EngineAudioGeneratedSampleSetPath { get; init; } = string.Empty;

    public string EngineAudioGenerationMethod { get; init; } = string.Empty;

    public string EngineAudioDspId { get; init; } = string.Empty;

    public string EngineAudioDspDisplayName { get; init; } = string.Empty;

    public string EngineAudioSampleGenerationKey { get; init; } = string.Empty;

    public string EngineAudioEngineId { get; init; } = string.Empty;

    public string EngineAudioEngineCode { get; init; } = string.Empty;

    public string EngineAudioEngineFamily { get; init; } = string.Empty;

    public string EngineAudioEngineCombinationId { get; init; } = string.Empty;

    public string EngineAudioBlockId { get; init; } = string.Empty;

    public string EngineAudioHeadId { get; init; } = string.Empty;

    public string EngineAudioValvetrain { get; init; } = string.Empty;

    public string EngineAudioTuneId { get; init; } = string.Empty;

    public string EngineAudioFuelId { get; init; } = string.Empty;

    public float EngineAudioDisplacementCc { get; init; }

    public float EngineAudioCompressionRatio { get; init; }

    public bool EngineAudioVtecEnabled { get; init; }

    public float EngineAudioVtecActivationRpm { get; init; }

    public float BaseSampleRpm { get; init; } = 3500f;

    public float MinimumPlaybackRatio { get; init; } = 0.32f;

    public float MaximumPlaybackRatio { get; init; } = 3.3f;

    public float EngineSampleCrossfadeWidthRpm { get; init; } = 24f;

    public float EngineIdleBlendOutRpm { get; init; } = 1650f;

    public float EngineSampleVolume { get; init; } = 0.72f;

    public string TurboLoopPath { get; init; } = string.Empty;

    public float EngineVolume { get; init; } = 0.62f;

    public float IdleVolume { get; init; } = 0.22f;

    public float ThrottleVolume { get; init; } = 0.34f;

    public float OverrunVolume { get; init; } = 0.18f;

    public float EngineBrakeVolume { get; init; } = 0.18f;

    public float ShiftKickVolume { get; init; } = 0.16f;

    public float HighRpmBlendInRpm { get; init; } = 5800f;

    public float HighRpmBlendWidthRpm { get; init; } = 650f;

    public float HighRpmMinimumThrottle { get; init; }

    public float HighRpmMinimumSpeedMetersPerSecond { get; init; }

    public float HighRpmVolumeBoost { get; init; } = 0.12f;

    public float LimiterStutterFrequencyHz { get; init; } = 15f;

    public float LimiterStutterOffDuty { get; init; } = 0.50f;

    public float LimiterStutterIntensity { get; init; } = 1f;

    public bool RTypeEngineEnabled { get; init; } = true;

    public string RTypeEngineBuildPath { get; init; } = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public string RTypeEngineProfilePath { get; init; } = string.Empty;

    public float RTypeEngineVolume { get; init; } = 0.72f;

    public float RaceAudioThrottleGamma { get; init; } = 2f;

    public float[] RaceAudioGearRatios { get; init; } = [3.23f, 2.105f, 1.458f, 1.107f, 0.848f];

    public float RaceAudioFinalDriveRatio { get; init; } = 3.55f;

    public bool EngineSimulatorEnabled { get; init; }

    public string EngineSimulatorProfilePath { get; init; } = string.Empty;

    public string EngineSimulatorProfileId { get; init; } = string.Empty;

    public string EngineSimulatorProfileDisplayName { get; init; } = string.Empty;

    public string EngineSimulatorMrScriptPath { get; init; } = string.Empty;

    public float EngineSimulatorVolume { get; init; }

    public int EngineSimulatorCylinderCount { get; init; } = 4;

    public int[] EngineSimulatorFiringOrder { get; init; } = [1, 3, 4, 2];

    public float EngineSimulatorBoreMillimeters { get; init; } = 81f;

    public float EngineSimulatorStrokeMillimeters { get; init; } = 87.2f;

    public float EngineSimulatorRodLengthMillimeters { get; init; } = 137.922f;

    public float EngineSimulatorFuelBurningEfficiency { get; init; } = 0.75f;

    public float EngineSimulatorFuelTurbulence { get; init; } = 2.5f;

    public float[] EngineSimulatorCylinderAttenuation { get; init; } = [0.9f, 1.1f, 0.8f, 0.9f];

    public int[] EngineSimulatorCylinderExhaust { get; init; } = [0, 1, 0, 1];

    public float[] EngineSimulatorExhaustVolumes { get; init; } = [6f, 8f];

    public string EngineSimulatorImpulseResponsePath { get; init; } = "Assets/Sounds/EngineSim/HondaB18C5/es/sound-library/new/mild_exhaust.wav";

    public float EngineSimulatorImpulseResponseVolume { get; init; } = 0.01f;

    public int EngineSimulatorImpulseResponseTaps { get; init; } = 512;

    public float EngineSimulatorSimulationFrequencyHz { get; init; } = 20000f;

    public int EngineSimulatorFluidSimulationSteps { get; init; } = 1;

    public float EngineSimulatorStarterTorqueNm { get; init; } = 94.91f;

    public float EngineSimulatorStarterSpeedRpm { get; init; } = -500f;

    public float EngineSimulatorCrankshaftFrictionTorqueNm { get; init; } = 1.36f;

    public float EngineSimulatorCrankshaftMomentOfInertiaKgM2 { get; init; } = 0.114934f;

    public float EngineSimulatorCrankshaftMassKg { get; init; } = 16.10f;

    public float EngineSimulatorFlywheelMassKg { get; init; } = 4.54f;

    public float EngineSimulatorTransmissionMaxClutchTorqueNm { get; init; } = 406.75f;

    public float[] EngineSimulatorTransmissionGearRatios { get; init; } = [3.23f, 2.105f, 1.458f, 1.107f, 0.848f];

    public float EngineSimulatorVehicleMassKg { get; init; } = 1088.62f;

    public float EngineSimulatorVehicleDiffRatio { get; init; } = 3.55f;

    public float EngineSimulatorVehicleTireRadiusMeters { get; init; } = 0.254f;

    public float EngineSimulatorVehicleRollingResistanceN { get; init; } = 300f;

    public float EngineSimulatorThrottleGamma { get; init; } = 2f;

    public float EngineSimulatorDspPressureScale { get; init; } = 1f;

    public float EngineSimulatorDspOutputGain { get; init; } = 1f;

    public float EngineSimulatorOverrunGain { get; init; } = 2.35f;

    public float EngineSimulatorShockGain { get; init; } = 1.75f;

    public float EngineSimulatorLimiterGain { get; init; } = 1f;

    public float[] EngineSimulatorIgnitionTimingRpm { get; init; } = [0f, 1000f, 2000f, 3000f, 4000f];

    public float[] EngineSimulatorIgnitionTimingDegrees { get; init; } = [-25f, -25f, -30f, -30f, -30f];

    public float EngineSimulatorIntakePlenumVolumeLiters { get; init; } = 1.325f;

    public float EngineSimulatorIntakeRunnerLengthInches { get; init; } = 7f;

    public float EngineSimulatorExhaustPrimaryTubeLengthInches { get; init; } = 10f;

    public float EngineSimulatorExhaustVolumeLiters { get; init; } = 100f;

    public float EngineSimulatorHighFrequencyGain { get; init; } = 0.002f;

    public float EngineSimulatorNoise { get; init; } = 0.253f;

    public float EngineSimulatorJitter { get; init; } = 0.195f;

    public float EngineSimulatorVtecIntensity { get; init; } = 0.58f;

    public float EngineSimulatorProfileMaxTorqueNm { get; init; }

    public float EngineSimulatorProfileMaxEngineBrakeTorqueNm { get; init; }

    public float[] EngineSimulatorProfileTorqueCurveRpm { get; init; } = [];

    public float[] EngineSimulatorProfileTorqueCurveNm { get; init; } = [];

    public float[] EngineSimulatorProfileEngineBrakeCurveRpm { get; init; } = [];

    public float[] EngineSimulatorProfileEngineBrakeCurveNm { get; init; } = [];

    public float EngineSimulatorLimiterDurationSeconds { get; init; } = 0.05f;

    public float EngineSimulatorLowIntakeDurationDegrees { get; init; } = 210f;

    public float EngineSimulatorLowIntakeLiftMillimeters { get; init; } = 6.9f;

    public float EngineSimulatorLowExhaustDurationDegrees { get; init; } = 190f;

    public float EngineSimulatorLowExhaustLiftMillimeters { get; init; } = 6.5f;

    public float EngineSimulatorLowCamGamma { get; init; } = 1f;

    public float EngineSimulatorLowIntakeCenterDegrees { get; init; } = 116f;

    public float EngineSimulatorLowExhaustCenterDegrees { get; init; } = 116f;

    public float EngineSimulatorVtecIntakeDurationDegrees { get; init; } = 240f;

    public float EngineSimulatorVtecIntakeLiftMillimeters { get; init; } = 11.5f;

    public float EngineSimulatorVtecExhaustDurationDegrees { get; init; } = 232f;

    public float EngineSimulatorVtecExhaustLiftMillimeters { get; init; } = 10.5f;

    public float EngineSimulatorVtecCamGamma { get; init; } = 0.5f;

    public float EngineSimulatorVtecIntakeCenterDegrees { get; init; } = 100f;

    public float EngineSimulatorVtecExhaustCenterDegrees { get; init; } = 100f;

    public bool TurboEnabled => !string.IsNullOrWhiteSpace(TurboLoopPath);

    public float TurboSpoolStartRpm { get; init; } = 2200f;

    public float TurboSpoolFullRpm { get; init; } = 5600f;

    public float TurboVolume { get; init; } = 0.36f;

    public float TurboResponseRate { get; init; } = 4.5f;

    public float TurboMinimumPlaybackRatio { get; init; } = 0.55f;

    public float TurboMaximumPlaybackRatio { get; init; } = 2.6f;
}
