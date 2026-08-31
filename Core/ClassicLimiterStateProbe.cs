using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicLimiterStateProbe
{
    private const float Dt = 1f / 120f;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic limiter state probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine(
            $"  limiter cut={parameters.LimiterHardCutRpm:0}rpm resume={parameters.RevLimiterResumeRpm:0}rpm " +
            $"minCut={parameters.RevLimiterFuelCutSeconds:0.000}s restore={parameters.RevLimiterRestoreSeconds:0.000}s " +
            $"cutMult={parameters.RevLimiterCutTorqueMultiplier:0.00}");
        Console.WriteLine(
            "  case event t speed gear wheelRpm physicsRpm displayRpm audioRpm limiter cutT restoreT mult engNm wheelN clutch locked");

        RunLaunch(parameters, engine);
        RunLaunchAfterLimiterHold(parameters, engine);
        RunHoldGearLimiter(parameters, engine, gear: 2);
        RunLiftAndReapply(parameters, engine, gear: 2);
        RunShiftAfterLimiterContact(parameters, engine, gear: 2);

        Console.WriteLine("Classic limiter state probe complete.");
    }

    private static void RunLaunch(VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, gear: 1, speedKmh: 0f);
        RunTimedCase("launch", simulator, 4.0f, (_, _) => new VehicleInput(1f, 0f, 0f));
    }

    private static void RunLaunchAfterLimiterHold(VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, gear: 1, speedKmh: 0f);
        for (int i = 0; i < SecondsToTicks(1.8f); i++)
        {
            simulator.UpdateRaceStartHold(new VehicleInput(1f, 0f, 0f), Dt);
            RpmPresentationSmoother.Update(simulator.State, Dt);
        }

        PrintSample("launch-after-hold", "pre", 0f, simulator.State);
        RunTimedCase("launch-after-hold", simulator, 3.0f, (_, _) => new VehicleInput(1f, 0f, 0f));
    }

    private static void RunHoldGearLimiter(VehicleSimulationParameters parameters, SimulationEngineParameters engine, int gear)
    {
        float startSpeed = MathF.Max(5f, SpeedKmhAtRpm(parameters, gear, parameters.LimiterHardCutRpm) - 10f);
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, gear, startSpeed);
        RunTimedCase($"gear{gear}-hold", simulator, 3.0f, (_, _) => new VehicleInput(1f, 0f, 0f));
    }

    private static void RunLiftAndReapply(VehicleSimulationParameters parameters, SimulationEngineParameters engine, int gear)
    {
        float startSpeed = MathF.Max(5f, SpeedKmhAtRpm(parameters, gear, parameters.LimiterHardCutRpm) - 6f);
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, gear, startSpeed);
        bool touchedLimiter = false;
        float liftStart = -1f;
        RunTimedCase(
            $"gear{gear}-lift-reapply",
            simulator,
            3.2f,
            (state, elapsed) =>
            {
                if (!touchedLimiter && state.RevLimiterActive)
                {
                    touchedLimiter = true;
                    liftStart = elapsed + 0.15f;
                }

                bool lifting = liftStart >= 0f && elapsed >= liftStart && elapsed < liftStart + 0.35f;
                return new VehicleInput(lifting ? 0f : 1f, 0f, 0f);
            });
    }

    private static void RunShiftAfterLimiterContact(VehicleSimulationParameters parameters, SimulationEngineParameters engine, int gear)
    {
        float startSpeed = MathF.Max(5f, SpeedKmhAtRpm(parameters, gear, parameters.LimiterHardCutRpm) - 6f);
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine, gear, startSpeed);
        bool shiftQueued = false;
        bool shifted = false;
        RunTimedCase(
            $"gear{gear}-shift-after-contact",
            simulator,
            3.2f,
            (state, _) =>
            {
                if (!shiftQueued && state.RevLimiterActive)
                {
                    shiftQueued = true;
                    return new VehicleInput(1f, 0f, 0f, shiftUpRequested: true);
                }

                if (shiftQueued && !shifted && state.Gear > gear)
                {
                    shifted = true;
                }

                return new VehicleInput(1f, 0f, 0f);
            });
    }

    private static void RunTimedCase(
        string name,
        ClassicFourWheelVehicleSimulator simulator,
        float seconds,
        Func<VehicleState, float, VehicleInput> inputAt)
    {
        bool previousLimiter = simulator.State.RevLimiterActive;
        int previousGear = simulator.State.Gear;
        float nextRegularPrint = 0f;
        int ticks = SecondsToTicks(seconds);
        for (int i = 0; i < ticks; i++)
        {
            float elapsed = (i + 1) * Dt;
            VehicleInput input = inputAt(simulator.State, elapsed);
            simulator.Update(input, Dt);
            RpmPresentationSmoother.Update(simulator.State, Dt);

            VehicleState state = simulator.State;
            bool limiterChanged = state.RevLimiterActive != previousLimiter;
            bool gearChanged = state.Gear != previousGear;
            bool regularPrint = elapsed + 0.0001f >= nextRegularPrint;
            if (regularPrint || limiterChanged || gearChanged)
            {
                string evt = limiterChanged
                    ? state.RevLimiterActive ? "cut-on" : "cut-off"
                    : gearChanged ? "shift"
                    : "sample";
                PrintSample(name, evt, elapsed, state);
                previousLimiter = state.RevLimiterActive;
                previousGear = state.Gear;
                if (regularPrint)
                {
                    nextRegularPrint += 0.10f;
                }
            }
        }

        PrintSample(name, "end", seconds, simulator.State);
    }

    private static void PrintSample(string name, string evt, float elapsed, VehicleState state)
    {
        Console.WriteLine(
            $"  {name,-25} {evt,-7} {elapsed,5:0.000} {state.SpeedMetersPerSecond * 3.6f,6:0.0} {state.Gear,4} " +
            $"{state.RevLimiterWheelImpliedRpm,8:0} {state.Rpm,10:0} {state.DisplayedRpm,10:0} {state.EnginePowerUnitCrankRpm,8:0} " +
            $"{state.RevLimiterActive,-7} {state.RevLimiterCutTimerSeconds,5:0.000} {state.RevLimiterRestoreTimerSeconds,8:0.000} " +
            $"{state.LimiterTorqueMultiplier,5:0.00} {state.RevLimiterEngineTorqueNm,6:0} {state.RevLimiterDeliveredWheelForceN,7:0} " +
            $"{state.ClutchEngagement,6:0.00} {state.ClutchIsLocked}");
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        int gear,
        float speedKmh)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Math.Clamp(gear, 1, parameters.ForwardGearRatios.Length);
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        return simulator;
    }

    private static float SpeedKmhAtRpm(VehicleSimulationParameters parameters, int gear, float rpm)
    {
        float ratio = parameters.ForwardGearRatios[Math.Clamp(gear, 1, parameters.ForwardGearRatios.Length) - 1];
        float wheelRpm = rpm / MathF.Max(0.001f, ratio * parameters.FinalDriveRatio);
        return wheelRpm / 60f * MathF.Tau * parameters.WheelRadiusMeters * 3.6f;
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
