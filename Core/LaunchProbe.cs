using Microsoft.Xna.Framework;
using RetroRacer.Data;
using RetroRacer.Vehicle;
using RetroRacer.World;

namespace RetroRacer.Core;

public static class LaunchProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
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

        Console.WriteLine($"pre rpm={simulator.State.Rpm:0} disp={simulator.State.DisplayedRpm:0} speed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0} slip={simulator.State.ClutchSlipRpm:0}");
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
                    $"roadRpm={roadRpm:0} gear={state.Gear} clutchSlip={state.ClutchSlipRpm:0} effThr={state.EffectiveThrottle:0.00} " +
                    $"slipFL/FR={state.FrontLeftSlipRatio:0.00}/{state.FrontRightSlipRatio:0.00} avgSlip={state.AverageSlipRatio:0.00} " +
                    $"drive={state.DriveForce / 1000f:0.0}kN");
            }
        }
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
