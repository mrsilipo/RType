using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBicycleProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic bicycle probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        ProbeSteeringSignConvention(parameters, engineParameters);
        ProbeSteeringTable(parameters, engineParameters);
        ProbeStraightAccelerationAndCoast(parameters, engineParameters);
        ProbeLowSpeedUTurn(parameters, engineParameters);
        ProbeReverse(parameters, engineParameters);
        ProbeManualShiftLatch(parameters, engineParameters);
        ProbeFfThrottleSaturation(parameters, engineParameters);
        Console.WriteLine("Classic bicycle probe passed.");
    }

    private static void ProbeSteeringSignConvention(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicBicycleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.State.Velocity = new Vector2(0f, 25f);
        float startHeading = simulator.State.HeadingRadians;

        for (int i = 0; i < 72; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0.55f), 1f / 120f);
        }

        VehicleState state = simulator.State;
        Console.WriteLine(
            $"  sign: rightInput latAccel={state.LateralAcceleration:0.00}m/s2 yaw={MathHelper.ToDegrees(state.YawRateRadiansPerSecond):0.00}deg/s headingDelta={MathHelper.ToDegrees(state.HeadingRadians - startHeading):0.00}deg");
        Require(state.LateralAcceleration > 0.05f, "right steering must generate rightward lateral acceleration");
        Require(state.YawRateRadiansPerSecond > 0.01f, "right steering must generate rightward positive yaw in the game +X convention");
        RequireFinite(state);
    }

    private static void ProbeSteeringTable(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        foreach ((float speedKmh, float expected) in new[] { (0f, 32f), (60f, 24f), (120f, 15f), (200f, 8f) })
        {
            ClassicBicycleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
            simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
            for (int i = 0; i < 80; i++)
            {
                simulator.Update(new VehicleInput(0f, 0f, 1f), 1f / 120f);
            }

            float actual = simulator.State.SteeringSpeedMatchedMaxAngleDegrees;
            Console.WriteLine($"  steering table {speedKmh:0}km/h: max={actual:0.00}deg expected={expected:0.00}deg");
            Require(MathF.Abs(actual - expected) < 0.75f, $"steering table mismatch at {speedKmh:0}km/h");
        }
    }

    private static void ProbeStraightAccelerationAndCoast(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicBicycleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        for (int i = 0; i < 300; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), 1f / 120f);
        }

        float acceleratedSpeed = simulator.State.SpeedMetersPerSecond;
        float yawAtSpeed = MathF.Abs(simulator.State.YawRateRadiansPerSecond);
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), 1f / 120f);
        }

        Console.WriteLine(
            $"  straight accel/coast: accelSpeed={acceleratedSpeed * 3.6f:0.0}km/h coastSpeed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0}km/h yaw={MathHelper.ToDegrees(yawAtSpeed):0.000}deg/s");
        Require(acceleratedSpeed > 7.0f, "full throttle launch did not accelerate cleanly");
        Require(MathF.Abs(simulator.State.LateralSpeed) < 0.05f, "straight coast generated lateral drift");
        Require(MathF.Abs(simulator.State.YawRateRadiansPerSecond) < 0.02f, "straight coast generated yaw oscillation");
        RequireFinite(simulator.State);
    }

    private static void ProbeLowSpeedUTurn(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicBicycleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.State.Velocity = new Vector2(0f, 8.5f);
        float startHeading = simulator.State.HeadingRadians;
        for (int i = 0; i < 360; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, 1f), 1f / 120f);
        }

        float headingDelta = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(simulator.State.HeadingRadians - startHeading)));
        Console.WriteLine(
            $"  low-speed U-turn: heading={headingDelta:0.0}deg speed={simulator.State.SpeedMetersPerSecond * 3.6f:0.0}km/h roadAngle={simulator.State.FrontLeftSteerAngleDegrees:0.0}deg");
        Require(headingDelta > 55f, "low-speed full steering did not rotate the car");
        RequireFinite(simulator.State);
    }

    private static void ProbeReverse(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicBicycleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, reverse: 1f), 1f / 120f);
        }

        Console.WriteLine($"  reverse: gear={simulator.State.Gear} signedSpeed={simulator.State.SignedForwardSpeed:0.00}m/s rpm={simulator.State.Rpm:0}");
        Require(simulator.State.Gear == -1, "Y/reverse input did not select reverse");
        Require(simulator.State.SignedForwardSpeed < -0.4f, "reverse input did not move backward");
        RequireFinite(simulator.State);
    }

    private static void ProbeManualShiftLatch(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicBicycleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 1;

        simulator.Update(new VehicleInput(0.5f, 0f, 0f, shiftUpRequested: true), 1f / 30f);
        Console.WriteLine($"  shift latch: after one upshift press gear={simulator.State.Gear}");
        Require(simulator.State.Gear == 2, "one upshift press must advance exactly one gear even when one frame contains multiple physics ticks");

        simulator.Update(new VehicleInput(0.5f, 0f, 0f), 1f / 30f);
        Require(simulator.State.Gear == 2, "held/released frame unexpectedly repeated the previous upshift");

        simulator.Update(new VehicleInput(0.5f, 0f, 0f, shiftDownRequested: true), 1f / 30f);
        Require(simulator.State.Gear == 1, "one downshift press must reduce exactly one gear even when one frame contains multiple physics ticks");
        RequireFinite(simulator.State);
    }

    private static void ProbeFfThrottleSaturation(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicBicycleVehicleSimulator coast = CreateSimulator(parameters, engineParameters);
        ClassicBicycleVehicleSimulator throttle = CreateSimulator(parameters, engineParameters);
        coast.State.Velocity = new Vector2(0f, 24f);
        throttle.State.Velocity = new Vector2(0f, 24f);
        for (int i = 0; i < 180; i++)
        {
            coast.Update(new VehicleInput(0f, 0f, 0.65f), 1f / 120f);
            throttle.Update(new VehicleInput(1f, 0f, 0.65f), 1f / 120f);
        }

        Console.WriteLine(
            $"  FF saturation: coastFront={coast.State.FrontLeftGripUsage:0.00} throttleFront={throttle.State.FrontLeftGripUsage:0.00} coastYaw={MathHelper.ToDegrees(coast.State.YawRateRadiansPerSecond):0.0} throttleYaw={MathHelper.ToDegrees(throttle.State.YawRateRadiansPerSecond):0.0}");
        Require(throttle.State.FrontLeftGripUsage >= coast.State.FrontLeftGripUsage, "FF throttle did not consume additional front grip budget");
        RequireFinite(coast.State);
        RequireFinite(throttle.State);
    }

    private static ClassicBicycleVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        return new ClassicBicycleVehicleSimulator(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Classic bicycle probe failed: {message}.");
        }
    }

    private static void RequireFinite(VehicleState state)
    {
        Require(float.IsFinite(state.Position.X) && float.IsFinite(state.Position.Z), "position became non-finite");
        Require(float.IsFinite(state.Velocity.X) && float.IsFinite(state.Velocity.Y), "velocity became non-finite");
        Require(float.IsFinite(state.HeadingRadians), "heading became non-finite");
        Require(float.IsFinite(state.YawRateRadiansPerSecond), "yaw rate became non-finite");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }
    }
}
