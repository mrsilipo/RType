using System.Text.Json;
using Microsoft.Xna.Framework;
using RetroRacer.Vehicle;

namespace RetroRacer.Data;

public static class VehicleDefinitionLoader
{
    private const float AirDensityKgM3 = 1.225f;
    private const string DefaultEngineSimulatorProfilePath = "Data/EngineProfiles/honda_b18c5_vtec_engine_sim.json";
    private const string DefaultEngineSimulatorMrScriptPath = "Assets/Sounds/EngineSim/HondaB18C5/assets/engines/honda_b18c5_vtec.mr";
    private const string DefaultEngineSimulatorImpulseResponsePath = "Assets/Sounds/EngineSim/HondaB18C5/es/sound-library/new/mild_exhaust.wav";

    public static VehicleSimulationParameters LoadSimulationParameters(string path)
    {
        return LoadSimulationParameters(path, null);
    }

    public static VehicleSimulationParameters LoadSimulationParameters(string path, string? engineSimulatorProfileOverridePath)
    {
        string resolvedPath = ResolveDataPath(path);
        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });

        JsonElement root = document.RootElement;
        TorqueCurvePoint[] torqueCurve = ReadTorqueCurve(root, "torqueCurveNm");
        TorqueCurvePoint[] engineBrakeTorqueCurve = ReadTorqueCurve(root, "engineBrakeTorqueCurveNm");
        float airDensity = ReadValueSingle(root, AirDensityKgM3, "simulation", "currentPrototype", "airDensityKgM3");
        float dragCoefficient = ReadValueSingle(root, 0.34f, "aero", "dragCoefficient");
        float frontalArea = ReadValueSingle(root, 1.95f, "aero", "frontalAreaSquareMeters");
        float frontLiftCoefficient = ReadValueSingle(root, 0.05f, "aero", "frontLiftCoefficient");
        float rearLiftCoefficient = ReadValueSingle(root, 0.03f, "aero", "rearLiftCoefficient");
        TyreAxleParameters frontTyres = ReadTyres(root, "front");
        TyreAxleParameters rearTyres = ReadTyres(root, "rear");

        return new VehicleSimulationParameters
        {
            Id = ReadString(root, "prototype_default", "id"),
            DisplayName = ReadString(root, "Prototype Car", "identity", "gameDisplayName"),
            MassKg = ReadValueSingle(root, 1125f, "massProperties", "curbMassKg"),
            WheelbaseMeters = ReadValueSingle(root, 2.55f, "chassis", "wheelbaseMeters"),
            FrontTrackMeters = ReadValueSingle(root, 1.48f, "chassis", "frontTrackMeters"),
            RearTrackMeters = ReadValueSingle(root, 1.48f, "chassis", "rearTrackMeters"),
            BodyLengthMeters = ReadValueSingle(root, 4.2f, "chassis", "lengthMeters"),
            BodyWidthMeters = ReadValueSingle(root, 1.7f, "chassis", "widthMeters"),
            FrontWeightDistribution = ReadValueSingle(root, 0.58f, "massProperties", "frontWeightDistribution"),
            CenterOfGravityHeightMeters = ReadValueSingle(root, 0.48f, "massProperties", "cg", "y"),
            YawInertiaKgM2 = ReadValueSingle(root, 1450f, "massProperties", "inertiaTensorKgM2", "izz"),
            WheelRadiusMeters = frontTyres.LoadedRadiusMeters,
            FinalDriveRatio = ReadValueSingle(root, 3.85f, "powertrain", "transmission", "finalDrive"),
            DrivetrainEfficiency = ReadValueSingle(root, 0.82f, "simulation", "currentPrototype", "drivetrainEfficiency"),
            ClosedThrottleEngineBrakeTorqueNm = ReadValueSingle(root, 32f, "simulation", "currentPrototype", "closedThrottleEngineBrakeTorqueNm"),
            IdleRpm = ReadValueSingle(root, 900f, "powertrain", "engine", "idleRpm"),
            RedlineRpm = ReadValueSingle(root, 6800f, "powertrain", "engine", "revLimiterRpm"),
            RevLimiterResumeRpm = ReadValueSingle(root, 6620f, "powertrain", "engine", "revLimiter", "resumeRpm"),
            RevLimiterFuelCutSeconds = ReadValueSingle(root, 0.08f, "powertrain", "engine", "revLimiter", "fuelCutSeconds"),
            RevLimiterRestoreSeconds = ReadValueSingle(root, 0.05f, "powertrain", "engine", "revLimiter", "restoreSeconds"),
            RevLimiterCutTorqueMultiplier = ReadValueSingle(root, 0.08f, "powertrain", "engine", "revLimiter", "cutTorqueMultiplier"),
            RevLimiterBounceRpm = ReadValueSingle(root, 140f, "powertrain", "engine", "revLimiter", "bounceRpm"),
            EngineRotationalInertiaKgM2 = ReadValueSingle(root, 0.22f, "powertrain", "engine", "rotationalInertiaKgM2"),
            VtecEnabled = ReadBoolean(root, false, "powertrain", "engine", "vtec", "enabled"),
            VtecActivationRpm = ReadValueSingle(root, 5800f, "powertrain", "engine", "vtec", "activationRpm"),
            VtecTransitionWidthRpm = ReadValueSingle(root, 650f, "powertrain", "engine", "vtec", "transitionWidthRpm"),
            VtecLowCamFlowMultiplier = ReadValueSingle(root, 1f, "powertrain", "engine", "vtec", "lowCamFlowMultiplier"),
            VtecHighCamFlowMultiplier = ReadValueSingle(root, 1.08f, "powertrain", "engine", "vtec", "highCamFlowMultiplier"),
            EngineSimulatorDrivesPhysics = ReadBoolean(root, false, "powertrain", "engine", "engineSimulator", "drivesPhysics"),
            EngineSimulatorFullDriveline = ReadBoolean(root, false, "powertrain", "engine", "engineSimulator", "fullDriveline"),
            EngineSimulatorPhysicsSimulationFrequencyHz = ReadValueSingle(root, 1000f, "powertrain", "engine", "engineSimulator", "simulationFrequencyHz"),
            EngineSimulatorPhysicsFluidSimulationSteps = Math.Clamp((int)ReadValueSingle(root, 2f, "powertrain", "engine", "engineSimulator", "fluidSimulationSteps"), 1, 16),
            EngineSimulatorPhysicsTorqueScale = ReadValueSingle(root, 1f, "powertrain", "engine", "engineSimulator", "torqueScale"),
            EngineSimulatorPhysicsTorqueBlend = ReadValueSingle(root, 0f, "powertrain", "engine", "engineSimulator", "torqueBlend"),
            EngineSimulatorPhysicsUseReferenceTorqueCalibration = ReadBoolean(root, false, "powertrain", "engine", "engineSimulator", "useReferenceTorqueCalibration"),
            EngineSimulatorPhysicsEngineBrakeScale = ReadValueSingle(root, 1f, "powertrain", "engine", "engineSimulator", "engineBrakeScale"),
            EngineSimulatorPhysicsEngineBrakeBlend = ReadValueSingle(root, 0f, "powertrain", "engine", "engineSimulator", "engineBrakeBlend"),
            EngineSimulatorPhysicsMaxTorqueNm = ReadValueSingle(root, 220f, "powertrain", "engine", "engineSimulator", "maxTorqueNm"),
            EngineSimulatorPhysicsMaxEngineBrakeTorqueNm = ReadValueSingle(root, 120f, "powertrain", "engine", "engineSimulator", "maxEngineBrakeTorqueNm"),
            ClutchTorqueCapacityNm = ReadValueSingle(root, 250f, "powertrain", "clutch", "maxTorqueNm"),
            ClutchEngagementPoint = ReadValueSingle(root, 0.55f, "powertrain", "clutch", "engagementPoint"),
            ClutchCouplingRate = ReadValueSingle(root, 10f, "powertrain", "clutch", "couplingRate"),
            EngineFreeRevResponseRate = ReadValueSingle(root, 7f, "simulation", "currentPrototype", "engineFreeRevResponseRate"),
            LaunchSlipTargetRpm = ReadValueSingle(root, 3800f, "simulation", "currentPrototype", "launchSlipTargetRpm"),
            LaunchSlipBlend = ReadValueSingle(root, 0.3f, "simulation", "currentPrototype", "launchSlipBlend"),
            UpshiftRpm = ReadValueSingle(root, 6250f, "simulation", "currentPrototype", "automaticUpshiftRpm"),
            DownshiftRpm = ReadValueSingle(root, 2350f, "simulation", "currentPrototype", "automaticDownshiftRpm"),
            AutomaticMinimumUpshiftSpeedMetersPerSecond = ReadValueSingle(root, 18f, "simulation", "currentPrototype", "automaticMinimumUpshiftSpeedKph") / 3.6f,
            ManualShiftTimeSeconds = ReadValueSingle(root, 0.32f, "powertrain", "transmission", "shiftModel", "manualShiftTimeSeconds"),
            AutomaticShiftTimeSeconds = ReadValueSingle(root, 0.18f, "powertrain", "transmission", "shiftModel", "automaticShiftTimeSeconds"),
            DownshiftOverRevToleranceRpm = ReadValueSingle(root, 250f, "powertrain", "transmission", "shiftModel", "downshiftOverRevToleranceRpm"),
            DownshiftMechanicalOverRevLimitRpm = ReadValueSingle(root, 0f, "powertrain", "transmission", "shiftModel", "downshiftMechanicalOverRevLimitRpm"),
            DownshiftOverRevBrakeMultiplier = ReadValueSingle(root, 2.35f, "powertrain", "transmission", "shiftModel", "downshiftOverRevBrakeMultiplier"),
            DownshiftOverRevShockSeconds = ReadValueSingle(root, 0.38f, "powertrain", "transmission", "shiftModel", "downshiftOverRevShockSeconds"),
            ForwardGearRatios = ReadForwardGearRatios(root),
            ReverseGearRatio = MathF.Abs(ReadValueSingle(root, -2.85f, "powertrain", "transmission", "gears", "reverse")),
            MaxBrakeForceN = ReadValueSingle(root, 11500f, "simulation", "currentPrototype", "maxBrakeForceN"),
            BrakeBiasFront = ReadValueSingle(root, 0.67f, "brakes", "brakeBiasFront"),
            Brakes = ReadBrakes(root),
            AeroDragFactor = 0.5f * airDensity * dragCoefficient * frontalArea,
            FrontLiftFactor = 0.5f * airDensity * frontLiftCoefficient * frontalArea,
            RearLiftFactor = 0.5f * airDensity * rearLiftCoefficient * frontalArea,
            RollingResistanceCoefficient = ReadValueSingle(root, 0.013f, "simulation", "currentPrototype", "rollingResistanceCoefficient"),
            LateralGripResponse = ReadValueSingle(root, 8.5f, "simulation", "currentPrototype", "lateralGripResponse"),
            ArcadeHandling = ReadArcadeHandling(root),
            SteeringRatio = ReadValueSingle(root, 15f, "steering", "ratio"),
            SteeringWheelLockDegrees = ReadValueSingle(root, 960f, "steering", "steeringWheelLockDegrees"),
            SteeringInputRatePerSecond = ReadValueSingle(root, 4.5f, "steering", "inputRatePerSecond"),
            SteeringReturnRatePerSecond = ReadValueSingle(root, 7.0f, "steering", "returnRatePerSecond"),
            SteeringHighSpeedInputRateMultiplier = ReadValueSingle(root, 0.35f, "steering", "speedSensitiveAssist", "highSpeedInputRateMultiplier"),
            SteeringHighSpeedReturnRateMultiplier = ReadValueSingle(root, 0.55f, "steering", "speedSensitiveAssist", "highSpeedReturnRateMultiplier"),
            SteeringFullLockSpeedMetersPerSecond = ReadValueSingle(root, 0f, "steering", "speedSensitiveAssist", "fullLockSpeedKph") / 3.6f,
            SteeringReducedLockSpeedMetersPerSecond = ReadValueSingle(root, 190f, "steering", "speedSensitiveAssist", "reducedLockSpeedKph") / 3.6f,
            SteeringHighSpeedLockMultiplier = ReadValueSingle(root, 0.48f, "steering", "speedSensitiveAssist", "highSpeedLockMultiplier"),
            SteeringTargetLateralAccelerationG = ReadValueSingle(root, 0.88f, "steering", "speedSensitiveAssist", "targetLateralAccelerationG"),
            SteeringPeakSlipAngleFraction = ReadValueSingle(root, 0.68f, "steering", "speedSensitiveAssist", "peakSlipAngleFraction"),
            SteeringLowSpeedReferenceMetersPerSecond = ReadValueSingle(root, 22f, "steering", "speedSensitiveAssist", "lowSpeedReferenceKph") / 3.6f,
            SteeringMinimumHighSpeedAngleRadians = MathHelper.ToRadians(ReadValueSingle(root, 4.3f, "steering", "speedSensitiveAssist", "minimumRoadWheelAngleDegrees")),
            MaxSteerAngleRadians = MathHelper.ToRadians(ReadValueSingle(root, 31.5f, "steering", "maxInnerWheelAngleDegrees")),
            AckermannPercent = ReadValueSingle(root, 100f, "steering", "ackermannPercent"),
            FrontSpringRateNPerM = ReadValueSingle(root, 33000f, "suspension", "front", "springRateNPerM"),
            RearSpringRateNPerM = ReadValueSingle(root, 25000f, "suspension", "rear", "springRateNPerM"),
            FrontAntiRollBarRateNmPerRad = ReadValueSingle(root, 15000f, "suspension", "antiRollBars", "frontRateNmPerRad"),
            RearAntiRollBarRateNmPerRad = ReadValueSingle(root, 10500f, "suspension", "antiRollBars", "rearRateNmPerRad"),
            FrontSuspensionGeometry = ReadSuspensionGeometry(root, "front"),
            RearSuspensionGeometry = ReadSuspensionGeometry(root, "rear"),
            DifferentialTorqueBiasRatio = ReadValueSingle(root, 1f, "powertrain", "differentials", "front", "torqueBiasRatio"),
            WheelInertiaKgM2 = EstimateWheelInertia(root, frontTyres.LoadedRadiusMeters),
            DrivenWheels = ReadDrivenWheels(root),
            FrontTyres = frontTyres,
            RearTyres = rearTyres,
            Audio = ReadAudio(root, engineSimulatorProfileOverridePath),
            WallCollisionPointRadiusMeters = ReadValueSingle(root, 0.08f, "simulation", "currentPrototype", "wallCollisionPointRadiusMeters"),
            WallCollisionRestitution = ReadValueSingle(root, 0.12f, "simulation", "currentPrototype", "wallCollisionRestitution"),
            WallImpactFriction = ReadValueSingle(root, 0.24f, "simulation", "currentPrototype", "wallImpactFriction"),
            WallScrapeFriction = ReadValueSingle(root, 0.045f, "simulation", "currentPrototype", "wallScrapeFriction"),
            WallYawImpulseScale = ReadValueSingle(root, 0.55f, "simulation", "currentPrototype", "wallYawImpulseScale"),
            TorqueCurve = torqueCurve.Length == 0 ? new VehicleSimulationParameters().TorqueCurve : torqueCurve,
            EngineBrakeTorqueCurve = engineBrakeTorqueCurve.Length == 0 ? new VehicleSimulationParameters().EngineBrakeTorqueCurve : engineBrakeTorqueCurve
        };
    }

    private static ArcadeHandlingParameters ReadArcadeHandling(JsonElement root)
    {
        ArcadeHandlingParameters defaults = new();
        return new ArcadeHandlingParameters
        {
            PseudoLateralTransferScale = ReadValueSingle(
                root,
                defaults.PseudoLateralTransferScale,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "pseudoLateralTransferScale"),
            PseudoLateralTransferBlend = ReadValueSingle(
                root,
                defaults.PseudoLateralTransferBlend,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "pseudoLateralTransferBlend"),
            DrivenGripAllowance = ReadValueSingle(
                root,
                defaults.DrivenGripAllowance,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "drivenGripAllowance"),
            GenericGripAllowance = ReadValueSingle(
                root,
                defaults.GenericGripAllowance,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "genericGripAllowance"),
            BrakingGripAllowance = ReadValueSingle(
                root,
                defaults.BrakingGripAllowance,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "brakingGripAllowance"),
            BrakingSlidingFrictionFloor = ReadValueSingle(
                root,
                defaults.BrakingSlidingFrictionFloor,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "brakingSlidingFrictionFloor"),
            PassiveSlideRecoveryLateralSpeedMetersPerSecond = ReadValueSingle(
                root,
                defaults.PassiveSlideRecoveryLateralSpeedMetersPerSecond,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "passiveSlideRecoveryLateralSpeedMetersPerSecond"),
            PassiveSlideRecoveryYawRateDegreesPerSecond = ReadValueSingle(
                root,
                defaults.PassiveSlideRecoveryYawRateDegreesPerSecond,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "passiveSlideRecoveryYawRateDegreesPerSecond"),
            WallImpactVelocityMultiplier = ReadValueSingle(
                root,
                defaults.WallImpactVelocityMultiplier,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "wallImpactVelocityMultiplier"),
            WallDirectImpactBlendStart = ReadValueSingle(
                root,
                defaults.WallDirectImpactBlendStart,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "wallDirectImpactBlendStart"),
            WallDirectImpactBlendEnd = ReadValueSingle(
                root,
                defaults.WallDirectImpactBlendEnd,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "wallDirectImpactBlendEnd"),
            VisualSuspensionMotionScale = ReadValueSingle(
                root,
                defaults.VisualSuspensionMotionScale,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualSuspensionMotionScale"),
            VisualSuspensionHeavePitchScale = ReadValueSingle(
                root,
                defaults.VisualSuspensionHeavePitchScale,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualSuspensionHeavePitchScale"),
            VisualSuspensionLoadTransferMeters = ReadValueSingle(
                root,
                defaults.VisualSuspensionLoadTransferMeters,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualSuspensionLoadTransferMeters"),
            VisualSuspensionSpringRate = ReadValueSingle(
                root,
                defaults.VisualSuspensionSpringRate,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualSuspensionSpringRate"),
            VisualSuspensionDampingRate = ReadValueSingle(
                root,
                defaults.VisualSuspensionDampingRate,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualSuspensionDampingRate"),
            FrontVisualSuspensionMultiplier = ReadValueSingle(
                root,
                defaults.FrontVisualSuspensionMultiplier,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "frontVisualSuspensionMultiplier"),
            RearVisualSuspensionMultiplier = ReadValueSingle(
                root,
                defaults.RearVisualSuspensionMultiplier,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "rearVisualSuspensionMultiplier"),
            VisualBodyPitchScale = ReadValueSingle(
                root,
                defaults.VisualBodyPitchScale,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualBodyPitchScale"),
            VisualBodyRollScale = ReadValueSingle(
                root,
                defaults.VisualBodyRollScale,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualBodyRollScale"),
            VisualBodyPitchLimitRadians = ReadValueSingle(
                root,
                defaults.VisualBodyPitchLimitRadians,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualBodyPitchLimitRadians"),
            VisualBodyRollLimitRadians = ReadValueSingle(
                root,
                defaults.VisualBodyRollLimitRadians,
                "simulation",
                "currentPrototype",
                "arcadeHandling",
                "visualBodyRollLimitRadians")
        };
    }

    private static VehicleAudioParameters ReadAudio(JsonElement root, string? engineSimulatorProfileOverridePath = null)
    {
        float vtecActivationRpm = ReadValueSingle(root, 5800f, "powertrain", "engine", "vtec", "activationRpm");
        float vtecTransitionWidthRpm = ReadValueSingle(root, 650f, "powertrain", "engine", "vtec", "transitionWidthRpm");
        bool engineSimulatorEnabled = ReadBoolean(root, false, "audio", "engineSimulator", "enabled");
        string engineSimulatorProfilePath = engineSimulatorProfileOverridePath ??
                                             ReadString(root, string.Empty, "audio", "engineSimulator", "profilePath");
        string directMrScriptPath = ReadString(root, string.Empty, "audio", "engineSimulator", "mrScriptPath");
        if (engineSimulatorEnabled &&
            string.IsNullOrWhiteSpace(engineSimulatorProfilePath) &&
            string.IsNullOrWhiteSpace(directMrScriptPath) &&
            ResolveOptionalDataPath(DefaultEngineSimulatorProfilePath) is not null)
        {
            engineSimulatorProfilePath = DefaultEngineSimulatorProfilePath;
        }

        EngineSimulatorAudioProfile? engineSimulatorProfile = LoadEngineSimulatorAudioProfile(engineSimulatorProfilePath);
        string mrScriptPath = ReadEngineSimulatorString(root, engineSimulatorProfile, string.Empty, "mrScriptPath");
        if (engineSimulatorEnabled && string.IsNullOrWhiteSpace(mrScriptPath))
        {
            mrScriptPath = DefaultEngineSimulatorMrScriptPath;
        }

        EngineSimMrProfile? mrProfile = EngineSimMrProfile.TryLoad(mrScriptPath);
        return new VehicleAudioParameters
        {
            TurboLoopPath = ReadString(root, string.Empty, "audio", "turbo", "loop"),
            EngineVolume = ReadValueSingle(root, 0.62f, "audio", "engineVolume"),
            IdleVolume = ReadValueSingle(root, 0.22f, "audio", "idleVolume"),
            ThrottleVolume = ReadValueSingle(root, 0.34f, "audio", "throttleVolume"),
            OverrunVolume = ReadValueSingle(root, 0.18f, "audio", "overrunVolume"),
            EngineBrakeVolume = ReadValueSingle(root, 0.18f, "audio", "engineBrakeVolume"),
            ShiftKickVolume = ReadValueSingle(root, 0.16f, "audio", "shiftKickVolume"),
            HighRpmBlendInRpm = ReadValueSingle(root, vtecActivationRpm, "audio", "highRpmBlendInRpm"),
            HighRpmBlendWidthRpm = ReadValueSingle(root, vtecTransitionWidthRpm, "audio", "highRpmBlendWidthRpm"),
            HighRpmMinimumThrottle = ReadValueSingle(root, 0f, "audio", "highRpmMinimumThrottle"),
            HighRpmMinimumSpeedMetersPerSecond = ReadValueSingle(root, 0f, "audio", "highRpmMinimumSpeedMetersPerSecond"),
            HighRpmVolumeBoost = ReadValueSingle(root, 0.12f, "audio", "highRpmVolumeBoost"),
            EngineSimulatorEnabled = engineSimulatorEnabled,
            EngineSimulatorProfilePath = engineSimulatorProfile?.ResolvedPath ?? engineSimulatorProfilePath,
            EngineSimulatorProfileId = engineSimulatorProfile?.Id ?? string.Empty,
            EngineSimulatorProfileDisplayName = engineSimulatorProfile?.DisplayName ?? string.Empty,
            EngineSimulatorMrScriptPath = mrProfile?.ResolvedScriptPath ?? mrScriptPath,
            EngineSimulatorVolume = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0f, "volume"),
            EngineSimulatorCylinderCount = Math.Max(1, (int)ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.CylinderCount ?? 4f, "cylinderCount")),
            EngineSimulatorFiringOrder = ReadEngineSimulatorIntArray(root, engineSimulatorProfile, mrProfile?.FiringOrder.Length > 0 ? mrProfile.FiringOrder : [1, 3, 4, 2], "firingOrder"),
            EngineSimulatorBoreMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.BoreMillimeters ?? 81f, "boreMillimeters"),
            EngineSimulatorStrokeMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.StrokeMillimeters ?? 87.2f, "strokeMillimeters"),
            EngineSimulatorRodLengthMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.RodLengthMillimeters ?? 137.922f, "rodLengthMillimeters"),
            EngineSimulatorFuelBurningEfficiency = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.FuelBurningEfficiency ?? 0.75f, "fuelBurningEfficiency"),
            EngineSimulatorFuelTurbulence = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.FuelTurbulence ?? 2.5f, "fuelTurbulence"),
            EngineSimulatorCylinderAttenuation = ReadEngineSimulatorFloatArray(root, engineSimulatorProfile, mrProfile?.CylinderAttenuation.Length > 0 ? mrProfile.CylinderAttenuation : [0.9f, 1.1f, 0.8f, 0.9f], "cylinderAttenuation"),
            EngineSimulatorCylinderExhaust = ReadEngineSimulatorIntArray(root, engineSimulatorProfile, mrProfile?.CylinderExhaust.Length > 0 ? mrProfile.CylinderExhaust : [0, 1, 0, 1], "cylinderExhaust"),
            EngineSimulatorExhaustVolumes = ReadEngineSimulatorFloatArray(root, engineSimulatorProfile, mrProfile?.ExhaustVolumes.Length > 0 ? mrProfile.ExhaustVolumes : [6f, 8f], "exhaustVolumes"),
            EngineSimulatorImpulseResponsePath = !string.IsNullOrWhiteSpace(mrProfile?.ImpulseResponsePath) ? mrProfile.ImpulseResponsePath : ReadEngineSimulatorString(root, engineSimulatorProfile, DefaultEngineSimulatorImpulseResponsePath, "impulseResponsePath"),
            EngineSimulatorImpulseResponseVolume = mrProfile?.ImpulseResponseVolume ?? ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.01f, "impulseResponseVolume"),
            EngineSimulatorImpulseResponseTaps = Math.Max(0, (int)ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 512f, "impulseResponseTaps")),
            EngineSimulatorSimulationFrequencyHz = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.SimulationFrequencyHz ?? 20000f, "simulationFrequencyHz"),
            EngineSimulatorFluidSimulationSteps = Math.Clamp((int)ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 1f, "fluidSimulationSteps"), 1, 16),
            EngineSimulatorStarterTorqueNm = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.StarterTorqueNm ?? 94.91f, "starterTorqueNm"),
            EngineSimulatorStarterSpeedRpm = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.StarterSpeedRpm ?? -500f, "starterSpeedRpm"),
            EngineSimulatorCrankshaftFrictionTorqueNm = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.CrankshaftFrictionTorqueNm ?? 1.36f, "crankshaftFrictionTorqueNm"),
            EngineSimulatorCrankshaftMomentOfInertiaKgM2 = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.CrankshaftMomentOfInertiaKgM2 ?? 0.114934f, "crankshaftMomentOfInertiaKgM2"),
            EngineSimulatorCrankshaftMassKg = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.CrankshaftMassKg ?? 16.10f, "crankshaftMassKg"),
            EngineSimulatorFlywheelMassKg = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.FlywheelMassKg ?? 4.54f, "flywheelMassKg"),
            EngineSimulatorTransmissionMaxClutchTorqueNm = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.TransmissionMaxClutchTorqueNm ?? 406.75f, "transmissionMaxClutchTorqueNm"),
            EngineSimulatorTransmissionGearRatios = ReadEngineSimulatorFloatArray(root, engineSimulatorProfile, mrProfile?.TransmissionGearRatios.Length > 0 ? mrProfile.TransmissionGearRatios : [3.23f, 2.105f, 1.458f, 1.107f, 0.848f], "transmissionGearRatios"),
            EngineSimulatorVehicleMassKg = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VehicleMassKg ?? 1088.62f, "vehicleMassKg"),
            EngineSimulatorVehicleDiffRatio = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VehicleDiffRatio ?? 3.55f, "vehicleDiffRatio"),
            EngineSimulatorVehicleTireRadiusMeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VehicleTireRadiusMeters ?? 0.254f, "vehicleTireRadiusMeters"),
            EngineSimulatorVehicleRollingResistanceN = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VehicleRollingResistanceN ?? 300f, "vehicleRollingResistanceN"),
            EngineSimulatorThrottleGamma = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.ThrottleGamma ?? 2f, "throttleGamma"),
            EngineSimulatorDspPressureScale = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 1f, "dspPressureScale"),
            EngineSimulatorDspOutputGain = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.62f, "dspOutputGain"),
            EngineSimulatorOverrunGain = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 2.35f, "overrunGain"),
            EngineSimulatorShockGain = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 1.75f, "shockGain"),
            EngineSimulatorLimiterGain = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 1f, "limiterGain"),
            EngineSimulatorIgnitionTimingRpm = ReadEngineSimulatorFloatArray(root, engineSimulatorProfile, mrProfile?.IgnitionTimingRpm.Length > 0 ? mrProfile.IgnitionTimingRpm : [0f, 1000f, 2000f, 3000f, 4000f], "ignitionTimingRpm"),
            EngineSimulatorIgnitionTimingDegrees = ReadEngineSimulatorFloatArray(root, engineSimulatorProfile, mrProfile?.IgnitionTimingDegrees.Length > 0 ? mrProfile.IgnitionTimingDegrees : [-25f, -25f, -30f, -30f, -30f], "ignitionTimingDegrees"),
            EngineSimulatorIntakePlenumVolumeLiters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.IntakePlenumVolumeLiters ?? 1.325f, "intakePlenumVolumeLiters"),
            EngineSimulatorIntakeRunnerLengthInches = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.IntakeRunnerLengthInches ?? 7f, "intakeRunnerLengthInches"),
            EngineSimulatorExhaustPrimaryTubeLengthInches = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.ExhaustPrimaryTubeLengthInches ?? 10f, "exhaustPrimaryTubeLengthInches"),
            EngineSimulatorExhaustVolumeLiters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.ExhaustVolumeLiters ?? 100f, "exhaustVolumeLiters"),
            EngineSimulatorHighFrequencyGain = mrProfile?.HighFrequencyGain ?? ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.002f, "highFrequencyGain"),
            EngineSimulatorNoise = mrProfile?.Noise ?? ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.253f, "noise"),
            EngineSimulatorJitter = mrProfile?.Jitter ?? ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.195f, "jitter"),
            EngineSimulatorVtecIntensity = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.58f, "vtecIntensity"),
            EngineSimulatorProfileMaxTorqueNm = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0f, "maxTorqueNm"),
            EngineSimulatorProfileMaxEngineBrakeTorqueNm = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0f, "maxEngineBrakeTorqueNm"),
            EngineSimulatorLimiterDurationSeconds = mrProfile?.LimiterDurationSeconds ?? ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, 0.05f, "limiterDurationSeconds"),
            EngineSimulatorLowIntakeDurationDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowIntakeDurationDegrees ?? 210f, "lowCam", "intakeDurationDegrees"),
            EngineSimulatorLowIntakeLiftMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowIntakeLiftMillimeters ?? 6.9f, "lowCam", "intakeLiftMillimeters"),
            EngineSimulatorLowExhaustDurationDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowExhaustDurationDegrees ?? 190f, "lowCam", "exhaustDurationDegrees"),
            EngineSimulatorLowExhaustLiftMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowExhaustLiftMillimeters ?? 6.5f, "lowCam", "exhaustLiftMillimeters"),
            EngineSimulatorLowCamGamma = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowCamGamma ?? 1f, "lowCam", "gamma"),
            EngineSimulatorLowIntakeCenterDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowIntakeCenterDegrees ?? 116f, "lowCam", "intakeCenterDegrees"),
            EngineSimulatorLowExhaustCenterDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.LowExhaustCenterDegrees ?? 116f, "lowCam", "exhaustCenterDegrees"),
            EngineSimulatorVtecIntakeDurationDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecIntakeDurationDegrees ?? 240f, "vtecCam", "intakeDurationDegrees"),
            EngineSimulatorVtecIntakeLiftMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecIntakeLiftMillimeters ?? 11.5f, "vtecCam", "intakeLiftMillimeters"),
            EngineSimulatorVtecExhaustDurationDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecExhaustDurationDegrees ?? 232f, "vtecCam", "exhaustDurationDegrees"),
            EngineSimulatorVtecExhaustLiftMillimeters = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecExhaustLiftMillimeters ?? 10.5f, "vtecCam", "exhaustLiftMillimeters"),
            EngineSimulatorVtecCamGamma = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecCamGamma ?? 0.5f, "vtecCam", "gamma"),
            EngineSimulatorVtecIntakeCenterDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecIntakeCenterDegrees ?? 100f, "vtecCam", "intakeCenterDegrees"),
            EngineSimulatorVtecExhaustCenterDegrees = ReadEngineSimulatorValueSingle(root, engineSimulatorProfile, mrProfile?.VtecExhaustCenterDegrees ?? 100f, "vtecCam", "exhaustCenterDegrees"),
            TurboSpoolStartRpm = ReadValueSingle(root, 2200f, "audio", "turbo", "spoolStartRpm"),
            TurboSpoolFullRpm = ReadValueSingle(root, 5600f, "audio", "turbo", "spoolFullRpm"),
            TurboVolume = ReadValueSingle(root, 0.36f, "audio", "turbo", "volume"),
            TurboResponseRate = ReadValueSingle(root, 4.5f, "audio", "turbo", "responseRate"),
            TurboMinimumPlaybackRatio = ReadValueSingle(root, 0.55f, "audio", "turbo", "minimumPlaybackRatio"),
            TurboMaximumPlaybackRatio = ReadValueSingle(root, 2.6f, "audio", "turbo", "maximumPlaybackRatio")
        };
    }

    private static EngineSimulatorAudioProfile? LoadEngineSimulatorAudioProfile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? resolvedPath = ResolveOptionalDataPath(path);
        if (resolvedPath is null)
        {
            throw new FileNotFoundException($"Engine Simulator profile JSON was not found: {path}", path);
        }

        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true
        });

        JsonElement root = document.RootElement;
        if (!TryGet(root, out JsonElement engineSimulatorElement, "engineSimulator") ||
            engineSimulatorElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Engine Simulator profile must contain an engineSimulator object: {resolvedPath}");
        }

        return new EngineSimulatorAudioProfile(
            resolvedPath,
            ReadString(root, string.Empty, "id"),
            ReadString(root, string.Empty, "displayName"),
            engineSimulatorElement.Clone());
    }

    private static string ReadEngineSimulatorString(
        JsonElement root,
        EngineSimulatorAudioProfile? profile,
        string fallback,
        params string[] path)
    {
        string profileFallback = profile is null
            ? fallback
            : ReadString(profile.EngineSimulator, fallback, path);
        return ReadString(root, profileFallback, EngineSimulatorPath(path));
    }

    private static bool ReadEngineSimulatorBoolean(
        JsonElement root,
        EngineSimulatorAudioProfile? profile,
        bool fallback,
        params string[] path)
    {
        bool profileFallback = profile is null
            ? fallback
            : ReadBoolean(profile.EngineSimulator, fallback, path);
        return ReadBoolean(root, profileFallback, EngineSimulatorPath(path));
    }

    private static float ReadEngineSimulatorValueSingle(
        JsonElement root,
        EngineSimulatorAudioProfile? profile,
        float fallback,
        params string[] path)
    {
        float profileFallback = profile is null
            ? fallback
            : ReadValueSingle(profile.EngineSimulator, fallback, path);
        return ReadValueSingle(root, profileFallback, EngineSimulatorPath(path));
    }

    private static int[] ReadEngineSimulatorIntArray(
        JsonElement root,
        EngineSimulatorAudioProfile? profile,
        int[] fallback,
        params string[] path)
    {
        int[] profileFallback = profile is null
            ? fallback
            : ReadIntArray(profile.EngineSimulator, fallback, path);
        return ReadIntArray(root, profileFallback, EngineSimulatorPath(path));
    }

    private static float[] ReadEngineSimulatorFloatArray(
        JsonElement root,
        EngineSimulatorAudioProfile? profile,
        float[] fallback,
        params string[] path)
    {
        float[] profileFallback = profile is null
            ? fallback
            : ReadFloatArray(profile.EngineSimulator, fallback, path);
        return ReadFloatArray(root, profileFallback, EngineSimulatorPath(path));
    }

    private static string[] EngineSimulatorPath(string[] path)
    {
        string[] fullPath = new string[path.Length + 2];
        fullPath[0] = "audio";
        fullPath[1] = "engineSimulator";
        Array.Copy(path, 0, fullPath, 2, path.Length);
        return fullPath;
    }

    private sealed record EngineSimulatorAudioProfile(
        string ResolvedPath,
        string Id,
        string DisplayName,
        JsonElement EngineSimulator);

    private static string ResolveDataPath(string path)
    {
        string? resolvedPath = ResolveOptionalDataPath(path);
        if (resolvedPath is not null)
        {
            return resolvedPath;
        }

        throw new FileNotFoundException($"Vehicle definition JSON was not found: {path}", path);
    }

    private static string? ResolveOptionalDataPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static TorqueCurvePoint[] ReadTorqueCurve(JsonElement root, string propertyName)
    {
        if (!TryGet(root, out JsonElement curveElement, "powertrain", "engine", propertyName) ||
            curveElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<TorqueCurvePoint> points = [];
        foreach (JsonElement point in curveElement.EnumerateArray())
        {
            float rpm = ReadSingle(point, 0f, "rpm");
            float torque = ReadSingle(point, 0f, "torqueNm");
            if (rpm > 0f && torque > 0f)
            {
                points.Add(new TorqueCurvePoint(rpm, torque));
            }
        }

        return [.. points.OrderBy(point => point.Rpm)];
    }

    private static BrakeSystemParameters ReadBrakes(JsonElement root)
    {
        return new BrakeSystemParameters
        {
            MaxLinePressurePa = ReadValueSingle(root, 85f, "brakes", "system", "maxLinePressureBar") * 100000f,
            BrakeBiasFront = ReadValueSingle(
                root,
                ReadValueSingle(root, 0.67f, "brakes", "brakeBiasFront"),
                "brakes",
                "system",
                "brakeBiasFront"),
            HandbrakeRearTorqueNm = ReadValueSingle(root, 950f, "brakes", "system", "handbrakeRearTorqueNm"),
            PressureRiseRatePerSecond = ReadValueSingle(root, 3.5f, "brakes", "system", "pressureRiseRatePerSecond"),
            PressureReleaseRatePerSecond = ReadValueSingle(root, 10f, "brakes", "system", "pressureReleaseRatePerSecond"),
            Front = ReadBrakeAxle(root, "front", 280f, 0.42f, 0.0022f, 2.0f, 0.40f),
            Rear = ReadBrakeAxle(root, "rear", 260f, 0.42f, 0.0010f, 2.0f, 0.38f),
            Abs = ReadAbs(root)
        };
    }

    private static BrakeAxleParameters ReadBrakeAxle(
        JsonElement root,
        string axle,
        float fallbackDiscDiameterMm,
        float fallbackEffectiveRadiusRatio,
        float fallbackPistonAreaSquareMeters,
        float fallbackClampForceMultiplier,
        float fallbackPadFriction)
    {
        float pistonArea = ReadPistonArea(root, axle, fallbackPistonAreaSquareMeters);
        return new BrakeAxleParameters
        {
            DiscDiameterMeters = ReadValueSingle(root, fallbackDiscDiameterMm, "brakes", axle, "discDiameterMm") / 1000f,
            EffectiveRadiusRatio = ReadValueSingle(root, fallbackEffectiveRadiusRatio, "brakes", axle, "effectiveRadiusRatio"),
            TotalPistonAreaSquareMeters = pistonArea,
            ClampForceMultiplier = ReadValueSingle(root, fallbackClampForceMultiplier, "brakes", axle, "clampForceMultiplier"),
            PadFrictionCoefficient = ReadValueSingle(root, fallbackPadFriction, "brakes", axle, "padFriction")
        };
    }

    private static float ReadPistonArea(JsonElement root, string axle, float fallback)
    {
        if (TryGet(root, out JsonElement pistonsElement, "brakes", axle, "pistonDiametersMm") &&
            pistonsElement.ValueKind == JsonValueKind.Array)
        {
            float area = 0f;
            foreach (JsonElement pistonElement in pistonsElement.EnumerateArray())
            {
                float diameterMm = ReadSingle(pistonElement, 0f);
                if (diameterMm <= 0f)
                {
                    continue;
                }

                float radiusMeters = diameterMm / 2000f;
                area += MathF.PI * radiusMeters * radiusMeters;
            }

            if (area > 0f)
            {
                return area;
            }
        }

        float pistonDiameter = ReadValueSingle(root, 0f, "brakes", axle, "pistonDiameterMm");
        if (pistonDiameter > 0f)
        {
            float caliperPistons = MathF.Max(1f, ReadValueSingle(root, 1f, "brakes", axle, "caliperPistons"));
            float radiusMeters = pistonDiameter / 2000f;
            return MathF.PI * radiusMeters * radiusMeters * caliperPistons;
        }

        return fallback;
    }

    private static AbsParameters ReadAbs(JsonElement root)
    {
        return new AbsParameters
        {
            Enabled = ReadBoolean(root, false, "brakes", "system", "abs", "enabled") ||
                      ReadBoolean(root, false, "brakes", "abs"),
            TargetSlipRatio = ReadValueSingle(root, -0.14f, "brakes", "system", "abs", "targetSlipRatio"),
            ReleaseSlipRatio = ReadValueSingle(root, -0.22f, "brakes", "system", "abs", "releaseSlipRatio"),
            ApplyRatePerSecond = ReadValueSingle(root, 8f, "brakes", "system", "abs", "applyRatePerSecond"),
            ReleaseRatePerSecond = ReadValueSingle(root, 18f, "brakes", "system", "abs", "releaseRatePerSecond"),
            MinimumSpeedMetersPerSecond = ReadValueSingle(root, 8f, "brakes", "system", "abs", "minimumSpeedKph") / 3.6f,
            MinimumPressureRatio = ReadValueSingle(root, 0.18f, "brakes", "system", "abs", "minimumPressureRatio")
        };
    }

    private static float[] ReadForwardGearRatios(JsonElement root)
    {
        if (!TryGet(root, out JsonElement forwardGearsElement, "powertrain", "transmission", "gears", "forward") ||
            forwardGearsElement.ValueKind != JsonValueKind.Array)
        {
            return new VehicleSimulationParameters().ForwardGearRatios;
        }

        List<(int Gear, float Ratio)> gears = [];
        foreach (JsonElement gear in forwardGearsElement.EnumerateArray())
        {
            int gearNumber = (int)ReadSingle(gear, 0f, "gear");
            float ratio = ReadSingle(gear, 0f, "ratio");
            if (gearNumber > 0 && ratio > 0f)
            {
                gears.Add((gearNumber, ratio));
            }
        }

        return gears.Count == 0
            ? new VehicleSimulationParameters().ForwardGearRatios
            : [.. gears.OrderBy(gear => gear.Gear).Select(gear => gear.Ratio)];
    }

    private static DrivenWheelSet ReadDrivenWheels(JsonElement root)
    {
        return new DrivenWheelSet(
            ReadBoolean(root, true, "architecture", "drivenWheels", "FL"),
            ReadBoolean(root, true, "architecture", "drivenWheels", "FR"),
            ReadBoolean(root, false, "architecture", "drivenWheels", "RL"),
            ReadBoolean(root, false, "architecture", "drivenWheels", "RR"));
    }

    private static TyreAxleParameters ReadTyres(JsonElement root, string axle)
    {
        float radius = ReadValueSingle(root, 0.33f, "tyres", axle, "loadedRadiusMeters");
        float peakFriction = ReadValueSingle(root, 1.05f, "tyres", axle, "peakFriction");
        float rollingResistance = ReadValueSingle(root, 0.013f, "tyres", axle, "rollingResistance");
        float loadSensitivity = ReadValueSingle(root, 0.12f, "tyres", axle, "loadSensitivity");
        float widthMm = ReadValueSingle(root, 195f, "tyres", axle, "widthMm");

        return new TyreAxleParameters
        {
            LoadedRadiusMeters = radius,
            PeakFriction = peakFriction,
            RollingResistanceCoefficient = rollingResistance,
            LoadSensitivity = loadSensitivity,
            CorneringStiffnessNPerRad = widthMm * 390f,
            LongitudinalStiffnessN = widthMm * 470f,
            LateralPeakSlipAngleRadians = MathHelper.ToRadians(ReadValueSingle(root, 7.0f, "tyres", axle, "lateralPeakSlipAngleDegrees")),
            LateralSlideSlipAngleRadians = MathHelper.ToRadians(ReadValueSingle(root, 18.0f, "tyres", axle, "lateralSlideSlipAngleDegrees")),
            LateralForceRiseShape = ReadValueSingle(root, 3.0f, "tyres", axle, "lateralForceRiseShape"),
            SlidingLateralFrictionMultiplier = ReadValueSingle(root, 0.65f, "tyres", axle, "slidingLateralFrictionMultiplier"),
            RelaxationLengthMeters = ReadValueSingle(root, 0.45f, "tyres", axle, "relaxationLengthMeters"),
            LateralScrubDragCoefficient = ReadValueSingle(root, 0.12f, "tyres", axle, "lateralScrubDragCoefficient"),
            IdealCamberRadians = MathHelper.ToRadians(ReadValueSingle(root, -1.0f, "tyres", axle, "idealCamberDegrees")),
            CamberGripLossPerDegree = ReadValueSingle(root, 0.025f, "tyres", axle, "camberGripLossPerDegree"),
            MinimumCamberGripMultiplier = ReadValueSingle(root, 0.78f, "tyres", axle, "minimumCamberGripMultiplier"),
            CamberThrustStiffnessNPerRad = ReadValueSingle(root, 1200f, "tyres", axle, "camberThrustStiffnessNPerRad"),
            LongitudinalPeakSlipRatio = ReadValueSingle(root, 0.15f, "tyres", axle, "longitudinalPeakSlipRatio"),
            LongitudinalForceRiseShape = ReadValueSingle(root, 3.0f, "tyres", axle, "longitudinalForceRiseShape"),
            LongitudinalSlideSlipRatio = ReadValueSingle(root, 1.0f, "tyres", axle, "longitudinalSlideSlipRatio"),
            SlidingFrictionMultiplier = ReadValueSingle(root, 0.62f, "tyres", axle, "slidingFrictionMultiplier")
        };
    }

    private static SuspensionGeometryParameters ReadSuspensionGeometry(JsonElement root, string axle)
    {
        return new SuspensionGeometryParameters
        {
            StaticCamberRadians = MathHelper.ToRadians(ReadValueSingle(root, 0f, "suspension", axle, "staticCamberDegrees")),
            StaticToeRadians = MathHelper.ToRadians(ReadValueSingle(root, 0f, "suspension", axle, "toeDegrees")),
            CasterRadians = MathHelper.ToRadians(ReadValueSingle(root, 0f, "suspension", axle, "casterDegrees")),
            CamberGainRadiansPerMeter = MathHelper.ToRadians(ReadValueSingle(root, 0f, "suspension", axle, "camberGainDegreesPerMeter")),
            ToeGainRadiansPerMeter = MathHelper.ToRadians(ReadValueSingle(root, 0f, "suspension", axle, "toeGainDegreesPerMeter")),
            BodyRollCamberMultiplier = ReadValueSingle(root, 1f, "suspension", axle, "bodyRollCamberMultiplier"),
            CasterCamberGain = ReadValueSingle(root, 0.6f, "suspension", axle, "casterCamberGain"),
            MaxCompressionMeters = ReadValueSingle(root, 0.085f, "suspension", axle, "maxCompressionMeters"),
            MaxDroopMeters = ReadValueSingle(root, 0.075f, "suspension", axle, "maxDroopMeters")
        };
    }

    private static float EstimateWheelInertia(JsonElement root, float radius)
    {
        float frontWheelMass = ReadValueSingle(root, 7.2f, "wheels", "front", "massKg");
        float frontTyreMass = ReadValueSingle(root, 8.0f, "tyres", "front", "massKg");
        float rotatingMass = frontWheelMass + frontTyreMass;
        return MathF.Max(0.35f, rotatingMass * radius * radius * 0.62f);
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement element, path) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        return TryGet(root, out JsonElement element, path) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : fallback;
    }

    private static float ReadValueSingle(JsonElement root, float fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path))
        {
            return fallback;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("value", out JsonElement valueElement))
        {
            return ReadSingle(valueElement, fallback);
        }

        return ReadSingle(element, fallback);
    }

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        if (path.Length > 0 && !TryGet(root, out root, path))
        {
            return fallback;
        }

        return root.ValueKind == JsonValueKind.Number && root.TryGetSingle(out float value)
            ? value
            : fallback;
    }

    private static int[] ReadIntArray(JsonElement root, int[] fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        List<int> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int value))
            {
                values.Add(value);
            }
        }

        return values.Count > 0 ? [.. values] : fallback;
    }

    private static float[] ReadFloatArray(JsonElement root, float[] fallback, params string[] path)
    {
        if (!TryGet(root, out JsonElement element, path) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        List<float> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            float value = ReadSingle(item, float.NaN);
            if (!float.IsNaN(value))
            {
                values.Add(value);
            }
        }

        return values.Count > 0 ? [.. values] : fallback;
    }

    private static bool TryGet(JsonElement root, out JsonElement element, params string[] path)
    {
        element = root;
        foreach (string segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(segment, out element))
            {
                return false;
            }
        }

        return true;
    }
}
