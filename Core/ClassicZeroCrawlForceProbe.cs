using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicZeroCrawlForceProbe
{
    private const float Dt = 1f / 120f;

    private static readonly float[] CheckpointsSeconds =
    [
        0.008333f,
        0.05f,
        0.10f,
        0.25f,
        0.50f,
        1.00f,
        1.50f,
        2.00f
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);

        Console.WriteLine($"Classic zero-crawl force probe: {parameters.DisplayName}");
        Console.WriteLine("  invariant: steer held + zero throttle at true standstill should produce near-zero yaw moment.");
        Console.WriteLine("  columns: case t speed throttle steer roadDeg yaw kinYaw yaw/kin yawAcc beta bodyU/V frontLongSide frontSlipFy rearFy frontYaw rearYaw netYaw FL(u/v slip slipFy rollFy blend finalFy relaxFy Fx/Fy) RL(u/v slip slipFy rollFy blend finalFy relaxFy Fx/Fy)");

        RunCase(parameters, options, "fresh-zero", throttle: 0f, steer: 1f, disableGuide: false, stopAndRestart: false);
        RunCase(parameters, options, "fresh-crawl", throttle: 0.08f, steer: 1f, disableGuide: false, stopAndRestart: false);
        RunCase(parameters, options, "fwd-028", throttle: 0.28f, steer: 1f, disableGuide: false, stopAndRestart: false);
        RunCase(parameters, options, "rev-028", throttle: 0.28f, steer: 1f, disableGuide: false, stopAndRestart: false, reverse: true);
        RunCase(parameters, options, "noguide-crawl", throttle: 0.08f, steer: 1f, disableGuide: true, stopAndRestart: false);
        RunCase(parameters, options, "norelax-crawl", throttle: 0.08f, steer: 1f, disableGuide: false, stopAndRestart: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            BypassLateralRelaxationBelowTransition = true
        });
        RunCase(parameters, options, "rolling-only", throttle: 0.08f, steer: 1f, disableGuide: false, stopAndRestart: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            RollingConstraintOnlyBelowTransition = true
        });
        RunCase(parameters, options, "slip-only", throttle: 0.08f, steer: 1f, disableGuide: false, stopAndRestart: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            SlipDerivedOnlyBelowTransition = true
        });
        RunCase(parameters, options, "stopped-crawl", throttle: 0.08f, steer: 1f, disableGuide: false, stopAndRestart: true);
        RunTrueRestCase(parameters, options);
        RunBrakeRestCase(parameters, options);

        Console.WriteLine("Classic zero-crawl force probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        GameLaunchOptions options,
        string label,
        float throttle,
        float steer,
        bool disableGuide,
        bool stopAndRestart,
        ClassicLowSpeedForceDiagnosticOptions? diagnostics = null,
        bool reverse = false)
    {
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        if (disableGuide)
        {
            engine.ClassicFourWheel.LowSpeed.KinematicYawBlend = 0f;
        }

        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine);
        if (diagnostics is not null)
        {
            simulator.LowSpeedForceDiagnosticOptionsForProbe = diagnostics;
        }

        if (stopAndRestart)
        {
            for (int tick = 0; tick < SecondsToTicks(1.25f); tick++)
            {
                simulator.Update(new VehicleInput(0.12f, 0f, steer), Dt);
            }

            simulator.State.Velocity = Vector2.Zero;
            simulator.State.YawRateRadiansPerSecond = 0f;
            for (int tick = 0; tick < SecondsToTicks(0.75f); tick++)
            {
                simulator.Update(new VehicleInput(0f, 0f, steer), Dt);
            }
        }

        int checkpointIndex = 0;
        float previousYawRate = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float previousHeading = MathHelper.ToDegrees(simulator.State.HeadingRadians);
        float maxYawRateStep = 0f;
        float maxHeadingStep = 0f;
        float maxNetYawAcceleration = 0f;
        float maxRearLocalLateralStep = 0f;
        float maxRearRelaxedForceStep = 0f;
        float maxRearBlendStep = 0f;
        int maxYawRateStepTick = 0;
        int maxHeadingStepTick = 0;
        int maxNetYawAccelerationTick = 0;
        int maxRearLocalLateralStepTick = 0;
        int maxRearRelaxedForceStepTick = 0;
        int maxRearBlendStepTick = 0;
        float previousRearLocalLateral = AverageRearLocalLateral(simulator.State);
        float previousRearRelaxedForce = AverageRearRelaxedForce(simulator.State);
        float previousRearBlend = AverageRearRollingBlend(simulator.State);

        for (int tick = 1; tick <= SecondsToTicks(2.0f); tick++)
        {
            float time = tick * Dt;
            simulator.Update(CreateInput(throttle, steer, brake: 0f, reverse), Dt);

            float yawRate = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
            float heading = MathHelper.ToDegrees(simulator.State.HeadingRadians);
            float yawRateStep = MathF.Abs(yawRate - previousYawRate);
            float headingStep = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(MathHelper.ToRadians(heading - previousHeading))));
            float netYawAcceleration = MathF.Abs(simulator.State.ClassicNaturalYawAccelerationDegreesPerSecondSquared);
            float rearLocalLateral = AverageRearLocalLateral(simulator.State);
            float rearRelaxedForce = AverageRearRelaxedForce(simulator.State);
            float rearBlend = AverageRearRollingBlend(simulator.State);
            float rearLocalLateralStep = MathF.Abs(rearLocalLateral - previousRearLocalLateral);
            float rearRelaxedForceStep = MathF.Abs(rearRelaxedForce - previousRearRelaxedForce);
            float rearBlendStep = MathF.Abs(rearBlend - previousRearBlend);
            if (yawRateStep > maxYawRateStep)
            {
                maxYawRateStep = yawRateStep;
                maxYawRateStepTick = tick;
            }

            if (headingStep > maxHeadingStep)
            {
                maxHeadingStep = headingStep;
                maxHeadingStepTick = tick;
            }

            if (netYawAcceleration > maxNetYawAcceleration)
            {
                maxNetYawAcceleration = netYawAcceleration;
                maxNetYawAccelerationTick = tick;
            }

            if (rearLocalLateralStep > maxRearLocalLateralStep)
            {
                maxRearLocalLateralStep = rearLocalLateralStep;
                maxRearLocalLateralStepTick = tick;
            }

            if (rearRelaxedForceStep > maxRearRelaxedForceStep)
            {
                maxRearRelaxedForceStep = rearRelaxedForceStep;
                maxRearRelaxedForceStepTick = tick;
            }

            if (rearBlendStep > maxRearBlendStep)
            {
                maxRearBlendStep = rearBlendStep;
                maxRearBlendStepTick = tick;
            }

            previousYawRate = yawRate;
            previousHeading = heading;
            previousRearLocalLateral = rearLocalLateral;
            previousRearRelaxedForce = rearRelaxedForce;
            previousRearBlend = rearBlend;

            if (checkpointIndex < CheckpointsSeconds.Length &&
                time + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(label, CheckpointsSeconds[checkpointIndex], simulator.State, parameters);
                checkpointIndex++;
            }
        }

        Console.WriteLine(
            $"  {label,-13} summary maxYawStep={maxYawRateStep:0.000}deg/s@{maxYawRateStepTick * Dt:0.000}s " +
            $"maxHeadingStep={maxHeadingStep:0.000}deg@{maxHeadingStepTick * Dt:0.000}s " +
            $"maxNaturalYawAccel={maxNetYawAcceleration:0.0}deg/s2@{maxNetYawAccelerationTick * Dt:0.000}s " +
            $"maxRearLatStep={maxRearLocalLateralStep:0.000}m/s@{maxRearLocalLateralStepTick * Dt:0.000}s " +
            $"maxRearRelaxStep={maxRearRelaxedForceStep:0.0}N@{maxRearRelaxedForceStepTick * Dt:0.000}s " +
            $"maxRearBlendStep={maxRearBlendStep:0.000}@{maxRearBlendStepTick * Dt:0.000}s " +
            $"finalSpeed={simulator.State.SpeedMetersPerSecond * 3.6f:0.00}km/h finalYaw={MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond):0.00}deg/s " +
            $"finalBeta={simulator.State.ClassicBodySlipAngleDegrees:0.00}deg");
    }

    private static void RunTrueRestCase(
        VehicleSimulationParameters parameters,
        GameLaunchOptions options)
    {
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine);
        for (int tick = 0; tick < SecondsToTicks(1.5f); tick++)
        {
            simulator.Update(new VehicleInput(0.10f, 0f, 0f), Dt);
        }

        float releaseSpeed = simulator.State.SpeedMetersPerSecond;
        float firstBelowDisplayedZeroSeconds = -1f;
        float firstBelowTrueRestSeconds = -1f;
        for (int tick = 1; tick <= SecondsToTicks(12f); tick++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), Dt);
            float time = tick * Dt;
            if (firstBelowDisplayedZeroSeconds < 0f && simulator.State.SpeedMetersPerSecond * 3.6f < 0.5f)
            {
                firstBelowDisplayedZeroSeconds = time;
            }

            if (firstBelowTrueRestSeconds < 0f && simulator.State.SpeedMetersPerSecond < 0.01f)
            {
                firstBelowTrueRestSeconds = time;
            }
        }

        Console.WriteLine(
            $"  true-rest     releaseSpeed={releaseSpeed * 3.6f:0.00}km/h " +
            $"displayZeroAt={FormatSeconds(firstBelowDisplayedZeroSeconds)} " +
            $"trueRestAt={FormatSeconds(firstBelowTrueRestSeconds)} " +
            $"finalSpeed={simulator.State.SpeedMetersPerSecond * 3.6f:0.000}km/h " +
            $"finalForward={simulator.State.SignedForwardSpeed:0.000}m/s finalLat={simulator.State.LateralSpeed:0.000}m/s " +
            $"finalYaw={MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond):0.000}deg/s " +
            $"rearRelax={AverageRearRelaxedForce(simulator.State):0.0}N");
    }

    private static void RunBrakeRestCase(
        VehicleSimulationParameters parameters,
        GameLaunchOptions options)
    {
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine);
        for (int tick = 0; tick < SecondsToTicks(1.5f); tick++)
        {
            simulator.Update(new VehicleInput(0.08f, 0f, 0f), Dt);
        }

        float releaseSpeed = simulator.State.SpeedMetersPerSecond;
        float firstBelowDisplayedZeroSeconds = -1f;
        float firstBelowTrueRestSeconds = -1f;
        float maximumBrakeForce = 0f;
        for (int tick = 1; tick <= SecondsToTicks(4f); tick++)
        {
            simulator.Update(new VehicleInput(0f, 0.45f, 0f), Dt);
            float time = tick * Dt;
            maximumBrakeForce = MathF.Max(maximumBrakeForce, simulator.State.BrakeForce);
            if (firstBelowDisplayedZeroSeconds < 0f && simulator.State.SpeedMetersPerSecond * 3.6f < 0.5f)
            {
                firstBelowDisplayedZeroSeconds = time;
            }

            if (firstBelowTrueRestSeconds < 0f && simulator.State.SpeedMetersPerSecond < 0.01f)
            {
                firstBelowTrueRestSeconds = time;
            }
        }

        Console.WriteLine(
            $"  brake-rest    releaseSpeed={releaseSpeed * 3.6f:0.00}km/h " +
            $"displayZeroAt={FormatSeconds(firstBelowDisplayedZeroSeconds)} " +
            $"trueRestAt={FormatSeconds(firstBelowTrueRestSeconds)} " +
            $"maxBrakeForce={maximumBrakeForce:0}N " +
            $"finalSpeed={simulator.State.SpeedMetersPerSecond * 3.6f:0.000}km/h " +
            $"finalForward={simulator.State.SignedForwardSpeed:0.000}m/s finalYaw={MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond):0.000}deg/s");
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 1;
        simulator.State.Rpm = parameters.IdleRpm;
        return simulator;
    }

    private static void PrintSample(string label, float time, VehicleState state, VehicleSimulationParameters parameters)
    {
        float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float steerRadians = MathHelper.ToRadians(roadAngle);
        float frontLongitudinal = state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN;
        float frontDriveSide = frontLongitudinal * MathF.Sin(steerRadians);
        float frontSlipFy = (state.FrontLeftRelaxedLateralForceN + state.FrontRightRelaxedLateralForceN) * MathF.Cos(steerRadians);
        float rearFy = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontYaw = state.ClassicFrontYawAccelerationDegreesPerSecondSquared;
        float rearYaw = state.ClassicRearYawAccelerationDegreesPerSecondSquared;
        float netYaw = state.ClassicNaturalYawAccelerationDegreesPerSecondSquared;
        float kinematicYaw = MathF.Abs(steerRadians) > 0.0001f
            ? -state.SignedForwardSpeed * MathF.Tan(steerRadians) / MathF.Max(0.25f, parameters.WheelbaseMeters)
            : 0f;
        float yawRatio = MathF.Abs(kinematicYaw) > 0.0001f
            ? state.YawRateRadiansPerSecond / kinematicYaw
            : 0f;

        Console.WriteLine(
            $"  {label,-13} {time,5:0.000} {state.SpeedMetersPerSecond * 3.6f,6:0.00} {state.Throttle,5:0.00} {state.Steer,5:0.00} {roadAngle,7:0.00} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),7:0.00} {MathHelper.ToDegrees(kinematicYaw),7:0.00} {yawRatio,6:0.02} {netYaw,8:0.0} {state.ClassicBodySlipAngleDegrees,7:0.00} " +
            $"{state.SignedForwardSpeed,6:0.00}/{state.LateralSpeed,6:0.00} {frontDriveSide,8:0} {frontSlipFy,8:0} {rearFy,8:0} " +
            $"{frontYaw,8:0.1} {rearYaw,8:0.1} {netYaw,8:0.1} " +
            $"FL({state.FrontLeftLocalForwardSpeedMetersPerSecond,5:0.00}/{state.FrontLeftLocalLateralSpeedMetersPerSecond,5:0.00} {state.FrontLeftSlipAngleDegrees,6:0.0} {state.FrontLeftLowSpeedSlipLateralForceN,7:0} {state.FrontLeftLowSpeedRollingConstraintForceN,7:0} {state.FrontLeftLowSpeedRollingBlend,4:0.00} {state.FrontLeftLowSpeedFinalLateralForceN,7:0} {state.FrontLeftRelaxedLateralForceN,7:0} {state.FrontLeftLongitudinalForceN,7:0}/{state.FrontLeftLateralForceN,7:0}) " +
            $"RL({state.RearLeftLocalForwardSpeedMetersPerSecond,5:0.00}/{state.RearLeftLocalLateralSpeedMetersPerSecond,5:0.00} {state.RearLeftSlipAngleDegrees,6:0.0} {state.RearLeftLowSpeedSlipLateralForceN,7:0} {state.RearLeftLowSpeedRollingConstraintForceN,7:0} {state.RearLeftLowSpeedRollingBlend,4:0.00} {state.RearLeftLowSpeedFinalLateralForceN,7:0} {state.RearLeftRelaxedLateralForceN,7:0} {state.RearLeftLongitudinalForceN,7:0}/{state.RearLeftLateralForceN,7:0})");
    }

    private static float AverageRearLocalLateral(VehicleState state)
    {
        return (state.RearLeftLocalLateralSpeedMetersPerSecond + state.RearRightLocalLateralSpeedMetersPerSecond) * 0.5f;
    }

    private static float AverageRearRelaxedForce(VehicleState state)
    {
        return (state.RearLeftRelaxedLateralForceN + state.RearRightRelaxedLateralForceN) * 0.5f;
    }

    private static float AverageRearRollingBlend(VehicleState state)
    {
        return (state.RearLeftLowSpeedRollingBlend + state.RearRightLowSpeedRollingBlend) * 0.5f;
    }

    private static VehicleInput CreateInput(float throttle, float steer, float brake, bool reverse)
    {
        return reverse
            ? new VehicleInput(0f, brake, steer, reverse: throttle)
            : new VehicleInput(throttle, brake, steer);
    }

    private static string FormatSeconds(float seconds)
    {
        return seconds >= 0f ? $"{seconds:0.000}s" : "never";
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
