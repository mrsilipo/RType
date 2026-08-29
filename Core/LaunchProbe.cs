using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class LaunchProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 60f;
        for (int i = 0; i < 360; i++)
        {
            simulator.UpdateRaceStartHold(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            if (i < 20 || i % 20 == 19)
            {
                Console.WriteLine($"hold t={(i + 1) * dt:0.000} rpm={simulator.State.Rpm:0} disp={simulator.State.DisplayedRpm:0}");
            }
        }

        Console.WriteLine(
            $"pre rpm={simulator.State.Rpm:0} disp={simulator.State.DisplayedRpm:0} speed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0} " +
            $"slip={simulator.State.ClutchSlipRpm:0} clutch={simulator.State.ClutchEngagement:0.00} locked={simulator.State.ClutchIsLocked}");
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            if (i < 12 || i % 10 == 9)
            {
                VehicleState state = simulator.State;
                float roadRpm = CalculateRoadCoupledRpm(parameters, state);
                Console.WriteLine(
                    $"t={(i + 1) * dt:0.000} rpm={state.Rpm:0} disp={state.DisplayedRpm:0} speed={state.SpeedMetersPerSecond * 3.6f:0.0} " +
                    $"roadRpm={roadRpm:0} gbRpm={state.GearboxInputOmegaRadiansPerSecond * (60f / MathF.Tau):0} gear={state.Gear} " +
                    $"clutchSlip={state.ClutchSlipRpm:0} clutch={state.ClutchEngagement:0.00} locked={state.ClutchIsLocked} clutchNm={state.ActiveClutchTorqueNm:0} effThr={state.EffectiveThrottle:0.00} " +
                    $"slipFL/FR={state.FrontLeftSlipRatio:0.00}/{state.FrontRightSlipRatio:0.00} relaxedFL/FR={state.FrontLeftRelaxedLongitudinalSlipRatio:0.00}/{state.FrontRightRelaxedLongitudinalSlipRatio:0.00} " +
                    $"ellipseFL={state.FrontLeftFrictionEllipseLongitudinalForceN:0}N/{state.FrontLeftFrictionEllipseGripUsage:0.00} avgSlip={state.AverageSlipRatio:0.00} " +
                    $"drive={state.DriveForce / 1000f:0.0}kN");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"clutch data capacity={parameters.ClutchTorqueCapacityNm:0}Nm bite={parameters.ClutchEngagementPoint:0.00} " +
            $"assist={parameters.ClutchLowSpeedAssistStrength:0.00} biteStart={parameters.ClutchBiteInputStartMultiplier:0.00} exponent={parameters.ClutchLaunchAssistExponent:0.00} " +
            $"throttleAssist={parameters.ClutchLowSpeedThrottleAssist:0.00} torqueAssist={parameters.ClutchLowSpeedTorqueAssistNm:0}Nm rollingLock={parameters.ClutchRollingLockSpeedMetersPerSecond:0.00}m/s/{parameters.ClutchRollingLockSlipRadiansPerSecond:0}rad/s");
        MeasurePartialPullAway(parameters, engineParameters, 0.10f);
        MeasurePartialPullAway(parameters, engineParameters, 0.25f);
        MeasurePartialPullAway(parameters, engineParameters, 0.35f);
        MeasurePartialPullAway(parameters, engineParameters, 0.50f);
    }

    private static void MeasurePartialPullAway(VehicleSimulationParameters parameters, SimulationEngineParameters engineParameters, float throttle)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 60f;
        float halfSecondSpeed = 0f;
        float oneSecondSpeed = 0f;
        float maxEngagement = 0f;
        float maxDriveForce = 0f;
        for (int i = 0; i < 120; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, 0f, throttleAssistEnabled: true), dt);
            if (i == 29)
            {
                halfSecondSpeed = simulator.State.SpeedMetersPerSecond;
            }

            if (i == 59)
            {
                oneSecondSpeed = simulator.State.SpeedMetersPerSecond;
            }

            maxEngagement = MathF.Max(maxEngagement, simulator.State.ClutchEngagement);
            maxDriveForce = MathF.Max(maxDriveForce, simulator.State.DriveForce);
        }

        Console.WriteLine(
            $"partial throttle={throttle:0.00} 0.5s={halfSecondSpeed * 3.6f:0.0}kph 1.0s={oneSecondSpeed * 3.6f:0.0}kph " +
            $"2.0s={simulator.State.SpeedMetersPerSecond * 3.6f:0.0}kph maxClutch={maxEngagement:0.00} maxDrive={maxDriveForce / 1000f:0.0}kN rpm={simulator.State.Rpm:0}");
    }

    private static float CalculateRoadCoupledRpm(VehicleSimulationParameters parameters, VehicleState state)
    {
        if (state.Gear <= 0 || state.Gear > parameters.ForwardGearRatios.Length)
        {
            return 0f;
        }

        float radius = MathF.Max(0.1f, parameters.FrontTyres.LoadedRadiusMeters);
        float wheelRpm = state.SpeedMetersPerSecond / radius / MathF.Tau * 60f;
        return wheelRpm * parameters.ForwardGearRatios[state.Gear - 1] * parameters.FinalDriveRatio;
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }
    }
}
