using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicYawDampingExperimentProbe
{
    private const float EntrySpeedKmh = 150f;
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

        Console.WriteLine($"Classic yaw damping experiment probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  cleanup=off, throttle=0.25, gear=4, surface=ROAD");
        Console.WriteLine($"  inertiaScale remains {engineParameters.ClassicFourWheel.Yaw.InertiaScale:0.00}; tyres/steering/drivetrain unchanged");
        Console.WriteLine("  intended turn yaw sign is positive for positive steer in this simulator; larger positive yaw magnitude is more rotation into the requested turn.");
        RunComparison(parameters, engineParameters, geometry, "medium", 0.35f);
        RunComparison(parameters, engineParameters, geometry, "hard", 0.65f);
        Console.WriteLine("Classic yaw damping experiment probe complete.");
    }

    private static void RunComparison(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters baselineEngineParameters,
        VehicleAxleGeometry geometry,
        string label,
        float steerInput)
    {
        SimulationEngineParameters currentDamping = CloneWithYawDamping(baselineEngineParameters, baselineEngineParameters.ClassicFourWheel.Yaw.Damping);
        SimulationEngineParameters zeroDamping = CloneWithYawDamping(baselineEngineParameters, 0f);

        ExperimentResult baseline = RunCase(parameters, currentDamping, geometry, steerInput);
        ExperimentResult zero = RunCase(parameters, zeroDamping, geometry, steerInput);

        Console.WriteLine($"  {label} steerInput={steerInput:0.00}");
        Console.WriteLine("    damping    speedDrop rpmDrop unstable? t yaw/ref beta slipF/R gripF/R latV latPowerW equivDrag");
        PrintResult("current", baseline);
        PrintResult("zero", zero);
        PrintDelta(baseline, zero);
    }

    private static ExperimentResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        VehicleAxleGeometry geometry,
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
        simulator.State.Velocity = new Vector2(0f, EntrySpeedKmh / 3.6f);

        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        float startRpm = simulator.State.Rpm;
        List<TimedSample> samples = [];
        bool unstable = false;

        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            if (tick is 12 or 30 or 60 or 120)
            {
                samples.Add(BuildSample(tick * Dt, simulator.State, parameters, geometry));
            }

            if (!IsFinite(simulator.State) ||
                MathF.Abs(simulator.State.ClassicBodySlipAngleDegrees) > 35f ||
                MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)) > 140f)
            {
                unstable = true;
                break;
            }
        }

        return new ExperimentResult(
            startSpeedKmh - simulator.State.SpeedMetersPerSecond * 3.6f,
            startRpm - simulator.State.Rpm,
            unstable,
            samples);
    }

    private static TimedSample BuildSample(
        float timeSeconds,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry)
    {
        ReferenceSnapshot reference = CalculateReference(
            parameters,
            geometry,
            MathF.Max(0.1f, state.SpeedMetersPerSecond),
            MathHelper.ToRadians((state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f));
        float frontSlip = (
            MathF.Abs(state.FrontLeftSlipAngleDegrees) +
            MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f;
        float rearSlip = (
            MathF.Abs(state.RearLeftSlipAngleDegrees) +
            MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f;
        float frontGrip = MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage);
        float rearGrip = MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);
        float lateralPowerW =
            state.FrontLeftLateralForceN * state.FrontLeftLocalLateralSpeedMetersPerSecond +
            state.FrontRightLateralForceN * state.FrontRightLocalLateralSpeedMetersPerSecond +
            state.RearLeftLateralForceN * state.RearLeftLocalLateralSpeedMetersPerSecond +
            state.RearRightLateralForceN * state.RearRightLocalLateralSpeedMetersPerSecond;
        float equivalentDragN = lateralPowerW / MathF.Max(0.1f, state.SpeedMetersPerSecond);

        return new TimedSample(
            timeSeconds,
            state.SpeedMetersPerSecond * 3.6f,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            reference.YawRateDegreesPerSecond,
            state.ClassicBodySlipAngleDegrees,
            frontSlip,
            rearSlip,
            frontGrip,
            rearGrip,
            state.LateralSpeed,
            lateralPowerW,
            equivalentDragN);
    }

    private static void PrintResult(string label, ExperimentResult result)
    {
        foreach (TimedSample sample in result.Samples)
        {
            Console.WriteLine(
                $"    {label,-8} {result.SpeedDropKmh,8:0.0} {result.RpmDrop,7:0} {result.Unstable,-8} " +
                $"{sample.TimeSeconds,4:0.00} {sample.YawRateDegreesPerSecond,6:0.0}/{sample.ReferenceYawRateDegreesPerSecond,5:0.0} " +
                $"{sample.BodySlipDegrees,5:0.0} {sample.FrontSlipDegrees,4:0.0}/{sample.RearSlipDegrees,4:0.0} " +
                $"{sample.FrontGripUsage,4:0.00}/{sample.RearGripUsage,4:0.00} {sample.LateralSpeedMetersPerSecond,5:0.00} " +
                $"{sample.LateralPowerWatts,9:0}W {sample.EquivalentDragN,7:0}N");
        }
    }

    private static void PrintDelta(ExperimentResult baseline, ExperimentResult zero)
    {
        TimedSample? baseline025 = FindSample(baseline, 0.25f);
        TimedSample? zero025 = FindSample(zero, 0.25f);
        TimedSample? baseline100 = FindSample(baseline, 1.0f);
        TimedSample? zero100 = FindSample(zero, 1.0f);
        if (baseline025 is null || zero025 is null || baseline100 is null || zero100 is null)
        {
            return;
        }

        Console.WriteLine(
            $"    delta    speedDrop {zero.SpeedDropKmh - baseline.SpeedDropKmh:+0.0;-0.0;0.0}km/h, " +
            $"yaw@0.25 {zero025.Value.YawRateDegreesPerSecond - baseline025.Value.YawRateDegreesPerSecond:+0.0;-0.0;0.0}deg/s, " +
            $"body@1.0 {zero100.Value.BodySlipDegrees - baseline100.Value.BodySlipDegrees:+0.0;-0.0;0.0}deg, " +
            $"rearGrip@1.0 {zero100.Value.RearGripUsage - baseline100.Value.RearGripUsage:+0.00;-0.00;0.00}, " +
            $"equivDrag@1.0 {zero100.Value.EquivalentDragN - baseline100.Value.EquivalentDragN:+0;-0;0}N");
    }

    private static TimedSample? FindSample(ExperimentResult result, float timeSeconds)
    {
        foreach (TimedSample sample in result.Samples)
        {
            if (MathF.Abs(sample.TimeSeconds - timeSeconds) < 0.01f)
            {
                return sample;
            }
        }

        return null;
    }

    private static SimulationEngineParameters CloneWithYawDamping(
        SimulationEngineParameters source,
        float yawDamping)
    {
        SimulationEngineParameters clone = new()
        {
            HandlingModel = source.HandlingModel,
            Timing = source.Timing,
            VehicleSafety = source.VehicleSafety,
            StabilityAssist = source.StabilityAssist,
            DigitalThrottleAssist = source.DigitalThrottleAssist,
            DigitalBrakeAssist = source.DigitalBrakeAssist,
            BrakeThrottlePriority = source.BrakeThrottlePriority,
            SteeringAssist = source.SteeringAssist,
            TyreForce = source.TyreForce,
            RpmResponse = source.RpmResponse,
            ClassicBicycle = source.ClassicBicycle,
            ClassicFourWheel = new ClassicBicycleParameters
            {
                Steering = source.ClassicFourWheel.Steering,
                FrontTyres = source.ClassicFourWheel.FrontTyres,
                RearTyres = source.ClassicFourWheel.RearTyres,
                Yaw = new ClassicBicycleYawParameters
                {
                    InertiaScale = source.ClassicFourWheel.Yaw.InertiaScale,
                    Damping = yawDamping,
                    LateralVelocityDamping = source.ClassicFourWheel.Yaw.LateralVelocityDamping
                },
                GripBudget = source.ClassicFourWheel.GripBudget,
                ChassisLoadTransfer = source.ClassicFourWheel.ChassisLoadTransfer,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance
            }
        };
        return clone;
    }

    private static ReferenceSnapshot CalculateReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f);
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
            return new ReferenceSnapshot(0f);
        }

        float yawRate = (a11 * b2 - b1 * a21) / det;
        return new ReferenceSnapshot(MathHelper.ToDegrees(yawRate));
    }

    private static bool IsFinite(VehicleState state)
    {
        return float.IsFinite(state.Position.X) &&
            float.IsFinite(state.Position.Z) &&
            float.IsFinite(state.Velocity.X) &&
            float.IsFinite(state.Velocity.Y) &&
            float.IsFinite(state.HeadingRadians) &&
            float.IsFinite(state.YawRateRadiansPerSecond);
    }

    private readonly record struct ExperimentResult(
        float SpeedDropKmh,
        float RpmDrop,
        bool Unstable,
        IReadOnlyList<TimedSample> Samples);

    private readonly record struct TimedSample(
        float TimeSeconds,
        float SpeedKmh,
        float YawRateDegreesPerSecond,
        float ReferenceYawRateDegreesPerSecond,
        float BodySlipDegrees,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float FrontGripUsage,
        float RearGripUsage,
        float LateralSpeedMetersPerSecond,
        float LateralPowerWatts,
        float EquivalentDragN);

    private readonly record struct ReferenceSnapshot(float YawRateDegreesPerSecond);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
