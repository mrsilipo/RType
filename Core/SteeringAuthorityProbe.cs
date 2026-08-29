using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class SteeringAuthorityProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Steering authority probe: {parameters.DisplayName}");
        RunDecelTurnInAuthorityCases(parameters, engineParameters);
        RunCoastSteeringDragSanityCases(parameters, engineParameters);
        RunCoastingAuthorityCase(parameters, engineParameters);
        RunThrottleProjectionCase(parameters, engineParameters);
        RunSmallSteerRpmStabilityCase(parameters, engineParameters);
        RunDigitalThrottleSteeringDecouplingCase(parameters, engineParameters);
        Console.WriteLine("Steering authority probe passed.");
    }

    private static void RunCoastSteeringDragSanityCases(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        DecelDragSample straight = MeasureCoastDrag(parameters, engineParameters, steer: 0f);
        DecelDragSample small = MeasureCoastDrag(parameters, engineParameters, steer: 0.18f);
        DecelDragSample medium = MeasureCoastDrag(parameters, engineParameters, steer: 0.35f);

        Console.WriteLine(
            $"150km/h coast drag: straightDrop={straight.SpeedDropKmh:0.00}km/h " +
            $"smallDrop={small.SpeedDropKmh:0.00}km/h mediumDrop={medium.SpeedDropKmh:0.00}km/h " +
            $"smallAngle={small.PeakRoadWheelAngleDegrees:0.00}deg mediumAngle={medium.PeakRoadWheelAngleDegrees:0.00}deg " +
            $"smallLong={small.AverageLongitudinalAcceleration:0.00}m/s2 mediumLong={medium.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"smallScrub={small.PeakScrubForceN:0}N mediumScrub={medium.PeakScrubForceN:0}N " +
            $"smallProj={small.PeakProjectionForceN:0}N mediumProj={medium.PeakProjectionForceN:0}N");

        if (small.SpeedDropKmh > straight.SpeedDropKmh + 9.0f)
        {
            Console.WriteLine(
                $"Steering authority probe warning: small lift-off steering has noticeable scrub. Straight drop {straight.SpeedDropKmh:0.00} km/h, small steer drop {small.SpeedDropKmh:0.00} km/h.");
        }

        if (medium.SpeedDropKmh > straight.SpeedDropKmh + 18.0f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: medium lift-off steering creates excessive scrub braking. Straight drop {straight.SpeedDropKmh:0.00} km/h, medium steer drop {medium.SpeedDropKmh:0.00} km/h.");
        }
    }

    private static void RunDecelTurnInAuthorityCases(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        DecelTurnInSample coast = MeasureDecelTurnIn(parameters, engineParameters, throttle: 0f, brake: 0f, steer: 0.85f, brakeAssistEnabled: false);
        DecelTurnInSample lightBrake = MeasureDecelTurnIn(parameters, engineParameters, throttle: 0f, brake: 0.32f, steer: 0.85f, brakeAssistEnabled: false);
        DecelTurnInSample digitalBrake = MeasureDecelTurnIn(parameters, engineParameters, throttle: 0f, brake: 0.65f, steer: 0.85f, brakeAssistEnabled: true);

        Console.WriteLine(
            $"150km/h coast turn-in: angle25={coast.RoadWheelAngleAtQuarterSecond:0.00}deg yaw25={coast.YawRateAtQuarterSecond:0.0}deg/s " +
            $"heading75={coast.HeadingChangeAtThreeQuarterSecond:0.0}deg frontLat={coast.AverageFrontLateralForceN:0}N rearLat={coast.AverageRearLateralForceN:0}N " +
            $"frontLong={coast.AverageFrontLongitudinalForceN:0}N rearLong={coast.AverageRearLongitudinalForceN:0}N gripPeak={coast.PeakFrontGripUsage:0.00}");
        Console.WriteLine(
            $"150km/h light-brake turn-in: angle25={lightBrake.RoadWheelAngleAtQuarterSecond:0.00}deg yaw25={lightBrake.YawRateAtQuarterSecond:0.0}deg/s " +
            $"heading75={lightBrake.HeadingChangeAtThreeQuarterSecond:0.0}deg frontLat={lightBrake.AverageFrontLateralForceN:0}N rearLat={lightBrake.AverageRearLateralForceN:0}N " +
            $"frontLong={lightBrake.AverageFrontLongitudinalForceN:0}N rearLong={lightBrake.AverageRearLongitudinalForceN:0}N gripPeak={lightBrake.PeakFrontGripUsage:0.00}");
        Console.WriteLine(
            $"150km/h digital-brake turn-in: angle25={digitalBrake.RoadWheelAngleAtQuarterSecond:0.00}deg yaw25={digitalBrake.YawRateAtQuarterSecond:0.0}deg/s " +
            $"heading75={digitalBrake.HeadingChangeAtThreeQuarterSecond:0.0}deg frontLat={digitalBrake.AverageFrontLateralForceN:0}N rearLat={digitalBrake.AverageRearLateralForceN:0}N " +
            $"frontLong={digitalBrake.AverageFrontLongitudinalForceN:0}N rearLong={digitalBrake.AverageRearLongitudinalForceN:0}N gripPeak={digitalBrake.PeakFrontGripUsage:0.00}");

        if (coast.RoadWheelAngleAtQuarterSecond < 28.0f ||
            coast.RoadWheelAngleAtQuarterSecond > 38.0f ||
            MathF.Abs(coast.AverageFrontLateralForceN) < 4500f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: lift-off turn-in is outside the mild speed-scaled window. angle25={coast.RoadWheelAngleAtQuarterSecond:0.00}, frontLat={coast.AverageFrontLateralForceN:0}N.");
        }

        if (coast.HeadingChangeAtThreeQuarterSecond < 6.2f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: lift-off corner entry did not rotate enough by 0.75s ({coast.HeadingChangeAtThreeQuarterSecond:0.0} degrees).");
        }

        if (lightBrake.RoadWheelAngleAtQuarterSecond < 28.0f ||
            lightBrake.RoadWheelAngleAtQuarterSecond > 38.0f ||
            MathF.Abs(lightBrake.AverageFrontLateralForceN) < 4500f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: braking turn-in is outside the mild speed-scaled window. angle25={lightBrake.RoadWheelAngleAtQuarterSecond:0.00}, frontLat={lightBrake.AverageFrontLateralForceN:0}N.");
        }
    }

    private static void RunCoastingAuthorityCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        SimpleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, 40.3f, 4);
        const float dt = 1f / 120f;

        float maxRoadWheelAngle = 0f;
        float maxYawRate = 0f;
        float maxFrontGripUsage = 0f;
        float startHeading = simulator.State.HeadingRadians;

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0.65f), dt);
            VehicleState state = simulator.State;
            maxRoadWheelAngle = MathF.Max(
                maxRoadWheelAngle,
                MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees)));
            maxYawRate = MathF.Max(maxYawRate, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
            maxFrontGripUsage = MathF.Max(maxFrontGripUsage, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage));
        }

        VehicleState end = simulator.State;
        float headingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(end.HeadingRadians - startHeading)));
        Console.WriteLine(
            $"145km/h coast steer: angle={maxRoadWheelAngle:0.00}deg yawPeak={maxYawRate:0.0}deg/s " +
            $"heading={headingChangeDegrees:0.0}deg frontGripPeak={maxFrontGripUsage:0.00} " +
            $"reserve={end.SteeringFrontGripReserve:0.00} committed={end.SteeringCommittedTurnAuthority:0.00} " +
            $"speedCap={end.SteeringSpeedMatchedMaxAngleDegrees:0.00}deg");

        if (maxRoadWheelAngle < 14.0f)
        {
            throw new InvalidOperationException($"Steering authority probe failed: driver-commanded high-speed steering angle stayed too low ({maxRoadWheelAngle:0.00} degrees).");
        }

        if (headingChangeDegrees < 10.0f || maxYawRate < 11f)
        {
            throw new InvalidOperationException($"Steering authority probe failed: coasting high-speed turn did not build enough yaw. Heading {headingChangeDegrees:0.0} degrees, yaw {maxYawRate:0.0} deg/s.");
        }
    }

    private static void RunThrottleProjectionCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        AccelerationSample straight = MeasureAcceleration(parameters, engineParameters, 0f, "straight");
        AccelerationSample corner = MeasureAcceleration(parameters, engineParameters, 0.35f, "committed corner");
        Console.WriteLine(
            $"145km/h throttle: straightAccel={straight.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"cornerAccel={corner.AverageLongitudinalAcceleration:0.00}m/s2 cornerLat={corner.AverageLateralForce:0}N " +
            $"cornerYaw={corner.PeakYawRateDegreesPerSecond:0.0}deg/s clamp={corner.PeakForwardClamp:0}N " +
            $"scrub={corner.PeakScrubForceN:0}N proj={corner.PeakProjectionForceN:0}N rpmIso={corner.PeakRpmScrubIsolation:0.00}");

        if (corner.AverageLongitudinalAcceleration > straight.AverageLongitudinalAcceleration + 0.22f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: turning still out-accelerates straight. Straight {straight.AverageLongitudinalAcceleration:0.00} m/s2, corner {corner.AverageLongitudinalAcceleration:0.00} m/s2.");
        }

        if (MathF.Abs(corner.AverageLateralForce) < 2800f || corner.PeakYawRateDegreesPerSecond < 8f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: throttle corner lost lateral/yaw authority. Lat {corner.AverageLateralForce:0} N, yaw {corner.PeakYawRateDegreesPerSecond:0.0} deg/s.");
        }
    }

    private static void RunSmallSteerRpmStabilityCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        AccelerationSample straight = MeasureAcceleration(parameters, engineParameters, 0f, "straight");
        AccelerationSample smallSteer = MeasureAcceleration(parameters, engineParameters, 0.08f, "small steer");
        Console.WriteLine(
            $"145km/h small steer: straightAccel={straight.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"steerAccel={smallSteer.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"straightRpm={straight.AverageRpm:0} steerRpm={smallSteer.AverageRpm:0} rpmDelta={smallSteer.AverageRpm - straight.AverageRpm:0} " +
            $"steerAngle={smallSteer.PeakRoadWheelAngleDegrees:0.00}deg lat={smallSteer.AverageLateralForce:0}N " +
            $"scrub={smallSteer.PeakScrubForceN:0}N proj={smallSteer.PeakProjectionForceN:0}N rpmIso={smallSteer.PeakRpmScrubIsolation:0.00}");

        if (smallSteer.AverageRpm > straight.AverageRpm + 110f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: small steering input raised RPM too much. Straight {straight.AverageRpm:0}, steer {smallSteer.AverageRpm:0}.");
        }

        float allowedSteeringScrubAccelerationLoss = engineParameters.SteeringAssist.HighSpeedInputCurveExponent <= 1.01f
            ? 0.70f
            : 0.42f;
        if (smallSteer.AverageLongitudinalAcceleration < straight.AverageLongitudinalAcceleration - allowedSteeringScrubAccelerationLoss)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: small steering input damaged acceleration too much. Straight {straight.AverageLongitudinalAcceleration:0.00}, steer {smallSteer.AverageLongitudinalAcceleration:0.00}.");
        }
    }

    private static void RunDigitalThrottleSteeringDecouplingCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        AccelerationSample straight = MeasureAcceleration(parameters, engineParameters, 0f, "digital straight", throttleAssistEnabled: true);
        AccelerationSample smallSteer = MeasureAcceleration(parameters, engineParameters, 0.08f, "digital small steer", throttleAssistEnabled: true);
        Console.WriteLine(
            $"145km/h digital A small steer: straightAccel={straight.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"steerAccel={smallSteer.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"straightRpm={straight.AverageRpm:0} steerRpm={smallSteer.AverageRpm:0} rpmDelta={smallSteer.AverageRpm - straight.AverageRpm:0} " +
            $"straightThr={straight.AverageEffectiveThrottle:0.00} steerThr={smallSteer.AverageEffectiveThrottle:0.00}");

        if (straight.AverageEffectiveThrottle < 0.99f || smallSteer.AverageEffectiveThrottle < 0.99f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: digital A throttle was still being reduced. Straight {straight.AverageEffectiveThrottle:0.00}, steer {smallSteer.AverageEffectiveThrottle:0.00}.");
        }

        if (smallSteer.AverageRpm > straight.AverageRpm + 110f)
        {
            throw new InvalidOperationException(
                $"Steering authority probe failed: digital A small steering input raised RPM too much. Straight {straight.AverageRpm:0}, steer {smallSteer.AverageRpm:0}.");
        }
    }

    private static AccelerationSample MeasureAcceleration(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float steer,
        string label,
        bool throttleAssistEnabled = false)
    {
        SimpleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, 40.3f, 4);
        const float dt = 1f / 120f;
        for (int i = 0; i < 45; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, 0f), dt);
        }

        float sumLongAccel = 0f;
        float sumLatForce = 0f;
        float peakYawRate = 0f;
        float peakForwardClamp = 0f;
        float peakRoadWheelAngle = 0f;
        float sumRpm = 0f;
        float sumEffectiveThrottle = 0f;
        float peakScrubForce = 0f;
        float peakProjectionForce = 0f;
        float peakRpmScrubIsolation = 0f;
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        const int frames = 180;
        for (int i = 0; i < frames; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, steer, throttleAssistEnabled: throttleAssistEnabled), dt);
            VehicleState state = simulator.State;
            sumRpm += state.Rpm;
            sumEffectiveThrottle += state.EffectiveThrottle;
            sumLongAccel += state.LongitudinalAcceleration;
            sumLatForce +=
                state.FrontLeftLateralForceN +
                state.FrontRightLateralForceN +
                state.RearLeftLateralForceN +
                state.RearRightLateralForceN;
            peakYawRate = MathF.Max(peakYawRate, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
            peakForwardClamp = MathF.Max(peakForwardClamp, state.SteeringForwardForceClampN);
            peakRoadWheelAngle = MathF.Max(
                peakRoadWheelAngle,
                MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees)));
            peakScrubForce = MathF.Max(peakScrubForce, state.PeakTyreScrubForceN);
            peakProjectionForce = MathF.Max(peakProjectionForce, state.PeakSteeringProjectionForceN);
            peakRpmScrubIsolation = MathF.Max(peakRpmScrubIsolation, state.RpmScrubIsolationIntensity);
        }

        return new AccelerationSample(
            label,
            sumLongAccel / frames,
            sumLatForce / frames,
            peakYawRate,
            peakForwardClamp,
            peakRoadWheelAngle,
            sumRpm / frames,
            sumEffectiveThrottle / frames,
            peakScrubForce,
            peakProjectionForce,
            peakRpmScrubIsolation,
            (simulator.State.SpeedMetersPerSecond - startSpeed) * 3.6f);
    }

    private static DecelTurnInSample MeasureDecelTurnIn(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float throttle,
        float brake,
        float steer,
        bool brakeAssistEnabled)
    {
        SimpleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, 41.7f, 4);
        const float dt = 1f / 120f;
        for (int i = 0; i < 60; i++)
        {
            simulator.Update(new VehicleInput(0.18f, 0f, 0f), dt);
        }

        float startHeading = simulator.State.HeadingRadians;
        float roadWheelAngleAtQuarter = 0f;
        float yawRateAtQuarter = 0f;
        float headingAtThreeQuarter = 0f;
        float sumFrontLat = 0f;
        float sumFrontLong = 0f;
        float sumRearLat = 0f;
        float sumRearLong = 0f;
        float peakFrontGripUsage = 0f;
        const int frames = 120;
        for (int i = 0; i < frames; i++)
        {
            simulator.Update(new VehicleInput(throttle, brake, steer, brakeAssistEnabled: brakeAssistEnabled), dt);
            VehicleState state = simulator.State;
            sumFrontLat += state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
            sumFrontLong += state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN;
            sumRearLat += state.RearLeftLateralForceN + state.RearRightLateralForceN;
            sumRearLong += state.RearLeftLongitudinalForceN + state.RearRightLongitudinalForceN;
            peakFrontGripUsage = MathF.Max(peakFrontGripUsage, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage));

            if (i == 29)
            {
                roadWheelAngleAtQuarter = MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees));
                yawRateAtQuarter = MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond));
            }

            if (i == 89)
            {
                headingAtThreeQuarter = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(state.HeadingRadians - startHeading)));
            }
        }

        return new DecelTurnInSample(
            roadWheelAngleAtQuarter,
            yawRateAtQuarter,
            headingAtThreeQuarter,
            sumFrontLat / frames,
            sumFrontLong / frames,
            sumRearLat / frames,
            sumRearLong / frames,
            peakFrontGripUsage);
    }

    private static DecelDragSample MeasureCoastDrag(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float steer)
    {
        SimpleVehicleSimulator simulator = CreateSimulator(parameters, engineParameters, 41.7f, 4);
        const float dt = 1f / 120f;
        for (int i = 0; i < 45; i++)
        {
            simulator.Update(new VehicleInput(0.18f, 0f, 0f), dt);
        }

        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        float sumLongAccel = 0f;
        float peakRoadWheelAngle = 0f;
        float peakScrubForce = 0f;
        float peakProjectionForce = 0f;
        const int frames = 120;
        for (int i = 0; i < frames; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, steer), dt);
            VehicleState state = simulator.State;
            sumLongAccel += state.LongitudinalAcceleration;
            peakRoadWheelAngle = MathF.Max(
                peakRoadWheelAngle,
                MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees)));
            peakScrubForce = MathF.Max(peakScrubForce, state.PeakTyreScrubForceN);
            peakProjectionForce = MathF.Max(peakProjectionForce, state.PeakSteeringProjectionForceN);
        }

        float endSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        return new DecelDragSample(
            startSpeedKmh - endSpeedKmh,
            peakRoadWheelAngle,
            sumLongAccel / frames,
            peakScrubForce,
            peakProjectionForce);
    }

    private static SimpleVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedMetersPerSecond,
        int gear)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = gear;
        simulator.State.Velocity = new Vector2(0f, speedMetersPerSecond);
        return simulator;
    }

    private readonly record struct AccelerationSample(
        string Label,
        float AverageLongitudinalAcceleration,
        float AverageLateralForce,
        float PeakYawRateDegreesPerSecond,
        float PeakForwardClamp,
        float PeakRoadWheelAngleDegrees,
        float AverageRpm,
        float AverageEffectiveThrottle,
        float PeakScrubForceN,
        float PeakProjectionForceN,
        float PeakRpmScrubIsolation,
        float SpeedDeltaKmh);

    private readonly record struct DecelTurnInSample(
        float RoadWheelAngleAtQuarterSecond,
        float YawRateAtQuarterSecond,
        float HeadingChangeAtThreeQuarterSecond,
        float AverageFrontLateralForceN,
        float AverageFrontLongitudinalForceN,
        float AverageRearLateralForceN,
        float AverageRearLongitudinalForceN,
        float PeakFrontGripUsage);

    private readonly record struct DecelDragSample(
        float SpeedDropKmh,
        float PeakRoadWheelAngleDegrees,
        float AverageLongitudinalAcceleration,
        float PeakScrubForceN,
        float PeakProjectionForceN);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
