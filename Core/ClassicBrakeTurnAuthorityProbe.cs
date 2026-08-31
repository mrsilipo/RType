using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBrakeTurnAuthorityProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float SteerCommand = 1.0f;
    private const float BrakePhaseSeconds = 0.50f;
    private const float TurnPhaseSeconds = 1.00f;

    private static readonly float[] CheckpointsSeconds = [0.10f, 0.25f, 0.50f, 1.00f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic brake-turn authority probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  start=150km/h, gear=4, throttle=0.00, cleanup=production, steer=1.00 after 0.50s setup");
        Console.WriteLine("  case t speed angle yaw beta slipF/R pressF/R regF/R forceF_lat/long gripF_lat/long rearGrip latG longG loss");

        RunCase(parameters, engine, "coast-turn", preBrake: 0f, turnBrake: 0f);
        RunCase(parameters, engine, "brake-held", preBrake: 1f, turnBrake: 1f);
        RunCase(parameters, engine, "brake-release", preBrake: 1f, turnBrake: 0f);
        RunCase(parameters, engine, "trail-25", preBrake: 1f, turnBrake: 0.25f);

        Console.WriteLine("Classic brake-turn authority probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string name,
        float preBrake,
        float turnBrake)
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

        int preTicks = SecondsToTicks(BrakePhaseSeconds);
        for (int i = 0; i < preTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, preBrake, 0f, brakeAssistEnabled: true), Dt);
        }

        float turnStartSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        int turnTicks = SecondsToTicks(TurnPhaseSeconds);
        int checkpointIndex = 0;
        for (int i = 1; i <= turnTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, turnBrake, SteerCommand, brakeAssistEnabled: true), Dt);
            float elapsed = i * Dt;
            if (checkpointIndex < CheckpointsSeconds.Length &&
                elapsed + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(name, CheckpointsSeconds[checkpointIndex], simulator.State, turnStartSpeedKmh);
                checkpointIndex++;
            }
        }
    }

    private static void PrintSample(string name, float elapsed, VehicleState state, float turnStartSpeedKmh)
    {
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float frontLateralForce = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float frontLongitudinalForce = state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN;
        float frontLateralUsage = state.ClassicFrontLateralGripUsage;
        float frontLongitudinalUsage = state.ClassicFrontLongitudinalGripUsage;
        float frontPressure = (state.FrontLeftBrakePressureRatio + state.FrontRightBrakePressureRatio) * 0.5f;
        float rearPressure = (state.RearLeftBrakePressureRatio + state.RearRightBrakePressureRatio) * 0.5f;
        int frontRegulatorsActive =
            (state.FrontLeftBrakePressureRegulatorActive ? 1 : 0) +
            (state.FrontRightBrakePressureRegulatorActive ? 1 : 0);
        int rearRegulatorsActive =
            (state.RearLeftBrakePressureRegulatorActive ? 1 : 0) +
            (state.RearRightBrakePressureRegulatorActive ? 1 : 0);
        float rearGrip = MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);
        float roadWheelAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float speedKmh = state.SpeedMetersPerSecond * 3.6f;

        Console.WriteLine(
            $"  {name,-13} {elapsed,4:F2} {speedKmh,6:F1} {roadWheelAngle,5:F2} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:F1} {state.ClassicBodySlipAngleDegrees,6:F2} " +
            $"{frontSlip,6:F2}/{rearSlip,6:F2} " +
            $"{frontPressure,4:F2}/{rearPressure,4:F2} {frontRegulatorsActive,1}/{rearRegulatorsActive,1} " +
            $"{frontLateralForce,8:F0}/{frontLongitudinalForce,8:F0} " +
            $"{frontLateralUsage,5:F2}/{frontLongitudinalUsage,5:F2} " +
            $"{rearGrip,6:F2} {state.LateralAcceleration / 9.81f,5:F2} " +
            $"{state.LongitudinalAcceleration / 9.81f,5:F2} {turnStartSpeedKmh - speedKmh,6:F2}");
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
