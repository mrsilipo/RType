using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicLateralBalanceProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int Ticks = 360;
    private const int Gear = 4;

    private static readonly float[] SlipSamplesDegrees = [1f, 2f, 3f, 5f, 8f, 10f];

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
        ClassicFourWheelTyres classicTyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);

        Console.WriteLine($"Classic lateral balance probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, production values unchanged");
        Console.WriteLine(
            $"  geometry wheelbase={geometry.WheelbaseMeters:F3}m cgFront={geometry.CgToFrontAxleMeters:F3}m cgRear={geometry.CgToRearAxleMeters:F3}m frontWeight={parameters.FrontWeightDistribution:P1}");

        PrintResolvedAxle("front", parameters.FrontTyres, classicTyres.Front, StaticAxleLoad(parameters, true), geometry.CgToFrontAxleMeters);
        PrintResolvedAxle("rear", parameters.RearTyres, classicTyres.Rear, StaticAxleLoad(parameters, false), geometry.CgToRearAxleMeters);
        PrintReferenceBalance(parameters, geometry);
        RunDynamicCase(parameters, engine, geometry, classicTyres, "medium", 0.35f);
        RunDynamicCase(parameters, engine, geometry, classicTyres, "hard", 0.65f);

        Console.WriteLine("Classic lateral balance probe complete.");
    }

    private static void PrintResolvedAxle(
        string label,
        TyreAxleParameters resolved,
        ClassicBicycleTyreParameters classic,
        float staticAxleLoad,
        float cgDistanceMeters)
    {
        float perWheelLoad = staticAxleLoad * 0.5f;
        float axleCapability = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(perWheelLoad, 1f, classic) * 2f;
        Console.WriteLine($"  {label} resolved axle:");
        Console.WriteLine(
            $"    staticAxleLoad={staticAxleLoad:F0}N perWheel={perWheelLoad:F0}N cgDistance={cgDistanceMeters:F3}m");
        Console.WriteLine(
            $"    physical tyre: corneringStiffness={resolved.CorneringStiffnessNPerRad:F0}N/rad loadSensitivity={resolved.LoadSensitivity:F3} " +
            $"peakMu={resolved.PeakFriction:F2} peakSlip={MathHelper.ToDegrees(resolved.LateralPeakSlipAngleRadians):F1}deg " +
            $"slideSlip={MathHelper.ToDegrees(resolved.LateralSlideSlipAngleRadians):F1}deg slidingMu={resolved.SlidingLateralFrictionMultiplier:F2}");
        Console.WriteLine(
            $"    classic adapter: stiffnessShape={classic.CorneringStiffness:F2} peak={classic.PeakSlipAngleDegrees:F1}deg " +
            $"falloff={classic.FalloffSlipAngleDegrees:F1}deg maxGrip={classic.MaxGrip:F2} slidingGrip={classic.SlidingGrip:F2} " +
            $"axleCapability={axleCapability:F0}N");
        Console.WriteLine("    force samples per wheel: slip Fy usage muEff");
        foreach (float slip in SlipSamplesDegrees)
        {
            float maxForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(perWheelLoad, 1f, classic);
            float force = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(
                MathHelper.ToRadians(slip),
                maxForce,
                classic);
            Console.WriteLine(
                $"      {slip,4:F1} {force,7:F0} {MathF.Abs(force) / MathF.Max(1f, maxForce),5:F2} {force / MathF.Max(1f, perWheelLoad),5:F2}");
        }
    }

    private static void PrintReferenceBalance(VehicleSimulationParameters parameters, VehicleAxleGeometry geometry)
    {
        float understeerIndex = CalculateUndersteerIndex(parameters, geometry);
        string tendency = understeerIndex > 0.0005f
            ? "understeer-biased"
            : understeerIndex < -0.0005f
                ? "oversteer-biased"
                : "neutral-ish";
        Console.WriteLine(
            $"  reference balance: understeerIndex={understeerIndex:F6} tendency={tendency} " +
            $"frontLoadShare={parameters.FrontWeightDistribution:P1} frontStiffShare={parameters.FrontTyres.CorneringStiffnessNPerRad / MathF.Max(1f, parameters.FrontTyres.CorneringStiffnessNPerRad + parameters.RearTyres.CorneringStiffnessNPerRad):P1}");

        foreach ((string Label, float SteerInput, float RoadAngle) item in new[]
        {
            ("medium", 0.35f, 3.45f),
            ("hard", 0.65f, 6.41f)
        })
        {
            ReferenceSnapshot reference = CalculateReference(
                parameters,
                geometry,
                EntrySpeedMetersPerSecond,
                MathHelper.ToRadians(item.RoadAngle));
            Console.WriteLine(
                $"    {item.Label} ref at roadAngle={item.RoadAngle:F2}deg: yaw={reference.YawRateDegreesPerSecond:F1}deg/s " +
                $"beta={reference.BetaDegrees:F2}deg slipF/R={reference.FrontSlipDegrees:F2}/{reference.RearSlipDegrees:F2}deg valid={reference.IsValid}");
        }
    }

    private static void RunDynamicCase(
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

        Console.WriteLine($"  dynamic {label} rawInput={steerInput:F2}");
        Console.WriteLine("    t speed steer beta yaw slipF/R latF F/R capF/R gripF/R loadF/R momentF/R/net refSlipF/R refYaw");
        BalanceSample? frontZero = null;
        BalanceSample? rearSat = null;
        BalanceSample previous = BuildSample(0f, simulator.State, parameters, geometry, tyres);

        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            BalanceSample current = BuildSample(tick * Dt, simulator.State, parameters, geometry, tyres);

            if (frontZero is null &&
                tick > 1 &&
                MathF.Sign(previous.FrontSlipDegrees) != 0f &&
                MathF.Sign(current.FrontSlipDegrees) != 0f &&
                MathF.Sign(previous.FrontSlipDegrees) != MathF.Sign(current.FrontSlipDegrees))
            {
                frontZero = current;
            }

            if (rearSat is null && current.RearGripUsage >= 0.98f)
            {
                rearSat = current;
            }

            if (tick is 30 or 60 or 120 or 360)
            {
                PrintDynamicSample(current);
            }

            previous = current;
        }

        Console.WriteLine(
            $"    events: frontSlipZero={FormatEvent(frontZero)} rearSaturation={FormatEvent(rearSat)} " +
            $"classification={Classify(frontZero, rearSat)}");
    }

    private static BalanceSample BuildSample(
        float time,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        ClassicFourWheelTyres tyres)
    {
        float frontLoad = state.FrontLeftLoadN + state.FrontRightLoadN;
        float rearLoad = state.RearLeftLoadN + state.RearRightLoadN;
        float frontLat = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLat = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontMoment =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearMoment =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float frontCap =
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.FrontLeftLoadN, 1f, tyres.Front) +
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.FrontRightLoadN, 1f, tyres.Front);
        float rearCap =
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.RearLeftLoadN, 1f, tyres.Rear) +
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(state.RearRightLoadN, 1f, tyres.Rear);
        ReferenceSnapshot reference = CalculateReference(
            parameters,
            geometry,
            state.SpeedMetersPerSecond,
            MathHelper.ToRadians(state.FrontLeftSteerAngleDegrees));

        return new BalanceSample(
            time,
            state.SpeedMetersPerSecond * 3.6f,
            state.FrontLeftSteerAngleDegrees,
            state.ClassicBodySlipAngleDegrees,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            frontSlip,
            rearSlip,
            frontLat,
            rearLat,
            frontCap,
            rearCap,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            frontLoad,
            rearLoad,
            frontMoment,
            rearMoment,
            frontMoment + rearMoment,
            reference.FrontSlipDegrees,
            reference.RearSlipDegrees,
            reference.YawRateDegreesPerSecond);
    }

    private static void PrintDynamicSample(BalanceSample s)
    {
        Console.WriteLine(
            $"    {s.TimeSeconds,4:F2} {s.SpeedKmh,6:F1} {s.SteerAngleDegrees,5:F2} {s.BetaDegrees,6:F2} {s.YawRateDegreesPerSecond,6:F1} " +
            $"{s.FrontSlipDegrees,6:F2}/{s.RearSlipDegrees,6:F2} {s.FrontLateralForceN,7:F0}/{s.RearLateralForceN,7:F0} " +
            $"{s.FrontCapabilityN,7:F0}/{s.RearCapabilityN,7:F0} {s.FrontGripUsage,4:F2}/{s.RearGripUsage,4:F2} " +
            $"{s.FrontLoadN,6:F0}/{s.RearLoadN,6:F0} {s.FrontYawMomentNm,8:F0}/{s.RearYawMomentNm,8:F0}/{s.NetYawMomentNm,8:F0} " +
            $"{s.ReferenceFrontSlipDegrees,6:F2}/{s.ReferenceRearSlipDegrees,6:F2} {s.ReferenceYawRateDegreesPerSecond,6:F1}");
    }

    private static string FormatEvent(BalanceSample? sample)
    {
        if (sample is null)
        {
            return "none";
        }

        BalanceSample s = sample.Value;
        return $"t{s.TimeSeconds:F3}s beta{s.BetaDegrees:F1} slipF/R{s.FrontSlipDegrees:F1}/{s.RearSlipDegrees:F1} gripF/R{s.FrontGripUsage:F2}/{s.RearGripUsage:F2}";
    }

    private static string Classify(BalanceSample? frontZero, BalanceSample? rearSat)
    {
        if (frontZero is not null &&
            (rearSat is null || frontZero.Value.TimeSeconds <= rearSat.Value.TimeSeconds))
        {
            return "reference predicts positive-slip balance, but four-wheel state loses front slip before rear saturation";
        }

        if (rearSat is not null)
        {
            return "rear axle saturates before the four-wheel state reaches reference balance";
        }

        return "no front reversal/rear saturation in window";
    }

    private static float StaticAxleLoad(VehicleSimulationParameters parameters, bool front)
    {
        float share = front ? parameters.FrontWeightDistribution : 1f - parameters.FrontWeightDistribution;
        return parameters.MassKg * 9.81f * share;
    }

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
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

    private readonly record struct BalanceSample(
        float TimeSeconds,
        float SpeedKmh,
        float SteerAngleDegrees,
        float BetaDegrees,
        float YawRateDegreesPerSecond,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontCapabilityN,
        float RearCapabilityN,
        float FrontGripUsage,
        float RearGripUsage,
        float FrontLoadN,
        float RearLoadN,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float NetYawMomentNm,
        float ReferenceFrontSlipDegrees,
        float ReferenceRearSlipDegrees,
        float ReferenceYawRateDegreesPerSecond);

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
