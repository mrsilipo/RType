using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ShiftProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 60f;
        int lastGear = simulator.State.Gear;
        bool lastShifting = simulator.State.IsShifting;
        float lastRpm = simulator.State.Rpm;
        float lastDisplayedRpm = simulator.State.DisplayedRpm;
        int rowsAfterShift = 0;

        for (int i = 0; i < 1800; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            VehicleState state = simulator.State;

            bool print = state.Gear != lastGear ||
                         state.IsShifting != lastShifting ||
                         state.IsShifting ||
                         rowsAfterShift > 0;

            if (state.IsShifting && !lastShifting)
            {
                rowsAfterShift = 18;
            }
            else if (!state.IsShifting && lastShifting)
            {
                rowsAfterShift = 24;
            }
            else if (rowsAfterShift > 0)
            {
                rowsAfterShift--;
            }

            if (print)
            {
                Console.WriteLine(
                    $"t={(i + 1) * dt:0.000} gear={state.Gear} shift={state.IsShifting} " +
                    $"rpm={state.Rpm:0} drpm={state.Rpm - lastRpm:0} " +
                    $"disp={state.DisplayedRpm:0} ddisp={state.DisplayedRpm - lastDisplayedRpm:0} " +
                    $"speed={state.SpeedMetersPerSecond * 3.6f:0.0} effThr={state.EffectiveThrottle:0.00} " +
                    $"kick={state.ShiftKickIntensity:0.00} shock={state.PowertrainShockIntensity:0.00} slip={state.AverageSlipRatio:0.00}");
            }

            lastGear = state.Gear;
            lastShifting = state.IsShifting;
            lastRpm = state.Rpm;
            lastDisplayedRpm = state.DisplayedRpm;
        }

        ProbeManualOverRevDownshift(parameters, engineParameters);
    }

    private static void ProbeManualOverRevDownshift(VehicleSimulationParameters parameters, SimulationEngineParameters engineParameters)
    {
        if (parameters.ForwardGearRatios.Length < 3)
        {
            return;
        }

        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 60f;
        float secondGearRatio = parameters.ForwardGearRatios[1];
        float targetOverRevRpm = parameters.RedlineRpm + parameters.DownshiftOverRevToleranceRpm + 650f;
        float targetSpeedMetersPerSecond =
            targetOverRevRpm /
            MathF.Max(0.1f, secondGearRatio * parameters.FinalDriveRatio) *
            MathF.Tau *
            MathF.Max(0.05f, parameters.WheelRadiusMeters) /
            60f;
        targetSpeedMetersPerSecond = MathHelper.Clamp(targetSpeedMetersPerSecond, 30f, 44f);

        for (int i = 0; i < 1800 && simulator.State.SpeedMetersPerSecond < targetSpeedMetersPerSecond; i++)
        {
            bool shiftUp = !simulator.State.IsShifting &&
                           simulator.State.Gear > 0 &&
                           simulator.State.Gear < parameters.ForwardGearRatios.Length &&
                           simulator.State.Rpm >= parameters.UpshiftRpm - 250f;
            simulator.Update(new VehicleInput(1f, 0f, 0f, shiftUpRequested: shiftUp, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
        }

        simulator.State.Gear = 3;
        float predictedSecondGearRpm =
            simulator.State.SpeedMetersPerSecond /
            MathF.Max(0.05f, parameters.WheelRadiusMeters) /
            MathF.Tau *
            60f *
            secondGearRatio *
            parameters.FinalDriveRatio;
        Console.WriteLine(
            $"manual over-rev setup speed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0} " +
            $"gear={simulator.State.Gear} predicted2={predictedSecondGearRpm:0} limiter={parameters.RedlineRpm:0}");

        simulator.Update(new VehicleInput(0f, 0f, 0f, shiftDownRequested: true), dt);
        float previousSpeed = simulator.State.SpeedMetersPerSecond;
        for (int i = 0; i < 96; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            VehicleState state = simulator.State;
            bool print = state.IsShifting || state.MechanicalOverRevActive || i % 6 == 0;
            if (!print)
            {
                previousSpeed = state.SpeedMetersPerSecond;
                continue;
            }

            Console.WriteLine(
                $"down t={(i + 2) * dt:0.000} gear={state.Gear} shift={state.IsShifting} " +
                $"rpm={state.Rpm:0} disp={state.DisplayedRpm:0} speed={state.SpeedMetersPerSecond * 3.6f:0.0} " +
                $"dv={(previousSpeed - state.SpeedMetersPerSecond) * 3.6f:0.00}kph " +
                $"ebrake={state.EngineBrakeTorqueNm:0} over={state.MechanicalOverRevRpm:0}/{state.MechanicalOverRevSeverity:0.00} " +
                $"shock={state.PowertrainShockIntensity:0.00} lim={state.RevLimiterBounceIntensity:0.00} " +
                $"slip={state.AverageSlipRatio:0.00}");
            previousSpeed = state.SpeedMetersPerSecond;
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
