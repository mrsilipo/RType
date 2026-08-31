using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTransientLoadTransferProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float SteerCommand = 0.85f;
    private const float RunSeconds = 1.40f;
    private const float Gravity = 9.81f;

    private static readonly float[] CheckpointsSeconds = [0.05f, 0.10f, 0.20f, 0.35f, 0.55f, 0.75f, 0.95f, 1.15f, 1.35f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic transient load-transfer probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  sequence=150km/h hard brake -> hold -> release -> turn-in -> countersteer, gear=4");
        Console.WriteLine("  transfer convention: +longitudinal moves load to front; signed lateral follows existing right-load convention");
        Console.WriteLine("  set case t brake steer angle speed longG latG longTarget/actual/vel frontLatTarget/actual/vel rearLatTarget/actual/vel loadF/R latDiffF/R pressF/R regF/R latF/R yawF/R yawRate beta");

        RunSet("stateful-mod-on", parameters, Clone(engine, loadTransferEnabled: true, brakeSteerModulationEnabled: true));
        RunSet("stateful-mod-off", parameters, Clone(engine, loadTransferEnabled: true, brakeSteerModulationEnabled: false));
        RunSet("instant-mod-off", parameters, Clone(engine, loadTransferEnabled: false, brakeSteerModulationEnabled: false));

        Console.WriteLine("Classic transient load-transfer probe complete.");
    }

    private static void RunSet(string label, VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        RunCase(label, "brake-release-steer-counter", parameters, engine);
    }

    private static void RunCase(
        string setLabel,
        string caseName,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
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

        int checkpointIndex = 0;
        for (int i = 1; i <= SecondsToTicks(RunSeconds); i++)
        {
            float elapsed = i * Dt;
            simulator.Update(new VehicleInput(0f, Brake(elapsed), Steer(elapsed), brakeAssistEnabled: true), Dt);

            if (checkpointIndex < CheckpointsSeconds.Length &&
                elapsed + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(setLabel, caseName, CheckpointsSeconds[checkpointIndex], simulator.State, parameters);
                checkpointIndex++;
            }
        }
    }

    private static float Brake(float elapsed)
    {
        if (elapsed < 0.55f)
        {
            return 1f;
        }

        return MathHelper.Lerp(1f, 0.12f, SmoothStep01((elapsed - 0.55f) / 0.42f));
    }

    private static float Steer(float elapsed)
    {
        if (elapsed < 0.35f)
        {
            return 0f;
        }

        return elapsed < 1.05f ? SteerCommand : -0.55f;
    }

    private static void PrintSample(
        string setLabel,
        string caseName,
        float elapsed,
        VehicleState state,
        VehicleSimulationParameters parameters)
    {
        AxleForces forces = BuildAxleForces(state, parameters);
        float roadWheelAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float frontLoad = state.ClassicDynamicFrontAxleLoadN;
        float rearLoad = state.ClassicDynamicRearAxleLoadN;
        float frontLatDiff = state.FrontLeftLoadN - state.FrontRightLoadN;
        float rearLatDiff = state.RearLeftLoadN - state.RearRightLoadN;
        float frontPressure = (state.FrontLeftBrakePressureRatio + state.FrontRightBrakePressureRatio) * 0.5f;
        float rearPressure = (state.RearLeftBrakePressureRatio + state.RearRightBrakePressureRatio) * 0.5f;
        int frontRegulatorsActive =
            (state.FrontLeftBrakePressureRegulatorActive ? 1 : 0) +
            (state.FrontRightBrakePressureRegulatorActive ? 1 : 0);
        int rearRegulatorsActive =
            (state.RearLeftBrakePressureRegulatorActive ? 1 : 0) +
            (state.RearRightBrakePressureRegulatorActive ? 1 : 0);

        Console.WriteLine(
            $"  {setLabel,-16} {caseName,-27} {elapsed,4:F2} {state.Brake,5:F2} {state.Steer,5:F2} {roadWheelAngle,5:F2} " +
            $"{state.SpeedMetersPerSecond * 3.6f,6:F1} {state.LongitudinalAcceleration / Gravity,5:F2} {state.LateralAcceleration / Gravity,5:F2} " +
            $"{state.ClassicTargetLongitudinalLoadTransferN,6:F0}/{state.ClassicActualLongitudinalLoadTransferN,6:F0}/{state.ClassicLongitudinalLoadTransferVelocityNPerSecond,7:F0} " +
            $"{state.ClassicTargetFrontLateralLoadTransferN,6:F0}/{state.ClassicActualFrontLateralLoadTransferN,6:F0}/{state.ClassicFrontLateralLoadTransferVelocityNPerSecond,7:F0} " +
            $"{state.ClassicTargetRearLateralLoadTransferN,6:F0}/{state.ClassicActualRearLateralLoadTransferN,6:F0}/{state.ClassicRearLateralLoadTransferVelocityNPerSecond,7:F0} " +
            $"{frontLoad,5:F0}/{rearLoad,5:F0} {frontLatDiff,6:F0}/{rearLatDiff,6:F0} " +
            $"{frontPressure,4:F2}/{rearPressure,4:F2} {frontRegulatorsActive,1}/{rearRegulatorsActive,1} " +
            $"{forces.FrontLateralForceN,6:F0}/{forces.RearLateralForceN,6:F0} " +
            $"{forces.FrontYawMomentNm,7:F0}/{forces.RearYawMomentNm,7:F0} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:F1} {state.ClassicBodySlipAngleDegrees,6:F2}");
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

    private static SimulationEngineParameters Clone(
        SimulationEngineParameters source,
        bool loadTransferEnabled,
        bool brakeSteerModulationEnabled)
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
                ChassisLoadTransfer = new ClassicChassisLoadTransferParameters
                {
                    Enabled = loadTransferEnabled,
                    LongitudinalNaturalFrequencyHz = source.ClassicFourWheel.ChassisLoadTransfer.LongitudinalNaturalFrequencyHz,
                    LongitudinalDampingRatio = source.ClassicFourWheel.ChassisLoadTransfer.LongitudinalDampingRatio,
                    LateralNaturalFrequencyHz = source.ClassicFourWheel.ChassisLoadTransfer.LateralNaturalFrequencyHz,
                    LateralDampingRatio = source.ClassicFourWheel.ChassisLoadTransfer.LateralDampingRatio
                },
                GripBudget = brakeSteerModulationEnabled
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
                    }
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
