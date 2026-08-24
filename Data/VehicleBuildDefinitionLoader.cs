using System.Text.Json;
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
        JsonElement drivetrain = Require(assembly, "drivetrain");
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
        ResolvedEngineBuild engineBuild = ReadReferenceEngine(vehicleDefinitionPath);
        float frontTyreRadius = RequirePositive(frontTyre, "data", "loadedRadiusMeters");
        float rearTyreRadius = RequirePositive(rearTyre, "data", "loadedRadiusMeters");

        return new ResolvedVehicleBuild
        {
            Id = ReadString(root, Path.GetFileNameWithoutExtension(resolvedBuildPath), "id"),
            DisplayName = ReadString(root, string.Empty, "displayName"),
            VehicleDefinitionPath = vehicleDefinitionPath,
            ChassisVehicleId = ReadString(chassis, string.Empty, "vehicleId"),
            ChassisCode = ReadString(chassis, string.Empty, "chassisCode"),
            DrivetrainLayout = ReadString(chassis, string.Empty, "drivetrainLayout"),
            BodyShellId = ReadString(bodyShell, string.Empty, "id"),
            BodyShellName = ReadString(bodyShell, string.Empty, "displayName"),
            Engine = engineBuild,
            Mass = new ResolvedVehicleMass
            {
                BodyShellKg = ReadWeight(bodyShell),
                EngineAssemblyKg = EstimateEngineAssemblyWeight(buildPath),
                GearboxKg = ReadWeight(gearbox),
                FinalDriveKg = ReadWeight(finalDrive),
                DifferentialKg = ReadWeight(differential),
                FrontSuspensionKg = ReadWeight(frontSuspension),
                RearSuspensionKg = ReadWeight(rearSuspension),
                FrontBrakesKg = ReadWeight(frontBrakes),
                RearBrakesKg = ReadWeight(rearBrakes),
                FrontWheelKg = ReadWeight(frontWheel),
                RearWheelKg = ReadWeight(rearWheel),
                FrontTyreKg = ReadWeight(frontTyre),
                RearTyreKg = ReadWeight(rearTyre),
                CatalogVehicleSideKg = ReadWeight(bodyShell) + ReadWeight(gearbox) + ReadWeight(finalDrive) + ReadWeight(differential) +
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
                FrontWeightDistribution = RequireRange(bodyShell, 0.05f, 0.95f, "data", "frontWeightDistribution"),
                CenterOfGravityHeightMeters = RequirePositive(bodyShell, "data", "cgHeightMeters"),
                TorsionalRigidityNmPerDeg = RequirePositive(bodyShell, "data", "torsionalRigidityNmPerDeg")
            },
            Drivetrain = new ResolvedDrivetrainBuild
            {
                GearboxId = ReadString(gearbox, string.Empty, "id"),
                GearboxName = ReadString(gearbox, string.Empty, "displayName"),
                ReverseGearRatio = MathF.Abs(RequireNonZero(gearbox, "data", "reverseRatio")),
                ForwardGearRatios = ReadForwardRatios(gearbox),
                FinalDriveId = ReadString(finalDrive, string.Empty, "id"),
                FinalDriveRatio = RequirePositive(finalDrive, "data", "ratio"),
                DifferentialId = ReadString(differential, string.Empty, "id"),
                DifferentialType = ReadString(differential, string.Empty, "data", "type"),
                DifferentialTorqueBiasRatio = RequirePositive(differential, "data", "torqueBiasRatio"),
                TransmissionEfficiency = RequireRange(gearbox, 0.1f, 1f, "data", "efficiency"),
                ManualShiftTimeSeconds = RequirePositive(gearbox, "data", "manualShiftTimeSeconds"),
                AutomaticShiftTimeSeconds = RequirePositive(gearbox, "data", "automaticShiftTimeSeconds"),
                DownshiftOverRevToleranceRpm = RequirePositive(gearbox, "data", "downshiftOverRevToleranceRpm"),
                DownshiftMechanicalOverRevLimitRpm = RequirePositive(gearbox, "data", "downshiftMechanicalOverRevLimitRpm"),
                DownshiftOverRevBrakeMultiplier = RequirePositive(gearbox, "data", "downshiftOverRevBrakeMultiplier"),
                DownshiftOverRevShockSeconds = RequirePositive(gearbox, "data", "downshiftOverRevShockSeconds")
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
                LaunchSlipTargetRpm = RequirePositive(handlingSetup, "data", "launchSlipTargetRpm"),
                LaunchSlipBlend = RequireRange(handlingSetup, 0f, 1f, "data", "launchSlipBlend"),
                WallCollisionPointRadiusMeters = RequirePositive(handlingSetup, "data", "wallCollisionPointRadiusMeters"),
                WallCollisionRestitution = RequireRange(handlingSetup, 0f, 1f, "data", "wallCollisionRestitution"),
                WallImpactFriction = RequireRange(handlingSetup, 0f, 1f, "data", "wallImpactFriction"),
                WallScrapeFriction = RequireRange(handlingSetup, 0f, 1f, "data", "wallScrapeFriction"),
                WallYawImpulseScale = RequireRange(handlingSetup, 0f, 2f, "data", "wallYawImpulseScale"),
                Arcade = ReadArcadeHandling(handlingSetup)
            }
        };
    }

    public static VehicleSimulationParameters LoadReferenceRuntimeParameters(string buildPath)
    {
        ResolvedVehicleBuild build = Load(buildPath);
        return VehicleDefinitionLoader.LoadSimulationParameters(build.VehicleDefinitionPath);
    }

    public static VehicleSimulationParameters LoadSimulationParameters(string buildPath)
    {
        ResolvedVehicleBuild build = Load(buildPath);
        VehicleSimulationParameters reference = LoadReferenceRuntimeParameters(buildPath);

        return new VehicleSimulationParameters
        {
            Id = build.Id,
            DisplayName = build.DisplayName,
            MassKg = build.Chassis.BaseCurbMassKg,
            WheelbaseMeters = build.Chassis.WheelbaseMeters,
            FrontTrackMeters = build.Chassis.FrontTrackMeters,
            RearTrackMeters = build.Chassis.RearTrackMeters,
            BodyLengthMeters = build.Chassis.LengthMeters,
            BodyWidthMeters = build.Chassis.WidthMeters,
            FrontWeightDistribution = build.Chassis.FrontWeightDistribution,
            CenterOfGravityHeightMeters = build.Chassis.CenterOfGravityHeightMeters,
            YawInertiaKgM2 = reference.YawInertiaKgM2,
            WheelRadiusMeters = build.Tyres.FrontLoadedRadiusMeters,
            FinalDriveRatio = build.Drivetrain.FinalDriveRatio,
            DrivetrainEfficiency = build.Handling.DrivetrainEfficiency,
            ClosedThrottleEngineBrakeTorqueNm = build.Handling.ClosedThrottleEngineBrakeTorqueNm,
            IdleRpm = build.Engine.IdleRpm,
            RedlineRpm = build.Engine.LimiterRpm,
            RevLimiterResumeRpm = reference.RevLimiterResumeRpm,
            RevLimiterFuelCutSeconds = reference.RevLimiterFuelCutSeconds,
            RevLimiterRestoreSeconds = reference.RevLimiterRestoreSeconds,
            RevLimiterCutTorqueMultiplier = reference.RevLimiterCutTorqueMultiplier,
            RevLimiterBounceRpm = reference.RevLimiterBounceRpm,
            EngineRotationalInertiaKgM2 = build.Engine.RotationalInertiaKgM2,
            VtecEnabled = build.Engine.VtecEnabled,
            VtecActivationRpm = build.Engine.VtecActivationRpm,
            VtecTransitionWidthRpm = build.Engine.VtecTransitionWidthRpm,
            VtecLowCamFlowMultiplier = reference.VtecLowCamFlowMultiplier,
            VtecHighCamFlowMultiplier = reference.VtecHighCamFlowMultiplier,
            EngineSimulatorDrivesPhysics = reference.EngineSimulatorDrivesPhysics,
            EngineSimulatorFullDriveline = reference.EngineSimulatorFullDriveline,
            EngineSimulatorPhysicsSimulationFrequencyHz = reference.EngineSimulatorPhysicsSimulationFrequencyHz,
            EngineSimulatorPhysicsFluidSimulationSteps = reference.EngineSimulatorPhysicsFluidSimulationSteps,
            EngineSimulatorPhysicsTorqueScale = reference.EngineSimulatorPhysicsTorqueScale,
            EngineSimulatorPhysicsTorqueBlend = reference.EngineSimulatorPhysicsTorqueBlend,
            EngineSimulatorPhysicsUseReferenceTorqueCalibration = reference.EngineSimulatorPhysicsUseReferenceTorqueCalibration,
            EngineSimulatorPhysicsEngineBrakeScale = reference.EngineSimulatorPhysicsEngineBrakeScale,
            EngineSimulatorPhysicsEngineBrakeBlend = reference.EngineSimulatorPhysicsEngineBrakeBlend,
            EngineSimulatorPhysicsMaxTorqueNm = reference.EngineSimulatorPhysicsMaxTorqueNm,
            EngineSimulatorPhysicsMaxEngineBrakeTorqueNm = reference.EngineSimulatorPhysicsMaxEngineBrakeTorqueNm,
            ClutchTorqueCapacityNm = reference.ClutchTorqueCapacityNm,
            ClutchEngagementPoint = reference.ClutchEngagementPoint,
            ClutchCouplingRate = reference.ClutchCouplingRate,
            EngineFreeRevResponseRate = build.Handling.EngineFreeRevResponseRate,
            LaunchSlipTargetRpm = build.Handling.LaunchSlipTargetRpm,
            LaunchSlipBlend = build.Handling.LaunchSlipBlend,
            UpshiftRpm = build.Handling.AutomaticUpshiftRpm,
            DownshiftRpm = build.Handling.AutomaticDownshiftRpm,
            AutomaticMinimumUpshiftSpeedMetersPerSecond = build.Handling.AutomaticMinimumUpshiftSpeedKph / 3.6f,
            ManualShiftTimeSeconds = build.Drivetrain.ManualShiftTimeSeconds,
            AutomaticShiftTimeSeconds = build.Drivetrain.AutomaticShiftTimeSeconds,
            DownshiftOverRevToleranceRpm = build.Drivetrain.DownshiftOverRevToleranceRpm,
            DownshiftMechanicalOverRevLimitRpm = build.Drivetrain.DownshiftMechanicalOverRevLimitRpm,
            DownshiftOverRevBrakeMultiplier = build.Drivetrain.DownshiftOverRevBrakeMultiplier,
            DownshiftOverRevShockSeconds = build.Drivetrain.DownshiftOverRevShockSeconds,
            ForwardGearRatios = [.. build.Drivetrain.ForwardGearRatios],
            ReverseGearRatio = build.Drivetrain.ReverseGearRatio,
            MaxBrakeForceN = build.Brakes.System.MaxBrakeForceN,
            BrakeBiasFront = build.Brakes.System.BrakeBiasFront,
            Brakes = MergeBrakes(reference.Brakes, build.Brakes),
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
            FrontAntiRollBarRateNmPerRad = build.Suspension.FrontAntiRollBarRateNmPerRad,
            RearAntiRollBarRateNmPerRad = build.Suspension.RearAntiRollBarRateNmPerRad,
            FrontSuspensionGeometry = MergeSuspensionGeometry(reference.FrontSuspensionGeometry, build.Suspension, true),
            RearSuspensionGeometry = MergeSuspensionGeometry(reference.RearSuspensionGeometry, build.Suspension, false),
            DifferentialTorqueBiasRatio = build.Drivetrain.DifferentialTorqueBiasRatio,
            WheelInertiaKgM2 = EstimateWheelInertia(build),
            DrivenWheels = ReadDrivenWheels(build.DrivetrainLayout),
            FrontTyres = MergeTyres(reference.FrontTyres, build.Tyres, true),
            RearTyres = MergeTyres(reference.RearTyres, build.Tyres, false),
            Audio = reference.Audio,
            WallCollisionPointRadiusMeters = build.Handling.WallCollisionPointRadiusMeters,
            WallCollisionRestitution = build.Handling.WallCollisionRestitution,
            WallImpactFriction = build.Handling.WallImpactFriction,
            WallScrapeFriction = build.Handling.WallScrapeFriction,
            WallYawImpulseScale = build.Handling.WallYawImpulseScale,
            TorqueCurve = reference.TorqueCurve,
            EngineBrakeTorqueCurve = reference.EngineBrakeTorqueCurve
        };
    }

    private static BrakeSystemParameters MergeBrakes(BrakeSystemParameters reference, ResolvedBrakeBuild build)
    {
        return new BrakeSystemParameters
        {
            MaxLinePressurePa = build.System.MaxLinePressureBar * 100000f,
            BrakeBiasFront = build.System.BrakeBiasFront,
            HandbrakeRearTorqueNm = build.System.HandbrakeRearTorqueNm,
            PressureRiseRatePerSecond = build.System.PressureRiseRatePerSecond,
            PressureReleaseRatePerSecond = build.System.PressureReleaseRatePerSecond,
            Front = MergeBrakeAxle(reference.Front, build.FrontDiscDiameterMm, build.FrontPadFriction),
            Rear = MergeBrakeAxle(reference.Rear, build.RearDiscDiameterMm, build.RearPadFriction),
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

    private static BrakeAxleParameters MergeBrakeAxle(BrakeAxleParameters reference, float discDiameterMm, float padFriction)
    {
        return new BrakeAxleParameters
        {
            DiscDiameterMeters = discDiameterMm / 1000f,
            EffectiveRadiusRatio = reference.EffectiveRadiusRatio,
            TotalPistonAreaSquareMeters = reference.TotalPistonAreaSquareMeters,
            ClampForceMultiplier = reference.ClampForceMultiplier,
            PadFrictionCoefficient = padFriction
        };
    }

    private static SuspensionGeometryParameters MergeSuspensionGeometry(
        SuspensionGeometryParameters reference,
        ResolvedSuspensionBuild build,
        bool front)
    {
        return new SuspensionGeometryParameters
        {
            StaticCamberRadians = MathHelper.ToRadians(front ? build.FrontCamberDegrees : build.RearCamberDegrees),
            StaticToeRadians = MathHelper.ToRadians(front ? build.FrontToeDegrees : build.RearToeDegrees),
            CasterRadians = front ? MathHelper.ToRadians(build.FrontCasterDegrees) : reference.CasterRadians,
            CamberGainRadiansPerMeter = reference.CamberGainRadiansPerMeter,
            ToeGainRadiansPerMeter = reference.ToeGainRadiansPerMeter,
            BodyRollCamberMultiplier = reference.BodyRollCamberMultiplier,
            CasterCamberGain = reference.CasterCamberGain,
            MaxCompressionMeters = reference.MaxCompressionMeters,
            MaxDroopMeters = reference.MaxDroopMeters
        };
    }

    private static TyreAxleParameters MergeTyres(TyreAxleParameters reference, ResolvedTyreBuild build, bool front)
    {
        ResolvedTyreModel model = front ? build.FrontModel : build.RearModel;
        return new TyreAxleParameters
        {
            LoadedRadiusMeters = front ? build.FrontLoadedRadiusMeters : build.RearLoadedRadiusMeters,
            PeakFriction = front ? build.FrontPeakFriction : build.RearPeakFriction,
            RollingResistanceCoefficient = front ? build.FrontRollingResistance : build.RearRollingResistance,
            LoadSensitivity = model.LoadSensitivity,
            CorneringStiffnessNPerRad = reference.CorneringStiffnessNPerRad,
            LongitudinalStiffnessN = reference.LongitudinalStiffnessN,
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
        return layout.Equals("FF", StringComparison.OrdinalIgnoreCase)
            ? new DrivenWheelSet(true, true, false, false)
            : new DrivenWheelSet(true, true, false, false);
    }

    private static float EstimateEngineAssemblyWeight(string buildPath)
    {
        string resolvedBuildPath = ResolveDataPath(buildPath);
        using FileStream stream = File.OpenRead(resolvedBuildPath);
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement engine = Require(document.RootElement, "assembly", "engine");
        CatalogLookup catalogs = CatalogLookup.Load("Data/RTypeEngineProfiles/PartCatalogs/part_catalog_index.json", "Data/RTypeEngineProfiles/Tunes/engine_tunes.json");

        float weight = 0f;
        weight += ReadWeight(catalogs.Require(ReadString(engine, string.Empty, "blockId")));
        weight += ReadWeight(catalogs.Require(ReadString(engine, string.Empty, "headId")));

        foreach (JsonProperty part in Require(engine, "installedParts").EnumerateObject())
        {
            string id = part.Value.ValueKind == JsonValueKind.String ? part.Value.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(id))
            {
                weight += ReadWeight(catalogs.Require(id));
            }
        }

        return weight;
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

    private static ResolvedEngineBuild ReadReferenceEngine(string vehicleDefinitionPath)
    {
        if (string.IsNullOrWhiteSpace(vehicleDefinitionPath))
        {
            return new ResolvedEngineBuild();
        }

        VehicleSimulationParameters reference = VehicleDefinitionLoader.LoadSimulationParameters(vehicleDefinitionPath);
        return new ResolvedEngineBuild
        {
            IdleRpm = reference.IdleRpm,
            LimiterRpm = reference.RedlineRpm,
            RotationalInertiaKgM2 = reference.EngineRotationalInertiaKgM2,
            VtecEnabled = reference.VtecEnabled,
            VtecActivationRpm = reference.VtecActivationRpm,
            VtecTransitionWidthRpm = reference.VtecTransitionWidthRpm
        };
    }

    private static string ResolveDataPath(string path)
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
    public float FrontWeightDistribution { get; init; }
    public float CenterOfGravityHeightMeters { get; init; }
    public float TorsionalRigidityNmPerDeg { get; init; }
}

internal sealed class ResolvedDrivetrainBuild
{
    public string GearboxId { get; init; } = string.Empty;
    public string GearboxName { get; init; } = string.Empty;
    public float ReverseGearRatio { get; init; }
    public float[] ForwardGearRatios { get; init; } = [];
    public string FinalDriveId { get; init; } = string.Empty;
    public float FinalDriveRatio { get; init; }
    public string DifferentialId { get; init; } = string.Empty;
    public string DifferentialType { get; init; } = string.Empty;
    public float DifferentialTorqueBiasRatio { get; init; }
    public float TransmissionEfficiency { get; init; }
    public float ManualShiftTimeSeconds { get; init; }
    public float AutomaticShiftTimeSeconds { get; init; }
    public float DownshiftOverRevToleranceRpm { get; init; }
    public float DownshiftMechanicalOverRevLimitRpm { get; init; }
    public float DownshiftOverRevBrakeMultiplier { get; init; }
    public float DownshiftOverRevShockSeconds { get; init; }
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
    public float LaunchSlipTargetRpm { get; init; }
    public float LaunchSlipBlend { get; init; }
    public float WallCollisionPointRadiusMeters { get; init; }
    public float WallCollisionRestitution { get; init; }
    public float WallImpactFriction { get; init; }
    public float WallScrapeFriction { get; init; }
    public float WallYawImpulseScale { get; init; }
    public ArcadeHandlingParameters Arcade { get; init; } = new();
}
