using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class TireRelaxationProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(options.SurfaceDefinitionPath);

        RunCase("roll to dead stop", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeToSpeed, CoastToStop);
        RunCase("handbrake to stop", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeToSpeed, HandbrakeToStop);
        RunCase("handbrake slide release", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeToSpeed, HandbrakeSlideRelease);
        RunCase("low speed steering crawl", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeIdle, LowSpeedSteeringCrawl);
        RunCase("reverse crawl", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeReverse, ReverseCrawl);
        RunCase("split mu braking", parameters, engineParameters, new SplitSurfaceSampler(surfaces.Grass, surfaces.Road), PrimeToSpeed, SplitMuBraking);
        RunCase("curb clip", parameters, engineParameters, new SplitSurfaceSampler(surfaces.Curb, surfaces.Road), PrimeToSpeed, CurbClip);
        RunCase("grass recovery", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Grass), PrimeToSpeed, GrassRecovery);
        RunCase("standing start", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeIdle, StandingStart);
        RunCase("high speed straight", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), PrimeHighSpeed, HighSpeedStraight);
    }

    private static void RunCase(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ITrackSurfaceSampler surfaceSampler,
        Action<SimpleVehicleSimulator, float> prime,
        Action<SimpleVehicleSimulator, ProbeStats, float> run)
    {
        SimpleVehicleSimulator simulator = new(
            surfaceSampler,
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        prime(simulator, dt);

        ProbeStats stats = new();
        run(simulator, stats, dt);
        stats.Capture(simulator.State);
        stats.Validate(label);

        VehicleState state = simulator.State;
        Console.WriteLine(
            $"{label}: speed={state.SpeedMetersPerSecond * 3.6f:0.0}kph yaw={MathHelper.ToDegrees(state.YawRateRadiansPerSecond):0.00}deg/s " +
            $"rawPeak={stats.PeakRawSlip:0.00} relaxedLongPeak={stats.PeakRelaxedLong:0.00} relaxedLatPeak={stats.PeakRelaxedLat:0.00} " +
            $"endRaw={state.PeakRawSlipRatio:0.00} endRelaxed={state.PeakRelaxedLongitudinalSlipRatio:0.00}/{state.PeakRelaxedLateralSlip:0.00} " +
            $"omega FL/FR/RL/RR={state.FrontLeftWheelOmegaRadiansPerSecond:0.0}/{state.FrontRightWheelOmegaRadiansPerSecond:0.0}/{state.RearLeftWheelOmegaRadiansPerSecond:0.0}/{state.RearRightWheelOmegaRadiansPerSecond:0.0} " +
            $"handbrakeLock={state.RearHandbrakeLockAmount:0.00} surfaces={state.FrontLeftSurfaceName}/{state.FrontRightSurfaceName}/{state.RearLeftSurfaceName}/{state.RearRightSurfaceName}");
    }

    private static void PrimeIdle(SimpleVehicleSimulator simulator, float dt)
    {
        for (int i = 0; i < 12; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, throttleAssistEnabled: true), dt);
        }
    }

    private static void PrimeToSpeed(SimpleVehicleSimulator simulator, float dt)
    {
        for (int i = 0; i < 300; i++)
        {
            simulator.Update(new VehicleInput(0.85f, 0f, 0f, throttleAssistEnabled: true), dt);
        }
    }

    private static void PrimeReverse(SimpleVehicleSimulator simulator, float dt)
    {
        simulator.Update(new VehicleInput(0f, 0f, 0f, shiftDownRequested: true, throttleAssistEnabled: true), dt);
        for (int i = 0; i < 24; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, 0f, 0.20f, throttleAssistEnabled: true), dt);
        }
    }

    private static void PrimeHighSpeed(SimpleVehicleSimulator simulator, float dt)
    {
        for (int i = 0; i < 2400; i++)
        {
            bool shiftUp = simulator.State.Rpm > simulator.State.PowerRedlineRpm - 350f &&
                           simulator.State.Gear is > 0 and < 5 &&
                           simulator.State.ShiftTimeRemainingSeconds <= 0f;
            simulator.Update(new VehicleInput(0.95f, 0f, 0f, shiftUpRequested: shiftUp, throttleAssistEnabled: true), dt);
            if (simulator.State.SpeedMetersPerSecond >= 135f / 3.6f)
            {
                break;
            }
        }
    }

    private static void CoastToStop(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 900; i++)
        {
            simulator.Update(new VehicleInput(0f, 0.10f, 0f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
            if (simulator.State.SpeedMetersPerSecond < 0.15f)
            {
                break;
            }
        }
    }

    private static void HandbrakeToStop(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 420; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, 1f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
            if (simulator.State.SpeedMetersPerSecond < 0.15f)
            {
                break;
            }
        }

        ReleaseAndSettle(simulator, stats, dt);
    }

    private static void HandbrakeSlideRelease(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0.58f, 1f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0.45f, 0f, 0.18f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void LowSpeedSteeringCrawl(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0.18f, 0f, 0.45f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void ReverseCrawl(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, -0.20f, 0f, 0.24f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void SplitMuBraking(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 0.72f, 0.08f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void CurbClip(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0.45f, 0f, 0.24f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void GrassRecovery(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 120; i++)
        {
            simulator.Update(new VehicleInput(0f, 0.20f, 0.16f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0.35f, 0f, -0.08f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void StandingStart(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0.65f, 0f, 0f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void HighSpeedStraight(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0.55f, 0f, 0f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private static void ReleaseAndSettle(SimpleVehicleSimulator simulator, ProbeStats stats, float dt)
    {
        for (int i = 0; i < 360; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, throttleAssistEnabled: true), dt);
            stats.Capture(simulator.State);
        }
    }

    private sealed class ProbeStats
    {
        public float PeakRawSlip { get; private set; }

        public float PeakRelaxedLong { get; private set; }

        public float PeakRelaxedLat { get; private set; }

        public float PeakYawRate { get; private set; }

        public float PeakWheelOmega { get; private set; }

        public void Capture(VehicleState state)
        {
            PeakRawSlip = MathF.Max(PeakRawSlip, state.PeakRawSlipRatio);
            PeakRelaxedLong = MathF.Max(PeakRelaxedLong, state.PeakRelaxedLongitudinalSlipRatio);
            PeakRelaxedLat = MathF.Max(PeakRelaxedLat, state.PeakRelaxedLateralSlip);
            PeakYawRate = MathF.Max(PeakYawRate, MathF.Abs(state.YawRateRadiansPerSecond));
            PeakWheelOmega = MathF.Max(
                PeakWheelOmega,
                MathF.Max(
                    MathF.Max(MathF.Abs(state.FrontLeftWheelOmegaRadiansPerSecond), MathF.Abs(state.FrontRightWheelOmegaRadiansPerSecond)),
                    MathF.Max(MathF.Abs(state.RearLeftWheelOmegaRadiansPerSecond), MathF.Abs(state.RearRightWheelOmegaRadiansPerSecond))));
        }

        public void Validate(string label)
        {
            if (!IsFinite(PeakRawSlip) ||
                !IsFinite(PeakRelaxedLong) ||
                !IsFinite(PeakRelaxedLat) ||
                !IsFinite(PeakYawRate) ||
                !IsFinite(PeakWheelOmega))
            {
                throw new InvalidOperationException($"Tire relaxation probe failed: {label} produced non-finite diagnostics.");
            }

            if (PeakRelaxedLong > 4.001f || PeakRelaxedLat > 4.001f)
            {
                throw new InvalidOperationException($"Tire relaxation probe failed: {label} exceeded relaxed slip clamp ({PeakRelaxedLong:0.00}/{PeakRelaxedLat:0.00}).");
            }

            if (PeakWheelOmega > 2500f)
            {
                throw new InvalidOperationException($"Tire relaxation probe failed: {label} produced implausible wheel omega ({PeakWheelOmega:0.0} rad/s).");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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

    private sealed class SplitSurfaceSampler : ITrackSurfaceSampler
    {
        private readonly SurfaceSample _left;
        private readonly SurfaceSample _right;

        public SplitSurfaceSampler(SurfaceSample left, SurfaceSample right)
        {
            _left = left;
            _right = right;
        }

        public SurfaceSample Sample(Vector3 position)
        {
            return position.X < 0f ? _left : _right;
        }
    }
}
