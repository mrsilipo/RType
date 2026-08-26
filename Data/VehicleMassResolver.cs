namespace RType.Data;

internal static class VehicleMassResolver
{
    public static ResolvedMassProperties Resolve(ResolvedVehicleBuild build, ResolvedEngineAssembly engine)
    {
        float wheelbase = MathF.Max(0.1f, build.Chassis.WheelbaseMeters);
        float calibratedCgZ = wheelbase * Math.Clamp(build.Chassis.FrontWeightDistribution, 0.05f, 0.95f);
        float calibratedCgY = MathF.Max(0.1f, build.Chassis.CenterOfGravityHeightMeters);

        List<ResolvedMassComponent> components = [];
        AddEngineMassComponents(components, build, engine, wheelbase);
        components.AddRange(
        [
            Component(build.Drivetrain.GearboxId, "gearbox", build.Mass.GearboxKg, -0.18f, 0.36f, wheelbase * 0.70f),
            Component(build.Drivetrain.FinalDriveId, "final_drive", build.Mass.FinalDriveKg, -0.12f, 0.34f, wheelbase * 0.67f),
            Component(build.Drivetrain.DifferentialId, "differential", build.Mass.DifferentialKg, 0f, 0.32f, wheelbase * 0.68f),
            Component("installed_swap_kits", "swap_kits", build.Mass.SwapKitKg, 0f, 0.37f, wheelbase * 0.69f),
            Component(build.Suspension.FrontId, "front_suspension", build.Mass.FrontSuspensionKg, 0f, 0.24f, wheelbase),
            Component(build.Suspension.RearId, "rear_suspension", build.Mass.RearSuspensionKg, 0f, 0.25f, 0f),
            Component(build.Brakes.FrontId, "front_brakes", build.Mass.FrontBrakesKg, 0f, 0.27f, wheelbase),
            Component(build.Brakes.RearId, "rear_brakes", build.Mass.RearBrakesKg, 0f, 0.27f, 0f),
            Component(build.Wheels.FrontId, "front_wheels_pair", build.Mass.FrontWheelKg * 2f, 0f, 0.30f, wheelbase),
            Component(build.Wheels.RearId, "rear_wheels_pair", build.Mass.RearWheelKg * 2f, 0f, 0.30f, 0f),
            Component(build.Tyres.FrontId, "front_tyres_pair", build.Mass.FrontTyreKg * 2f, 0f, 0.30f, wheelbase),
            Component(build.Tyres.RearId, "rear_tyres_pair", build.Mass.RearTyreKg * 2f, 0f, 0.30f, 0f)
        ]);

        float boltOnMass = components.Sum(component => component.MassKg);
        float catalogMass = boltOnMass + build.Mass.BodyShellKg;
        float residualMass = float.IsFinite(build.Chassis.CalibrationResidualMassKg)
            ? build.Chassis.CalibrationResidualMassKg
            : MathF.Max(1f, build.Chassis.BaseCurbMassKg) - catalogMass;
        float calibratedMass = catalogMass + residualMass;
        if (MathF.Abs(residualMass) > 0.001f)
        {
            float residualY = 0.55f;
            float residualZ = wheelbase * 0.46f;
            components.Add(Component("stock_calibration_residual", "fluids_driver_interior_unmodelled", residualMass, 0f, residualY, residualZ));
        }

        float nonBodyYMoment = components.Sum(component => component.MassKg * component.Y);
        float nonBodyZMoment = components.Sum(component => component.MassKg * component.Z);
        float bodyMass = MathF.Max(0f, build.Mass.BodyShellKg);
        if (bodyMass > 0.001f)
        {
            float bodyY = float.IsFinite(build.Chassis.BodyMassCenterY)
                ? build.Chassis.BodyMassCenterY
                : (calibratedMass * calibratedCgY - nonBodyYMoment) / bodyMass;
            float bodyZ = float.IsFinite(build.Chassis.BodyMassCenterLongitudinalMeters)
                ? build.Chassis.BodyMassCenterLongitudinalMeters
                : (calibratedMass * calibratedCgZ - nonBodyZMoment) / bodyMass;
            components.Insert(0, Component(build.BodyShellId, "body_shell_mass_center", bodyMass, 0f, bodyY, bodyZ));
        }

        float totalMass = components.Sum(component => component.MassKg);
        if (totalMass <= 0f)
        {
            YawInertiaResolution fallbackYaw = EstimateYawInertia(build, components, calibratedMass, calibratedCgZ);
            return new ResolvedMassProperties
            {
                TotalMassKg = calibratedMass,
                FrontWeightDistribution = build.Chassis.FrontWeightDistribution,
                CenterOfGravityHeightMeters = calibratedCgY,
                CenterOfGravityLongitudinalMeters = calibratedCgZ,
                YawInertiaKgM2 = fallbackYaw.FinalYawInertiaKgM2,
                CatalogMassKg = catalogMass,
                CalibrationResidualMassKg = residualMass,
                Trace = BuildTrace(build, components, build.Mass.BodyShellKg, boltOnMass, catalogMass, residualMass, calibratedMass, calibratedCgY, calibratedCgZ, fallbackYaw),
                Components = components
            };
        }

        float cgY = components.Sum(component => component.MassKg * component.Y) / totalMass;
        float cgZ = components.Sum(component => component.MassKg * component.Z) / totalMass;
        YawInertiaResolution yaw = EstimateYawInertia(build, components, totalMass, cgZ);
        return new ResolvedMassProperties
        {
            TotalMassKg = totalMass,
            FrontWeightDistribution = Math.Clamp(cgZ / wheelbase, 0.05f, 0.95f),
            CenterOfGravityHeightMeters = Math.Clamp(cgY, 0.1f, 1.2f),
            CenterOfGravityLongitudinalMeters = cgZ,
            YawInertiaKgM2 = yaw.FinalYawInertiaKgM2,
            CatalogMassKg = catalogMass,
            CalibrationResidualMassKg = residualMass,
            Trace = BuildTrace(build, components, build.Mass.BodyShellKg, boltOnMass, catalogMass, residualMass, totalMass, Math.Clamp(cgY, 0.1f, 1.2f), cgZ, yaw),
            Components = components
        };
    }

