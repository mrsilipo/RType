using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTrailBrakeDynamicsProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float SteerCommand = 0.85f;
    private const float SetupSeconds = 0.50f;
    private const float RunSeconds = 1.50f;
    private const float Gravity = 9.81f;

    private static readonly float[] CheckpointsSeconds = [0.10f, 0.25f, 0.50f, 0.75f, 1.00f, 1.50f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SimulationEngineParameters modulationOff = CloneWithBrakeSteerModulation(engine, enabled: false);

        Console.WriteLine($"Classic trail-brake dynamics probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  start=150km/h, gear=4, steer=0.85, cleanup=production");
        PrintLoadReference(parameters);

        RunSet("mod-on", parameters, engine);
        RunSet("mod-off", parameters, modulationOff);

        Console.WriteLine("Classic trail-brake dynamics probe complete.");
    }

    private static void RunSet(string label, VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        Console.WriteLine();
        Console.WriteLine($"  {label}");
        Console.WriteLine("    case t brake steer angle speed longG latG loadF/R capF/R pressF/R regF/R latF/R longUseF/R latUseF/R yawF/R yawRate yawAccel beta slipF/R rearGrip");

        RunCase(label, parameters, engine, "straight-brake", BrakeStraight, steer: _ => 0f);
        RunCase(label, parameters, engine, "brake-steer", BrakeHeld, steer: _ => SteerCommand);
        RunCase(label, parameters, engine, "release-steer", BrakeRelease, steer: _ => SteerCommand);
        RunCase(label, parameters, engine, "release-counter", BrakeReleaseCounter, steer: CounterSteer);
    }

    private static void RunCase(
        string setLabel,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string caseName,
        Func<float, float> brake,
        Func<float, float> steer)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, StartSpeedKmh / 3.6f);

        for (int i = 0; i < SecondsToTicks(SetupSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true), Dt);
        }

        int checkpointIndex = 0;
        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            float elapsed = i * Dt;
            float brakeInput = MathHelper.Clamp(brake(elapsed), 0f, 1f);
            float steerInput = MathHelper.Clamp(steer(elapsed), -1f, 1f);
            simulator.Update(new VehicleInput(0f, brakeInput, steerInput, brakeAssistEnabled: true), Dt);

            if (checkpointIndex < CheckpointsSeconds.Length &&
                elapsed + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(caseName, CheckpointsSeconds[checkpointIndex], simulator.State, parameters);
                checkpointIndex++;
            }
        }
    }

    private static float BrakeStraight(float elapsed) => 1f;

    private static float BrakeHeld(float elapsed) => 1f;

    private static float BrakeRelease(float elapsed)
    {
        return elapsed < 0.15f
            ? 1f
            : MathHelper.Lerp(1f, 0.15f, SmoothStep01((elapsed - 0.15f) / 0.70f));
    }

    private static float BrakeReleaseCounter(float elapsed) => BrakeRelease(elapsed);

    private static float CounterSteer(float elapsed)
    {
        return elapsed < 0.62f ? SteerCommand : -0.55f;
    }

    private static void PrintSample(string caseName, float elapsed, VehicleState state, VehicleSimulationParameters parameters)
    {
        AxleForces forces = BuildAxleForces(state, parameters);
        float roadWheelAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float frontCapacity = state.FrontLeftFrictionEllipseGripBudgetN + state.FrontRightFrictionEllipseGripBudgetN;
        float rearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
        float frontLoad = state.ClassicDynamicFrontAxleLoadN;
        float rearLoad = state.ClassicDynamicRearAxleLoadN;
        float frontPressure = (state.FrontLeftBrakePressureRatio + state.FrontRightBrakePressureRatio) * 0.5f;
        float rearPressure = (state.RearLeftBrakePressureRatio + state.RearRightBrakePressureRatio) * 0.5f;
        int frontRegulatorsActive =
            (state.FrontLeftBrakePressureRegulatorActive ? 1 : 0) +
            (state.FrontRightBrakePressureRegulatorActive ? 1 : 0);
        int rearRegulatorsActive =
            (state.RearLeftBrakePressureRegulatorActive ? 1 : 0) +
            (state.RearRightBrakePressureRegulatorActive ? 1 : 0);
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float yawAcceleration =
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared;

        Console.WriteLine(
            $"    {caseName,-15} {elapsed,4:F2} {state.Brake,5:F2} {state.Steer,5:F2} {roadWheelAngle,5:F2} " +
            $"{state.SpeedMetersPerSecond * 3.6f,6:F1} {state.LongitudinalAcceleration / Gravity,5:F2} {state.LateralAcceleration / Gravity,5:F2} " +
            $"{frontLoad,5:F0}/{rearLoad,5:F0} {frontCapacity,5:F0}/{rearCapacity,5:F0} " +
            $"{frontPressure,4:F2}/{rearPressure,4:F2} {frontRegulatorsActive,1}/{rearRegulatorsActive,1} " +
            $"{forces.FrontLateralForceN,6:F0}/{forces.RearLateralForceN,6:F0} " +
            $"{state.ClassicFrontLongitudinalGripUsage,4:F2}/{state.ClassicRearLongitudinalGripUsage,4:F2} " +
            $"{state.ClassicFrontLateralGripUsage,4:F2}/{state.ClassicRearLateralGripUsage,4:F2} " +
            $"{forces.FrontYawMomentNm,6:F0}/{forces.RearYawMomentNm,6:F0} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:F1} {yawAcceleration,7:F0} " +
            $"{state.ClassicBodySlipAngleDegrees,5:F2} {frontSlip,5:F2}/{rearSlip,5:F2} " +
            $"{MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),4:F2}");
    }

    private static AxleForces BuildAxleForces(VehicleState state, VehicleSimulationParameters parameters)
    {
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        float flRight = -geometry.FrontTrackMeters * 0.5f;
        float frRight = geometry.FrontTrackMeters * 0.5f;
        float rlRight = -geometry.RearTrackMeters * 0.5f;
        float rrRight = geometry.RearTrackMeters * 0.5f;
        float frontForward = geometry.CgToFrontAxleMeters;
        float rearForward = -geometry.CgToRearAxleMeters;
        float frontLat = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLat = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontYaw =
            -frontForward * state.FrontLeftLateralForceN +
            flRight * state.FrontLeftLongitudinalForceN -
            frontForward * state.FrontRightLateralForceN +
            frRight * state.FrontRightLongitudinalForceN;
        float rearYaw =
            -rearForward * state.RearLeftLateralForceN +
            rlRight * state.RearLeftLongitudinalForceN -
            rearForward * state.RearRightLateralForceN +
            rrRight * state.RearRightLongitudinalForceN;
        return new AxleForces(frontLat, rearLat, frontYaw, rearYaw);
    }

    private static void PrintLoadReference(VehicleSimulationParameters parameters)
    {
        float staticFront = parameters.MassKg * Gravity * MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f);
        float staticRear = parameters.MassKg * Gravity - staticFront;
        Console.WriteLine(
            $"  static load F/R={staticFront:F0}/{staticRear:F0}N, CGh={parameters.CenterOfGravityHeightMeters:F2}m, wheelbase={parameters.WheelbaseMeters:F2}m");
        foreach (float decelG in new[] { 0.5f, 0.8f, 1.0f, 1.2f })
        {
            float transfer = parameters.MassKg * decelG * Gravity *
                MathHelper.Clamp(parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f) /
                MathF.Max(0.1f, parameters.WheelbaseMeters);
            Console.WriteLine(
                $"  expected {decelG:F1}g braking load F/R={staticFront + transfer:F0}/{staticRear - transfer:F0}N transfer={transfer:F0}N");
        }
    }

    private static SimulationEngineParameters CloneWithBrakeSteerModulation(SimulationEngineParameters source, bool enabled)
    {
        ClassicBicycleGripBudgetParameters grip = source.ClassicFourWheel.GripBudget;
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
                Yaw = source.ClassicFourWheel.Yaw,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance,
                GripBudget = enabled
                    ? grip
                    : new ClassicBicycleGripBudgetParameters
                    {
                        CombinedGripExponent = grip.CombinedGripExponent,
                        BrakingSteeringLateralPriority = 0f,
                        BrakingSteeringPrioritySteerStart = grip.BrakingSteeringPrioritySteerStart,
                        BrakingSteeringPrioritySteerEnd = grip.BrakingSteeringPrioritySteerEnd,
                        BrakingSteeringPriorityBrakeStart = grip.BrakingSteeringPriorityBrakeStart,
                        BrakingSteeringPriorityBrakeEnd = grip.BrakingSteeringPriorityBrakeEnd,
                        BrakingSteeringFrontBrakeMultiplier = 1f,
                        BrakingSteeringRearBrakeMultiplier = 1f,
                        BrakePressureFrontTargetGripUsage = grip.BrakePressureFrontTargetGripUsage,
                        BrakePressureRearTargetGripUsage = grip.BrakePressureRearTargetGripUsage,
                        BrakePressureApplyRatePerSecond = grip.BrakePressureApplyRatePerSecond,
                        BrakePressureReleaseRatePerSecond = grip.BrakePressureReleaseRatePerSecond,
                        BrakePressureMinimumRatio = grip.BrakePressureMinimumRatio,
                        BrakePressureMinimumSpeedMetersPerSecond = grip.BrakePressureMinimumSpeedMetersPerSecond
                    },
                ChassisLoadTransfer = source.ClassicFourWheel.ChassisLoadTransfer
            }
        };
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private readonly record struct AxleForces(
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawMomentNm,
        float RearYawMomentNm);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
