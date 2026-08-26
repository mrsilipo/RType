using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class FreeRevProbe
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
        const float dt = 1f / 120f;
        float previousRpm = simulator.State.Rpm;
        float maximumRiseRate = 0f;
        float vtecToLimiterSeconds = 0f;
        bool enteredVtec = false;
        bool hitLimiter = false;

        Console.WriteLine(
            $"{parameters.DisplayName} free-rev: idle={parameters.IdleRpm:0}rpm, " +
            $"vtec={(parameters.VtecEnabled ? parameters.VtecActivationRpm.ToString("0") : "off")}, " +
            $"limiter={parameters.LimiterHardCutRpm:0}rpm, inertia={parameters.EngineRotationalInertiaKgM2:0.000}kgm2, " +
            $"riseCap={parameters.MaxFreeRevRiseRpmPerSecond:0}rpm/s, fallCap={parameters.MaxFreeRevFallRpmPerSecond:0}rpm/s");

        for (int i = 0; i < 420; i++)
        {
            simulator.UpdateRaceStartHold(new VehicleInput(1f, 0f, 0f), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);

            VehicleState state = simulator.State;
            float rpmPerSecond = (state.Rpm - previousRpm) / dt;
            maximumRiseRate = MathF.Max(maximumRiseRate, rpmPerSecond);
            previousRpm = state.Rpm;

            if (!enteredVtec && parameters.VtecEnabled && state.Rpm >= parameters.VtecActivationRpm)
            {
                enteredVtec = true;
            }

            if (enteredVtec && !hitLimiter)
            {
                vtecToLimiterSeconds += dt;
            }

            if (state.RevLimiterActive || state.Rpm >= parameters.LimiterHardCutRpm - 2f)
            {
                hitLimiter = true;
            }

            if (i < 12 || i % 12 == 11 || hitLimiter)
            {
                float vtecBlend = parameters.VtecEnabled && parameters.VtecTransitionWidthRpm > 0f
                    ? MathHelper.Clamp((state.Rpm - parameters.VtecActivationRpm) / parameters.VtecTransitionWidthRpm, 0f, 1f)
                    : 0f;
                float resolvedTorque = parameters.TorqueAtRpm(state.Rpm) * state.Throttle * state.LimiterTorqueMultiplier;
                Console.WriteLine(
                    $"t={(i + 1) * dt:0.000}s rpm={state.Rpm:0} disp={state.DisplayedRpm:0} " +
                    $"rise={rpmPerSecond:0}rpm/s vtec={vtecBlend:0.00} " +
                    $"limiter={state.RevLimiterActive} gear={state.Gear} torque={resolvedTorque:0.0}Nm " +
                    $"fuelCut={state.EnginePowerUnitFuelCutBlend:0.00}");
            }

            if (hitLimiter && i > 12)
            {
                break;
            }
        }

        Console.WriteLine(
            $"summary maxRise={maximumRiseRate:0}rpm/s, " +
            $"vtecToLimiter={(enteredVtec ? vtecToLimiterSeconds.ToString("0.000") : "n/a")}s, " +
            $"hitLimiter={hitLimiter}");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }
    }
}
