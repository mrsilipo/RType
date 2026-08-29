using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class SurfaceProbe
{
    private const string ShowroomStockBuildPath = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(ShowroomStockBuildPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(options.SurfaceDefinitionPath);

        RunCase("all road corner exit", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), new VehicleInput(0.95f, 0f, 0.62f));
        RunCase("all curb corner exit", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Curb), new VehicleInput(0.95f, 0f, 0.62f));
        RunCase("left curb split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Curb, surfaces.Road), new VehicleInput(0.95f, 0f, 0.28f));
        RunCase("right curb split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Road, surfaces.Curb), new VehicleInput(0.95f, 0f, -0.28f));
        RunCase("left curb right grass boundary", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Curb, surfaces.Grass), new VehicleInput(0.95f, 0f, 0.18f));
        RunCase("curb grass blend 25", parameters, engineParameters, new FixedSurfaceSampler(SurfaceSample.Blend("CURB_GRASS", surfaces.Curb, surfaces.Grass, 0.25f)), new VehicleInput(0.95f, 0f, 0.62f));
        RunCase("curb grass blend 50", parameters, engineParameters, new FixedSurfaceSampler(SurfaceSample.Blend("CURB_GRASS", surfaces.Curb, surfaces.Grass, 0.50f)), new VehicleInput(0.95f, 0f, 0.62f));
        RunCase("curb grass blend 75", parameters, engineParameters, new FixedSurfaceSampler(SurfaceSample.Blend("CURB_GRASS", surfaces.Curb, surfaces.Grass, 0.75f)), new VehicleInput(0.95f, 0f, 0.62f));
        RunCase("left curb right blend 50", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Curb, SurfaceSample.Blend("CURB_GRASS", surfaces.Curb, surfaces.Grass, 0.50f)), new VehicleInput(0.95f, 0f, 0.18f));
        RunCase("all grass launch", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Grass), new VehicleInput(0.95f, 0f, 0.10f));
        RunCase("left grass split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Grass, surfaces.Road), new VehicleInput(0.95f, 0f, 0.28f));
        RunCase("right grass split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Road, surfaces.Grass), new VehicleInput(0.95f, 0f, -0.28f));
        RunCase("all dirt launch", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Dirt), new VehicleInput(0.95f, 0f, 0.10f));
        RunCase("left dirt split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Dirt, surfaces.Road), new VehicleInput(0.95f, 0f, 0.28f));
        RunHandbrakeCase("road straight handbrake", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), 0f, expectRotation: false, expectLowScreech: false);
        RunHandbrakeCase("road turn handbrake", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Road), 0.58f, expectRotation: true, expectLowScreech: false);
        RunHandbrakeCase("grass turn handbrake", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Grass), 0.58f, expectRotation: true, expectLowScreech: true);
    }

    private static void RunCase(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ITrackSurfaceSampler surfaceSampler,
        VehicleInput input)
    {
        SimpleVehicleSimulator simulator = new(
            surfaceSampler,
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        float startSpeedKph = simulator.State.SpeedMetersPerSecond * 3.6f;
        float peakManagedTorque = 0f;
        float peakYawMoment = 0f;
        float peakFrontSlip = 0f;
        float peakLeftRumble = 0f;
        float peakRightRumble = 0f;
        for (int i = 0; i < 180; i++)
        {
            if (surfaceSampler is VehicleRelativeSplitSurfaceSampler splitSurface)
            {
                splitSurface.Center = simulator.State.Position;
                splitSurface.HeadingRadians = simulator.State.HeadingRadians;
            }

            simulator.Update(input, dt);
            VehicleState state = simulator.State;
            peakManagedTorque = MathF.Max(peakManagedTorque, state.FfLsdManagedFrontAxleTorqueNm);
            peakYawMoment = MathF.Max(peakYawMoment, MathF.Abs(state.FrontDriveTorqueSteerYawMomentNm));
            peakFrontSlip = MathF.Max(
                peakFrontSlip,
                MathF.Max(MathF.Abs(state.FrontLeftSlipRatio), MathF.Abs(state.FrontRightSlipRatio)));
            peakLeftRumble = MathF.Max(peakLeftRumble, state.SurfaceRumbleLeft);
            peakRightRumble = MathF.Max(peakRightRumble, state.SurfaceRumbleRight);
        }

        VehicleState end = simulator.State;
        Console.WriteLine(
            $"{label}: {startSpeedKph:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h, " +
            $"surfaces FL/FR/RL/RR {end.FrontLeftSurfaceName}/{end.FrontRightSurfaceName}/{end.RearLeftSurfaceName}/{end.RearRightSurfaceName}, " +
            $"mu FL/FR {end.FrontLeftSurfaceMu:0.00}/{end.FrontRightSurfaceMu:0.00}, " +
            $"slip FL/FR {end.FrontLeftSlipRatio:0.00}/{end.FrontRightSlipRatio:0.00}, peak front slip {peakFrontSlip:0.00}, " +
            $"lsd anchor {end.FfLsdLowGripAnchor}, FL/FR torque {end.FfLsdFrontLeftActualTorqueNm:0}/{end.FfLsdFrontRightActualTorqueNm:0}Nm, " +
            $"managed {peakManagedTorque:0}Nm, yaw diag {peakYawMoment:0}Nm, drag FL/FR {end.FrontLeftDisplacementDragForceN:0}/{end.FrontRightDisplacementDragForceN:0}N, " +
            $"blend FL/FR {end.FrontLeftSurfaceBlend:0.00}/{end.FrontRightSurfaceBlend:0.00}, peak rumble L/R {peakLeftRumble:0.00}/{peakRightRumble:0.00}, " +
            $"curb wheels {end.CurbContactWheelCount}, curb load FL/FR {end.FrontLeftCurbLoadMultiplier:0.00}/{end.FrontRightCurbLoadMultiplier:0.00}, " +
            $"surface vib wheels {end.SurfaceVibrationContactWheelCount}, surface load FL/FR {end.FrontLeftSurfaceLoadMultiplier:0.00}/{end.FrontRightSurfaceLoadMultiplier:0.00}");
    }

    private static void RunHandbrakeCase(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ITrackSurfaceSampler surfaceSampler,
        float steer,
        bool expectRotation,
        bool expectLowScreech)
    {
        SimpleVehicleSimulator simulator = new(
            surfaceSampler,
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        for (int i = 0; i < 360; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        float startSpeedKph = simulator.State.SpeedMetersPerSecond * 3.6f;
        float peakLock = 0f;
        float peakSlideAudio = 0f;
        float peakYawRate = 0f;
        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, steer, 1f), dt);
            VehicleState state = simulator.State;
            peakLock = MathF.Max(peakLock, state.RearHandbrakeLockAmount);
            peakSlideAudio = MathF.Max(peakSlideAudio, state.RearHandbrakeSlideIntensity);
            peakYawRate = MathF.Max(peakYawRate, MathF.Abs(state.YawRateRadiansPerSecond));
        }

        float releaseStartLock = simulator.State.RearHandbrakeLockAmount;
        for (int i = 0; i < 120; i++)
        {
            simulator.Update(new VehicleInput(0.45f, 0f, steer * 0.35f, 0f), dt);
        }

        VehicleState end = simulator.State;
        if (peakLock < 0.45f)
        {
            throw new InvalidOperationException($"Surface probe failed: {label} did not lock the rear axle enough ({peakLock:0.00}).");
        }

        if (expectRotation && peakYawRate < 0.12f)
        {
            throw new InvalidOperationException($"Surface probe failed: {label} did not create enough handbrake rotation ({peakYawRate:0.00} rad/s).");
        }

        if (!expectRotation && peakYawRate > 0.22f)
        {
            throw new InvalidOperationException($"Surface probe failed: {label} rotated too much while straight ({peakYawRate:0.00} rad/s).");
        }

        if (expectLowScreech && peakSlideAudio > 0.30f)
        {
            throw new InvalidOperationException($"Surface probe failed: {label} produced too much handbrake screech ({peakSlideAudio:0.00}).");
        }

        if (end.RearHandbrakeLockAmount > releaseStartLock * 0.65f && end.SpeedMetersPerSecond > 2f)
        {
            throw new InvalidOperationException($"Surface probe failed: {label} rear wheels did not recover after release ({releaseStartLock:0.00}->{end.RearHandbrakeLockAmount:0.00}).");
        }

        Console.WriteLine(
            $"{label}: {startSpeedKph:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h, " +
            $"lock peak/end {peakLock:0.00}/{end.RearHandbrakeLockAmount:0.00}, " +
            $"slide audio peak {peakSlideAudio:0.00}, yaw peak {MathHelper.ToDegrees(peakYawRate):0.0}deg/s, " +
            $"rear slip L/R {end.RearLeftSlipRatio:0.00}/{end.RearRightSlipRatio:0.00}, " +
            $"rear surfaces {end.RearLeftSurfaceName}/{end.RearRightSurfaceName}");
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

    private sealed class VehicleRelativeSplitSurfaceSampler : ITrackSurfaceSampler
    {
        private readonly SurfaceSample _leftSurface;
        private readonly SurfaceSample _rightSurface;

        public VehicleRelativeSplitSurfaceSampler(SurfaceSample leftSurface, SurfaceSample rightSurface)
        {
            _leftSurface = leftSurface;
            _rightSurface = rightSurface;
        }

        public Vector3 Center { get; set; }

        public float HeadingRadians { get; set; }

        public SurfaceSample Sample(Vector3 position)
        {
            Vector3 right = new(MathF.Cos(HeadingRadians), 0f, -MathF.Sin(HeadingRadians));
            float lateralOffset = Vector3.Dot(position - Center, right);
            return lateralOffset < 0f ? _leftSurface : _rightSurface;
        }
    }
}
