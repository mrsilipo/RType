using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class SurfaceProbe
{
    private const string ShowroomStockBuildPath = "Data/VehicleBuilds/ek9_showroom_stock.json";

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
        RunCase("all grass launch", parameters, engineParameters, new FixedSurfaceSampler(surfaces.Grass), new VehicleInput(0.95f, 0f, 0.10f));
        RunCase("left grass split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Grass, surfaces.Road), new VehicleInput(0.95f, 0f, 0.28f));
        RunCase("right grass split throttle", parameters, engineParameters, new VehicleRelativeSplitSurfaceSampler(surfaces.Road, surfaces.Grass), new VehicleInput(0.95f, 0f, -0.28f));
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
        }

        VehicleState end = simulator.State;
        Console.WriteLine(
            $"{label}: {startSpeedKph:0.0}->{end.SpeedMetersPerSecond * 3.6f:0.0} km/h, " +
            $"surfaces FL/FR/RL/RR {end.FrontLeftSurfaceName}/{end.FrontRightSurfaceName}/{end.RearLeftSurfaceName}/{end.RearRightSurfaceName}, " +
            $"mu FL/FR {end.FrontLeftSurfaceMu:0.00}/{end.FrontRightSurfaceMu:0.00}, " +
            $"slip FL/FR {end.FrontLeftSlipRatio:0.00}/{end.FrontRightSlipRatio:0.00}, peak front slip {peakFrontSlip:0.00}, " +
            $"lsd anchor {end.FfLsdLowGripAnchor}, FL/FR torque {end.FfLsdFrontLeftActualTorqueNm:0}/{end.FfLsdFrontRightActualTorqueNm:0}Nm, " +
            $"managed {peakManagedTorque:0}Nm, yaw diag {peakYawMoment:0}Nm, drag FL/FR {end.FrontLeftDisplacementDragForceN:0}/{end.FrontRightDisplacementDragForceN:0}N, " +
            $"curb wheels {end.CurbContactWheelCount}, curb load FL/FR {end.FrontLeftCurbLoadMultiplier:0.00}/{end.FrontRightCurbLoadMultiplier:0.00}");
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
