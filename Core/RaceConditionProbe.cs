using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class RaceConditionProbe
{
    private const string ShowroomStockBuildPath = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(ShowroomStockBuildPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(options.SurfaceDefinitionPath);

        ProbeLimiterPresentation(parameters, engineParameters);
        ProbeVtecTransition(parameters, engineParameters);
        ProbeBankingGravity(parameters, engineParameters);
        ProbeCurbGrassProgression(parameters, engineParameters, surfaces);
    }

    private static void ProbeLimiterPresentation(VehicleSimulationParameters parameters, SimulationEngineParameters engineParameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        bool limiterActive = false;
        float maximumActualRpm = 0f;
        float minimumDisplayedRpm = float.MaxValue;
        float maximumDisplayedRpm = 0f;
        float maximumSpeedDisplayError = 0f;
        float minimumLimiterTorqueMultiplier = 1f;
        float fuelCutWhileLimited = 0f;
        for (int i = 0; i < 3600; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            RaceEnginePresentationBridge.ApplyAudioState(simulator.State, parameters, dt);

            VehicleState state = simulator.State;
            if (state.RevLimiterActive)
            {
                limiterActive = true;
                fuelCutWhileLimited = MathF.Max(fuelCutWhileLimited, state.EnginePowerUnitFuelCutBlend);
                minimumLimiterTorqueMultiplier = MathF.Min(minimumLimiterTorqueMultiplier, state.LimiterTorqueMultiplier);
                maximumActualRpm = MathF.Max(maximumActualRpm, state.Rpm);
                minimumDisplayedRpm = MathF.Min(minimumDisplayedRpm, state.DisplayedRpm);
                maximumDisplayedRpm = MathF.Max(maximumDisplayedRpm, state.DisplayedRpm);
                maximumSpeedDisplayError = MathF.Max(
                    maximumSpeedDisplayError,
                    MathF.Abs(state.DisplayedSpeedMetersPerSecond - MathF.Abs(state.SignedForwardSpeed)));
            }
        }

        if (!limiterActive)
        {
            throw new InvalidOperationException("Race condition probe failed: limiter never activated during full-throttle race run.");
        }

        if (fuelCutWhileLimited < 0.99f || minimumLimiterTorqueMultiplier > 0.001f)
        {
            throw new InvalidOperationException($"Race condition probe failed: limiter did not hold hard cut. FuelCut {fuelCutWhileLimited:0.00}, torque multiplier {minimumLimiterTorqueMultiplier:0.000}.");
        }

        if (maximumActualRpm > parameters.LimiterHardCutRpm + 0.5f)
        {
            throw new InvalidOperationException($"Race condition probe failed: actual RPM exceeded hard cut. Actual {maximumActualRpm:0}, cut {parameters.LimiterHardCutRpm:0}.");
        }

        if (maximumDisplayedRpm > parameters.LimiterHardCutRpm + 0.5f)
        {
            throw new InvalidOperationException($"Race condition probe failed: displayed RPM exceeded hard cut. Display {maximumDisplayedRpm:0}, cut {parameters.LimiterHardCutRpm:0}.");
        }

        float minimumAllowedDisplayedRpm = parameters.LimiterHardCutRpm -
                                          RevLimiterPresentationRules.CalculateBounceDepthRpm(parameters.LimiterHardCutRpm) * 0.62f;
        if (minimumDisplayedRpm < minimumAllowedDisplayedRpm)
        {
            throw new InvalidOperationException($"Race condition probe failed: displayed RPM fell too far during limiter. Min {minimumDisplayedRpm:0}, expected >= {minimumAllowedDisplayedRpm:0}.");
        }

        if (maximumSpeedDisplayError > 0.001f)
        {
            throw new InvalidOperationException($"Race condition probe failed: speedometer detached from ground speed. Error {maximumSpeedDisplayError:0.0000} m/s.");
        }

        Console.WriteLine(
            $"limiter race sync: active={limiterActive}, cut={parameters.LimiterHardCutRpm:0}rpm, " +
            $"actualMax={maximumActualRpm:0}rpm, display={minimumDisplayedRpm:0}-{maximumDisplayedRpm:0}rpm, fuelCut={fuelCutWhileLimited:0.00}, " +
            $"torqueMultMin={minimumLimiterTorqueMultiplier:0.000}, speedDisplayError={maximumSpeedDisplayError:0.0000}m/s");
    }

    private static void ProbeVtecTransition(VehicleSimulationParameters parameters, SimulationEngineParameters engineParameters)
    {
        if (!parameters.VtecEnabled)
        {
            Console.WriteLine("vtec transition: skipped, vehicle has no VTEC");
            return;
        }

        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        float peakVtecBlend = 0f;
        float peakVtecKick = 0f;
        float speedAtFirstVtec = 0f;
        for (int i = 0; i < 1800; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            RpmPresentationSmoother.Update(simulator.State, dt);
            RaceEnginePresentationBridge.ApplyAudioState(simulator.State, parameters, dt);

            VehicleState state = simulator.State;
            peakVtecBlend = MathF.Max(peakVtecBlend, state.EnginePowerUnitVtecBlend);
            peakVtecKick = MathF.Max(peakVtecKick, state.EnginePowerUnitVtecKickIntensity);
            if (speedAtFirstVtec <= 0f && state.EnginePowerUnitVtecBlend > 0.5f)
            {
                speedAtFirstVtec = state.SpeedMetersPerSecond;
            }
        }

        if (peakVtecBlend < 0.85f || speedAtFirstVtec <= 0f)
        {
            throw new InvalidOperationException($"Race condition probe failed: VTEC blend did not engage under race throttle. Peak {peakVtecBlend:0.00}, speed {speedAtFirstVtec * 3.6f:0.0} km/h.");
        }

        Console.WriteLine(
            $"vtec transition: activation={parameters.VtecActivationRpm:0}rpm, peakBlend={peakVtecBlend:0.00}, " +
            $"peakKick={peakVtecKick:0.00}, firstVtecSpeed={speedAtFirstVtec * 3.6f:0.0}km/h");
    }

    private static void ProbeBankingGravity(VehicleSimulationParameters parameters, SimulationEngineParameters engineParameters)
    {
        SimpleVehicleSimulator banked = new(
            new RightSlopeElevationSampler(0.10f),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 16; i++)
        {
            banked.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        VehicleState state = banked.State;
        if (state.TrackLateralGravityForceN >= -300f || MathF.Abs(state.TrackLongitudinalGravityForceN) > 5f)
        {
            throw new InvalidOperationException($"Race condition probe failed: banking gravity force invalid. Lateral {state.TrackLateralGravityForceN:0.0}N, longitudinal {state.TrackLongitudinalGravityForceN:0.0}N.");
        }

        Console.WriteLine(
            $"banking gravity: roll={MathHelper.ToDegrees(state.TrackRollRadians):0.00}deg, " +
            $"latForce={state.TrackLateralGravityForceN:0}N, longForce={state.TrackLongitudinalGravityForceN:0}N");
    }

    private static void ProbeCurbGrassProgression(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        SurfaceLibrary surfaces)
    {
        SurfaceResult curb = RunSurfaceCase(parameters, engineParameters, surfaces.Curb);
        SurfaceResult blend50 = RunSurfaceCase(parameters, engineParameters, SurfaceSample.Blend("CURB_GRASS", surfaces.Curb, surfaces.Grass, 0.50f));
        SurfaceResult grass = RunSurfaceCase(parameters, engineParameters, surfaces.Grass);

        if (curb.FrontDragN > 1f)
        {
            throw new InvalidOperationException($"Race condition probe failed: dry curb has drag. Front drag {curb.FrontDragN:0.0}N.");
        }

        if (blend50.FrontDragN <= curb.FrontDragN || grass.FrontDragN <= blend50.FrontDragN)
        {
            throw new InvalidOperationException($"Race condition probe failed: curb-grass drag is not progressive. Curb {curb.FrontDragN:0.0}N, blend {blend50.FrontDragN:0.0}N, grass {grass.FrontDragN:0.0}N.");
        }

        if (blend50.FrontMu >= curb.FrontMu || blend50.FrontMu <= grass.FrontMu)
        {
            throw new InvalidOperationException($"Race condition probe failed: curb-grass mu is not between curb and grass. Curb {curb.FrontMu:0.00}, blend {blend50.FrontMu:0.00}, grass {grass.FrontMu:0.00}.");
        }

        Console.WriteLine(
            $"curb/grass progression: drag curb/blend/grass {curb.FrontDragN:0}/{blend50.FrontDragN:0}/{grass.FrontDragN:0}N, " +
            $"mu curb/blend/grass {curb.FrontMu:0.00}/{blend50.FrontMu:0.00}/{grass.FrontMu:0.00}, " +
            $"rumble L/R blend {blend50.LeftRumble:0.00}/{blend50.RightRumble:0.00}");
    }

    private static SurfaceResult RunSurfaceCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        SurfaceSample surface)
    {
        SimpleVehicleSimulator simulator = new(
            new FixedSurfaceSampler(surface),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);

        const float dt = 1f / 120f;
        float peakLeftRumble = 0f;
        float peakRightRumble = 0f;
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0.75f, 0f, 0.24f, throttleAssistEnabled: true), dt);
            peakLeftRumble = MathF.Max(peakLeftRumble, simulator.State.SurfaceRumbleLeft);
            peakRightRumble = MathF.Max(peakRightRumble, simulator.State.SurfaceRumbleRight);
        }

        VehicleState state = simulator.State;
        float frontDrag = (state.FrontLeftDisplacementDragForceN + state.FrontRightDisplacementDragForceN) * 0.5f;
        float frontMu = (state.FrontLeftSurfaceMu + state.FrontRightSurfaceMu) * 0.5f;
        return new SurfaceResult(frontDrag, frontMu, peakLeftRumble, peakRightRumble);
    }

    private readonly record struct SurfaceResult(float FrontDragN, float FrontMu, float LeftRumble, float RightRumble);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }
    }

    private sealed class FixedSurfaceSampler : ITrackSurfaceSampler
    {
        private readonly SurfaceSample _surface;

        public FixedSurfaceSampler(SurfaceSample surface)
        {
            _surface = surface;
        }

        public SurfaceSample Sample(Vector3 position)
        {
            return _surface;
        }
    }

    private sealed class RightSlopeElevationSampler : ITrackSurfaceSampler
    {
        private readonly float _slope;

        public RightSlopeElevationSampler(float slope)
        {
            _slope = slope;
        }

        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }

        public float GetElevation(Vector2 position)
        {
            return position.X * _slope;
        }
    }
}
