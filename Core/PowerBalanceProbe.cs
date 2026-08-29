using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class PowerBalanceProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Power balance probe: {parameters.DisplayName}");
        Console.WriteLine($"mass={parameters.MassKg:0}kg powerRedline={parameters.PowerRedlineRpm:0} limiter={parameters.LimiterHardCutRpm:0} dragFactor={parameters.AeroDragFactor:0.000}");

        foreach (float speed in new[] { 33.3f, 38.9f, 44.4f, 50.0f })
        {
            RunCase(parameters, engineParameters, speed, 0f, "straight");
            RunCase(parameters, engineParameters, speed, 0.18f, "light corner");
            RunCase(parameters, engineParameters, speed, 0.32f, "committed corner");
        }

        Console.WriteLine("Locked 3rd-gear rolling cases:");
        foreach (float speed in new[] { 27.8f, 33.3f, 38.9f, 42.0f })
        {
            RunLockedGearCase(parameters, engineParameters, 3, speed, 0f, "straight");
            RunLockedGearCase(parameters, engineParameters, 3, speed, 0.18f, "light corner");
            RunLockedGearCase(parameters, engineParameters, 3, speed, 0.32f, "committed corner");
        }

        Console.WriteLine("Manual shift into 3rd cases:");
        RunManualThirdCase(parameters, engineParameters, 0f, "straight");
        RunManualThirdCase(parameters, engineParameters, 0.18f, "light corner");
        RunManualThirdCase(parameters, engineParameters, 0.32f, "committed corner");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float targetSpeedMetersPerSecond,
        float steer,
        string label)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, targetSpeedMetersPerSecond, dt);

        // Stabilize at the target speed with light throttle before the measured sample.
        for (int i = 0; i < 60; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, 0f), dt);
        }

        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float sumLongForce = 0f;
        float sumLatForce = 0f;
        float sumLongAccel = 0f;
        float sumThrottle = 0f;
        float sumFrontDriveTorque = 0f;
        float peakGripUsage = 0f;

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, steer), dt);
            VehicleState state = simulator.State;
            sumLongForce +=
                state.FrontLeftLongitudinalForceN +
                state.FrontRightLongitudinalForceN +
                state.RearLeftLongitudinalForceN +
                state.RearRightLongitudinalForceN;
            sumLatForce +=
                state.FrontLeftLateralForceN +
                state.FrontRightLateralForceN +
                state.RearLeftLateralForceN +
                state.RearRightLateralForceN;
            sumLongAccel += state.LongitudinalAcceleration;
            sumThrottle += state.EffectiveThrottle;
            sumFrontDriveTorque += state.FrontDifferentialLeftActualTorqueNm + state.FrontDifferentialRightActualTorqueNm;
            peakGripUsage = MathF.Max(peakGripUsage, MathF.Max(
                MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
                MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage)));
        }

        VehicleState end = simulator.State;
        float inv = 1f / 180f;
        Console.WriteLine(
            $"{targetSpeedMetersPerSecond * 3.6f,3:0} km/h {label,-16} " +
            $"{startSpeed * 3.6f:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h " +
            $"gear={end.Gear} rpm={end.Rpm:0} effThr={sumThrottle * inv:0.00} " +
            $"avgLongForce={sumLongForce * inv:0}N avgLatForce={sumLatForce * inv:0}N " +
            $"avgLongAccel={sumLongAccel * inv:0.00}m/s2 frontDrive={sumFrontDriveTorque * inv:0}Nm gripPeak={peakGripUsage:0.00}");
    }

    private static void DriveToSpeed(SimpleVehicleSimulator simulator, float targetSpeedMetersPerSecond, float dt)
    {
        for (int i = 0; i < 7200 && simulator.State.SpeedMetersPerSecond < targetSpeedMetersPerSecond; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }
    }

    private static void RunLockedGearCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        int gear,
        float targetSpeedMetersPerSecond,
        float steer,
        string label)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = gear;
        simulator.State.Velocity = new Vector2(0f, targetSpeedMetersPerSecond);

        const float dt = 1f / 120f;
        for (int i = 0; i < 45; i++)
        {
            simulator.Update(new VehicleInput(0.35f, 0f, 0f), dt);
        }

        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float sumLongForce = 0f;
        float sumBodyAccel = 0f;
        float sumFrontDriveTorque = 0f;
        float sumRpm = 0f;
        float maxSpeed = startSpeed;

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, steer), dt);
            VehicleState state = simulator.State;
            sumLongForce +=
                state.FrontLeftLongitudinalForceN +
                state.FrontRightLongitudinalForceN +
                state.RearLeftLongitudinalForceN +
                state.RearRightLongitudinalForceN;
            sumBodyAccel += state.LongitudinalAcceleration;
            sumFrontDriveTorque += state.FrontDifferentialLeftActualTorqueNm + state.FrontDifferentialRightActualTorqueNm;
            sumRpm += state.Rpm;
            maxSpeed = MathF.Max(maxSpeed, state.SpeedMetersPerSecond);
        }

        VehicleState end = simulator.State;
        float inv = 1f / 180f;
        Console.WriteLine(
            $"gear {gear} {targetSpeedMetersPerSecond * 3.6f,3:0} km/h {label,-16} " +
            $"{startSpeed * 3.6f:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h max={maxSpeed * 3.6f:0.0} " +
            $"rpmAvg={sumRpm * inv:0} rpmEnd={end.Rpm:0} " +
            $"avgLongForce={sumLongForce * inv:0}N avgLongAccel={sumBodyAccel * inv:0.00}m/s2 frontDrive={sumFrontDriveTorque * inv:0}Nm");
    }

    private static void RunManualThirdCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float steer,
        string label)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        int shiftCooldown = 0;
        for (int i = 0; i < 3000 && simulator.State.Gear < 3; i++)
        {
            bool requestShift = false;
            if (shiftCooldown <= 0 &&
                simulator.State.Gear > 0 &&
                simulator.State.Gear < 3 &&
                simulator.State.Rpm >= parameters.PowerRedlineRpm - 250f)
            {
                requestShift = true;
                shiftCooldown = 42;
            }

            simulator.Update(new VehicleInput(1f, 0f, 0f, shiftUpRequested: requestShift), dt);
            shiftCooldown = Math.Max(0, shiftCooldown - 1);
        }

        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float startRpm = simulator.State.Rpm;
        float sumLongForce = 0f;
        float sumBodyAccel = 0f;
        float sumFrontDriveTorque = 0f;
        float sumRpm = 0f;
        float minRpm = float.MaxValue;
        float maxRpm = 0f;
        float maxSpeed = startSpeed;

        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, steer), dt);
            VehicleState state = simulator.State;
            float wheelLongForce =
                state.FrontLeftLongitudinalForceN +
                state.FrontRightLongitudinalForceN +
                state.RearLeftLongitudinalForceN +
                state.RearRightLongitudinalForceN;
            sumLongForce += wheelLongForce;
            sumBodyAccel += state.LongitudinalAcceleration;
            sumFrontDriveTorque += state.FrontDifferentialLeftActualTorqueNm + state.FrontDifferentialRightActualTorqueNm;
            sumRpm += state.Rpm;
            minRpm = MathF.Min(minRpm, state.Rpm);
            maxRpm = MathF.Max(maxRpm, state.Rpm);
            maxSpeed = MathF.Max(maxSpeed, state.SpeedMetersPerSecond);
        }

        VehicleState end = simulator.State;
        float inv = 1f / 240f;
        Console.WriteLine(
            $"manual 3rd {label,-16} {startSpeed * 3.6f:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h max={maxSpeed * 3.6f:0.0} " +
            $"startRpm={startRpm:0} rpmAvg={sumRpm * inv:0} rpmMinMax={minRpm:0}/{maxRpm:0} gear={end.Gear} " +
            $"avgLongForce={sumLongForce * inv:0}N avgLongAccel={sumBodyAccel * inv:0.00}m/s2 frontDrive={sumFrontDriveTorque * inv:0}Nm");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
