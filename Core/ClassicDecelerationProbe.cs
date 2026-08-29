using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicDecelerationProbe
{
    private const float Dt = 1f / 120f;
    private const float InitialSpeedMps = 100f / 3.6f;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        CoastResult neutral = RunCoast(parameters, engineParameters, 0, 240);
        CoastResult fifth = RunCoast(parameters, engineParameters, 5, 240);
        CoastResult third = RunCoast(parameters, engineParameters, 3, 240);
        CoastResult hardBrake = RunBraking(parameters, engineParameters, 4, 0.85f, 90);
        CornerResult cornerCoast = RunCorner(parameters, engineParameters, 4, 0f, 0.55f, 150);
        CornerResult cornerThrottle = RunCorner(parameters, engineParameters, 4, 0.85f, 0.55f, 150);

        Console.WriteLine($"Classic deceleration probe: {parameters.DisplayName}");
        Console.WriteLine(Format("neutral coast", neutral));
        Console.WriteLine(Format("5th coast", fifth));
        Console.WriteLine(Format("3rd coast", third));
        Console.WriteLine(Format("hard brake", hardBrake));
        Console.WriteLine(
            $"  corner coast: speedDrop={cornerCoast.SpeedDropKmh:0.00}km/h frontGrip={cornerCoast.PeakFrontGripUsage:0.00} lat={cornerCoast.PeakLateralAcceleration:0.00}m/s2 yaw={MathHelper.ToDegrees(cornerCoast.PeakAbsYawRate):0.0}deg/s");
        Console.WriteLine(
            $"  corner throttle: speedDrop={cornerThrottle.SpeedDropKmh:0.00}km/h frontGrip={cornerThrottle.PeakFrontGripUsage:0.00} lat={cornerThrottle.PeakLateralAcceleration:0.00}m/s2 yaw={MathHelper.ToDegrees(cornerThrottle.PeakAbsYawRate):0.0}deg/s");

        Require(fifth.SpeedDropMps > neutral.SpeedDropMps + 0.15f, "in-gear coast did not decelerate more than neutral coast.");
        Require(third.SpeedDropMps > fifth.SpeedDropMps + 0.20f, "lower gear did not produce stronger engine braking than fifth gear.");
        Require(third.PeakEngineBrakeForceN > fifth.PeakEngineBrakeForceN + 100f, "engine brake force did not increase with lower gear multiplication.");
        Require(hardBrake.SpeedDropMps > third.SpeedDropMps, "service braking did not exceed engine-braking coast-down.");
        Require(hardBrake.PeakDynamicFrontLoadN > hardBrake.StaticFrontLoadN + 100f, "deceleration did not shift load toward the front axle.");
        Require(hardBrake.MinDynamicRearLoadN < hardBrake.StaticRearLoadN - 100f, "deceleration did not unload the rear axle.");
        Require(cornerThrottle.PeakFrontGripUsage >= cornerCoast.PeakFrontGripUsage, "FF throttle did not consume more front axle grip while cornering.");
        Require(cornerCoast.PeakLateralAcceleration > 0.5f, "coast steering did not create lateral tyre force.");

        Console.WriteLine("Classic deceleration probe passed.");
    }

    private static CoastResult RunCoast(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        int gear,
        int frames)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = gear;
        simulator.State.Velocity = new Vector2(0f, InitialSpeedMps);
        simulator.State.Rpm = gear > 0 ? RoadRpm(parameters, InitialSpeedMps, gear) : parameters.IdleRpm;

        return RunLongitudinal(simulator, frames, _ => new VehicleInput(0f, 0f, 0f));
    }

    private static CoastResult RunBraking(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        int gear,
        float brake,
        int frames)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = gear;
        simulator.State.Velocity = new Vector2(0f, InitialSpeedMps);
        simulator.State.Rpm = RoadRpm(parameters, InitialSpeedMps, gear);

        return RunLongitudinal(simulator, frames, _ => new VehicleInput(0f, brake, 0f));
    }

    private static CornerResult RunCorner(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        int gear,
        float throttle,
        float steer,
        int frames)
    {
        const float cornerSpeed = 120f / 3.6f;
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = gear;
        simulator.State.Velocity = new Vector2(0f, cornerSpeed);
        simulator.State.Rpm = RoadRpm(parameters, cornerSpeed, gear);

        float peakFrontGrip = 0f;
        float peakLateralAcceleration = 0f;
        float peakAbsYawRate = 0f;
        for (int i = 0; i < frames; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, steer), Dt);
            VehicleState state = simulator.State;
            peakFrontGrip = MathF.Max(peakFrontGrip, state.FrontLeftGripUsage);
            peakLateralAcceleration = MathF.Max(peakLateralAcceleration, MathF.Abs(state.LateralAcceleration));
            peakAbsYawRate = MathF.Max(peakAbsYawRate, MathF.Abs(state.YawRateRadiansPerSecond));
            RequireFinite(state);
        }

        return new CornerResult(
            (cornerSpeed - simulator.State.SpeedMetersPerSecond) * 3.6f,
            peakFrontGrip,
            peakLateralAcceleration,
            peakAbsYawRate);
    }

    private static CoastResult RunLongitudinal(
        ClassicFourWheelVehicleSimulator simulator,
        int frames,
        Func<int, VehicleInput> inputForFrame)
    {
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float peakEngineBrakeForce = 0f;
        float peakServiceBrakeForce = 0f;
        float peakDynamicFrontLoad = simulator.State.FrontStaticAxleLoadN;
        float minDynamicRearLoad = simulator.State.RearStaticAxleLoadN;
        float peakRolling = 0f;
        float peakAero = 0f;
        float minLongAccel = 0f;

        for (int i = 0; i < frames; i++)
        {
            simulator.Update(inputForFrame(i), Dt);
            VehicleState state = simulator.State;
            peakEngineBrakeForce = MathF.Max(peakEngineBrakeForce, MathF.Abs(state.ClassicEngineBrakeForceRequestN));
            peakServiceBrakeForce = MathF.Max(peakServiceBrakeForce, MathF.Abs(state.ClassicServiceBrakeForceRequestN));
            peakDynamicFrontLoad = MathF.Max(peakDynamicFrontLoad, state.ClassicDynamicFrontAxleLoadN);
            minDynamicRearLoad = MathF.Min(minDynamicRearLoad, state.ClassicDynamicRearAxleLoadN);
            peakRolling = MathF.Max(peakRolling, MathF.Abs(state.ClassicRollingResistanceForceN));
            peakAero = MathF.Max(peakAero, MathF.Abs(state.ClassicAeroDragForceN));
            minLongAccel = MathF.Min(minLongAccel, state.LongitudinalAcceleration);
            RequireFinite(state);
        }

        VehicleState final = simulator.State;
        return new CoastResult(
            startSpeed - final.SpeedMetersPerSecond,
            peakEngineBrakeForce,
            peakServiceBrakeForce,
            peakDynamicFrontLoad,
            minDynamicRearLoad,
            final.ClassicStaticFrontAxleLoadN,
            final.ClassicStaticRearAxleLoadN,
            peakRolling,
            peakAero,
            minLongAccel);
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        return new ClassicFourWheelVehicleSimulator(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
    }

    private static float RoadRpm(VehicleSimulationParameters parameters, float speedMps, int gear)
    {
        if (gear <= 0 || gear > parameters.ForwardGearRatios.Length)
        {
            return parameters.IdleRpm;
        }

        return MathHelper.Clamp(
            MathF.Abs(speedMps) / MathF.Max(0.05f, parameters.WheelRadiusMeters) *
            parameters.ForwardGearRatios[gear - 1] *
            parameters.FinalDriveRatio *
            60f /
            MathF.Tau,
            parameters.IdleRpm,
            parameters.LimiterHardCutRpm);
    }

    private static string Format(string label, CoastResult result)
    {
        return
            $"  {label}: speedDrop={result.SpeedDropKmh:0.00}km/h, " +
            $"engineBrake={result.PeakEngineBrakeForceN:0}N, serviceBrake={result.PeakServiceBrakeForceN:0}N, " +
            $"load F/R peak-min={result.PeakDynamicFrontLoadN:0}/{result.MinDynamicRearLoadN:0}N, " +
            $"static F/R={result.StaticFrontLoadN:0}/{result.StaticRearLoadN:0}N, " +
            $"roll/aero={result.PeakRollingResistanceN:0}/{result.PeakAeroDragN:0}N, minAx={result.MinLongitudinalAcceleration:0.00}m/s2";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Classic deceleration probe failed: {message}");
        }
    }

    private static void RequireFinite(VehicleState state)
    {
        Require(float.IsFinite(state.Position.X) && float.IsFinite(state.Position.Z), "position became non-finite.");
        Require(float.IsFinite(state.Velocity.X) && float.IsFinite(state.Velocity.Y), "velocity became non-finite.");
        Require(float.IsFinite(state.HeadingRadians), "heading became non-finite.");
        Require(float.IsFinite(state.YawRateRadiansPerSecond), "yaw rate became non-finite.");
        Require(float.IsFinite(state.ClassicDynamicFrontAxleLoadN), "front dynamic axle load became non-finite.");
        Require(float.IsFinite(state.ClassicDynamicRearAxleLoadN), "rear dynamic axle load became non-finite.");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private readonly record struct CoastResult(
        float SpeedDropMps,
        float PeakEngineBrakeForceN,
        float PeakServiceBrakeForceN,
        float PeakDynamicFrontLoadN,
        float MinDynamicRearLoadN,
        float StaticFrontLoadN,
        float StaticRearLoadN,
        float PeakRollingResistanceN,
        float PeakAeroDragN,
        float MinLongitudinalAcceleration)
    {
        public float SpeedDropKmh => SpeedDropMps * 3.6f;
    }

    private readonly record struct CornerResult(
        float SpeedDropKmh,
        float PeakFrontGripUsage,
        float PeakLateralAcceleration,
        float PeakAbsYawRate);
}

