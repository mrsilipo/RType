using Microsoft.Xna.Framework;
using RType.Audio;
using RType.Camera;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ThrottleReleaseProbe
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
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        for (int i = 0; i < 900; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            if (simulator.State.Rpm > parameters.PowerRedlineRpm * 0.94f &&
                simulator.State.SpeedMetersPerSecond > 20f)
            {
                break;
            }
        }

        Console.WriteLine(
            $"release-start rpm={simulator.State.Rpm:0} disp={simulator.State.DisplayedRpm:0} " +
            $"speed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0} throttle={simulator.State.Throttle:0.00} eff={simulator.State.EffectiveThrottle:0.00}");

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);

            if (i < 12 || i % 20 == 19)
            {
                VehicleState state = simulator.State;
                EngineAudioFrame frame = EngineAudioFrame.FromVehicleState(
                    parameters.Audio,
                    state,
                    state.Rpm,
                    state.EnginePowerUnitVtecBlend,
                    parameters.Audio.EngineVolume,
                    CameraMode.Chase1,
                    paused: false,
                    throttleTransient: 0f,
                    dt);
                Console.WriteLine(
                    $"release t={(i + 1) * dt:0.000} rpm={state.Rpm:0} disp={state.DisplayedRpm:0} " +
                    $"speed={state.SpeedMetersPerSecond * 3.6f:0.0} throttle={state.Throttle:0.00} eff={state.EffectiveThrottle:0.00} " +
                    $"audioThr={frame.Throttle:0.00} shaped={frame.ShapedThrottle:0.00} load={frame.Load:0.00} overrun={frame.Overrun:0.00} " +
                    $"clutch={state.ClutchEngagement:0.00} locked={state.ClutchIsLocked}");
            }
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
