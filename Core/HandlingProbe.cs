using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class HandlingProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        RunSet(parameters, engineParameters, 7.0f);
        RunSet(parameters, engineParameters, 16.7f);
        RunSet(parameters, engineParameters, 27.8f);
    }

    private static void RunSet(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float targetSpeedMetersPerSecond)
    {
        RunCase(parameters, engineParameters, targetSpeedMetersPerSecond, "coast", new VehicleInput(0f, 0f, 0.65f));
        RunCase(parameters, engineParameters, targetSpeedMetersPerSecond, "maintenance throttle", new VehicleInput(0.22f, 0f, 0.65f));
        RunCase(parameters, engineParameters, targetSpeedMetersPerSecond, "full throttle", new VehicleInput(1f, 0f, 0.65f));
        RunCase(parameters, engineParameters, targetSpeedMetersPerSecond, "digital full throttle assist", new VehicleInput(1f, 0f, 0.65f, throttleAssistEnabled: true));
        RunCase(parameters, engineParameters, targetSpeedMetersPerSecond, "braking turn", new VehicleInput(0f, 0.58f, 0.65f));
        RunCase(parameters, engineParameters, targetSpeedMetersPerSecond, "digital braking turn", new VehicleInput(0f, 0.58f, 0.65f, brakeAssistEnabled: true));
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float targetSpeedMetersPerSecond,
        string label,
        VehicleInput cornerInput)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, targetSpeedMetersPerSecond, dt);

        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float startHeading = simulator.State.HeadingRadians;
        float peakLatG = 0f;
        float peakYawRateDeg = 0f;
        float peakBodyRollDeg = 0f;
        float endFrontGripUsage = 0f;
        float endRearGripUsage = 0f;
        float endEffectiveThrottle = 0f;
        float peakLsdBite = 0f;
        float peakManagedFrontTorque = 0f;

        for (int i = 0; i < 144; i++)
        {
            simulator.Update(cornerInput, dt);
            VehicleState state = simulator.State;
            peakLatG = MathF.Max(peakLatG, MathF.Abs(state.LateralAcceleration) / 9.81f);
            peakYawRateDeg = MathF.Max(peakYawRateDeg, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
            peakBodyRollDeg = MathF.Max(peakBodyRollDeg, MathF.Abs(MathHelper.ToDegrees(state.BodyRollRadians - state.GroundRollRadians)));
            endFrontGripUsage = (state.FrontLeftGripUsage + state.FrontRightGripUsage) * 0.5f;
            endRearGripUsage = (state.RearLeftGripUsage + state.RearRightGripUsage) * 0.5f;
            endEffectiveThrottle = state.EffectiveThrottle;
            peakLsdBite = MathF.Max(peakLsdBite, state.FfLsdCornerExitBite);
            peakManagedFrontTorque = MathF.Max(peakManagedFrontTorque, state.FfLsdManagedFrontAxleTorqueNm);
        }

        VehicleState end = simulator.State;
        float headingChangeDeg = MathHelper.ToDegrees(MathHelper.WrapAngle(end.HeadingRadians - startHeading));
        Console.WriteLine(
            $"{parameters.DisplayName} target {targetSpeedMetersPerSecond * 3.6f:0} km/h {label}: {startSpeed * 3.6f:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h, " +
            $"heading {headingChangeDeg:0.0} deg, peak yaw {peakYawRateDeg:0.0} deg/s, peak lat {peakLatG:0.00} g, " +
            $"peak roll {peakBodyRollDeg:0.00} deg, grip F/R {endFrontGripUsage:0.00}/{endRearGripUsage:0.00}, eff thr {endEffectiveThrottle:0.00}, " +
            $"lsd bite {peakLsdBite:0.00}, managed front {peakManagedFrontTorque:0}Nm, gear {end.Gear}, rpm {end.Rpm:0}");
    }

    private static void DriveToSpeed(SimpleVehicleSimulator simulator, float targetSpeedMetersPerSecond, float dt)
    {
        for (int i = 0; i < 3600 && simulator.State.SpeedMetersPerSecond < targetSpeedMetersPerSecond; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        for (int i = 0; i < 30; i++)
        {
            simulator.Update(new VehicleInput(0.2f, 0f, 0f), dt);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }
    }
}
