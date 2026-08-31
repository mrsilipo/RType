using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using RType.Vehicle;

namespace RType.Data;

internal static class VehicleBuildDefinitionLoader
{
    private const string VehicleCatalogIndexPath = "Data/Parts/part_catalog_index.json";
    private const string ChassisTuneIndexPath = "Data/Tunes/Chassis/chassis_tune_index.json";

    public static ResolvedVehicleBuild Load(string buildPath)
    {
        string resolvedBuildPath = ResolveDataPath(buildPath);
        using FileStream stream = File.OpenRead(resolvedBuildPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        JsonElement assembly = Require(root, "assembly");
        CatalogLookup catalogs = CatalogLookup.Load(VehicleCatalogIndexPath);
        CatalogLookup chassisTunes = CatalogLookup.Load(ChassisTuneIndexPath);

        JsonElement chassis = Require(assembly, "chassis");
        JsonElement engine = Require(assembly, "engine");
        JsonElement drivetrain = Require(assembly, "drivetrain");
        IReadOnlyDictionary<string, string> swapKitIds = ReadStringMap(assembly, "swapKits");
        JsonElement suspension = Require(assembly, "suspension");
        JsonElement brakes = Require(assembly, "brakes");
        JsonElement wheels = Require(assembly, "wheels");
        JsonElement tyres = Require(assembly, "tyres");
        JsonElement aero = Require(assembly, "aero");
        JsonElement tuning = Require(assembly, "tuning");

        JsonElement bodyShell = catalogs.Require(ReadString(chassis, string.Empty, "bodyShell"));
        JsonElement gearbox = catalogs.Require(ReadString(drivetrain, string.Empty, "gearbox"));
        JsonElement finalDrive = catalogs.Require(ReadString(drivetrain, string.Empty, "finalDrive"));
        JsonElement differential = catalogs.Require(ReadString(drivetrain, string.Empty, "differential"));
        string gearboxType = ReadString(gearbox, "manual", "data", "type");
        float swapKitMassKg = SumCatalogWeights(catalogs, swapKitIds.Values);
        JsonElement frontSuspension = catalogs.Require(ReadString(suspension, string.Empty, "front"));
        JsonElement rearSuspension = catalogs.Require(ReadString(suspension, string.Empty, "rear"));
        JsonElement alignment = catalogs.Require(ReadString(suspension, string.Empty, "alignment"));
        JsonElement frontBrakes = catalogs.Require(ReadString(brakes, string.Empty, "front"));
        JsonElement rearBrakes = catalogs.Require(ReadString(brakes, string.Empty, "rear"));
        JsonElement brakeSystem = catalogs.Require(ReadString(brakes, string.Empty, "system"));
        JsonElement frontWheel = catalogs.Require(ReadString(wheels, string.Empty, "front"));
        JsonElement rearWheel = catalogs.Require(ReadString(wheels, string.Empty, "rear"));
        JsonElement frontTyre = catalogs.Require(ReadString(tyres, string.Empty, "frontCompound"));
        JsonElement rearTyre = catalogs.Require(ReadString(tyres, string.Empty, "rearCompound"));
        JsonElement frontTyreModel = catalogs.Require(ReadString(tyres, string.Empty, "frontModel"));
        JsonElement rearTyreModel = catalogs.Require(ReadString(tyres, string.Empty, "rearModel"));
        JsonElement aeroPackage = catalogs.Require(ReadString(aero, string.Empty, "package"));
        JsonElement steeringSetup = chassisTunes.Require(ReadString(tuning, string.Empty, "steering"));
        JsonElement handlingSetup = chassisTunes.Require(ReadString(tuning, string.Empty, "handling"));

        string vehicleDefinitionPath = ReadString(root, string.Empty, "vehicleDefinitionPath");
        ResolvedEngineBuild engineBuild = ReadResolvedEngine(engine);
        ResolvedEngineAssembly engineAssembly = EngineAssemblyResolver.Resolve(engine);
        float frontTyreRadius = RequirePositive(frontTyre, "data", "loadedRadiusMeters");
        float rearTyreRadius = RequirePositive(rearTyre, "data", "loadedRadiusMeters");

        return new ResolvedVehicleBuild
        {
            Id = ReadString(root, Path.GetFileNameWithoutExtension(resolvedBuildPath), "id"),
            DisplayName = ReadString(root, string.Empty, "displayName"),
            VehicleDefinitionPath = vehicleDefinitionPath,
            ChassisVehicleId = ReadString(chassis, ReadString(chassis, string.Empty, "vehicleId"), "chassisId"),
            ChassisCode = ReadString(chassis, string.Empty, "chassisCode"),
            DrivetrainLayout = ReadString(chassis, string.Empty, "drivetrainLayout"),
            BodyShellId = ReadString(bodyShell, string.Empty, "id"),
            BodyShellName = ReadString(bodyShell, string.Empty, "displayName"),
            Engine = engineBuild,
            Mass = new ResolvedVehicleMass
            {
                BodyShellKg = ReadWeight(bodyShell),
                EngineAssemblyKg = engineAssembly.EstimatedAssemblyMassKg,
                GearboxKg = ReadWeight(gearbox),
                FinalDriveKg = ReadWeight(finalDrive),
                DifferentialKg = ReadWeight(differential),
                SwapKitKg = swapKitMassKg,
                FrontSuspensionKg = ReadWeight(frontSuspension),
                RearSuspensionKg = ReadWeight(rearSuspension),
                FrontBrakesKg = ReadWeight(frontBrakes),
                RearBrakesKg = ReadWeight(rearBrakes),
                FrontWheelKg = ReadWeight(frontWheel),
                RearWheelKg = ReadWeight(rearWheel),
                FrontTyreKg = ReadWeight(frontTyre),
                RearTyreKg = ReadWeight(rearTyre),
                CatalogVehicleSideKg = ReadWeight(bodyShell) + ReadWeight(gearbox) + ReadWeight(finalDrive) + ReadWeight(differential) + swapKitMassKg +
                    ReadWeight(frontSuspension) + ReadWeight(rearSuspension) + ReadWeight(frontBrakes) + ReadWeight(rearBrakes) +
                    ReadWeight(frontWheel) + ReadWeight(rearWheel) + ReadWeight(frontTyre) + ReadWeight(rearTyre)
            },
            Chassis = new ResolvedChassisBuild
            {
                WheelbaseMeters = RequirePositive(bodyShell, "data", "wheelbaseMeters"),
                FrontTrackMeters = RequirePositive(bodyShell, "data", "frontTrackMeters"),
                RearTrackMeters = RequirePositive(bodyShell, "data", "rearTrackMeters"),
                LengthMeters = RequirePositive(bodyShell, "data", "lengthMeters"),
                WidthMeters = RequirePositive(bodyShell, "data", "widthMeters"),
                HeightMeters = RequirePositive(bodyShell, "data", "heightMeters"),
                BaseCurbMassKg = RequirePositive(bodyShell, "data", "baseCurbMassKg"),
                CalibrationResidualMassKg = ReadSingle(bodyShell, float.NaN, "data", "calibrationResidualMassKg"),
                YawInertiaCalibrationScale = RequirePositive(bodyShell, "data", "yawInertiaCalibrationScale"),
                FrontWeightDistribution = RequireRange(bodyShell, 0.05f, 0.95f, "data", "frontWeightDistribution"),
                CenterOfGravityHeightMeters = RequirePositive(bodyShell, "data", "cgHeightMeters") + ReadSingle(bodyShell, 0f, "data", "cgHeightDeltaMeters"),
                BodyMassCenterY = ReadSingle(bodyShell, float.NaN, "data", "bodyMassCenterY") + ReadSingle(bodyShell, 0f, "data", "cgHeightDeltaMeters"),
                BodyMassCenterLongitudinalMeters = ReadSingle(bodyShell, float.NaN, "data", "bodyMassCenterLongitudinalMeters"),
                TorsionalRigidityNmPerDeg = RequirePositive(bodyShell, "data", "torsionalRigidityNmPerDeg"),
                FrontSuspensionHardPoints = ReadSuspensionHardPoints(bodyShell, "front"),
                RearSuspensionHardPoints = ReadSuspensionHardPoints(bodyShell, "rear")
            },
            Drivetrain = new ResolvedDrivetrainBuild
            {
                GearboxId = ReadString(gearbox, string.Empty, "id"),
                GearboxName = ReadString(gearbox, string.Empty, "displayName"),
                GearboxType = gearboxType,
                ReverseGearRatio = MathF.Abs(RequireNonZero(gearbox, "data", "reverseRatio")),
                ForwardGearRatios = ReadForwardRatios(gearbox),
                FinalDriveId = ReadString(finalDrive, string.Empty, "id"),
                FinalDriveRatio = RequirePositive(finalDrive, "data", "ratio"),
                DifferentialId = ReadString(differential, string.Empty, "id"),
                DifferentialType = ReadString(differential, string.Empty, "data", "type"),
                DifferentialTorqueBiasRatio = RequirePositive(differential, "data", "torqueBiasRatio"),
                DifferentialPreloadTorqueNm = ReadSingle(differential, 0f, "data", "preloadNm"),
                TransmissionEfficiency = RequireRange(gearbox, 0.1f, 1f, "data", "efficiency"),
                ManualShiftTimeSeconds = RequirePositive(gearbox, "data", "manualShiftTimeSeconds"),
                AutomaticShiftTimeSeconds = RequirePositive(gearbox, "data", "automaticShiftTimeSeconds"),
                ShiftShockMultiplier = ReadSingle(
                    gearbox,
                    CalculateDefaultGearboxShiftShockMultiplier(gearboxType),
                    "data",
                    "shiftShockMultiplier"),
                DownshiftOverRevToleranceRpm = RequirePositive(gearbox, "data", "downshiftOverRevToleranceRpm"),
                DownshiftMechanicalOverRevLimitRpm = RequirePositive(gearbox, "data", "downshiftMechanicalOverRevLimitRpm"),
                DownshiftOverRevBrakeMultiplier = RequirePositive(gearbox, "data", "downshiftOverRevBrakeMultiplier"),
                DownshiftOverRevShockSeconds = RequirePositive(gearbox, "data", "downshiftOverRevShockSeconds")
            },
            SwapKits = new ResolvedSwapKitBuild
            {
                InstalledParts = swapKitIds,
                TotalMassKg = swapKitMassKg
            },
            Suspension = new ResolvedSuspensionBuild
            {
                FrontId = ReadString(frontSuspension, string.Empty, "id"),
                RearId = ReadString(rearSuspension, string.Empty, "id"),
                AlignmentId = ReadString(alignment, string.Empty, "id"),
                FrontSpringRateNPerM = RequirePositive(frontSuspension, "data", "springRateNPerM"),
                RearSpringRateNPerM = RequirePositive(rearSuspension, "data", "springRateNPerM"),
                FrontBumpDampingNsPerM = RequirePositive(frontSuspension, "data", "bumpDampingNsPerM"),
                RearBumpDampingNsPerM = RequirePositive(rearSuspension, "data", "bumpDampingNsPerM"),
                FrontReboundDampingNsPerM = RequirePositive(frontSuspension, "data", "reboundDampingNsPerM"),
                RearReboundDampingNsPerM = RequirePositive(rearSuspension, "data", "reboundDampingNsPerM"),
                FrontRideHeightMeters = RequirePositive(frontSuspension, "data", "rideHeightMeters"),
                RearRideHeightMeters = RequirePositive(rearSuspension, "data", "rideHeightMeters"),
                FrontRollCentreHeightMeters = RequirePositive(frontSuspension, "data", "rollCentreHeightMeters"),
                RearRollCentreHeightMeters = RequirePositive(rearSuspension, "data", "rollCentreHeightMeters"),
                FrontMaxCompressionMeters = RequirePositive(frontSuspension, "data", "maxCompressionMeters"),
                RearMaxCompressionMeters = RequirePositive(rearSuspension, "data", "maxCompressionMeters"),
                FrontMaxDroopMeters = RequirePositive(frontSuspension, "data", "maxDroopMeters"),
                RearMaxDroopMeters = RequirePositive(rearSuspension, "data", "maxDroopMeters"),
                FrontAntiRollBarRateNmPerRad = RequirePositive(frontSuspension, "data", "antiRollBarRateNmPerRad"),
                RearAntiRollBarRateNmPerRad = RequirePositive(rearSuspension, "data", "antiRollBarRateNmPerRad"),
                FrontCamberDegrees = ReadSingle(alignment, 0f, "data", "frontCamberDegrees"),
                RearCamberDegrees = ReadSingle(alignment, 0f, "data", "rearCamberDegrees"),
                FrontToeDegrees = ReadSingle(alignment, 0f, "data", "frontToeDegrees"),
                RearToeDegrees = ReadSingle(alignment, 0f, "data", "rearToeDegrees"),
                FrontCasterDegrees = ReadSingle(alignment, 0f, "data", "frontCasterDegrees")
            },
            Brakes = new ResolvedBrakeBuild
            {
                FrontId = ReadString(frontBrakes, string.Empty, "id"),
                RearId = ReadString(rearBrakes, string.Empty, "id"),
                FrontDiscDiameterMm = RequirePositive(frontBrakes, "data", "discDiameterMm"),
                RearDiscDiameterMm = RequirePositive(rearBrakes, "data", "discDiameterMm"),
                FrontEffectiveRadiusRatio = ReadSingle(frontBrakes, 0.42f, "data", "effectiveRadiusRatio"),
                RearEffectiveRadiusRatio = ReadSingle(rearBrakes, 0.42f, "data", "effectiveRadiusRatio"),
                FrontTotalPistonAreaSquareMeters = CalculateTotalPistonAreaSquareMeters(frontBrakes),
                RearTotalPistonAreaSquareMeters = CalculateTotalPistonAreaSquareMeters(rearBrakes),
                FrontClampForceMultiplier = ReadSingle(frontBrakes, 2f, "data", "clampForceMultiplier"),
                RearClampForceMultiplier = ReadSingle(rearBrakes, 2f, "data", "clampForceMultiplier"),
                FrontPadFriction = RequirePositive(frontBrakes, "data", "padFriction"),
                RearPadFriction = RequirePositive(rearBrakes, "data", "padFriction"),
                System = new ResolvedBrakeSystemBuild
                {
                    Id = ReadString(brakeSystem, string.Empty, "id"),
                    MaxLinePressureBar = RequirePositive(brakeSystem, "data", "maxLinePressureBar"),
                    BrakeBiasFront = RequireRange(brakeSystem, 0.05f, 0.95f, "data", "brakeBiasFront"),
                    HandbrakeRearTorqueNm = RequirePositive(brakeSystem, "data", "handbrakeRearTorqueNm"),
                    PressureRiseRatePerSecond = RequirePositive(brakeSystem, "data", "pressureRiseRatePerSecond"),
                    PressureReleaseRatePerSecond = RequirePositive(brakeSystem, "data", "pressureReleaseRatePerSecond"),
                    MaxBrakeForceN = RequirePositive(brakeSystem, "data", "maxBrakeForceN"),
                    AbsEnabled = ReadBoolean(brakeSystem, false, "data", "abs", "enabled"),
                    AbsTargetSlipRatio = ReadSingle(brakeSystem, -0.1f, "data", "abs", "targetSlipRatio"),
                    AbsReleaseSlipRatio = ReadSingle(brakeSystem, -0.17f, "data", "abs", "releaseSlipRatio"),
                    AbsApplyRatePerSecond = RequirePositive(brakeSystem, "data", "abs", "applyRatePerSecond"),
                    AbsReleaseRatePerSecond = RequirePositive(brakeSystem, "data", "abs", "releaseRatePerSecond"),
                    AbsMinimumSpeedKph = RequirePositive(brakeSystem, "data", "abs", "minimumSpeedKph"),
                    AbsMinimumPressureRatio = RequireRange(brakeSystem, 0f, 1f, "data", "abs", "minimumPressureRatio")
                }
            },
            Wheels = new ResolvedWheelBuild
            {
                FrontId = ReadString(frontWheel, string.Empty, "id"),
                RearId = ReadString(rearWheel, string.Empty, "id"),
                FrontDiameterIn = RequirePositive(frontWheel, "data", "diameterIn"),
                RearDiameterIn = RequirePositive(rearWheel, "data", "diameterIn"),
                FrontWidthIn = RequirePositive(frontWheel, "data", "widthIn"),
                RearWidthIn = RequirePositive(rearWheel, "data", "widthIn"),
                FrontOffsetMm = ReadSingle(frontWheel, 0f, "data", "offsetMm"),
                RearOffsetMm = ReadSingle(rearWheel, 0f, "data", "offsetMm")
            },
            Tyres = new ResolvedTyreBuild
            {
                FrontId = ReadString(frontTyre, string.Empty, "id"),
                RearId = ReadString(rearTyre, string.Empty, "id"),
                FrontSize = ReadTyreSize(frontTyre),
                RearSize = ReadTyreSize(rearTyre),
                FrontLoadedRadiusMeters = frontTyreRadius,
                RearLoadedRadiusMeters = rearTyreRadius,
                FrontPeakFriction = RequirePositive(frontTyre, "data", "frontPeakFriction"),
                RearPeakFriction = RequirePositive(rearTyre, "data", "rearPeakFriction"),
                FrontRollingResistance = RequirePositive(frontTyre, "data", "rollingResistance"),
                RearRollingResistance = RequirePositive(rearTyre, "data", "rollingResistance"),
                FrontModel = ReadTyreModel(frontTyreModel),
                RearModel = ReadTyreModel(rearTyreModel)
            },
            Aero = new ResolvedAeroBuild
            {
                Id = ReadString(aeroPackage, string.Empty, "id"),
                DragCoefficient = RequirePositive(aeroPackage, "data", "dragCoefficient"),
                FrontalAreaSquareMeters = RequirePositive(aeroPackage, "data", "frontalAreaSquareMeters"),
                FrontLiftCoefficient = ReadSingle(aeroPackage, 0f, "data", "frontLiftCoefficient"),
                RearLiftCoefficient = ReadSingle(aeroPackage, 0f, "data", "rearLiftCoefficient")
            },
            Steering = new ResolvedSteeringSetup
            {
                Id = ReadString(steeringSetup, string.Empty, "id"),
                Ratio = RequirePositive(steeringSetup, "data", "ratio"),
                SteeringWheelLockDegrees = RequirePositive(steeringSetup, "data", "steeringWheelLockDegrees"),
                InputRatePerSecond = RequirePositive(steeringSetup, "data", "inputRatePerSecond"),
                ReturnRatePerSecond = RequirePositive(steeringSetup, "data", "returnRatePerSecond"),
                MaxInnerWheelAngleDegrees = RequirePositive(steeringSetup, "data", "maxInnerWheelAngleDegrees"),
                AckermannPercent = RequireRange(steeringSetup, 0f, 150f, "data", "ackermannPercent"),
                FullLockSpeedKph = ReadSingle(steeringSetup, 0f, "data", "speedSensitiveAssist", "fullLockSpeedKph"),
                ReducedLockSpeedKph = RequirePositive(steeringSetup, "data", "speedSensitiveAssist", "reducedLockSpeedKph"),
                HighSpeedLockMultiplier = RequireRange(steeringSetup, 0f, 1f, "data", "speedSensitiveAssist", "highSpeedLockMultiplier"),
                HighSpeedInputRateMultiplier = RequireRange(steeringSetup, 0f, 1f, "data", "speedSensitiveAssist", "highSpeedInputRateMultiplier"),
                HighSpeedReturnRateMultiplier = RequireRange(steeringSetup, 0f, 1f, "data", "speedSensitiveAssist", "highSpeedReturnRateMultiplier"),
                TargetLateralAccelerationG = RequirePositive(steeringSetup, "data", "speedSensitiveAssist", "targetLateralAccelerationG"),
                PeakSlipAngleFraction = RequireRange(steeringSetup, 0f, 1f, "data", "speedSensitiveAssist", "peakSlipAngleFraction"),
                LowSpeedReferenceKph = RequirePositive(steeringSetup, "data", "speedSensitiveAssist", "lowSpeedReferenceKph"),
                MinimumRoadWheelAngleDegrees = RequirePositive(steeringSetup, "data", "speedSensitiveAssist", "minimumRoadWheelAngleDegrees")
            },
            Handling = new ResolvedHandlingSetup
            {
                Id = ReadString(handlingSetup, string.Empty, "id"),
                DrivetrainEfficiency = RequireRange(handlingSetup, 0.1f, 1f, "data", "drivetrainEfficiency"),
                ClosedThrottleEngineBrakeTorqueNm = RequirePositive(handlingSetup, "data", "closedThrottleEngineBrakeTorqueNm"),
                LateralGripResponse = RequirePositive(handlingSetup, "data", "lateralGripResponse"),
                RollingResistanceCoefficient = RequirePositive(handlingSetup, "data", "rollingResistanceCoefficient"),
                AirDensityKgM3 = RequirePositive(handlingSetup, "data", "airDensityKgM3"),
                AutomaticUpshiftRpm = RequirePositive(handlingSetup, "data", "automaticUpshiftRpm"),
                AutomaticMinimumUpshiftSpeedKph = RequirePositive(handlingSetup, "data", "automaticMinimumUpshiftSpeedKph"),
                AutomaticDownshiftRpm = RequirePositive(handlingSetup, "data", "automaticDownshiftRpm"),
                EngineFreeRevResponseRate = RequirePositive(handlingSetup, "data", "engineFreeRevResponseRate"),
                MaxFreeRevRiseRpmPerSecond = RequirePositive(handlingSetup, "data", "maxFreeRevRiseRpmPerSecond"),
                MaxFreeRevFallRpmPerSecond = RequirePositive(handlingSetup, "data", "maxFreeRevFallRpmPerSecond"),
                WallCollisionPointRadiusMeters = RequirePositive(handlingSetup, "data", "wallCollisionPointRadiusMeters"),
                WallCollisionRestitution = RequireRange(handlingSetup, 0f, 1f, "data", "wallCollisionRestitution"),
                WallImpactFriction = RequireRange(handlingSetup, 0f, 1f, "data", "wallImpactFriction"),
                WallScrapeFriction = RequireRange(handlingSetup, 0f, 1f, "data", "wallScrapeFriction"),
                WallYawImpulseScale = RequireRange(handlingSetup, 0f, 2f, "data", "wallYawImpulseScale"),
                Arcade = ReadArcadeHandling(handlingSetup)
            }
        };
    }

    public static JsonElement LoadVehicleCatalogItemForDiagnostics(string id)
    {
        return CatalogLookup.Load(VehicleCatalogIndexPath).Require(id);
    }

    public static VehicleSimulationParameters LoadSimulationParameters(string buildPath)
    {
        ResolvedVehicleBuild build = Load(buildPath);
        ResolvedVehicleAssembly assembly = VehicleAssemblyResolver.Resolve(buildPath);
        ResolvedEngineAssembly engineAssembly = assembly.Engine;
        ResolvedMassProperties massProperties = assembly.MassProperties;

        return new VehicleSimulationParameters
        {
            Id = build.Id,
            DisplayName = build.DisplayName,
            MassKg = massProperties.TotalMassKg,
            WheelbaseMeters = build.Chassis.WheelbaseMeters,
            FrontTrackMeters = build.Chassis.FrontTrackMeters,
            RearTrackMeters = build.Chassis.RearTrackMeters,
            BodyLengthMeters = build.Chassis.LengthMeters,
            BodyWidthMeters = build.Chassis.WidthMeters,
            FrontWeightDistribution = massProperties.FrontWeightDistribution,
            CenterOfGravityHeightMeters = massProperties.CenterOfGravityHeightMeters,
            YawInertiaKgM2 = massProperties.YawInertiaKgM2,
            WheelRadiusMeters = build.Tyres.FrontLoadedRadiusMeters,
            FinalDriveRatio = build.Drivetrain.FinalDriveRatio,
            DrivetrainEfficiency = build.Handling.DrivetrainEfficiency,
            ClosedThrottleEngineBrakeTorqueNm = build.Handling.ClosedThrottleEngineBrakeTorqueNm,
            IdleRpm = engineAssembly.IdleRpm,
            PowerRedlineRpm = engineAssembly.PowerRedlineRpm,
            LimiterHardCutRpm = engineAssembly.LimiterHardCutRpm,
            MaxGaugeRpm = engineAssembly.MaxGaugeRpm > 0f
                ? engineAssembly.MaxGaugeRpm
                : CalculateDefaultMaxGaugeRpm(engineAssembly.LimiterHardCutRpm),
            RevLimiterResumeRpm = engineAssembly.LimiterResumeRpm,
            RevLimiterFuelCutSeconds = engineAssembly.LimiterFuelCutSeconds,
            RevLimiterRestoreSeconds = engineAssembly.LimiterRestoreSeconds,
            RevLimiterCutTorqueMultiplier = engineAssembly.LimiterCutTorqueMultiplier,
            RevLimiterBounceRpm = CalculateRevLimiterBounceRpm(engineAssembly.LimiterHardCutRpm),
            EngineRotationalInertiaKgM2 = engineAssembly.RotationalInertiaKgM2,
            VtecEnabled = engineAssembly.VtecEnabled,
            VtecActivationRpm = engineAssembly.VtecActivationRpm,
            VtecTransitionWidthRpm = engineAssembly.VtecTransitionWidthRpm,
            VtecLowCamFlowMultiplier = engineAssembly.LowCamFlowMultiplier,
            VtecHighCamFlowMultiplier = engineAssembly.HighCamFlowMultiplier,
            EngineSimulatorDrivesPhysics = false,
            EngineSimulatorFullDriveline = false,
            EngineSimulatorPhysicsTorqueBlend = 0f,
            EngineSimulatorPhysicsUseReferenceTorqueCalibration = false,
            EngineSimulatorPhysicsEngineBrakeBlend = 0f,
            ClutchTorqueCapacityNm = engineAssembly.ClutchTorqueCapacityNm,
            ClutchEngagementPoint = engineAssembly.ClutchBitePoint,
            ClutchCouplingRate = engineAssembly.ClutchCouplingRate,
            ClutchEngagementSharpness = engineAssembly.ClutchEngagementSharpness,
            ClutchSlipDamping = engineAssembly.ClutchSlipDamping,
            ClutchShiftKickIntensity = engineAssembly.ClutchShiftKickIntensity,
            ClutchLowSpeedAssistStrength = engineAssembly.ClutchLowSpeedAssistStrength,
            ClutchBiteInputStartMultiplier = engineAssembly.ClutchBiteInputStartMultiplier,
            ClutchLaunchAssistExponent = engineAssembly.ClutchLaunchAssistExponent,
            ClutchLowSpeedThrottleGamma = engineAssembly.ClutchLowSpeedThrottleGamma,
            ClutchLowSpeedThrottleAssist = engineAssembly.ClutchLowSpeedThrottleAssist,
            ClutchLowSpeedTorqueAssistNm = engineAssembly.ClutchLowSpeedTorqueAssistNm,
            ClutchRollingLockSpeedMetersPerSecond = engineAssembly.ClutchRollingLockSpeedMetersPerSecond,
            ClutchRollingLockSlipRadiansPerSecond = engineAssembly.ClutchRollingLockSlipRadiansPerSecond,
            EngineFreeRevResponseRate = build.Handling.EngineFreeRevResponseRate,
            MaxFreeRevRiseRpmPerSecond = build.Handling.MaxFreeRevRiseRpmPerSecond,
            MaxFreeRevFallRpmPerSecond = build.Handling.MaxFreeRevFallRpmPerSecond,
            UpshiftRpm = build.Handling.AutomaticUpshiftRpm,
            DownshiftRpm = build.Handling.AutomaticDownshiftRpm,
            AutomaticMinimumUpshiftSpeedMetersPerSecond = build.Handling.AutomaticMinimumUpshiftSpeedKph / 3.6f,
            ManualShiftTimeSeconds = build.Drivetrain.ManualShiftTimeSeconds,
            AutomaticShiftTimeSeconds = build.Drivetrain.AutomaticShiftTimeSeconds,
            GearboxType = build.Drivetrain.GearboxType,
            GearboxShiftShockMultiplier = build.Drivetrain.ShiftShockMultiplier,
            DownshiftOverRevToleranceRpm = build.Drivetrain.DownshiftOverRevToleranceRpm,
            DownshiftMechanicalOverRevLimitRpm = build.Drivetrain.DownshiftMechanicalOverRevLimitRpm,
            DownshiftOverRevBrakeMultiplier = build.Drivetrain.DownshiftOverRevBrakeMultiplier,
            DownshiftOverRevShockSeconds = build.Drivetrain.DownshiftOverRevShockSeconds,
            ForwardGearRatios = [.. build.Drivetrain.ForwardGearRatios],
            ReverseGearRatio = build.Drivetrain.ReverseGearRatio,
            MaxBrakeForceN = build.Brakes.System.MaxBrakeForceN,
            BrakeBiasFront = build.Brakes.System.BrakeBiasFront,
            Brakes = BuildBrakes(build.Brakes),
            AeroDragFactor = 0.5f * build.Handling.AirDensityKgM3 * build.Aero.DragCoefficient * build.Aero.FrontalAreaSquareMeters,
            FrontLiftFactor = 0.5f * build.Handling.AirDensityKgM3 * build.Aero.FrontLiftCoefficient * build.Aero.FrontalAreaSquareMeters,
            RearLiftFactor = 0.5f * build.Handling.AirDensityKgM3 * build.Aero.RearLiftCoefficient * build.Aero.FrontalAreaSquareMeters,
            RollingResistanceCoefficient = build.Handling.RollingResistanceCoefficient,
            LateralGripResponse = build.Handling.LateralGripResponse,
            ArcadeHandling = build.Handling.Arcade,
            SteeringRatio = build.Steering.Ratio,
            SteeringWheelLockDegrees = build.Steering.SteeringWheelLockDegrees,
            SteeringInputRatePerSecond = build.Steering.InputRatePerSecond,
            SteeringReturnRatePerSecond = build.Steering.ReturnRatePerSecond,
            SteeringHighSpeedInputRateMultiplier = build.Steering.HighSpeedInputRateMultiplier,
            SteeringHighSpeedReturnRateMultiplier = build.Steering.HighSpeedReturnRateMultiplier,
            SteeringFullLockSpeedMetersPerSecond = build.Steering.FullLockSpeedKph / 3.6f,
            SteeringReducedLockSpeedMetersPerSecond = build.Steering.ReducedLockSpeedKph / 3.6f,
            SteeringHighSpeedLockMultiplier = build.Steering.HighSpeedLockMultiplier,
            SteeringTargetLateralAccelerationG = build.Steering.TargetLateralAccelerationG,
            SteeringPeakSlipAngleFraction = build.Steering.PeakSlipAngleFraction,
            SteeringLowSpeedReferenceMetersPerSecond = build.Steering.LowSpeedReferenceKph / 3.6f,
            SteeringMinimumHighSpeedAngleRadians = MathHelper.ToRadians(build.Steering.MinimumRoadWheelAngleDegrees),
            MaxSteerAngleRadians = MathHelper.ToRadians(build.Steering.MaxInnerWheelAngleDegrees),
            AckermannPercent = build.Steering.AckermannPercent,
            FrontSpringRateNPerM = build.Suspension.FrontSpringRateNPerM,
            RearSpringRateNPerM = build.Suspension.RearSpringRateNPerM,
            FrontBumpDampingNsPerM = build.Suspension.FrontBumpDampingNsPerM,
            RearBumpDampingNsPerM = build.Suspension.RearBumpDampingNsPerM,
            FrontReboundDampingNsPerM = build.Suspension.FrontReboundDampingNsPerM,
            RearReboundDampingNsPerM = build.Suspension.RearReboundDampingNsPerM,
            FrontAntiRollBarRateNmPerRad = build.Suspension.FrontAntiRollBarRateNmPerRad,
            RearAntiRollBarRateNmPerRad = build.Suspension.RearAntiRollBarRateNmPerRad,
            FrontSuspensionGeometry = BuildSuspensionGeometry(build.Chassis.FrontSuspensionHardPoints, build.Suspension, true),
            RearSuspensionGeometry = BuildSuspensionGeometry(build.Chassis.RearSuspensionHardPoints, build.Suspension, false),
            DifferentialTorqueBiasRatio = build.Drivetrain.DifferentialTorqueBiasRatio,
            DifferentialPreloadTorqueNm = build.Drivetrain.DifferentialPreloadTorqueNm,
            DrivetrainLayout = ReadDrivetrainLayout(build.DrivetrainLayout),
            FrontTorqueShare = CalculateFrontTorqueShare(ReadDrivetrainLayout(build.DrivetrainLayout)),
            FrontDifferential = BuildDifferentialParameters(build.Drivetrain, ReadDrivetrainLayout(build.DrivetrainLayout), frontAxle: true),
            RearDifferential = BuildDifferentialParameters(build.Drivetrain, ReadDrivetrainLayout(build.DrivetrainLayout), frontAxle: false),
            WheelInertiaKgM2 = EstimateWheelInertia(build),
            DrivenWheels = ReadDrivenWheels(build.DrivetrainLayout),
            FrontTyres = BuildTyres(build.Tyres, true),
            RearTyres = BuildTyres(build.Tyres, false),
            Audio = VehicleRaceSampleAudioBuilder.Build(engineAssembly, build.Drivetrain, buildPath),
            WallCollisionPointRadiusMeters = build.Handling.WallCollisionPointRadiusMeters,
            WallCollisionRestitution = build.Handling.WallCollisionRestitution,
            WallImpactFriction = build.Handling.WallImpactFriction,
            WallScrapeFriction = build.Handling.WallScrapeFriction,
            WallYawImpulseScale = build.Handling.WallYawImpulseScale,
            TorqueCurve = engineAssembly.TorqueCurve,
            EngineBrakeTorqueCurve = engineAssembly.EngineBrakeTorqueCurve
        };
    }

    private static float CalculateRevLimiterBounceRpm(float redlineRpm)
    {
        return RevLimiterPresentationRules.CalculateBounceDepthRpm(redlineRpm);
    }

    private static float CalculateDefaultMaxGaugeRpm(float limiterRpm)
    {
        float padded = MathF.Max(1000f, limiterRpm) + 1000f;
        return MathF.Ceiling(padded / 1000f) * 1000f;
    }

    private static BrakeSystemParameters BuildBrakes(ResolvedBrakeBuild build)
    {
        return new BrakeSystemParameters
        {
            MaxLinePressurePa = build.System.MaxLinePressureBar * 100000f,
            BrakeBiasFront = build.System.BrakeBiasFront,
            HandbrakeRearTorqueNm = build.System.HandbrakeRearTorqueNm,
            PressureRiseRatePerSecond = build.System.PressureRiseRatePerSecond,
            PressureReleaseRatePerSecond = build.System.PressureReleaseRatePerSecond,
            Front = MergeBrakeAxle(build.FrontDiscDiameterMm, build.FrontEffectiveRadiusRatio, build.FrontTotalPistonAreaSquareMeters, build.FrontClampForceMultiplier, build.FrontPadFriction),
            Rear = MergeBrakeAxle(build.RearDiscDiameterMm, build.RearEffectiveRadiusRatio, build.RearTotalPistonAreaSquareMeters, build.RearClampForceMultiplier, build.RearPadFriction),
            Abs = new AbsParameters
            {
                Enabled = build.System.AbsEnabled,
                TargetSlipRatio = build.System.AbsTargetSlipRatio,
                ReleaseSlipRatio = build.System.AbsReleaseSlipRatio,
                ApplyRatePerSecond = build.System.AbsApplyRatePerSecond,
                ReleaseRatePerSecond = build.System.AbsReleaseRatePerSecond,
                MinimumSpeedMetersPerSecond = build.System.AbsMinimumSpeedKph / 3.6f,
                MinimumPressureRatio = build.System.AbsMinimumPressureRatio
            }
        };
    }

    private static BrakeAxleParameters MergeBrakeAxle(
        float discDiameterMm,
        float effectiveRadiusRatio,
        float totalPistonAreaSquareMeters,
        float clampForceMultiplier,
        float padFriction)
    {
        return new BrakeAxleParameters
        {
            DiscDiameterMeters = discDiameterMm / 1000f,
            EffectiveRadiusRatio = effectiveRadiusRatio,
            TotalPistonAreaSquareMeters = totalPistonAreaSquareMeters,
            ClampForceMultiplier = clampForceMultiplier,
            PadFrictionCoefficient = padFriction
        };
    }

    private static SuspensionGeometryParameters BuildSuspensionGeometry(
        ResolvedSuspensionHardPoints hardPoints,
        ResolvedSuspensionBuild build,
        bool front)
    {
        return new SuspensionGeometryParameters
        {
            StaticCamberRadians = MathHelper.ToRadians(front ? build.FrontCamberDegrees : build.RearCamberDegrees),
            StaticToeRadians = MathHelper.ToRadians(front ? build.FrontToeDegrees : build.RearToeDegrees),
            CasterRadians = MathHelper.ToRadians(front ? build.FrontCasterDegrees : hardPoints.CasterDegrees),
            CamberGainRadiansPerMeter = MathHelper.ToRadians(hardPoints.CamberGainDegreesPerMeter),
            ToeGainRadiansPerMeter = MathHelper.ToRadians(hardPoints.ToeGainDegreesPerMeter),
            BodyRollCamberMultiplier = hardPoints.BodyRollCamberMultiplier,
            CasterCamberGain = hardPoints.CasterCamberGain,
            MaxCompressionMeters = front ? build.FrontMaxCompressionMeters : build.RearMaxCompressionMeters,
            MaxDroopMeters = front ? build.FrontMaxDroopMeters : build.RearMaxDroopMeters
        };
    }

    private static ResolvedSuspensionHardPoints ReadSuspensionHardPoints(JsonElement bodyShell, string axle)
    {
        return new ResolvedSuspensionHardPoints
        {
            Type = ReadString(bodyShell, string.Empty, "data", "suspensionHardPoints", axle, "type"),
            RollCentreHeightMeters = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "rollCentreHeightMeters"),
            CamberGainDegreesPerMeter = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "camberGainDegreesPerMeter"),
            ToeGainDegreesPerMeter = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "toeGainDegreesPerMeter"),
            BodyRollCamberMultiplier = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "bodyRollCamberMultiplier"),
            CasterDegrees = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "casterDegrees"),
            CasterCamberGain = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "casterCamberGain"),
            MaxCompressionMeters = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "maxCompressionMeters"),
            MaxDroopMeters = ReadSingle(bodyShell, 0f, "data", "suspensionHardPoints", axle, "maxDroopMeters")
        };
    }

    private static TyreAxleParameters BuildTyres(ResolvedTyreBuild build, bool front)
    {
        ResolvedTyreModel model = front ? build.FrontModel : build.RearModel;
        return new TyreAxleParameters
        {
            LoadedRadiusMeters = front ? build.FrontLoadedRadiusMeters : build.RearLoadedRadiusMeters,
            PeakFriction = front ? build.FrontPeakFriction : build.RearPeakFriction,
            RollingResistanceCoefficient = front ? build.FrontRollingResistance : build.RearRollingResistance,
            LoadSensitivity = model.LoadSensitivity,
            CorneringStiffnessNPerRad = model.CorneringStiffnessNPerRad,
            LongitudinalStiffnessN = model.LongitudinalStiffnessN,
            LateralPeakSlipAngleRadians = MathHelper.ToRadians(model.LateralPeakSlipAngleDegrees),
            LateralSlideSlipAngleRadians = MathHelper.ToRadians(model.LateralSlideSlipAngleDegrees),
            LateralForceRiseShape = model.LateralForceRiseShape,
            SlidingLateralFrictionMultiplier = model.SlidingLateralFrictionMultiplier,
            RelaxationLengthMeters = model.RelaxationLengthMeters,
            LateralScrubDragCoefficient = model.LateralScrubDragCoefficient,
            IdealCamberRadians = MathHelper.ToRadians(model.IdealCamberDegrees),
            CamberGripLossPerDegree = model.CamberGripLossPerDegree,
            MinimumCamberGripMultiplier = model.MinimumCamberGripMultiplier,
            CamberThrustStiffnessNPerRad = model.CamberThrustStiffnessNPerRad,
            LongitudinalPeakSlipRatio = model.LongitudinalPeakSlipRatio,
            LongitudinalForceRiseShape = model.LongitudinalForceRiseShape,
            LongitudinalSlideSlipRatio = model.LongitudinalSlideSlipRatio,
            SlidingFrictionMultiplier = model.SlidingFrictionMultiplier
        };
    }

    private static ResolvedTyreModel ReadTyreModel(JsonElement model)
    {
        return new ResolvedTyreModel
        {
            Id = ReadString(model, string.Empty, "id"),
            LoadSensitivity = RequirePositive(model, "data", "loadSensitivity"),
            CorneringStiffnessNPerRad = RequirePositive(model, "data", "corneringStiffnessNPerRad"),
            LongitudinalStiffnessN = RequirePositive(model, "data", "longitudinalStiffnessN"),
            LateralPeakSlipAngleDegrees = RequirePositive(model, "data", "lateralPeakSlipAngleDegrees"),
            LateralSlideSlipAngleDegrees = RequirePositive(model, "data", "lateralSlideSlipAngleDegrees"),
            LateralForceRiseShape = RequirePositive(model, "data", "lateralForceRiseShape"),
            SlidingLateralFrictionMultiplier = RequireRange(model, 0f, 2f, "data", "slidingLateralFrictionMultiplier"),
            RelaxationLengthMeters = RequirePositive(model, "data", "relaxationLengthMeters"),
            LateralScrubDragCoefficient = RequireRange(model, 0f, 2f, "data", "lateralScrubDragCoefficient"),
            IdealCamberDegrees = ReadSingle(model, 0f, "data", "idealCamberDegrees"),
            CamberGripLossPerDegree = RequirePositive(model, "data", "camberGripLossPerDegree"),
            MinimumCamberGripMultiplier = RequireRange(model, 0f, 1f, "data", "minimumCamberGripMultiplier"),
            CamberThrustStiffnessNPerRad = RequirePositive(model, "data", "camberThrustStiffnessNPerRad"),
            LongitudinalPeakSlipRatio = RequirePositive(model, "data", "longitudinalPeakSlipRatio"),
            LongitudinalForceRiseShape = RequirePositive(model, "data", "longitudinalForceRiseShape"),
            LongitudinalSlideSlipRatio = RequirePositive(model, "data", "longitudinalSlideSlipRatio"),
            SlidingFrictionMultiplier = RequireRange(model, 0f, 2f, "data", "slidingFrictionMultiplier")
        };
    }

    private static ArcadeHandlingParameters ReadArcadeHandling(JsonElement handling)
    {
        return new ArcadeHandlingParameters
        {
            PseudoLateralTransferScale = RequirePositive(handling, "data", "arcadeHandling", "pseudoLateralTransferScale"),
            PseudoLateralTransferBlend = RequireRange(handling, 0f, 1f, "data", "arcadeHandling", "pseudoLateralTransferBlend"),
            DrivenGripAllowance = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "drivenGripAllowance"),
            GenericGripAllowance = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "genericGripAllowance"),
            BrakingGripAllowance = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "brakingGripAllowance"),
            BrakingSlidingFrictionFloor = RequireRange(handling, 0f, 1f, "data", "arcadeHandling", "brakingSlidingFrictionFloor"),
            PassiveSlideRecoveryLateralSpeedMetersPerSecond = RequirePositive(handling, "data", "arcadeHandling", "passiveSlideRecoveryLateralSpeedMetersPerSecond"),
            PassiveSlideRecoveryYawRateDegreesPerSecond = RequirePositive(handling, "data", "arcadeHandling", "passiveSlideRecoveryYawRateDegreesPerSecond"),
            WallImpactVelocityMultiplier = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "wallImpactVelocityMultiplier"),
            WallDirectImpactBlendStart = RequireRange(handling, 0f, 1f, "data", "arcadeHandling", "wallDirectImpactBlendStart"),
            WallDirectImpactBlendEnd = RequireRange(handling, 0f, 1f, "data", "arcadeHandling", "wallDirectImpactBlendEnd"),
            VisualSuspensionMotionScale = RequirePositive(handling, "data", "arcadeHandling", "visualSuspensionMotionScale"),
            VisualSuspensionHeavePitchScale = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "visualSuspensionHeavePitchScale"),
            VisualSuspensionLoadTransferMeters = RequirePositive(handling, "data", "arcadeHandling", "visualSuspensionLoadTransferMeters"),
            VisualSuspensionSpringRate = RequirePositive(handling, "data", "arcadeHandling", "visualSuspensionSpringRate"),
            VisualSuspensionDampingRate = RequirePositive(handling, "data", "arcadeHandling", "visualSuspensionDampingRate"),
            FrontVisualSuspensionMultiplier = RequirePositive(handling, "data", "arcadeHandling", "frontVisualSuspensionMultiplier"),
            RearVisualSuspensionMultiplier = RequirePositive(handling, "data", "arcadeHandling", "rearVisualSuspensionMultiplier"),
            VisualBodyPitchScale = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "visualBodyPitchScale"),
            VisualBodyRollScale = RequireRange(handling, 0f, 2f, "data", "arcadeHandling", "visualBodyRollScale"),
            VisualBodyPitchLimitRadians = RequirePositive(handling, "data", "arcadeHandling", "visualBodyPitchLimitRadians"),
            VisualBodyRollLimitRadians = RequirePositive(handling, "data", "arcadeHandling", "visualBodyRollLimitRadians")
        };
    }

    private static float EstimateWheelInertia(ResolvedVehicleBuild build)
    {
        float frontRotatingMass = build.Mass.FrontWheelKg + build.Mass.FrontTyreKg;
        float radius = MathF.Max(0.1f, build.Tyres.FrontLoadedRadiusMeters);
        return MathF.Max(0.35f, frontRotatingMass * radius * radius * 0.62f);
    }

    private static DrivenWheelSet ReadDrivenWheels(string layout)
    {
        return ReadDrivetrainLayout(layout) switch
        {
            DrivetrainLayout.FF => new DrivenWheelSet(true, true, false, false),
            DrivetrainLayout.FR => new DrivenWheelSet(false, false, true, true),
            DrivetrainLayout.AWD => new DrivenWheelSet(true, true, true, true),
            _ => new DrivenWheelSet(true, true, false, false)
        };
    }

    private static DrivetrainLayout ReadDrivetrainLayout(string layout)
    {
        return layout.Trim().ToUpperInvariant() switch
        {
            "FR" or "MR" or "RR" => DrivetrainLayout.FR,
            "AWD" or "4WD" => DrivetrainLayout.AWD,
            _ => DrivetrainLayout.FF
        };
    }

    private static float CalculateFrontTorqueShare(DrivetrainLayout layout)
    {
        return layout switch
        {
            DrivetrainLayout.FF => 1f,
            DrivetrainLayout.FR => 0f,
            DrivetrainLayout.AWD => 0.5f,
            _ => 1f
        };
    }

    private static DifferentialParameters BuildDifferentialParameters(
        ResolvedDrivetrainBuild drivetrain,
        DrivetrainLayout layout,
        bool frontAxle)
    {
        bool legacyDiffAppliesToAxle = layout switch
        {
            DrivetrainLayout.FF => frontAxle,
            DrivetrainLayout.FR => !frontAxle,
            DrivetrainLayout.AWD => true,
            _ => frontAxle
        };
        if (!legacyDiffAppliesToAxle)
        {
            return DifferentialParameters.Open;
        }

        return new DifferentialParameters
        {
            TorqueBiasRatio = drivetrain.DifferentialTorqueBiasRatio,
            PreloadTorqueNm = drivetrain.DifferentialPreloadTorqueNm
        };
    }

    private static float[] ReadForwardRatios(JsonElement gearbox)
    {
        JsonElement ratios = Require(gearbox, "data", "forwardRatios");
        if (ratios.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Gearbox forwardRatios must be an array.");
        }

        List<float> result = [];
        foreach (JsonElement ratio in ratios.EnumerateArray())
        {
            float value = ReadSingle(ratio, 0f);
            if (value <= 0f)
            {
                throw new InvalidDataException("Gearbox forward ratios must be positive.");
            }

            result.Add(value);
        }

        return result.Count > 0 ? [.. result] : throw new InvalidDataException("Gearbox must define at least one forward ratio.");
    }

    private static string ReadTyreSize(JsonElement tyre)
    {
        float width = RequirePositive(tyre, "data", "widthMm");
        float aspect = RequirePositive(tyre, "data", "aspectRatio");
        float rim = RequirePositive(tyre, "data", "rimDiameterIn");
        return $"{width:0}/{aspect:0}R{rim:0}";
    }

    private static float ReadWeight(JsonElement item)
    {
        return ReadSingle(item, 0f, "weightKg") +
            ReadSingle(item, 0f, "weightDeltaKg") +
            ReadSingle(item, 0f, "data", "weightKg") +
            ReadSingle(item, 0f, "data", "massKg");
    }

    private static float SumCatalogWeights(CatalogLookup catalogs, IEnumerable<string> ids)
    {
        float total = 0f;
        foreach (string id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                total += ReadWeight(catalogs.Require(id));
            }
        }

        return total;
    }

    private static float CalculateTotalPistonAreaSquareMeters(JsonElement brake)
    {
        if (!TryGet(brake, out JsonElement diameters, "data", "pistonDiametersMm") ||
            diameters.ValueKind != JsonValueKind.Array)
        {
            return 0.0015f;
        }

        float total = 0f;
        foreach (JsonElement diameterElement in diameters.EnumerateArray())
        {
            if (!diameterElement.TryGetSingle(out float diameterMm) || diameterMm <= 0f)
            {
                continue;
            }

            float radiusMeters = diameterMm * 0.0005f;
            total += MathF.PI * radiusMeters * radiusMeters;
        }

        return total > 0f ? total : 0.0015f;
    }

    private static float CalculateDefaultGearboxShiftShockMultiplier(string gearboxType)
    {
        return gearboxType.Equals("dogbox", StringComparison.OrdinalIgnoreCase)
            ? 1.18f
            : 0.82f;
    }

    private static float RequirePositive(JsonElement root, params string[] path)
    {
        float value = ReadSingle(root, float.NaN, path);
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidDataException($"Field '{string.Join(".", path)}' must be positive.");
        }

        return value;
    }

    private static float RequireNonZero(JsonElement root, params string[] path)
    {
        float value = ReadSingle(root, float.NaN, path);
        if (!float.IsFinite(value) || MathF.Abs(value) <= 0.0001f)
        {
            throw new InvalidDataException($"Field '{string.Join(".", path)}' must be non-zero.");
        }

        return value;
    }

    private static float RequireRange(JsonElement root, float min, float max, params string[] path)
    {
        float value = ReadSingle(root, float.NaN, path);
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidDataException($"Field '{string.Join(".", path)}' must be between {min:0.###} and {max:0.###}.");
        }

        return value;
    }

    private static JsonElement Require(JsonElement root, params string[] path)
    {
        return TryGet(root, out JsonElement value, path)
            ? value
            : throw new InvalidDataException($"Missing required JSON path '{string.Join(".", path)}'.");
    }

    private static string ReadString(JsonElement root, string fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement root, params string[] path)
    {
        if (!TryGet(root, out JsonElement value, path) || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string id = property.Value.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result[property.Name] = id;
                }
            }
        }

        return result;
    }

    private static bool ReadBoolean(JsonElement root, bool fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static float ReadSingle(JsonElement root, float fallback, params string[] path)
    {
        return TryGet(root, out JsonElement value, path) && value.TryGetSingle(out float result) ? result : fallback;
    }

    private static bool TryGet(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static ResolvedEngineBuild ReadResolvedEngine(JsonElement engine)
    {
        ResolvedEngineAssembly assembly = EngineAssemblyResolver.Resolve(engine);
        return new ResolvedEngineBuild
        {
            IdleRpm = assembly.IdleRpm,
            PowerRedlineRpm = assembly.PowerRedlineRpm,
            LimiterRpm = assembly.LimiterHardCutRpm,
            RotationalInertiaKgM2 = assembly.RotationalInertiaKgM2,
            VtecEnabled = assembly.VtecEnabled,
            VtecActivationRpm = assembly.VtecActivationRpm,
            VtecTransitionWidthRpm = assembly.VtecTransitionWidthRpm
        };
    }

    private static string ResolveDataPath(string path)
    {
        path = VehiclePathMigration.ResolveLegacyBuildPath(path);

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

        throw new FileNotFoundException($"Data file was not found: {path}", path);
    }

    private sealed class CatalogLookup
    {
        private readonly Dictionary<string, JsonElement> _items = new(StringComparer.OrdinalIgnoreCase);

        public static CatalogLookup Load(string catalogIndexPath)
        {
            CatalogLookup lookup = new();
            lookup.LoadIndex(catalogIndexPath);
            return lookup;
        }

        public static CatalogLookup Load(string catalogIndexPath, string tuneCatalogPath)
        {
            CatalogLookup lookup = Load(catalogIndexPath);
            lookup.LoadCatalog(tuneCatalogPath);
            return lookup;
        }

        public JsonElement Require(string id)
        {
            return _items.TryGetValue(id, out JsonElement item)
                ? item
                : throw new InvalidDataException($"Missing catalog id '{id}'.");
        }

        private void LoadIndex(string catalogIndexPath)
        {
            string resolvedIndexPath = ResolveDataPath(catalogIndexPath);
            using FileStream stream = File.OpenRead(resolvedIndexPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            foreach (JsonElement catalog in VehicleBuildDefinitionLoader.Require(document.RootElement, "catalogs").EnumerateArray())
            {
                string path = ReadString(catalog, string.Empty, "path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    LoadCatalog(path);
                }
            }

            ResolveInheritedItems();
        }

        private void LoadCatalog(string path)
        {
            string resolvedPath = ResolveDataPath(path);
            using FileStream stream = File.OpenRead(resolvedPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement root = document.RootElement.Clone();
            AddItems(root, "parts");
            AddItems(root, "engines");
            AddItems(root, "blocks");
            AddItems(root, "heads");
            AddItems(root, "tunes");
        }

        private void AddItems(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                string id = ReadString(item, string.Empty, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _items[id] = item.Clone();
                }
            }
        }

        private void ResolveInheritedItems()
        {
            foreach (string id in _items.Keys.ToArray())
            {
                _items[id] = ResolveInheritedItem(id, []);
            }
        }

        private JsonElement ResolveInheritedItem(string id, HashSet<string> stack)
        {
            if (!_items.TryGetValue(id, out JsonElement item))
            {
                throw new InvalidDataException($"Missing inherited catalog id '{id}'.");
            }

            string baseId = ReadString(item, string.Empty, "inherits");
            if (string.IsNullOrWhiteSpace(baseId))
            {
                return item;
            }

            if (!stack.Add(id))
            {
                throw new InvalidDataException($"Catalog inheritance cycle detected at '{id}'.");
            }

            JsonElement inherited = ResolveInheritedItem(baseId, stack);
            stack.Remove(id);

            JsonNode? baseNode = JsonNode.Parse(inherited.GetRawText());
            JsonNode? overrideNode = JsonNode.Parse(item.GetRawText());
            if (baseNode is not JsonObject baseObject || overrideNode is not JsonObject overrideObject)
            {
                return item;
            }

            DeepMerge(baseObject, overrideObject);
            using JsonDocument mergedDocument = JsonDocument.Parse(baseObject.ToJsonString());
            return mergedDocument.RootElement.Clone();
        }

        private static void DeepMerge(JsonObject target, JsonObject overlay)
        {
            foreach (KeyValuePair<string, JsonNode?> property in overlay)
            {
                if (target[property.Key] is JsonObject targetChild &&
                    property.Value is JsonObject overlayChild)
                {
                    DeepMerge(targetChild, overlayChild);
                }
                else
                {
                    target[property.Key] = property.Value?.DeepClone();
                }
            }
        }
    }
}

internal sealed class ResolvedVehicleBuild
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string VehicleDefinitionPath { get; init; } = string.Empty;
    public string ChassisVehicleId { get; init; } = string.Empty;
    public string ChassisCode { get; init; } = string.Empty;
    public string DrivetrainLayout { get; init; } = string.Empty;
    public string BodyShellId { get; init; } = string.Empty;
    public string BodyShellName { get; init; } = string.Empty;
    public ResolvedEngineBuild Engine { get; init; } = new();
    public ResolvedVehicleMass Mass { get; init; } = new();
    public ResolvedChassisBuild Chassis { get; init; } = new();
    public ResolvedDrivetrainBuild Drivetrain { get; init; } = new();
    public ResolvedSwapKitBuild SwapKits { get; init; } = new();
    public ResolvedSuspensionBuild Suspension { get; init; } = new();
    public ResolvedBrakeBuild Brakes { get; init; } = new();
    public ResolvedWheelBuild Wheels { get; init; } = new();
    public ResolvedTyreBuild Tyres { get; init; } = new();
    public ResolvedAeroBuild Aero { get; init; } = new();
    public ResolvedSteeringSetup Steering { get; init; } = new();
    public ResolvedHandlingSetup Handling { get; init; } = new();
}

internal sealed class ResolvedEngineBuild
{
    public float IdleRpm { get; init; } = 900f;
    public float PowerRedlineRpm { get; init; } = 8200f;
    public float LimiterRpm { get; init; } = 8400f;
    public float RotationalInertiaKgM2 { get; init; } = 0.22f;
    public bool VtecEnabled { get; init; } = true;
    public float VtecActivationRpm { get; init; } = 5800f;
    public float VtecTransitionWidthRpm { get; init; } = 650f;
}

internal sealed class ResolvedVehicleMass
{
    public float BodyShellKg { get; init; }
    public float EngineAssemblyKg { get; init; }
    public float GearboxKg { get; init; }
    public float FinalDriveKg { get; init; }
    public float DifferentialKg { get; init; }
    public float SwapKitKg { get; init; }
    public float FrontSuspensionKg { get; init; }
    public float RearSuspensionKg { get; init; }
    public float FrontBrakesKg { get; init; }
    public float RearBrakesKg { get; init; }
    public float FrontWheelKg { get; init; }
    public float RearWheelKg { get; init; }
    public float FrontTyreKg { get; init; }
    public float RearTyreKg { get; init; }
    public float CatalogVehicleSideKg { get; init; }
    public float CatalogTotalWithEngineKg => CatalogVehicleSideKg + EngineAssemblyKg;
}

internal sealed class ResolvedChassisBuild
{
    public float WheelbaseMeters { get; init; }
    public float FrontTrackMeters { get; init; }
    public float RearTrackMeters { get; init; }
    public float LengthMeters { get; init; }
    public float WidthMeters { get; init; }
    public float HeightMeters { get; init; }
    public float BaseCurbMassKg { get; init; }
    public float CalibrationResidualMassKg { get; init; } = float.NaN;
    public float YawInertiaCalibrationScale { get; init; } = 1f;
    public float FrontWeightDistribution { get; init; }
    public float CenterOfGravityHeightMeters { get; init; }
    public float BodyMassCenterY { get; init; } = float.NaN;
    public float BodyMassCenterLongitudinalMeters { get; init; } = float.NaN;
    public float TorsionalRigidityNmPerDeg { get; init; }
    public ResolvedSuspensionHardPoints FrontSuspensionHardPoints { get; init; } = new();
    public ResolvedSuspensionHardPoints RearSuspensionHardPoints { get; init; } = new();
}

internal sealed class ResolvedSuspensionHardPoints
{
    public string Type { get; init; } = string.Empty;
    public float RollCentreHeightMeters { get; init; }
    public float CamberGainDegreesPerMeter { get; init; }
    public float ToeGainDegreesPerMeter { get; init; }
    public float BodyRollCamberMultiplier { get; init; }
    public float CasterDegrees { get; init; }
    public float CasterCamberGain { get; init; }
    public float MaxCompressionMeters { get; init; }
    public float MaxDroopMeters { get; init; }
}

internal sealed class ResolvedDrivetrainBuild
{
    public string GearboxId { get; init; } = string.Empty;
    public string GearboxName { get; init; } = string.Empty;
    public string GearboxType { get; init; } = "manual";
    public float ReverseGearRatio { get; init; }
    public float[] ForwardGearRatios { get; init; } = [];
    public string FinalDriveId { get; init; } = string.Empty;
    public float FinalDriveRatio { get; init; }
    public string DifferentialId { get; init; } = string.Empty;
    public string DifferentialType { get; init; } = string.Empty;
    public float DifferentialTorqueBiasRatio { get; init; }
    public float DifferentialPreloadTorqueNm { get; init; }
    public float TransmissionEfficiency { get; init; }
    public float ManualShiftTimeSeconds { get; init; }
    public float AutomaticShiftTimeSeconds { get; init; }
    public float ShiftShockMultiplier { get; init; } = 1f;
    public float DownshiftOverRevToleranceRpm { get; init; }
    public float DownshiftMechanicalOverRevLimitRpm { get; init; }
    public float DownshiftOverRevBrakeMultiplier { get; init; }
    public float DownshiftOverRevShockSeconds { get; init; }
}

internal sealed class ResolvedSwapKitBuild
{
    public IReadOnlyDictionary<string, string> InstalledParts { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public float TotalMassKg { get; init; }
}

internal sealed class ResolvedSuspensionBuild
{
    public string FrontId { get; init; } = string.Empty;
    public string RearId { get; init; } = string.Empty;
    public string AlignmentId { get; init; } = string.Empty;
    public float FrontSpringRateNPerM { get; init; }
    public float RearSpringRateNPerM { get; init; }
    public float FrontBumpDampingNsPerM { get; init; }
    public float RearBumpDampingNsPerM { get; init; }
    public float FrontReboundDampingNsPerM { get; init; }
    public float RearReboundDampingNsPerM { get; init; }
    public float FrontRideHeightMeters { get; init; }
    public float RearRideHeightMeters { get; init; }
    public float FrontRollCentreHeightMeters { get; init; }
    public float RearRollCentreHeightMeters { get; init; }
    public float FrontMaxCompressionMeters { get; init; }
    public float RearMaxCompressionMeters { get; init; }
    public float FrontMaxDroopMeters { get; init; }
    public float RearMaxDroopMeters { get; init; }
    public float FrontAntiRollBarRateNmPerRad { get; init; }
    public float RearAntiRollBarRateNmPerRad { get; init; }
    public float FrontCamberDegrees { get; init; }
    public float RearCamberDegrees { get; init; }
    public float FrontToeDegrees { get; init; }
    public float RearToeDegrees { get; init; }
    public float FrontCasterDegrees { get; init; }
}

internal sealed class ResolvedBrakeBuild
{
    public string FrontId { get; init; } = string.Empty;
    public string RearId { get; init; } = string.Empty;
    public float FrontDiscDiameterMm { get; init; }
    public float RearDiscDiameterMm { get; init; }
    public float FrontEffectiveRadiusRatio { get; init; }
    public float RearEffectiveRadiusRatio { get; init; }
    public float FrontTotalPistonAreaSquareMeters { get; init; }
    public float RearTotalPistonAreaSquareMeters { get; init; }
    public float FrontClampForceMultiplier { get; init; }
    public float RearClampForceMultiplier { get; init; }
    public float FrontPadFriction { get; init; }
    public float RearPadFriction { get; init; }
    public ResolvedBrakeSystemBuild System { get; init; } = new();
}

internal sealed class ResolvedBrakeSystemBuild
{
    public string Id { get; init; } = string.Empty;
    public float MaxLinePressureBar { get; init; }
    public float BrakeBiasFront { get; init; }
    public float HandbrakeRearTorqueNm { get; init; }
    public float PressureRiseRatePerSecond { get; init; }
    public float PressureReleaseRatePerSecond { get; init; }
    public float MaxBrakeForceN { get; init; }
    public bool AbsEnabled { get; init; }
    public float AbsTargetSlipRatio { get; init; }
    public float AbsReleaseSlipRatio { get; init; }
    public float AbsApplyRatePerSecond { get; init; }
    public float AbsReleaseRatePerSecond { get; init; }
    public float AbsMinimumSpeedKph { get; init; }
    public float AbsMinimumPressureRatio { get; init; }
}

internal sealed class ResolvedWheelBuild
{
    public string FrontId { get; init; } = string.Empty;
    public string RearId { get; init; } = string.Empty;
    public float FrontDiameterIn { get; init; }
    public float RearDiameterIn { get; init; }
    public float FrontWidthIn { get; init; }
    public float RearWidthIn { get; init; }
    public float FrontOffsetMm { get; init; }
    public float RearOffsetMm { get; init; }
}

internal sealed class ResolvedTyreBuild
{
    public string FrontId { get; init; } = string.Empty;
    public string RearId { get; init; } = string.Empty;
    public string FrontSize { get; init; } = string.Empty;
    public string RearSize { get; init; } = string.Empty;
    public float FrontLoadedRadiusMeters { get; init; }
    public float RearLoadedRadiusMeters { get; init; }
    public float FrontPeakFriction { get; init; }
    public float RearPeakFriction { get; init; }
    public float FrontRollingResistance { get; init; }
    public float RearRollingResistance { get; init; }
    public ResolvedTyreModel FrontModel { get; init; } = new();
    public ResolvedTyreModel RearModel { get; init; } = new();
}

internal sealed class ResolvedTyreModel
{
    public string Id { get; init; } = string.Empty;
    public float LoadSensitivity { get; init; }
    public float CorneringStiffnessNPerRad { get; init; }
    public float LongitudinalStiffnessN { get; init; }
    public float LateralPeakSlipAngleDegrees { get; init; }
    public float LateralSlideSlipAngleDegrees { get; init; }
    public float LateralForceRiseShape { get; init; }
    public float SlidingLateralFrictionMultiplier { get; init; }
    public float RelaxationLengthMeters { get; init; }
    public float LateralScrubDragCoefficient { get; init; }
    public float IdealCamberDegrees { get; init; }
    public float CamberGripLossPerDegree { get; init; }
    public float MinimumCamberGripMultiplier { get; init; }
    public float CamberThrustStiffnessNPerRad { get; init; }
    public float LongitudinalPeakSlipRatio { get; init; }
    public float LongitudinalForceRiseShape { get; init; }
    public float LongitudinalSlideSlipRatio { get; init; }
    public float SlidingFrictionMultiplier { get; init; }
}

internal sealed class ResolvedAeroBuild
{
    public string Id { get; init; } = string.Empty;
    public float DragCoefficient { get; init; }
    public float FrontalAreaSquareMeters { get; init; }
    public float FrontLiftCoefficient { get; init; }
    public float RearLiftCoefficient { get; init; }
}

internal sealed class ResolvedSteeringSetup
{
    public string Id { get; init; } = string.Empty;
    public float Ratio { get; init; }
    public float SteeringWheelLockDegrees { get; init; }
    public float InputRatePerSecond { get; init; }
    public float ReturnRatePerSecond { get; init; }
    public float MaxInnerWheelAngleDegrees { get; init; }
    public float AckermannPercent { get; init; }
    public float FullLockSpeedKph { get; init; }
    public float ReducedLockSpeedKph { get; init; }
    public float HighSpeedLockMultiplier { get; init; }
    public float HighSpeedInputRateMultiplier { get; init; }
    public float HighSpeedReturnRateMultiplier { get; init; }
    public float TargetLateralAccelerationG { get; init; }
    public float PeakSlipAngleFraction { get; init; }
    public float LowSpeedReferenceKph { get; init; }
    public float MinimumRoadWheelAngleDegrees { get; init; }
}

internal sealed class ResolvedHandlingSetup
{
    public string Id { get; init; } = string.Empty;
    public float DrivetrainEfficiency { get; init; }
    public float ClosedThrottleEngineBrakeTorqueNm { get; init; }
    public float LateralGripResponse { get; init; }
    public float RollingResistanceCoefficient { get; init; }
    public float AirDensityKgM3 { get; init; }
    public float AutomaticUpshiftRpm { get; init; }
    public float AutomaticMinimumUpshiftSpeedKph { get; init; }
    public float AutomaticDownshiftRpm { get; init; }
    public float EngineFreeRevResponseRate { get; init; }
    public float MaxFreeRevRiseRpmPerSecond { get; init; }
    public float MaxFreeRevFallRpmPerSecond { get; init; }
    public float WallCollisionPointRadiusMeters { get; init; }
    public float WallCollisionRestitution { get; init; }
    public float WallImpactFriction { get; init; }
    public float WallScrapeFriction { get; init; }
    public float WallYawImpulseScale { get; init; }
    public ArcadeHandlingParameters Arcade { get; init; } = new();
}
