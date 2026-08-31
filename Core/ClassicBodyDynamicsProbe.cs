using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBodyDynamicsProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int MeasurementTicks = 120;
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

        Console.WriteLine($"Classic body dynamics probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, surface=ROAD");
        Console.WriteLine("  intended turn yaw sign is positive for positive steer; signed values follow game +X yaw convention.");
        RunComparison(parameters, engineParameters, "medium", 0.35f);
        RunComparison(parameters, engineParameters, "hard", 0.65f);
        Console.WriteLine("Classic body dynamics probe complete.");
    }

    private static void RunComparison(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters baselineEngineParameters,
        string label,
        float steerInput)
    {
        SimulationEngineParameters currentDamping = CloneWithYawDamping(
            baselineEngineParameters,
            baselineEngineParameters.ClassicFourWheel.Yaw.Damping);
        SimulationEngineParameters zeroDamping = CloneWithYawDamping(baselineEngineParameters, 0f);

        Console.WriteLine($"  {label} steerInput={steerInput:F2}");
        ProbeResult current = RunCase(parameters, currentDamping, steerInput);
        ProbeResult zero = RunCase(parameters, zeroDamping, steerInput);

        PrintResult("current", current);
        PrintResult("zero", zero);
        PrintComparison(current, zero);
    }

    private static ProbeResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
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

        DynamicsSnapshot previous = BuildSnapshot(0f, simulator.State, parameters);
        List<DynamicsSnapshot> checkpoints = [];
        Divergence? firstDivergence = null;
        Divergence? firstBetaMismatch = null;
        float maxBodyLatAccelError = 0f;
        float maxBetaDotError = 0f;

        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            DynamicsSnapshot current = BuildSnapshot(tick * Dt, simulator.State, parameters);
            StepComparison step = CompareStep(previous, current);

            maxBodyLatAccelError = MathF.Max(maxBodyLatAccelError, MathF.Abs(step.BodyLatAccelError));
            maxBetaDotError = MathF.Max(maxBetaDotError, MathF.Abs(step.BetaDotErrorDegreesPerSecond));

            if (firstBetaMismatch is null && MathF.Abs(current.SimulatorBetaDegrees - current.BodyAtanBetaDegrees) > 0.5f)
            {
                firstBetaMismatch = Divergence.FromStep("beta calculation", current, step);
            }

            if (firstDivergence is null &&
                current.TimeSeconds > 0.05f &&
                (MathF.Abs(step.BodyLatAccelError) > 1.0f ||
                    MathF.Abs(step.BetaDotErrorDegreesPerSecond) > 3f))
            {
                firstDivergence = Divergence.FromStep("body dynamics", current, step);
            }

            if (tick is 12 or 30 or 60 or 120)
            {
                checkpoints.Add(current with
                {
                    MeasuredBodyLatAccel = step.MeasuredBodyLatAccel,
                    PredictedBodyLatAccel = step.PredictedBodyLatAccel,
                    MeasuredBetaDotDegreesPerSecond = step.MeasuredBetaDotDegreesPerSecond,
                    PredictedBetaDotDegreesPerSecond = step.PredictedBetaDotDegreesPerSecond
                });
            }

            previous = current;
        }

        return new ProbeResult(
            engineParameters.ClassicFourWheel.Yaw.Damping,
            checkpoints,
            firstDivergence,
            firstBetaMismatch,
            maxBodyLatAccelError,
            maxBetaDotError,
            Classify(firstDivergence, firstBetaMismatch));
    }

    private static DynamicsSnapshot BuildSnapshot(
        float timeSeconds,
        VehicleState state,
        VehicleSimulationParameters parameters)
    {
        Vector3 forward3 = state.Forward;
        Vector3 right3 = state.Right;
        Vector2 forward = new(forward3.X, forward3.Z);
        Vector2 right = new(right3.X, right3.Z);
        float bodyForwardSpeed = Vector2.Dot(state.Velocity, forward);
        float bodyLateralSpeed = Vector2.Dot(state.Velocity, right);
        float bodyAtanBeta = MathF.Atan2(bodyLateralSpeed, MathF.Max(2f, MathF.Abs(bodyForwardSpeed)));
        float travelAngle = MathF.Atan2(state.Velocity.X, state.Velocity.Y);
        float travelMinusHeading = MathHelper.WrapAngle(travelAngle - state.HeadingRadians);
        float totalBodyLateralForce =
            state.FrontLeftLateralForceN +
            state.FrontRightLateralForceN +
            state.RearLeftLateralForceN +
            state.RearRightLateralForceN;
        float totalBodyLongitudinalForce =
            state.FrontLeftLongitudinalForceN +
            state.FrontRightLongitudinalForceN +
            state.RearLeftLongitudinalForceN +
            state.RearRightLongitudinalForceN;
        float forceBodyLatAccel = totalBodyLateralForce / MathF.Max(1f, parameters.MassKg);
        float forceBodyLongAccel = totalBodyLongitudinalForce / MathF.Max(1f, parameters.MassKg);
        float yawCoupling = -bodyForwardSpeed * state.YawRateRadiansPerSecond;
        float predictedBodyLatAccel = forceBodyLatAccel + yawCoupling;
        float predictedBodyLongAccel = forceBodyLongAccel + bodyLateralSpeed * state.YawRateRadiansPerSecond;
        float predictedBetaDot = CalculateBetaDot(
            bodyForwardSpeed,
            bodyLateralSpeed,
            predictedBodyLongAccel,
            predictedBodyLatAccel);
        ForceTransformSnapshot forceTransform = BuildForceTransformSnapshot(state);

        return new DynamicsSnapshot(
            timeSeconds,
            state.SpeedMetersPerSecond * 3.6f,
            state.HeadingRadians,
            MathHelper.ToDegrees(travelAngle),
            bodyForwardSpeed,
            bodyLateralSpeed,
            state.Velocity.X,
            state.Velocity.Y,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            state.ClassicBodySlipAngleDegrees,
            MathHelper.ToDegrees(bodyAtanBeta),
            MathHelper.ToDegrees(travelMinusHeading),
            totalBodyLateralForce,
            totalBodyLongitudinalForce,
            forceBodyLatAccel,
            yawCoupling,
            predictedBodyLatAccel,
            MathHelper.ToDegrees(predictedBetaDot),
            0f,
            0f,
            forceTransform);
    }

    private static ForceTransformSnapshot BuildForceTransformSnapshot(VehicleState state)
    {
        WheelForceTransform fl = BuildWheelForceTransform(
            state.FrontLeftSteerAngleDegrees,
            state.FrontLeftLongitudinalForceN,
            state.FrontLeftLateralForceN,
            state.Forward,
            state.Right);
        WheelForceTransform fr = BuildWheelForceTransform(
            state.FrontRightSteerAngleDegrees,
            state.FrontRightLongitudinalForceN,
            state.FrontRightLateralForceN,
            state.Forward,
            state.Right);

        return new ForceTransformSnapshot(fl, fr);
    }

    private static WheelForceTransform BuildWheelForceTransform(
        float steerAngleDegrees,
        float bodyForwardForce,
        float bodyRightForce,
        Vector3 forward,
        Vector3 right)
    {
        float steerRadians = MathHelper.ToRadians(steerAngleDegrees);
        float sin = MathF.Sin(steerRadians);
        float cos = MathF.Cos(steerRadians);
        float wheelLongitudinal = bodyForwardForce * cos + bodyRightForce * sin;
        float wheelLateral = bodyRightForce * cos - bodyForwardForce * sin;
        float rebuiltBodyForward = wheelLongitudinal * cos - wheelLateral * sin;
        float rebuiltBodyRight = wheelLongitudinal * sin + wheelLateral * cos;
        float worldX = forward.X * bodyForwardForce + right.X * bodyRightForce;
        float worldZ = forward.Z * bodyForwardForce + right.Z * bodyRightForce;
        float rebuildError = MathF.Abs(rebuiltBodyForward - bodyForwardForce) + MathF.Abs(rebuiltBodyRight - bodyRightForce);

        return new WheelForceTransform(
            wheelLongitudinal,
            wheelLateral,
            bodyForwardForce,
            bodyRightForce,
            worldX,
            worldZ,
            rebuildError);
    }

    private static StepComparison CompareStep(DynamicsSnapshot previous, DynamicsSnapshot current)
    {
        float measuredBodyLatAccel = (current.BodyLateralSpeedMetersPerSecond - previous.BodyLateralSpeedMetersPerSecond) / Dt;
        float measuredBetaDot = MathHelper.WrapAngle(
            MathHelper.ToRadians(current.BodyAtanBetaDegrees) -
            MathHelper.ToRadians(previous.BodyAtanBetaDegrees)) / Dt;

        return new StepComparison(
            measuredBodyLatAccel,
            current.PredictedBodyLatAccel,
            measuredBodyLatAccel - current.PredictedBodyLatAccel,
            MathHelper.ToDegrees(measuredBetaDot),
            current.PredictedBetaDotDegreesPerSecond,
            MathHelper.ToDegrees(measuredBetaDot) - current.PredictedBetaDotDegreesPerSecond);
    }

    private static float CalculateBetaDot(float u, float v, float uDot, float vDot)
    {
        float denominator = u * u + v * v;
        if (denominator <= 0.001f)
        {
            return 0f;
        }

        return (u * vDot - v * uDot) / denominator;
    }

    private static string Classify(Divergence? firstDivergence, Divergence? firstBetaMismatch)
    {
        if (firstBetaMismatch is not null)
        {
            return "beta calculation mismatch";
        }

        if (firstDivergence is null)
        {
            return "body-frame dynamics consistent; instability is downstream of rigid-body coupling";
        }

        return firstDivergence.Value.MeasuredBodyLatAccel * firstDivergence.Value.PredictedBodyLatAccel < 0f
            ? "wrong-sign or double-applied rotational coupling"
            : "integration/reprojection body-dynamics divergence";
    }

    private static void PrintResult(string label, ProbeResult result)
    {
        Console.WriteLine($"    {label} yawDamping={result.YawDamping:F2}");
        Console.WriteLine("      t speed yaw betaSim/betaBody/travel betaDot m/p bodyLatAccel m/p forceLat yawCouple u/v worldVelX/Z");
        foreach (DynamicsSnapshot sample in result.Checkpoints)
        {
            Console.WriteLine(
                $"      {sample.TimeSeconds,4:F2} {sample.SpeedKmh,6:F1} {sample.YawRateDegreesPerSecond,6:F1} " +
                $"{sample.SimulatorBetaDegrees,6:F1}/{sample.BodyAtanBetaDegrees,6:F1}/{sample.TravelMinusHeadingDegrees,6:F1} " +
                $"{sample.MeasuredBetaDotDegreesPerSecond,7:F1}/{sample.PredictedBetaDotDegreesPerSecond,7:F1} " +
                $"{sample.MeasuredBodyLatAccel,7:F2}/{sample.PredictedBodyLatAccel,7:F2} " +
                $"{sample.ForceBodyLatAccel,7:F2} {sample.YawCouplingLatAccel,8:F2} " +
                $"{sample.BodyForwardSpeedMetersPerSecond,6:F2}/{sample.BodyLateralSpeedMetersPerSecond,5:F2} " +
                $"{sample.WorldVelocityX,6:F2}/{sample.WorldVelocityZ,6:F2}");
            Console.WriteLine(
                $"           FL wheelLong/lat={sample.ForceTransform.FrontLeft.WheelLongitudinalForceN,7:F0}/{sample.ForceTransform.FrontLeft.WheelLateralForceN,7:F0}N " +
                $"bodyF/R={sample.ForceTransform.FrontLeft.BodyForwardForceN,7:F0}/{sample.ForceTransform.FrontLeft.BodyRightForceN,7:F0}N " +
                $"worldX/Z={sample.ForceTransform.FrontLeft.WorldForceX,7:F0}/{sample.ForceTransform.FrontLeft.WorldForceZ,7:F0}N err={sample.ForceTransform.FrontLeft.RebuildErrorN:F3}N");
            Console.WriteLine(
                $"           FR wheelLong/lat={sample.ForceTransform.FrontRight.WheelLongitudinalForceN,7:F0}/{sample.ForceTransform.FrontRight.WheelLateralForceN,7:F0}N " +
                $"bodyF/R={sample.ForceTransform.FrontRight.BodyForwardForceN,7:F0}/{sample.ForceTransform.FrontRight.BodyRightForceN,7:F0}N " +
                $"worldX/Z={sample.ForceTransform.FrontRight.WorldForceX,7:F0}/{sample.ForceTransform.FrontRight.WorldForceZ,7:F0}N err={sample.ForceTransform.FrontRight.RebuildErrorN:F3}N");
        }

        PrintDivergence("first body-dynamics divergence", result.FirstDivergence);
        PrintDivergence("first beta mismatch", result.FirstBetaMismatch);
        Console.WriteLine(
            $"      max errors: bodyLatAccel={result.MaxBodyLatAccelError:F2}m/s2 betaDot={result.MaxBetaDotErrorDegreesPerSecond:F1}deg/s; classification={result.Classification}");
    }

    private static void PrintDivergence(string label, Divergence? divergence)
    {
        if (divergence is null)
        {
            Console.WriteLine($"      {label}: none");
            return;
        }

        Divergence d = divergence.Value;
        Console.WriteLine(
            $"      {label}: t={d.TimeSeconds:F3}s source={d.Source} " +
            $"bodyLatAccel m/p={d.MeasuredBodyLatAccel:F2}/{d.PredictedBodyLatAccel:F2}m/s2 " +
            $"betaDot m/p={d.MeasuredBetaDotDegreesPerSecond:F1}/{d.PredictedBetaDotDegreesPerSecond:F1}deg/s " +
            $"betaSim/body={d.SimulatorBetaDegrees:F2}/{d.BodyAtanBetaDegrees:F2}deg");
    }

    private static void PrintComparison(ProbeResult current, ProbeResult zero)
    {
        DynamicsSnapshot current100 = current.Checkpoints[^1];
        DynamicsSnapshot zero100 = zero.Checkpoints[^1];
        Console.WriteLine(
            $"    zero-current delta @1.00s: yaw={zero100.YawRateDegreesPerSecond - current100.YawRateDegreesPerSecond:+0.0;-0.0;0.0}deg/s " +
            $"beta={zero100.SimulatorBetaDegrees - current100.SimulatorBetaDegrees:+0.0;-0.0;0.0}deg " +
            $"bodyLatAccelErr={zero.MaxBodyLatAccelError - current.MaxBodyLatAccelError:+0.00;-0.00;0.00}m/s2");
    }

    private static SimulationEngineParameters CloneWithYawDamping(
        SimulationEngineParameters source,
        float yawDamping)
    {
        return new SimulationEngineParameters
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
    }

    private readonly record struct ProbeResult(
        float YawDamping,
        IReadOnlyList<DynamicsSnapshot> Checkpoints,
        Divergence? FirstDivergence,
        Divergence? FirstBetaMismatch,
        float MaxBodyLatAccelError,
        float MaxBetaDotErrorDegreesPerSecond,
        string Classification);

    private readonly record struct DynamicsSnapshot(
        float TimeSeconds,
        float SpeedKmh,
        float HeadingRadians,
        float TravelAngleDegrees,
        float BodyForwardSpeedMetersPerSecond,
        float BodyLateralSpeedMetersPerSecond,
        float WorldVelocityX,
        float WorldVelocityZ,
        float YawRateDegreesPerSecond,
        float SimulatorBetaDegrees,
        float BodyAtanBetaDegrees,
        float TravelMinusHeadingDegrees,
        float TotalBodyLateralForceN,
        float TotalBodyLongitudinalForceN,
        float ForceBodyLatAccel,
        float YawCouplingLatAccel,
        float PredictedBodyLatAccel,
        float PredictedBetaDotDegreesPerSecond,
        float MeasuredBodyLatAccel,
        float MeasuredBetaDotDegreesPerSecond,
        ForceTransformSnapshot ForceTransform);

    private readonly record struct StepComparison(
        float MeasuredBodyLatAccel,
        float PredictedBodyLatAccel,
        float BodyLatAccelError,
        float MeasuredBetaDotDegreesPerSecond,
        float PredictedBetaDotDegreesPerSecond,
        float BetaDotErrorDegreesPerSecond);

    private readonly record struct Divergence(
        string Source,
        float TimeSeconds,
        float MeasuredBodyLatAccel,
        float PredictedBodyLatAccel,
        float MeasuredBetaDotDegreesPerSecond,
        float PredictedBetaDotDegreesPerSecond,
        float SimulatorBetaDegrees,
        float BodyAtanBetaDegrees)
    {
        public static Divergence FromStep(string source, DynamicsSnapshot snapshot, StepComparison step)
        {
            return new Divergence(
                source,
                snapshot.TimeSeconds,
                step.MeasuredBodyLatAccel,
                step.PredictedBodyLatAccel,
                step.MeasuredBetaDotDegreesPerSecond,
                step.PredictedBetaDotDegreesPerSecond,
                snapshot.SimulatorBetaDegrees,
                snapshot.BodyAtanBetaDegrees);
        }
    }

    private readonly record struct ForceTransformSnapshot(
        WheelForceTransform FrontLeft,
        WheelForceTransform FrontRight);

    private readonly record struct WheelForceTransform(
        float WheelLongitudinalForceN,
        float WheelLateralForceN,
        float BodyForwardForceN,
        float BodyRightForceN,
        float WorldForceX,
        float WorldForceZ,
        float RebuildErrorN);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
