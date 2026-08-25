using System.Reflection;
using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class WeightTransferProbe
{
    private const string ShowroomStockBuildPath = "Data/VehicleBuilds/ek9_showroom_stock.json";

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleBuildDefinitionLoader.LoadSimulationParameters(ShowroomStockBuildPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ProbeResult normalVisual = RunCase(parameters, engineParameters, visualPseudoScaleMultiplier: 1f);
        ProbeResult doubledVisual = RunCase(CloneWithVisualPseudoScale(parameters, 2f), engineParameters, visualPseudoScaleMultiplier: 2f);

        Console.WriteLine(Format("normal visual pseudo", normalVisual));
        Console.WriteLine(Format("double visual pseudo", doubledVisual));
        Console.WriteLine(
            $"delta physical loads FL/FR/RL/RR {doubledVisual.FrontLeftLoadN - normalVisual.FrontLeftLoadN:0.0}/" +
            $"{doubledVisual.FrontRightLoadN - normalVisual.FrontRightLoadN:0.0}/" +
            $"{doubledVisual.RearLeftLoadN - normalVisual.RearLeftLoadN:0.0}/" +
            $"{doubledVisual.RearRightLoadN - normalVisual.RearRightLoadN:0.0}N, " +
            $"delta body roll {doubledVisual.BodyRollDegrees - normalVisual.BodyRollDegrees:0.00}deg, " +
            $"delta LSD FL/FR {doubledVisual.FrontLeftLsdTorqueNm - normalVisual.FrontLeftLsdTorqueNm:0.0}/" +
            $"{doubledVisual.FrontRightLsdTorqueNm - normalVisual.FrontRightLsdTorqueNm:0.0}Nm");
    }

    private static ProbeResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float visualPseudoScaleMultiplier)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        for (int i = 0; i < 360 && simulator.State.SpeedMetersPerSecond < 14f; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0.42f, 0f, 0.32f), dt);
        }

        VehicleState state = simulator.State;
        return new ProbeResult(
            visualPseudoScaleMultiplier,
            state.FrontLeftLoadN,
            state.FrontRightLoadN,
            state.RearLeftLoadN,
            state.RearRightLoadN,
            MathHelper.ToDegrees(state.BodyRollRadians - state.GroundRollRadians),
            state.PhysicalLoadTransferLongitudinalAcceleration,
            state.PhysicalLoadTransferLateralAcceleration,
            state.VisualLoadTransferLateralAcceleration,
            state.LongitudinalLoadTransferN,
            state.FrontLateralLoadTransferN,
            state.RearLateralLoadTransferN,
            state.FfLsdFrontLeftActualTorqueNm,
            state.FfLsdFrontRightActualTorqueNm,
            state.FfLsdManagedFrontAxleTorqueNm,
            state.FfLsdLowGripAnchor);
    }

    private static VehicleSimulationParameters CloneWithVisualPseudoScale(VehicleSimulationParameters source, float multiplier)
    {
        VehicleSimulationParameters clone = CopyInitProperties(source, new VehicleSimulationParameters());
        ArcadeHandlingParameters arcade = CopyInitProperties(source.ArcadeHandling, new ArcadeHandlingParameters());
        typeof(ArcadeHandlingParameters)
            .GetProperty(nameof(ArcadeHandlingParameters.PseudoLateralTransferScale), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(arcade, source.ArcadeHandling.PseudoLateralTransferScale * multiplier);
        typeof(VehicleSimulationParameters)
            .GetProperty(nameof(VehicleSimulationParameters.ArcadeHandling), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(clone, arcade);
        return clone;
    }

    private static T CopyInitProperties<T>(T source, T destination)
    {
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(destination, property.GetValue(source));
            }
        }

        return destination;
    }

    private static string Format(string label, ProbeResult result)
    {
        return
            $"{label}: visual scale x{result.VisualPseudoScaleMultiplier:0.0}, " +
            $"loads FL/FR/RL/RR {result.FrontLeftLoadN:0}/{result.FrontRightLoadN:0}/{result.RearLeftLoadN:0}/{result.RearRightLoadN:0}N, " +
            $"body roll {result.BodyRollDegrees:0.00}deg, " +
            $"phys accel LON/LAT {result.PhysicalLongitudinalAcceleration:0.00}/{result.PhysicalLateralAcceleration:0.00}m/s2, " +
            $"visual lat {result.VisualLateralAcceleration:0.00}m/s2, " +
            $"transfer long/frontLat/rearLat {result.LongitudinalTransferN:0}/{result.FrontLateralTransferN:0}/{result.RearLateralTransferN:0}N, " +
            $"lsd {result.LowGripAnchor} FL/FR {result.FrontLeftLsdTorqueNm:0}/{result.FrontRightLsdTorqueNm:0}Nm managed {result.ManagedFrontTorqueNm:0}Nm";
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private readonly record struct ProbeResult(
        float VisualPseudoScaleMultiplier,
        float FrontLeftLoadN,
        float FrontRightLoadN,
        float RearLeftLoadN,
        float RearRightLoadN,
        float BodyRollDegrees,
        float PhysicalLongitudinalAcceleration,
        float PhysicalLateralAcceleration,
        float VisualLateralAcceleration,
        float LongitudinalTransferN,
        float FrontLateralTransferN,
        float RearLateralTransferN,
        float FrontLeftLsdTorqueNm,
        float FrontRightLsdTorqueNm,
        float ManagedFrontTorqueNm,
        string LowGripAnchor);
}