    private static MassResolutionTrace BuildTrace(
        ResolvedVehicleBuild build,
        IReadOnlyList<ResolvedMassComponent> components,
        float bodyShellMassKg,
        float boltOnMassKg,
        float catalogMassKg,
        float residualMassKg,
        float totalMassKg,
        float cgY,
        float cgZ,
        YawInertiaResolution yaw)
    {
        float wheelbase = MathF.Max(0.1f, build.Chassis.WheelbaseMeters);
        return new MassResolutionTrace(
            bodyShellMassKg,
            boltOnMassKg,
            catalogMassKg,
            residualMassKg,
            totalMassKg,
            components.Count,
            components.Sum(component => component.MassKg * component.Y),
            components.Sum(component => component.MassKg * component.Z),
            cgY,
            cgZ,
            Math.Clamp(cgZ / wheelbase, 0.05f, 0.95f),
            yaw.RawYawInertiaKgM2,
            yaw.CalibrationScale,
            yaw.CalibratedYawInertiaKgM2,
            yaw.FinalYawInertiaKgM2);
    }

    private static YawInertiaResolution EstimateYawInertia(
        ResolvedVehicleBuild build,
        IReadOnlyList<ResolvedMassComponent> components,
        float totalMass,
        float cgZ)
    {
        float bodyLength = MathF.Max(0.1f, build.Chassis.LengthMeters);
        float bodyWidth = MathF.Max(0.1f, build.Chassis.WidthMeters);
        float frontTrack = MathF.Max(0.1f, build.Chassis.FrontTrackMeters);
        float rearTrack = MathF.Max(0.1f, build.Chassis.RearTrackMeters);
        float yawInertia = 0f;

        foreach (ResolvedMassComponent component in components)
        {
            float dz = component.Z - cgZ;
            float localSpread = component.Role switch
            {
                "body_shell_calibrated" or "body_shell_mass_center" => (bodyLength * bodyLength + bodyWidth * bodyWidth) / 12f,
                "front_wheels_pair" or "front_tyres_pair" or "front_brakes" or "front_suspension" => frontTrack * frontTrack * 0.25f,
                "rear_wheels_pair" or "rear_tyres_pair" or "rear_brakes" or "rear_suspension" => rearTrack * rearTrack * 0.25f,
                _ => component.X * component.X
            };

            yawInertia += component.MassKg * (localSpread + dz * dz);
        }

        if (yawInertia <= 1f)
        {
            yawInertia = totalMass * (bodyLength * bodyLength + bodyWidth * bodyWidth) / 12f;
        }

        float rawYawInertia = yawInertia;
        float calibrationScale = Math.Clamp(build.Chassis.YawInertiaCalibrationScale, 0.5f, 1.5f);
        float calibratedYawInertia = rawYawInertia * calibrationScale;
        float finalYawInertia = Math.Clamp(calibratedYawInertia, totalMass * 0.45f, totalMass * 2.2f);
        return new YawInertiaResolution(rawYawInertia, calibrationScale, calibratedYawInertia, finalYawInertia);
    }

    private static ResolvedMassComponent Component(string id, string role, float massKg, float x, float y, float z)
    {
        return new ResolvedMassComponent(id, role, massKg, x, y, z);
    }

    private static void AddEngineMassComponents(
        List<ResolvedMassComponent> components,
        ResolvedVehicleBuild build,
        ResolvedEngineAssembly engine,
        float wheelbase)
    {
        if (engine.MassComponents.Count == 0)
        {
            components.Add(Component(engine.EngineId, "engine_assembly", build.Mass.EngineAssemblyKg, 0f, 0.42f, wheelbase * 0.74f));
            return;
        }

        float engineBaseZ = wheelbase * 0.74f;
        foreach (ResolvedEngineMassComponent component in engine.MassComponents)
        {
            components.Add(Component(
                component.Id,
                $"engine_{component.Role}",
                component.MassKg,
                component.LocalX,
                component.LocalY,
                engineBaseZ + component.LocalZ));
        }
    }

    private readonly record struct YawInertiaResolution(
        float RawYawInertiaKgM2,
        float CalibrationScale,
        float CalibratedYawInertiaKgM2,
        float FinalYawInertiaKgM2);
}
