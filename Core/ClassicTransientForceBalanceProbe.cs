using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTransientForceBalanceProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int Ticks = 48;
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
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);

        Console.WriteLine($"Classic transient force-balance probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, first 0.40s, production values unchanged");
        Console.WriteLine(
            $"  geometry wheelbase={geometry.WheelbaseMeters:F3}m cgFront={geometry.CgToFrontAxleMeters:F3}m cgRear={geometry.CgToRearAxleMeters:F3}m mass={parameters.MassKg:F0}kg");

        RunCase(parameters, engine, geometry, tyres, "medium", 0.35f);
        RunCase(parameters, engine, geometry, tyres, "hard", 0.65f);

        Console.WriteLine("Classic transient force-balance probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        ClassicFourWheelTyres tyres,
        string label,
        float steerInput)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            AssistOptions = CleanupOff
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);

        Console.WriteLine($"  {label} rawInput={steerInput:F2}");
        Console.WriteLine("    t speed steer beta yaw slipF/R latF F/R/total latAcc act/ref/kin yawAcc pathYaw refYaw latImpulse act/ref frontImpulse rearImpulse");

        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engine.ClassicFourWheel.Yaw.InertiaScale));
        float actualLateralImpulse = 0f;
        float referenceLateralImpulse = 0f;
        float frontImpulse = 0f;
        float rearImpulse = 0f;
        float referenceFrontImpulse = 0f;
        float referenceRearImpulse = 0f;
        float peakFrontForce = 0f;
        float? frontForceFirstRise = null;
        float? frontForcePeakTime = null;
        float? frontForceFallStart = null;
        float? frontSlipZero = null;
        float? rearSlipZero = null;
        float? previousFrontAbsForce = null;
        float previousFrontSlip = 0f;
        float previousRearSlip = 0f;
        TransientSample last = default;

        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            TransientSample sample = BuildSample(tick * Dt, simulator.State, parameters, engine, geometry, tyres, yawInertia);
            last = sample;

            actualLateralImpulse += MathF.Abs(sample.TotalLateralForceN) * Dt;
            referenceLateralImpulse += MathF.Abs(sample.ReferenceTotalLateralForceN) * Dt;
            frontImpulse += MathF.Abs(sample.FrontLateralForceN) * Dt;
            rearImpulse += MathF.Abs(sample.RearLateralForceN) * Dt;
            referenceFrontImpulse += MathF.Abs(sample.ReferenceFrontLateralForceN) * Dt;
            referenceRearImpulse += MathF.Abs(sample.ReferenceRearLateralForceN) * Dt;

            float frontAbsForce = MathF.Abs(sample.FrontLateralForceN);
            if (frontForceFirstRise is null && frontAbsForce > 250f)
            {
                frontForceFirstRise = sample.TimeSeconds;
            }

            if (frontAbsForce > peakFrontForce)
            {
                peakFrontForce = frontAbsForce;
                frontForcePeakTime = sample.TimeSeconds;
            }

            if (frontForceFallStart is null &&
                previousFrontAbsForce is not null &&
                frontForcePeakTime is not null &&
                sample.TimeSeconds > frontForcePeakTime.Value &&
                frontAbsForce < previousFrontAbsForce.Value - 100f)
            {
                frontForceFallStart = sample.TimeSeconds;
            }

            if (frontSlipZero is null &&
                tick > 1 &&
                MathF.Sign(previousFrontSlip) != 0f &&
                MathF.Sign(sample.FrontSlipDegrees) != 0f &&
                MathF.Sign(previousFrontSlip) != MathF.Sign(sample.FrontSlipDegrees))
            {
                frontSlipZero = sample.TimeSeconds;
            }

            if (rearSlipZero is null &&
                tick > 1 &&
                MathF.Sign(previousRearSlip) != 0f &&
                MathF.Sign(sample.RearSlipDegrees) != 0f &&
                MathF.Sign(previousRearSlip) != MathF.Sign(sample.RearSlipDegrees))
            {
                rearSlipZero = sample.TimeSeconds;
            }

            if (tick is 6 or 12 or 18 or 24 or 30 or 36 or 42 or 48)
            {
                PrintSample(sample);
            }

            previousFrontAbsForce = frontAbsForce;
            previousFrontSlip = sample.FrontSlipDegrees;
            previousRearSlip = sample.RearSlipDegrees;
        }

        Console.WriteLine(
            $"    events: frontRise={FormatTime(frontForceFirstRise)} frontPeak={FormatTime(frontForcePeakTime)} " +
            $"frontFall={FormatTime(frontForceFallStart)} frontSlipZero={FormatTime(frontSlipZero)} rearSlipZero={FormatTime(rearSlipZero)}");
        Console.WriteLine(
            $"    impulses 0-{last.TimeSeconds:F2}s: total act/ref={actualLateralImpulse:F0}/{referenceLateralImpulse:F0}Ns " +
            $"front act/ref={frontImpulse:F0}/{referenceFrontImpulse:F0}Ns rear act/ref={rearImpulse:F0}/{referenceRearImpulse:F0}Ns");
        Console.WriteLine($"    demand: {Classify(last, actualLateralImpulse, referenceLateralImpulse, frontSlipZero, rearSlipZero)}");
    }

    private static TransientSample BuildSample(
        float time,
        VehicleState state,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        ClassicFourWheelTyres tyres,
        float yawInertia)
    {
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float totalLateral = frontLateral + rearLateral;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float yawAcceleration = (frontMoment + rearMoment) / yawInertia -
            state.YawRateRadiansPerSecond * MathF.Max(0f, engine.ClassicFourWheel.Yaw.Damping);
        float steerRadians = MathHelper.ToRadians(state.FrontLeftSteerAngleDegrees);
        ReferenceSnapshot reference = CalculateReference(parameters, geometry, state.SpeedMetersPerSecond, steerRadians);
        float referenceLatAcceleration = MathF.Abs(state.SpeedMetersPerSecond * MathHelper.ToRadians(reference.YawRateDegreesPerSecond));
        float kinematicRadius = MathF.Abs(MathF.Tan(steerRadians)) <= 0.0001f
            ? float.PositiveInfinity
            : geometry.WheelbaseMeters / MathF.Abs(MathF.Tan(steerRadians));
        float kinematicLatAcceleration = float.IsFinite(kinematicRadius)
            ? state.SpeedMetersPerSecond * state.SpeedMetersPerSecond / kinematicRadius
            : 0f;
        float pathYawRate = CalculatePathYawRateDegreesPerSecond(state);
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float referenceFrontForce = parameters.FrontTyres.CorneringStiffnessNPerRad * MathHelper.ToRadians(reference.FrontSlipDegrees);
        float referenceRearForce = parameters.RearTyres.CorneringStiffnessNPerRad * MathHelper.ToRadians(reference.RearSlipDegrees);

        return new TransientSample(
            time,
            state.SpeedMetersPerSecond * 3.6f,
            state.FrontLeftSteerAngleDegrees,
            state.ClassicBodySlipAngleDegrees,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            frontSlip,
            rearSlip,
            frontLateral,
            rearLateral,
            totalLateral,
            totalLateral / MathF.Max(1f, parameters.MassKg),
            referenceLatAcceleration,
            kinematicLatAcceleration,
            MathHelper.ToDegrees(yawAcceleration),
            pathYawRate,
            reference.YawRateDegreesPerSecond,
            MathF.Abs(referenceFrontForce) + MathF.Abs(referenceRearForce),
            referenceFrontForce,
            referenceRearForce,
            state.FrontLeftLocalLateralSpeedMetersPerSecond,
            state.RearLeftLocalLateralSpeedMetersPerSecond,
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.FrontLeftLoadN, 1f, tyres.Front) +
                ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.FrontRightLoadN, 1f, tyres.Front),
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.RearLeftLoadN, 1f, tyres.Rear) +
                ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.RearRightLoadN, 1f, tyres.Rear));
    }

    private static void PrintSample(TransientSample s)
    {
        Console.WriteLine(
            $"    {s.TimeSeconds,4:F2} {s.SpeedKmh,6:F1} {s.SteerAngleDegrees,5:F2} {s.BetaDegrees,6:F2} {s.YawRateDegreesPerSecond,6:F1} " +
            $"{s.FrontSlipDegrees,6:F2}/{s.RearSlipDegrees,6:F2} {s.FrontLateralForceN,7:F0}/{s.RearLateralForceN,7:F0}/{s.TotalLateralForceN,7:F0} " +
            $"{s.ActualLateralAcceleration,5:F2}/{s.ReferenceLateralAcceleration,5:F2}/{s.KinematicLateralAcceleration,5:F2} " +
            $"{s.YawAccelerationDegreesPerSecondSquared,7:F1} {s.PathYawRateDegreesPerSecond,7:F1} {s.ReferenceYawRateDegreesPerSecond,6:F1} " +
            $"{s.FrontCapabilityN,7:F0}/{s.RearCapabilityN,7:F0}");
    }

    private static string Classify(
        TransientSample last,
        float actualImpulse,
        float referenceImpulse,
        float? frontSlipZero,
        float? rearSlipZero)
    {
        float impulseRatio = referenceImpulse > 1f ? actualImpulse / referenceImpulse : 0f;
        bool demandImpossible = last.ReferenceLateralAcceleration > 9.81f * 1.15f;
        if (demandImpossible)
        {
            return $"reference demand may be unrealistic: refLat={last.ReferenceLateralAcceleration:F2}m/s2 ratio={impulseRatio:F2}";
        }

        if (impulseRatio < 0.65f)
        {
            return $"insufficient early total lateral impulse: actual/reference ratio={impulseRatio:F2}";
        }

        if (rearSlipZero is not null && (frontSlipZero is null || rearSlipZero.Value < frontSlipZero.Value))
        {
            return $"rear slip changes sign before front slip; transient state diverges before front force duration is exhausted";
        }

        if (frontSlipZero is not null)
        {
            return $"front force duration collapse: front slip crosses zero at {frontSlipZero.Value:F3}s despite impulse ratio={impulseRatio:F2}";
        }

        return $"no early slip reversal in window; impulse ratio={impulseRatio:F2}";
    }

    private static string FormatTime(float? time)
    {
        return time is null ? "none" : $"{time.Value:F3}s";
    }

    private static float CalculatePathYawRateDegreesPerSecond(VehicleState state)
    {
        Vector2 velocity = state.Velocity;
        float speedSquared = velocity.LengthSquared();
        if (speedSquared <= 0.001f)
        {
            return 0f;
        }

        Vector2 forward = new(state.Forward.X, state.Forward.Z);
        Vector2 right = new(state.Right.X, state.Right.Z);
        float longAccel = state.LongitudinalAcceleration;
        float latAccel = state.LateralAcceleration;
        Vector2 acceleration = forward * longAccel + right * latAccel;
        float cross = velocity.X * acceleration.Y - velocity.Y * acceleration.X;
        return MathHelper.ToDegrees(cross / speedSquared);
    }

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
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

    private readonly record struct TransientSample(
        float TimeSeconds,
        float SpeedKmh,
        float SteerAngleDegrees,
        float BetaDegrees,
        float YawRateDegreesPerSecond,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float TotalLateralForceN,
        float ActualLateralAcceleration,
        float ReferenceLateralAcceleration,
        float KinematicLateralAcceleration,
        float YawAccelerationDegreesPerSecondSquared,
        float PathYawRateDegreesPerSecond,
        float ReferenceYawRateDegreesPerSecond,
        float ReferenceTotalLateralForceN,
        float ReferenceFrontLateralForceN,
        float ReferenceRearLateralForceN,
        float FrontLocalLateralSpeed,
        float RearLocalLateralSpeed,
        float FrontCapabilityN,
        float RearCapabilityN);

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
