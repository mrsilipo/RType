using System.Reflection;
using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class DrivetrainLayoutProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters source = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        ProbeResult ff = RunCase(CreateLayoutParameters(source, DrivetrainLayout.FF, 1f), engine);
        ProbeResult fr = RunCase(CreateLayoutParameters(source, DrivetrainLayout.FR, 0f), engine);
        ProbeResult awd = RunCase(CreateLayoutParameters(source, DrivetrainLayout.AWD, 0.50f), engine);

        Require(ff.FrontTorqueNm > 1f, "FF did not route torque to the front axle.");
        Require(ff.RearTorqueNm < 0.1f, "FF leaked drive torque to the rear axle.");
        Require(fr.RearTorqueNm > 1f, "FR did not route torque to the rear axle.");
        Require(fr.FrontTorqueNm < 0.1f, "FR leaked drive torque to the front axle.");
        Require(awd.FrontTorqueNm > 1f && awd.RearTorqueNm > 1f, "AWD did not route torque to both axles.");

        float awdShare = awd.FrontTorqueNm / MathF.Max(1f, awd.FrontTorqueNm + awd.RearTorqueNm);
        Require(MathF.Abs(awdShare - 0.50f) < 0.12f, $"AWD front torque share was {awdShare:0.00}, expected near 0.50.");

        Console.WriteLine(Format("FF", ff));
        Console.WriteLine(Format("FR", fr));
        Console.WriteLine(Format("AWD", awd));
        Console.WriteLine("Drivetrain layout probe passed: FF, FR, and AWD route drive torque to the configured driven axles.");
    }

    private static ProbeResult RunCase(VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        VehicleState state = simulator.State;
        float frontTorque =
            MathF.Abs(state.FrontLeftDriveTorqueNm) +
            MathF.Abs(state.FrontRightDriveTorqueNm);
        float rearTorque =
            MathF.Abs(state.RearLeftDriveTorqueNm) +
            MathF.Abs(state.RearRightDriveTorqueNm);
        return new ProbeResult(
            state.SpeedMetersPerSecond,
            frontTorque,
            rearTorque,
            state.FrontLeftDriveTorqueNm,
            state.FrontRightDriveTorqueNm,
            state.RearLeftDriveTorqueNm,
            state.RearRightDriveTorqueNm,
            state.FrontDifferentialManagedAxleTorqueNm,
            state.RearDifferentialManagedAxleTorqueNm);
    }

    private static VehicleSimulationParameters CreateLayoutParameters(
        VehicleSimulationParameters source,
        DrivetrainLayout layout,
        float frontTorqueShare)
    {
        VehicleSimulationParameters clone = CopyInitProperties(source, new VehicleSimulationParameters());
        DifferentialParameters lsd = new()
        {
            TorqueBiasRatio = MathF.Max(3.0f, source.DifferentialTorqueBiasRatio),
            PreloadTorqueNm = MathF.Max(45f, source.DifferentialPreloadTorqueNm)
        };
        DifferentialParameters open = DifferentialParameters.Open;

        Set(clone, nameof(VehicleSimulationParameters.DrivetrainLayout), layout);
        Set(clone, nameof(VehicleSimulationParameters.FrontTorqueShare), frontTorqueShare);
        Set(clone, nameof(VehicleSimulationParameters.DrivenWheels), layout switch
        {
            DrivetrainLayout.FF => new DrivenWheelSet(true, true, false, false),
            DrivetrainLayout.FR => new DrivenWheelSet(false, false, true, true),
            DrivetrainLayout.AWD => new DrivenWheelSet(true, true, true, true),
            _ => new DrivenWheelSet(true, true, false, false)
        });
        Set(clone, nameof(VehicleSimulationParameters.FrontDifferential), layout == DrivetrainLayout.FR ? open : lsd);
        Set(clone, nameof(VehicleSimulationParameters.RearDifferential), layout == DrivetrainLayout.FF ? open : lsd);
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

    private static void Set<TValue>(VehicleSimulationParameters parameters, string propertyName, TValue value)
    {
        typeof(VehicleSimulationParameters)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parameters, value);
    }

    private static string Format(string label, ProbeResult result)
    {
        return
            $"{label}: speed {result.SpeedMetersPerSecond * 3.6f:0.0}km/h, " +
            $"front/rear torque {result.FrontTorqueNm:0.0}/{result.RearTorqueNm:0.0}Nm, " +
            $"wheels FL/FR/RL/RR {result.FrontLeftTorqueNm:0.0}/{result.FrontRightTorqueNm:0.0}/" +
            $"{result.RearLeftTorqueNm:0.0}/{result.RearRightTorqueNm:0.0}Nm, " +
            $"managed front/rear {result.FrontManagedTorqueNm:0.0}/{result.RearManagedTorqueNm:0.0}Nm";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Drivetrain layout probe failed: {message}");
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private readonly record struct ProbeResult(
        float SpeedMetersPerSecond,
        float FrontTorqueNm,
        float RearTorqueNm,
        float FrontLeftTorqueNm,
        float FrontRightTorqueNm,
        float RearLeftTorqueNm,
        float RearRightTorqueNm,
        float FrontManagedTorqueNm,
        float RearManagedTorqueNm);
}
