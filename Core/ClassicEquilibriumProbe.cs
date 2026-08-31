using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicEquilibriumProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int MeasurementTicks = 360;
    private const int Gear = 4;

    private static readonly ClassicFourWheelAssistOptions CleanupOff = new()
    {
        BodySlipDampingEnabled = false,
        LateralVelocityDampingEnabled = false,
        RearFollowEnabled = false,
        YawRecoveryEnabled = false,
        SpeedRetentionEnabled = false
    };

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic equilibrium probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, surface=ROAD, production steering/yaw/tyre values unchanged");
        Console.WriteLine(
            $"  geometry: wheelbase={geometry.WheelbaseMeters:F3}m cgToFront={geometry.CgToFrontAxleMeters:F3}m cgToRear={geometry.CgToRearAxleMeters:F3}m frontWeight={parameters.FrontWeightDistribution:P1}");
        Console.WriteLine(
            $"  tyre stiffness reference: Cf={parameters.FrontTyres.CorneringStiffnessNPerRad:F0}N/rad Cr={parameters.RearTyres.CorneringStiffnessNPerRad:F0}N/rad understeerIndex={CalculateUndersteerIndex(parameters, geometry):F6}");

        RunCase(parameters, engineParameters, geometry, "medium", 0.35f);
        RunCase(parameters, engineParameters, geometry, "hard", 0.65f);

        Console.WriteLine("Classic equilibrium probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        VehicleAxleGeometry geometry,
        string label,
        float steerInput)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters)
        {
            AssistOptions = CleanupOff
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);

        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engineParameters.ClassicFourWheel.Yaw.InertiaScale));
        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        List<EquilibriumSample> checkpoints = [];
        EquilibriumSample? firstFrontSlipZero = null;
        EquilibriumSample? firstRearSaturation = null;
        EquilibriumSample? firstEquilibriumLike = null;
        EquilibriumSample previous = BuildSample(0f, simulator.State, parameters, engineParameters, geometry, yawInertia, 0f, 0f);

        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            EquilibriumSample current = BuildSample(tick * Dt, simulator.State, parameters, engineParameters, geometry, yawInertia, previous.BodySlipDegrees, previous.YawRateDegreesPerSecond);

            if (firstFrontSlipZero is null &&
                tick > 1 &&
                MathF.Sign(previous.FrontSlipDegrees) != 0f &&
                MathF.Sign(current.FrontSlipDegrees) != 0f &&
                MathF.Sign(previous.FrontSlipDegrees) != MathF.Sign(current.FrontSlipDegrees))
            {
                firstFrontSlipZero = current;
            }

            if (firstRearSaturation is null && current.RearGripUsage >= 0.98f)
            {
                firstRearSaturation = current;
            }

            if (firstEquilibriumLike is null &&
                current.TimeSeconds > 0.25f &&
                MathF.Abs(current.BetaDotDegreesPerSecond) <= 1.0f &&
                MathF.Abs(current.YawAccelerationDegreesPerSecondSquared) <= 5.0f &&
                current.FrontSlipDegrees > 0.5f &&
                current.RearGripUsage < 0.90f)
            {
                firstEquilibriumLike = current;
            }

            if (tick is 12 or 30 or 60 or 120 or 180 or 240 or 360)
            {
                checkpoints.Add(current);
            }

            previous = current;
        }

        Console.WriteLine($"  {label} rawInput={steerInput:F2}");
        Console.WriteLine("    t speed steer beta betaDot yaw yawAcc slipF/R refSlipF/R latF F/R moment F/R/net gripF/R refYaw/refBeta refState");
        foreach (EquilibriumSample sample in checkpoints)
        {
            PrintSample(sample);
        }

        Console.WriteLine(
            $"    events: equilibriumLike={FormatEvent(firstEquilibriumLike)} frontSlipZero={FormatEvent(firstFrontSlipZero)} rearSat={FormatEvent(firstRearSaturation)} speedDrop={startSpeedKmh - simulator.State.SpeedMetersPerSecond * 3.6f:F1}km/h");
        Console.WriteLine($"    classification: {Classify(firstEquilibriumLike, firstFrontSlipZero, firstRearSaturation, checkpoints[^1])}");
    }

    private static EquilibriumSample BuildSample(
        float timeSeconds,
        VehicleState state,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        VehicleAxleGeometry geometry,
        float yawInertia,
        float previousBodySlipDegrees,
        float previousYawRateDegreesPerSecond)
    {
        float frontLateralForce = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateralForce = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontMoment = CalculateAxleMoment(
            -geometry.FrontTrackMeters * 0.5f,
            geometry.CgToFrontAxleMeters,
            state.FrontLeftLongitudinalForceN,
            state.FrontLeftLateralForceN) +
            CalculateAxleMoment(
                geometry.FrontTrackMeters * 0.5f,
                geometry.CgToFrontAxleMeters,
                state.FrontRightLongitudinalForceN,
                state.FrontRightLateralForceN);
        float rearMoment = CalculateAxleMoment(
            -geometry.RearTrackMeters * 0.5f,
            -geometry.CgToRearAxleMeters,
            state.RearLeftLongitudinalForceN,
            state.RearLeftLateralForceN) +
            CalculateAxleMoment(
                geometry.RearTrackMeters * 0.5f,
                -geometry.CgToRearAxleMeters,
                state.RearRightLongitudinalForceN,
                state.RearRightLateralForceN);
        float naturalYawAcceleration = (frontMoment + rearMoment) / yawInertia;
        float yawDampingAcceleration = -state.YawRateRadiansPerSecond * MathF.Max(0f, engineParameters.ClassicFourWheel.Yaw.Damping);
        float yawAcceleration = naturalYawAcceleration + yawDampingAcceleration;
        float yawRateDegrees = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float betaDot = timeSeconds <= 0f
            ? 0f
            : (state.ClassicBodySlipAngleDegrees - previousBodySlipDegrees) / Dt;
        float measuredYawAcceleration = timeSeconds <= 0f
            ? 0f
            : (yawRateDegrees - previousYawRateDegreesPerSecond) / Dt;
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float frontGrip = MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage);
        float rearGrip = MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);
        ReferenceSnapshot reference = CalculateReference(
            parameters,
            geometry,
            state.SpeedMetersPerSecond,
            MathHelper.ToRadians(state.FrontLeftSteerAngleDegrees));

        return new EquilibriumSample(
            timeSeconds,
            state.SpeedMetersPerSecond * 3.6f,
            state.FrontLeftSteerAngleDegrees,
            state.ClassicBodySlipAngleDegrees,
            betaDot,
            yawRateDegrees,
            MathHelper.ToDegrees(yawAcceleration),
            measuredYawAcceleration,
            frontSlip,
            rearSlip,
            frontLateralForce,
            rearLateralForce,
            frontMoment,
            rearMoment,
            frontMoment + rearMoment,
            frontGrip,
            rearGrip,
            reference.YawRateDegreesPerSecond,
            reference.BetaDegrees,
            reference.FrontSlipDegrees,
            reference.RearSlipDegrees,
            reference.IsValid);
    }

    private static void PrintSample(EquilibriumSample sample)
    {
        Console.WriteLine(
            $"    {sample.TimeSeconds,4:F2} {sample.SpeedKmh,6:F1} {sample.SteerAngleDegrees,5:F2} " +
            $"{sample.BodySlipDegrees,6:F2} {sample.BetaDotDegreesPerSecond,7:F1} " +
            $"{sample.YawRateDegreesPerSecond,6:F1} {sample.YawAccelerationDegreesPerSecondSquared,7:F1}/{sample.MeasuredYawAccelerationDegreesPerSecondSquared,7:F1} " +
            $"{sample.FrontSlipDegrees,6:F2}/{sample.RearSlipDegrees,6:F2} " +
            $"{sample.ReferenceFrontSlipDegrees,6:F2}/{sample.ReferenceRearSlipDegrees,6:F2} " +
            $"{sample.FrontLateralForceN,7:F0}/{sample.RearLateralForceN,7:F0} " +
            $"{sample.FrontYawMomentNm,8:F0}/{sample.RearYawMomentNm,8:F0}/{sample.NetYawMomentNm,8:F0} " +
            $"{sample.FrontGripUsage,4:F2}/{sample.RearGripUsage,4:F2} " +
            $"{sample.ReferenceYawRateDegreesPerSecond,6:F1}/{sample.ReferenceBetaDegrees,6:F2} " +
            $"{(sample.ReferenceValid ? "valid" : "invalid")}");
    }

    private static string FormatEvent(EquilibriumSample? sample)
    {
        if (sample is null)
        {
            return "none";
        }

        EquilibriumSample s = sample.Value;
        return $"t{s.TimeSeconds:F3}s beta{s.BodySlipDegrees:F1} slipF{s.FrontSlipDegrees:F1} yaw{s.YawRateDegreesPerSecond:F1} rearGrip{s.RearGripUsage:F2}";
    }

    private static string Classify(
        EquilibriumSample? firstEquilibriumLike,
        EquilibriumSample? firstFrontSlipZero,
        EquilibriumSample? firstRearSaturation,
        EquilibriumSample final)
    {
        if (firstEquilibriumLike is not null)
        {
            return "stable equilibrium-like state appears before front-slip reversal/rear saturation";
        }

        if (firstFrontSlipZero is not null &&
            (firstRearSaturation is null || firstFrontSlipZero.Value.TimeSeconds <= firstRearSaturation.Value.TimeSeconds))
        {
            return "no stable equilibrium reached; front slip collapses/reverses before the car settles";
        }

        if (firstRearSaturation is not null)
        {
            return "no stable equilibrium reached; rear axle saturates before betaDot/yaw acceleration settle";
        }

        if (MathF.Abs(final.BetaDotDegreesPerSecond) > 1f || MathF.Abs(final.YawAccelerationDegreesPerSecondSquared) > 5f)
        {
            return "no stable equilibrium reached in the measured window; beta/yaw are still changing";
        }

        return "near-equilibrium at end of window, but not with the required positive front slip and rear grip reserve";
    }

    private static float CalculateAxleMoment(
        float localRightMeters,
        float localForwardMeters,
        float localForwardForceN,
        float localRightForceN)
    {
        return localRightMeters * localForwardForceN - localForwardMeters * localRightForceN;
    }

    private static float CalculateUndersteerIndex(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry)
    {
        float cf = MathF.Max(1f, parameters.FrontTyres.CorneringStiffnessNPerRad);
        float cr = MathF.Max(1f, parameters.RearTyres.CorneringStiffnessNPerRad);
        return parameters.MassKg / MathF.Max(0.1f, geometry.WheelbaseMeters) *
            (geometry.CgToRearAxleMeters / cf - geometry.CgToFrontAxleMeters / cr);
    }

    private static ReferenceSnapshot CalculateReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f, 0f, 0f, 0f, false);
        }

        float mass = MathF.Max(1f, parameters.MassKg);
        float cf = MathF.Max(1f, parameters.FrontTyres.CorneringStiffnessNPerRad);
        float cr = MathF.Max(1f, parameters.RearTyres.CorneringStiffnessNPerRad);
        float a = geometry.CgToFrontAxleMeters;
        float b = geometry.CgToRearAxleMeters;
        float safeSpeed = MathF.Max(0.1f, speed);

        float a11 = -cf - cr;
        float a12 = (-cf * a + cr * b) / safeSpeed - mass * safeSpeed;
        float b1 = -cf * steerRadians;
        float a21 = -a * cf + b * cr;
        float a22 = -(a * a * cf + b * b * cr) / safeSpeed;
        float b2 = -a * cf * steerRadians;
        float det = a11 * a22 - a12 * a21;
        if (MathF.Abs(det) <= 0.001f)
        {
            return new ReferenceSnapshot(0f, 0f, 0f, 0f, false);
        }

        float beta = (b1 * a22 - a12 * b2) / det;
        float yawRate = (a11 * b2 - b1 * a21) / det;
        float frontSlip = steerRadians - beta - a * yawRate / safeSpeed;
        float rearSlip = -beta + b * yawRate / safeSpeed;
        return new ReferenceSnapshot(
            MathHelper.ToDegrees(yawRate),
            MathHelper.ToDegrees(beta),
            MathHelper.ToDegrees(frontSlip),
            MathHelper.ToDegrees(rearSlip),
            true);
    }

    private readonly record struct EquilibriumSample(
        float TimeSeconds,
        float SpeedKmh,
        float SteerAngleDegrees,
        float BodySlipDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float MeasuredYawAccelerationDegreesPerSecondSquared,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float FrontGripUsage,
        float RearGripUsage,
        float ReferenceYawRateDegreesPerSecond,
        float ReferenceBetaDegrees,
        float ReferenceFrontSlipDegrees,
        float ReferenceRearSlipDegrees,
        bool ReferenceValid);

    private readonly record struct ReferenceSnapshot(
        float YawRateDegreesPerSecond,
        float BetaDegrees,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        bool IsValid);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
