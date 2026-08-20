using Microsoft.Xna.Framework;
using RetroRacer.Data;
using RetroRacer.Vehicle;
using RetroRacer.World;

namespace RetroRacer.Core;

public static class PhysicsSmokeTest
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(options.VehicleDefinitionPath);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(options.SurfaceDefinitionPath);
        VerifySynchronous60HzCadenceDoesNotProjectExtraTick(parameters);
        VerifyRaceSessionTracksSectorsLapsAndInvalidation();
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        if (MathF.Abs(simulator.State.RedlineRpm - parameters.RedlineRpm) > 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: vehicle state redline does not match definition. State {simulator.State.RedlineRpm:0}, definition {parameters.RedlineRpm:0}.");
        }

        float peakSpeed = 0f;
        float speedBeforeBraking = 0f;
        float finalSpeed = 0f;
        float maxSteeringPhaseHeading = 0f;
        const float dt = 1f / 120f;

        for (int i = 0; i < 960; i++)
        {
            float time = i * dt;
            VehicleInput input = time switch
            {
                < 3.0f => new VehicleInput(1f, 0f, 0f),
                < 5.6f => new VehicleInput(1f, 0f, 0.45f),
                < 6.4f => new VehicleInput(0.2f, 0f, -0.30f),
                _ => new VehicleInput(0f, 1f, 0f)
            };

            if (MathF.Abs(time - 6.4f) < dt * 0.5f)
            {
                speedBeforeBraking = simulator.State.SpeedMetersPerSecond;
            }

            simulator.Update(input, dt);
            VehicleState state = simulator.State;
            peakSpeed = MathF.Max(peakSpeed, state.SpeedMetersPerSecond);
            finalSpeed = state.SpeedMetersPerSecond;
            if (time >= 3.0f && time < 5.6f)
            {
                maxSteeringPhaseHeading = MathF.Max(maxSteeringPhaseHeading, MathF.Abs(state.HeadingRadians));
            }

            if (!IsFinite(state.Position.X) ||
                !IsFinite(state.Position.Z) ||
                !IsFinite(state.Velocity.X) ||
                !IsFinite(state.Velocity.Y) ||
                !IsFinite(state.HeadingRadians) ||
                state.FrontLeftLoadN <= 0f ||
                state.FrontRightLoadN <= 0f ||
                state.RearLeftLoadN <= 0f ||
                state.RearRightLoadN <= 0f)
            {
                throw new InvalidOperationException("Physics smoke test failed: invalid numeric state or wheel load.");
            }
        }

        if (peakSpeed < 9.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: car did not accelerate enough. Peak speed {peakSpeed:0.00} m/s.");
        }

        if (maxSteeringPhaseHeading < MathHelper.ToRadians(4f))
        {
            throw new InvalidOperationException("Physics smoke test failed: steering did not create meaningful yaw.");
        }

        if (finalSpeed >= speedBeforeBraking)
        {
            throw new InvalidOperationException("Physics smoke test failed: braking did not reduce speed.");
        }

        float roadCoastDrop = MeasureCoastSpeedDrop(parameters, surfaces.Road);
        float grassCoastDrop = MeasureCoastSpeedDrop(parameters, surfaces.Grass);

        if (roadCoastDrop < 1.0f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: road coast-down was too weak. Speed drop {roadCoastDrop:0.00} m/s.");
        }

        if (grassCoastDrop <= roadCoastDrop + 1.8f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: grass did not slow the car more than road. Road drop {roadCoastDrop:0.00}, grass drop {grassCoastDrop:0.00} m/s.");
        }

        float grassLaunchSpeed = MeasureGrassLaunchFromHighGear(parameters, surfaces.Grass);
        if (grassLaunchSpeed < 2.2f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: automatic transmission could not pull away from grass after stopping. Speed {grassLaunchSpeed * 3.6f:0.0} km/h.");
        }

        VerifyGrassLaunchRpmControl(parameters, surfaces.Grass);
        VerifyRoadLaunchRpmControl(parameters);

        float rightSteerHeading = MeasureSteeringHeading(parameters, 0.45f);
        float leftSteerHeading = MeasureSteeringHeading(parameters, -0.45f);

        if (rightSteerHeading >= -MathHelper.ToRadians(2.0f))
        {
            throw new InvalidOperationException($"Physics smoke test failed: right steering did not produce a right turn. Heading {MathHelper.ToDegrees(rightSteerHeading):0.00} degrees.");
        }

        if (leftSteerHeading <= MathHelper.ToRadians(2.0f))
        {
            throw new InvalidOperationException($"Physics smoke test failed: left steering did not produce a left turn. Heading {MathHelper.ToDegrees(leftSteerHeading):0.00} degrees.");
        }

        VerifySteeringRateLimit(parameters);
        VerifyCornerScrubSlowsCar(parameters);
        VerifySpeedMatchedSteeringResponse(parameters);
        VerifyDigitalThrottleAssistPreservesCornering(parameters);
        VerifyHighSpeedStepSteerIsProgressive(parameters);
        VerifyHighSpeedSteeringKeepsTyreReserve(parameters);
        VerifyHighSpeedSideSlipRecovers(parameters);
        VerifyCounterSteerRecoversHighSpeedSlide(parameters);
        VerifyCounterSteerHelpsLateralSlide(parameters);
        VerifyGtStyleCombinedGripSwitch(parameters);
        VerifyWallCollisionHullMatchesBodyWidth(parameters);
        VerifyWallCollision(parameters);
        VerifyGtStyleWallImpactClampsVelocity(parameters);
        VerifyWallGlanceYawsAwayAndSlides(parameters);
        VerifySuspensionGeometryAffectsTyres(parameters);
        VerifyManualShiftDelay(parameters);
        VerifyManualHighRpmDownshiftIsAccepted(parameters);
        VerifyManualOverRevDownshiftCreatesEngineBraking(parameters);
        VerifyRevLimiter(parameters);
        VerifyEngineBraking(parameters);
        VerifyBrakeHardwareAndAbs(parameters);
        VerifyHardBrakingDoesNotRearLockFirst(parameters);
        VerifyStraightLineBrakingStability(parameters);
        VerifyDigitalBrakeAssistModulatesLocking(parameters);
        VerifyTrailBrakingKeepsSteeringAuthority(parameters);
        VerifyHighSpeedTrailBrakingHasBite(parameters);
        VerifyPostBrakeReleaseTurnKeepsSteeringAuthority(parameters);
        VerifyBrakeOverridesThrottleInHighSpeedTurn(parameters);
        VerifyVehiclePoseTracksWheelGroundContact(parameters);
        VerifyVisualSuspensionUsesFourCornerSupport(parameters);
        VerifyNeutralFreeRevUsesEngineSimulator(parameters);
        VerifyRaceStartHoldAllowsRevsBeforeTraction(parameters);
        VerifyPreRevLaunchUsesSlippingClutch(parameters);
        VerifyAcceleratorRegatesReverseToFirst(parameters);
        VerifyWallScrapePreservesMomentum(parameters);
        VerifyWallContactDoesNotTrapCar(parameters);
    }

    private static void VerifySynchronous60HzCadenceDoesNotProjectExtraTick(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 60f;
        float highestAlpha = 0f;
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
            highestAlpha = MathF.Max(highestAlpha, simulator.State.PhysicsTickAlpha);
        }

        if (highestAlpha > 0.001f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: 60 Hz game loop left stale physics interpolation alpha {highestAlpha:0.0000}.");
        }
    }

    private static void VerifyRaceSessionTracksSectorsLapsAndInvalidation()
    {
        RaceSession session = new(new ScriptedProgressTrack(), 2);
        VehicleState vehicle = new()
        {
            SurfaceName = "ROAD",
            Velocity = new Vector2(24f, 0f)
        };

        vehicle.Position = new Vector3(0.00f, 0f, 0f);
        session.Update(vehicle, TimeSpan.Zero);
        vehicle.Position = new Vector3(0.34f, 0f, 0f);
        session.Update(vehicle, TimeSpan.FromSeconds(10));
        vehicle.Position = new Vector3(0.67f, 0f, 0f);
        session.Update(vehicle, TimeSpan.FromSeconds(10));
        vehicle.Position = new Vector3(0.02f, 0f, 0f);
        session.Update(vehicle, TimeSpan.FromSeconds(10));

        if (session.State.CompletedLaps != 1 ||
            session.State.CurrentLap != 2 ||
            session.State.LastLapTime != TimeSpan.FromSeconds(30) ||
            session.State.BestLapTime != TimeSpan.FromSeconds(30) ||
            session.State.LastLapWasValid == false)
        {
            throw new InvalidOperationException("Physics smoke test failed: race session did not record a valid timed lap.");
        }

        vehicle.SurfaceName = "GRASS";
        vehicle.Position = new Vector3(0.34f, 0f, 0f);
        session.Update(vehicle, TimeSpan.FromSeconds(10));
        vehicle.SurfaceName = "ROAD";
        vehicle.Position = new Vector3(0.67f, 0f, 0f);
        session.Update(vehicle, TimeSpan.FromSeconds(10));
        vehicle.Position = new Vector3(0.02f, 0f, 0f);
        session.Update(vehicle, TimeSpan.FromSeconds(10));

        if (!session.State.Finished ||
            session.State.CompletedLaps != 2 ||
            session.State.LastLapWasValid ||
            session.State.BestLapTime != TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("Physics smoke test failed: race session did not preserve invalid lap state or finish correctly.");
        }

        RaceSession wrongWaySession = new(new ScriptedProgressTrack(), 1);
        vehicle = new VehicleState
        {
            SurfaceName = "ROAD",
            Velocity = new Vector2(-12f, 0f),
            Position = new Vector3(0.50f, 0f, 0f)
        };
        wrongWaySession.Update(vehicle, TimeSpan.Zero);
        for (int i = 0; i < 8; i++)
        {
            vehicle.Position = new Vector3(0.49f - i * 0.01f, 0f, 0f);
            wrongWaySession.Update(vehicle, TimeSpan.FromSeconds(0.1));
        }

        if (!wrongWaySession.State.WrongWay || !wrongWaySession.State.CurrentLapInvalid)
        {
            throw new InvalidOperationException("Physics smoke test failed: race session did not flag wrong-way driving.");
        }
    }

    private static float MeasureCoastSpeedDrop(VehicleSimulationParameters parameters, SurfaceSample surface)
    {
        MutableSurfaceSampler surfaceSampler = new(new SurfaceSample("ROAD", 1.0f, 1.0f, 0.0f, 0.0f));
        SimpleVehicleSimulator simulator = new(
            surfaceSampler,
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 720; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        surfaceSampler.Surface = surface;
        float coastStartSpeed = simulator.State.SpeedMetersPerSecond;
        for (int i = 0; i < 480; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        return coastStartSpeed - simulator.State.SpeedMetersPerSecond;
    }

    private static float MeasureGrassLaunchFromHighGear(VehicleSimulationParameters parameters, SurfaceSample grass)
    {
        SimpleVehicleSimulator simulator = new(
            new MutableSurfaceSampler(grass),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        simulator.State.Gear = parameters.ForwardGearRatios.Length;
        simulator.State.Rpm = parameters.IdleRpm;

        const float dt = 1f / 120f;
        simulator.Update(new VehicleInput(1f, 0f, 0f), 1f / 60f);
        if (!simulator.State.IsShifting && simulator.State.Gear != 1)
        {
            throw new InvalidOperationException($"Physics smoke test failed: automatic transmission did not begin selecting first gear for a low-speed grass launch. Gear {simulator.State.Gear}.");
        }

        int shiftSteps = Math.Max(2, (int)MathF.Ceiling(parameters.AutomaticShiftTimeSeconds / dt) + 4);
        for (int i = 0; i < shiftSteps; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        if (simulator.State.Gear != 1)
        {
            throw new InvalidOperationException($"Physics smoke test failed: automatic transmission did not select first gear for a low-speed grass launch. Gear {simulator.State.Gear}.");
        }

        for (int i = 1; i < 360; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        return simulator.State.SpeedMetersPerSecond;
    }

    private static void VerifyGrassLaunchRpmControl(VehicleSimulationParameters parameters, SurfaceSample grass)
    {
        SimpleVehicleSimulator simulator = new(
            new MutableSurfaceSampler(grass),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        float maxRpm = 0f;
        bool limiterActivated = false;
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
            maxRpm = MathF.Max(maxRpm, simulator.State.Rpm);
            limiterActivated |= simulator.State.RevLimiterActive;
        }

        if (limiterActivated || maxRpm > parameters.UpshiftRpm * 0.92f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: grass launch RPM flared too much. Max RPM {maxRpm:0}, upshift {parameters.UpshiftRpm:0}, limiter active {limiterActivated}.");
        }
    }

    private static void VerifyRoadLaunchRpmControl(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        float maxRpm = 0f;
        bool limiterActivated = false;
        for (int i = 0; i < 150; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
            maxRpm = MathF.Max(maxRpm, simulator.State.Rpm);
            limiterActivated |= simulator.State.RevLimiterActive;
        }

        if (limiterActivated || maxRpm > parameters.RedlineRpm * 0.88f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: road launch RPM reached the limiter region too quickly. Max RPM {maxRpm:0}, limiter {parameters.RedlineRpm:0}, limiter active {limiterActivated}.");
        }
    }

    private static float MeasureSteeringHeading(VehicleSimulationParameters parameters, float steer)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        for (int i = 0; i < 120; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, steer), dt);
        }

        return simulator.State.HeadingRadians;
    }

    private static void VerifySteeringRateLimit(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 60f;
        simulator.Update(new VehicleInput(0f, 0f, 1f), dt);

        float expectedMax = parameters.SteeringInputRatePerSecond * dt + 0.025f;
        if (simulator.State.Steer <= 0f || simulator.State.Steer > expectedMax)
        {
            throw new InvalidOperationException($"Physics smoke test failed: steering input was not rate-limited. Steer {simulator.State.Steer:0.000}, expected <= {expectedMax:0.000}.");
        }
    }

    private static void VerifyCornerScrubSlowsCar(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator straight = CreateAcceleratedSimulator(parameters);
        SimpleVehicleSimulator cornering = CreateAcceleratedSimulator(parameters);
        straight.SetManualTransmission(true);
        cornering.SetManualTransmission(true);
        straight.State.Gear = 0;
        cornering.State.Gear = 0;

        float straightStart = straight.State.SpeedMetersPerSecond;
        float cornerStart = cornering.State.SpeedMetersPerSecond;
        const float dt = 1f / 120f;

        for (int i = 0; i < 300; i++)
        {
            straight.Update(new VehicleInput(0f, 0f, 0f), dt);
            cornering.Update(new VehicleInput(0f, 0f, 0.85f), dt);
        }

        float straightDrop = straightStart - straight.State.SpeedMetersPerSecond;
        float cornerDrop = cornerStart - cornering.State.SpeedMetersPerSecond;
        if (cornerDrop <= straightDrop + 0.15f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: corner scrub did not slow the car more than straight coasting. Corner drop {cornerDrop:0.00}, straight drop {straightDrop:0.00}.");
        }
    }

    private static void VerifySpeedMatchedSteeringResponse(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, 16.7f, dt);
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float startHeading = simulator.State.HeadingRadians;

        for (int i = 0; i < 144; i++)
        {
            simulator.Update(new VehicleInput(0.22f, 0f, 0.65f), dt);
        }

        float headingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(simulator.State.HeadingRadians - startHeading)));
        float speedRetained = startSpeed > 0.1f ? simulator.State.SpeedMetersPerSecond / startSpeed : 0f;

        if (headingChangeDegrees < 24f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: speed-matched steering response was too lazy. Heading change {headingChangeDegrees:0.0} degrees.");
        }

        if (speedRetained < 0.90f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: speed-matched steering scrubbed too much speed. Retained {speedRetained * 100f:0.0}%.");
        }
    }

    private static void VerifyDigitalThrottleAssistPreservesCornering(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator assisted = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        SimpleVehicleSimulator raw = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(assisted, 27.8f, dt);
        DriveToSpeed(raw, 27.8f, dt);

        float assistedStartSpeed = assisted.State.SpeedMetersPerSecond;
        float assistedStartHeading = assisted.State.HeadingRadians;
        float rawStartHeading = raw.State.HeadingRadians;
        float minimumAssistedThrottle = 1f;

        for (int i = 0; i < 144; i++)
        {
            assisted.Update(new VehicleInput(1f, 0f, 0.65f, throttleAssistEnabled: true), dt);
            raw.Update(new VehicleInput(1f, 0f, 0.65f), dt);
            minimumAssistedThrottle = MathF.Min(minimumAssistedThrottle, assisted.State.EffectiveThrottle);
        }

        float assistedHeadingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(assisted.State.HeadingRadians - assistedStartHeading)));
        float rawHeadingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(raw.State.HeadingRadians - rawStartHeading)));
        float speedDelta = assisted.State.SpeedMetersPerSecond - assistedStartSpeed;

        if (assistedHeadingChangeDegrees < 27.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: digital throttle assist still understeers under power. Heading change {assistedHeadingChangeDegrees:0.0} degrees.");
        }

        if (assistedHeadingChangeDegrees < rawHeadingChangeDegrees - 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: digital throttle assist made powered cornering worse. Assisted {assistedHeadingChangeDegrees:0.0} degrees, raw {rawHeadingChangeDegrees:0.0} degrees.");
        }

        if (speedDelta < 0.35f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: digital throttle assist cut too much acceleration. Speed delta {speedDelta:0.00} m/s.");
        }

        if (minimumAssistedThrottle > 0.94f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: digital throttle assist did not modulate throttle. Minimum throttle {minimumAssistedThrottle:0.00}.");
        }
    }

    private static void VerifyHighSpeedStepSteerIsProgressive(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 40f, dt);

        if (simulator.State.SpeedMetersPerSecond < 35f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: could not reach high-speed steering test speed. Speed {simulator.State.SpeedMetersPerSecond * 3.6f:0.0} km/h.");
        }

        float maximumYawRateDegreesPerSecond = 0f;
        float maximumLateralSpeedMetersPerSecond = 0f;
        for (int i = 0; i < 48; i++)
        {
            simulator.Update(new VehicleInput(0.15f, 0f, -1f), dt);
            maximumYawRateDegreesPerSecond = MathF.Max(
                maximumYawRateDegreesPerSecond,
                MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)));
            maximumLateralSpeedMetersPerSecond = MathF.Max(
                maximumLateralSpeedMetersPerSecond,
                MathF.Abs(simulator.State.LateralSpeed));
        }

        if (maximumYawRateDegreesPerSecond > 42f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed step steering builds yaw too quickly. Peak yaw {maximumYawRateDegreesPerSecond:0.0} deg/s.");
        }

        if (maximumLateralSpeedMetersPerSecond > 4.2f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed step steering creates side-slip too quickly. Peak lateral speed {maximumLateralSpeedMetersPerSecond:0.0} m/s.");
        }
    }

    private static void VerifyHighSpeedSteeringKeepsTyreReserve(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 0;
        simulator.State.Velocity = new Vector2(0f, 44f);

        const float dt = 1f / 60f;
        int allTyreSaturationFrames = 0;
        float maximumRoadWheelAngleDegrees = 0f;
        float maximumLateralSpeedMetersPerSecond = 0f;
        float maximumAverageGripUsage = 0f;
        float maximumYawRateDegreesPerSecond = 0f;

        for (int i = 0; i < 21; i++)
        {
            float steer = i < 8 ? -1f : 0f;
            simulator.Update(new VehicleInput(0f, 0f, steer), dt);
            VehicleState state = simulator.State;
            float roadWheelAngle = MathF.Max(
                MathF.Abs(state.FrontLeftSteerAngleDegrees),
                MathF.Abs(state.FrontRightSteerAngleDegrees));
            float averageGripUsage = (state.FrontLeftGripUsage + state.FrontRightGripUsage + state.RearLeftGripUsage + state.RearRightGripUsage) * 0.25f;
            maximumRoadWheelAngleDegrees = MathF.Max(maximumRoadWheelAngleDegrees, roadWheelAngle);
            maximumLateralSpeedMetersPerSecond = MathF.Max(maximumLateralSpeedMetersPerSecond, MathF.Abs(state.LateralSpeed));
            maximumAverageGripUsage = MathF.Max(maximumAverageGripUsage, averageGripUsage);
            maximumYawRateDegreesPerSecond = MathF.Max(
                maximumYawRateDegreesPerSecond,
                MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));

            if (state.FrontLeftGripUsage > 0.98f &&
                state.FrontRightGripUsage > 0.98f &&
                state.RearLeftGripUsage > 0.98f &&
                state.RearRightGripUsage > 0.98f)
            {
                allTyreSaturationFrames++;
            }
        }

        if (maximumRoadWheelAngleDegrees < 3.6f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed steering did not allow enough road-wheel angle. Peak {maximumRoadWheelAngleDegrees:0.00} degrees.");
        }

        if (maximumRoadWheelAngleDegrees > 6.2f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed steering allowed too much road-wheel angle. Peak {maximumRoadWheelAngleDegrees:0.00} degrees.");
        }

        if (maximumYawRateDegreesPerSecond < 16f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed steering did not build enough yaw. Peak yaw {maximumYawRateDegreesPerSecond:0.0} deg/s.");
        }

        if (allTyreSaturationFrames > 8)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed steering saturated all four tyres. Frames {allTyreSaturationFrames}, peak average grip {maximumAverageGripUsage:0.00}.");
        }

        if (maximumLateralSpeedMetersPerSecond > 5.8f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed steering created too much whole-car side slip. Peak lateral speed {maximumLateralSpeedMetersPerSecond:0.00} m/s.");
        }
    }

    private static void VerifyHighSpeedSideSlipRecovers(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 36f, dt);

        Vector2 forward = new(MathF.Sin(simulator.State.HeadingRadians), MathF.Cos(simulator.State.HeadingRadians));
        Vector2 right = new(MathF.Cos(simulator.State.HeadingRadians), -MathF.Sin(simulator.State.HeadingRadians));
        simulator.State.Velocity = forward * 34f - right * 14f;
        simulator.State.YawRateRadiansPerSecond = MathHelper.ToRadians(48f);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 0;

        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float startLateralSpeed = MathF.Abs(Vector2.Dot(simulator.State.Velocity, right));
        float startYawRate = MathF.Abs(simulator.State.YawRateRadiansPerSecond);

        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        float endLateralSpeed = MathF.Abs(simulator.State.LateralSpeed);
        float endYawRate = MathF.Abs(simulator.State.YawRateRadiansPerSecond);
        if (endLateralSpeed > startLateralSpeed * 0.62f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed side-slip did not settle. Start {startLateralSpeed:0.00} m/s, end {endLateralSpeed:0.00} m/s.");
        }

        if (endYawRate > startYawRate * 0.45f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed yaw did not damp. Start {MathHelper.ToDegrees(startYawRate):0.0} deg/s, end {MathHelper.ToDegrees(endYawRate):0.0} deg/s.");
        }

        if (simulator.State.SpeedMetersPerSecond > startSpeed - 3.0f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed slide did not scrub speed. Start {startSpeed:0.00} m/s, end {simulator.State.SpeedMetersPerSecond:0.00} m/s.");
        }
    }

    private static void VerifyCounterSteerRecoversHighSpeedSlide(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 34f, dt);

        Vector2 forward = new(MathF.Sin(simulator.State.HeadingRadians), MathF.Cos(simulator.State.HeadingRadians));
        Vector2 right = new(MathF.Cos(simulator.State.HeadingRadians), -MathF.Sin(simulator.State.HeadingRadians));
        simulator.State.Velocity = forward * 30f + right * 8f;
        simulator.State.YawRateRadiansPerSecond = MathHelper.ToRadians(42f);

        float startLateralSpeed = MathF.Abs(Vector2.Dot(simulator.State.Velocity, right));
        float startYawRate = MathF.Abs(simulator.State.YawRateRadiansPerSecond);
        int saturatedFrames = 0;

        for (int i = 0; i < 120; i++)
        {
            float counterSteer = MathF.Sign(simulator.State.YawRateRadiansPerSecond);
            if (counterSteer == 0f)
            {
                counterSteer = 1f;
            }

            simulator.Update(new VehicleInput(0.55f, 0f, counterSteer, throttleAssistEnabled: true), dt);
            VehicleState state = simulator.State;
            if (state.FrontLeftGripUsage > 0.98f &&
                state.FrontRightGripUsage > 0.98f &&
                state.RearLeftGripUsage > 0.98f &&
                state.RearRightGripUsage > 0.98f)
            {
                saturatedFrames++;
            }
        }

        float endLateralSpeed = MathF.Abs(simulator.State.LateralSpeed);
        float endYawRate = MathF.Abs(simulator.State.YawRateRadiansPerSecond);
        if (endLateralSpeed > startLateralSpeed * 0.52f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: counter-steer did not recover side-slip. Start {startLateralSpeed:0.00} m/s, end {endLateralSpeed:0.00} m/s.");
        }

        if (endYawRate > startYawRate * 0.36f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: counter-steer did not recover yaw. Start {MathHelper.ToDegrees(startYawRate):0.0} deg/s, end {MathHelper.ToDegrees(endYawRate):0.0} deg/s.");
        }

        if (saturatedFrames > 24)
        {
            throw new InvalidOperationException($"Physics smoke test failed: counter-steer stayed in four-tyre saturation for too long. Frames {saturatedFrames}.");
        }
    }

    private static void VerifyCounterSteerHelpsLateralSlide(VehicleSimulationParameters parameters)
    {
        LateralSlideRecoveryResult counterSteer = MeasureLateralSlideRecovery(parameters, 0.65f);
        LateralSlideRecoveryResult neutral = MeasureLateralSlideRecovery(parameters, 0f);
        LateralSlideRecoveryResult withSlide = MeasureLateralSlideRecovery(parameters, -0.65f);

        if (counterSteer.PeakRecoveryIntensity < 0.28f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: lateral slide counter-steer did not trigger recovery. Peak {counterSteer.PeakRecoveryIntensity:0.00}.");
        }

        if (counterSteer.FirstLowLateralSpeedFrame < 0)
        {
            throw new InvalidOperationException($"Physics smoke test failed: lateral slide counter-steer never caught the side speed. Start {counterSteer.StartLateralSpeedMetersPerSecond:0.00} m/s, best {counterSteer.MinimumLateralSpeedMetersPerSecond:0.00} m/s, end {counterSteer.EndLateralSpeedMetersPerSecond:0.00} m/s.");
        }

        if (neutral.FirstLowLateralSpeedFrame >= 0 &&
            counterSteer.FirstLowLateralSpeedFrame > neutral.FirstLowLateralSpeedFrame)
        {
            throw new InvalidOperationException($"Physics smoke test failed: lateral slide counter-steer did not catch the slide earlier than neutral. Counter frame {counterSteer.FirstLowLateralSpeedFrame}, neutral frame {neutral.FirstLowLateralSpeedFrame}, with-slide frame {withSlide.FirstLowLateralSpeedFrame}.");
        }

        if (withSlide.FirstLowLateralSpeedFrame >= 0 &&
            counterSteer.FirstLowLateralSpeedFrame > withSlide.FirstLowLateralSpeedFrame + 8)
        {
            throw new InvalidOperationException($"Physics smoke test failed: lateral slide counter-steer lagged too far behind steering with the slide. Counter frame {counterSteer.FirstLowLateralSpeedFrame}, with-slide frame {withSlide.FirstLowLateralSpeedFrame}, neutral frame {neutral.FirstLowLateralSpeedFrame}.");
        }
    }

    private static LateralSlideRecoveryResult MeasureLateralSlideRecovery(VehicleSimulationParameters parameters, float steer)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 34f, dt);

        Vector2 forward = new(MathF.Sin(simulator.State.HeadingRadians), MathF.Cos(simulator.State.HeadingRadians));
        Vector2 right = new(MathF.Cos(simulator.State.HeadingRadians), -MathF.Sin(simulator.State.HeadingRadians));
        simulator.State.Velocity = forward * 30f - right * 8f;
        simulator.State.YawRateRadiansPerSecond = 0f;

        float startLateralSpeed = MathF.Abs(Vector2.Dot(simulator.State.Velocity, right));
        float minimumLateralSpeed = startLateralSpeed;
        float peakRecoveryIntensity = 0f;
        int firstLowLateralSpeedFrame = -1;

        for (int i = 0; i < 120; i++)
        {
            simulator.Update(new VehicleInput(0.2f, 0f, steer, throttleAssistEnabled: true), dt);
            peakRecoveryIntensity = MathF.Max(peakRecoveryIntensity, simulator.State.CounterSteerRecoveryIntensity);
            VehicleState state = simulator.State;
            float absLateralSpeed = MathF.Abs(state.LateralSpeed);
            minimumLateralSpeed = MathF.Min(minimumLateralSpeed, absLateralSpeed);
            if (firstLowLateralSpeedFrame < 0 && absLateralSpeed <= 3.0f)
            {
                firstLowLateralSpeedFrame = i;
            }
        }

        return new LateralSlideRecoveryResult(
            startLateralSpeed,
            MathF.Abs(simulator.State.LateralSpeed),
            minimumLateralSpeed,
            firstLowLateralSpeedFrame,
            peakRecoveryIntensity);
    }

    private readonly struct LateralSlideRecoveryResult
    {
        public LateralSlideRecoveryResult(
            float startLateralSpeedMetersPerSecond,
            float endLateralSpeedMetersPerSecond,
            float minimumLateralSpeedMetersPerSecond,
            int firstLowLateralSpeedFrame,
            float peakRecoveryIntensity)
        {
            StartLateralSpeedMetersPerSecond = startLateralSpeedMetersPerSecond;
            EndLateralSpeedMetersPerSecond = endLateralSpeedMetersPerSecond;
            MinimumLateralSpeedMetersPerSecond = minimumLateralSpeedMetersPerSecond;
            FirstLowLateralSpeedFrame = firstLowLateralSpeedFrame;
            PeakRecoveryIntensity = peakRecoveryIntensity;
        }

        public float StartLateralSpeedMetersPerSecond { get; }

        public float EndLateralSpeedMetersPerSecond { get; }

        public float MinimumLateralSpeedMetersPerSecond { get; }

        public int FirstLowLateralSpeedFrame { get; }

        public float PeakRecoveryIntensity { get; }
    }

    private static void VerifyWallCollision(VehicleSimulationParameters parameters)
    {
        const float wallHalfWidth = 5.0f;
        SimpleVehicleSimulator simulator = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - parameters.BodyWidthMeters * 0.35f, 0.06f, 0f),
            0f,
            parameters);

        simulator.State.Velocity = new Vector2(12f, 5f);

        const float dt = 1f / 120f;
        float maximumImpactSpeedKph = 0f;
        for (int i = 0; i < 24; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
            maximumImpactSpeedKph = MathF.Max(maximumImpactSpeedKph, simulator.State.LastImpactSpeedKph);
        }

        float maximumAllowedCenterX = wallHalfWidth - parameters.BodyWidthMeters * 0.5f + 0.16f;
        if (simulator.State.Position.X > maximumAllowedCenterX)
        {
            throw new InvalidOperationException($"Physics smoke test failed: car crossed the wall boundary. X {simulator.State.Position.X:0.00}, max {maximumAllowedCenterX:0.00}.");
        }

        if (simulator.State.Velocity.X > 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall collision did not stop outward velocity. VX {simulator.State.Velocity.X:0.00}.");
        }

        if (!simulator.State.CollisionActive || maximumImpactSpeedKph <= 5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall impact state was not reported. Active {simulator.State.CollisionActive}, impact {maximumImpactSpeedKph:0.0} km/h.");
        }
    }

    private static void VerifyGtStyleCombinedGripSwitch(VehicleSimulationParameters parameters)
    {
        MutableSurfaceSampler surfaceSampler = new(new SurfaceSample("ROAD", 1.0f));
        SimpleVehicleSimulator simulator = new(
            surfaceSampler,
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 34f, dt);
        surfaceSampler.Surface = new SurfaceSample("LOW_GRIP", 0.62f);

        float maxGripUsage = 0f;
        float highestStaticRatio = 0f;
        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(0f, 0.85f, 0.95f), dt);
            VehicleState state = simulator.State;
            maxGripUsage = MathF.Max(
                maxGripUsage,
                MathF.Max(
                    MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
                    MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage)));
            highestStaticRatio = MathF.Max(
                highestStaticRatio,
                CalculateForceToGtGripLimitRatio(parameters, parameters.FrontTyres, state.FrontLeftSurfaceGrip, state.FrontLeftLoadN, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN, state.FrontLeftSlipAngleDegrees, isFront: true, braking: true));
            highestStaticRatio = MathF.Max(
                highestStaticRatio,
                CalculateForceToGtGripLimitRatio(parameters, parameters.FrontTyres, state.FrontRightSurfaceGrip, state.FrontRightLoadN, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN, state.FrontRightSlipAngleDegrees, isFront: true, braking: true));
            highestStaticRatio = MathF.Max(
                highestStaticRatio,
                CalculateForceToGtGripLimitRatio(parameters, parameters.RearTyres, state.RearLeftSurfaceGrip, state.RearLeftLoadN, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN, state.RearLeftSlipAngleDegrees, isFront: false, braking: true));
            highestStaticRatio = MathF.Max(
                highestStaticRatio,
                CalculateForceToGtGripLimitRatio(parameters, parameters.RearTyres, state.RearRightSurfaceGrip, state.RearRightLoadN, state.RearRightLongitudinalForceN, state.RearRightLateralForceN, state.RearRightSlipAngleDegrees, isFront: false, braking: true));
        }

        if (maxGripUsage < 1.02f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: GT-style grip switch did not cross the static grip boundary. Usage {maxGripUsage:0.00}.");
        }

        if (highestStaticRatio > 1.04f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: GT-style grip switch exceeded the grip-table limit after saturation. Force/limit ratio {highestStaticRatio:0.00}.");
        }
    }

    private static float CalculateForceToGtGripLimitRatio(
        VehicleSimulationParameters parameters,
        TyreAxleParameters tyres,
        float surfaceGrip,
        float normalLoadN,
        float longitudinalForceN,
        float lateralForceN,
        float slipAngleDegrees,
        bool isFront,
        bool braking)
    {
        float referenceLoad = parameters.MassKg * 9.81f * 0.25f;
        float loadSensitivity = MathHelper.Clamp(tyres.LoadSensitivity, 0f, 0.35f);
        float loadScale = MathF.Pow(referenceLoad / MathF.Max(150f, normalLoadN), loadSensitivity);
        loadScale = MathHelper.Clamp(loadScale, 0.72f, 1.18f);
        float staticGripLimit = tyres.PeakFriction * loadScale * surfaceGrip * MathF.Max(0f, normalLoadN);
        float lateralDemand = SmoothStep(0.05f, 0.20f, MathF.Abs(MathHelper.ToRadians(slipAngleDegrees)));
        float allowance = braking
            ? 0.46f
            : isFront ? 0.34f : 0.05f;
        float gtGripLimit = staticGripLimit * (1f + lateralDemand * allowance);
        float force = MathF.Sqrt(longitudinalForceN * longitudinalForceN + lateralForceN * lateralForceN);
        return gtGripLimit > 1f ? force / gtGripLimit : 0f;
    }

    private static void VerifyWallCollisionHullMatchesBodyWidth(VehicleSimulationParameters parameters)
    {
        const float wallHalfWidth = 5.0f;
        float bodyHalfWidth = parameters.BodyWidthMeters * 0.5f;
        const float visibleClearance = 0.025f;
        SimpleVehicleSimulator clear = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - bodyHalfWidth - visibleClearance, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 60f;
        clear.Update(new VehicleInput(0f, 0f, 0f), dt);
        if (clear.State.WallContactCount != 0 ||
            clear.State.Position.X > wallHalfWidth - bodyHalfWidth - visibleClearance * 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall collision hull triggered before visible body contact. Contacts {clear.State.WallContactCount}, X {clear.State.Position.X:0.000}.");
        }

        SimpleVehicleSimulator overlapping = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - bodyHalfWidth + visibleClearance, 0.06f, 0f),
            0f,
            parameters);

        overlapping.Update(new VehicleInput(0f, 0f, 0f), dt);
        if (overlapping.State.WallContactCount == 0 ||
            overlapping.State.Position.X > wallHalfWidth - bodyHalfWidth + 0.01f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall collision hull did not catch visible body overlap. Contacts {overlapping.State.WallContactCount}, X {overlapping.State.Position.X:0.000}.");
        }
    }

    private static void VerifyGtStyleWallImpactClampsVelocity(VehicleSimulationParameters parameters)
    {
        const float wallHalfWidth = 5.0f;
        float bodyHalfWidth = parameters.BodyWidthMeters * 0.5f;
        SimpleVehicleSimulator simulator = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - bodyHalfWidth + 0.04f, 0.06f, 0f),
            0f,
            parameters);

        simulator.State.Velocity = new Vector2(12f, 0f);
        simulator.Update(new VehicleInput(0f, 0f, 0f), 1f / 60f);

        if (simulator.State.Velocity.X > -1.4f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: GT-style wall impact did not reflect outward strongly enough. VX {simulator.State.Velocity.X:0.00}.");
        }

        if (simulator.State.Velocity.X < -2.9f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: GT-style wall impact bounced away too aggressively. VX {simulator.State.Velocity.X:0.00}.");
        }

        if (simulator.State.SpeedMetersPerSecond > 3.8f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: GT-style wall impact did not clamp speed harshly enough. Speed {simulator.State.SpeedMetersPerSecond:0.00} m/s.");
        }
    }

    private static void VerifyWallGlanceYawsAwayAndSlides(VehicleSimulationParameters parameters)
    {
        const float wallHalfWidth = 5.0f;
        SimpleVehicleSimulator simulator = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - parameters.BodyWidthMeters * 0.5f - 0.15f, 0.06f, 0f),
            MathHelper.ToRadians(6f),
            parameters);
        SimpleVehicleSimulator baseline = new(
            new FlatSurfaceSampler(),
            new Vector3(wallHalfWidth - parameters.BodyWidthMeters * 0.5f - 0.15f, 0.06f, 0f),
            MathHelper.ToRadians(6f),
            parameters);

        simulator.State.Velocity = new Vector2(3.2f, 24f);
        baseline.State.Velocity = simulator.State.Velocity;

        const float dt = 1f / 120f;
        for (int i = 0; i < 8; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
            baseline.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        float maximumAllowedCenterX = wallHalfWidth - parameters.BodyWidthMeters * 0.5f + 0.16f;
        if (simulator.State.Position.X > maximumAllowedCenterX)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall glance let the car cross the boundary. X {simulator.State.Position.X:0.00}.");
        }

        float wallYawRate = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float baselineYawRate = MathHelper.ToDegrees(baseline.State.YawRateRadiansPerSecond);
        if (wallYawRate > baselineYawRate - 3f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall glance pulled the nose into the wall. Wall yaw {wallYawRate:0.0} deg/s, no-wall yaw {baselineYawRate:0.0} deg/s.");
        }

        if (simulator.State.Velocity.Y < 20f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall glance gripped instead of sliding. Tangential VY {simulator.State.Velocity.Y:0.00} m/s.");
        }
    }

    private static void VerifySuspensionGeometryAffectsTyres(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0.35f, 0f, 0.85f), dt);
        }

        VehicleState state = simulator.State;
        float frontCamberSplit = MathF.Abs(state.FrontLeftCamberDegrees - state.FrontRightCamberDegrees);
        float rearCamberSplit = MathF.Abs(state.RearLeftCamberDegrees - state.RearRightCamberDegrees);
        float toeMagnitude = MathF.Abs(state.FrontLeftToeDegrees) +
                             MathF.Abs(state.FrontRightToeDegrees) +
                             MathF.Abs(state.RearLeftToeDegrees) +
                             MathF.Abs(state.RearRightToeDegrees);

        if (frontCamberSplit < 0.05f && rearCamberSplit < 0.05f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: suspension geometry did not create dynamic camber split. Front {frontCamberSplit:0.000}, rear {rearCamberSplit:0.000}.");
        }

        if (toeMagnitude <= 0.01f)
        {
            throw new InvalidOperationException("Physics smoke test failed: wheel toe alignment did not reach the tyre model.");
        }
    }

    private static void VerifyManualShiftDelay(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 60f;
        for (int i = 0; i < 600 && simulator.State.Rpm < parameters.UpshiftRpm * 0.90f; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        float rpmBeforeShift = simulator.State.Rpm;
        float allowedThrottleShiftFlareRpm = MathF.Max(120f, parameters.RedlineRpm * 0.035f);
        simulator.Update(new VehicleInput(1f, 0f, 0f, shiftUpRequested: true), dt);
        if (!simulator.State.IsShifting || simulator.State.Gear != 2)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift did not select the next gear immediately. Gear {simulator.State.Gear}, shifting {simulator.State.IsShifting}.");
        }

        if (simulator.State.Rpm > rpmBeforeShift + allowedThrottleShiftFlareRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift flared RPM too far while throttle was held. Before {rpmBeforeShift:0}, during {simulator.State.Rpm:0}.");
        }

        int shiftSteps = Math.Max(2, (int)MathF.Ceiling(parameters.ManualShiftTimeSeconds / dt) + 4);
        float maxShiftRpm = simulator.State.Rpm;
        float previousShiftRpm = simulator.State.Rpm;
        for (int i = 0; i < shiftSteps; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
            if (simulator.State.IsShifting)
            {
                if (simulator.State.Rpm > rpmBeforeShift + allowedThrottleShiftFlareRpm)
                {
                    throw new InvalidOperationException($"Physics smoke test failed: manual upshift RPM climbed past the bounded throttle flare. Before {rpmBeforeShift:0}, current {simulator.State.Rpm:0}.");
                }

                maxShiftRpm = MathF.Max(maxShiftRpm, simulator.State.Rpm);
                previousShiftRpm = simulator.State.Rpm;
            }
        }

        if (maxShiftRpm > rpmBeforeShift + allowedThrottleShiftFlareRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift flared RPM during the shift. Before {rpmBeforeShift:0}, max {maxShiftRpm:0}.");
        }

        if (simulator.State.Gear != 2 || simulator.State.IsShifting)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift did not complete into second gear. Gear {simulator.State.Gear}, shifting {simulator.State.IsShifting}.");
        }

        float postShiftStartRpm = simulator.State.Rpm;
        float previousPostShiftRpm = simulator.State.Rpm;
        float minimumPostShiftRpm = simulator.State.Rpm;
        float maximumPostShiftSingleStepDrop = 0f;
        float maximumShiftKick = simulator.State.ShiftKickIntensity;
        for (int i = 0; i < 18; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
            maximumPostShiftSingleStepDrop = MathF.Max(
                maximumPostShiftSingleStepDrop,
                previousPostShiftRpm - simulator.State.Rpm);
            minimumPostShiftRpm = MathF.Min(minimumPostShiftRpm, simulator.State.Rpm);
            maximumShiftKick = MathF.Max(maximumShiftKick, simulator.State.ShiftKickIntensity);

            previousPostShiftRpm = simulator.State.Rpm;
        }

        if (maximumPostShiftSingleStepDrop > 28f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift RPM settled too sharply after handoff. Drop {maximumPostShiftSingleStepDrop:0} RPM/frame.");
        }

        if (minimumPostShiftRpm < postShiftStartRpm - 180f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift RPM sagged too far after handoff. Start {postShiftStartRpm:0}, min {minimumPostShiftRpm:0}.");
        }

        if (maximumShiftKick <= 0.05f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual upshift did not produce a clutch-kick shock signal. Max kick {maximumShiftKick:0.00}.");
        }
    }

    private static void VerifyManualHighRpmDownshiftIsAccepted(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 35f, dt);
        simulator.State.Gear = 3;

        float secondGearRatio = parameters.ForwardGearRatios[Math.Min(1, parameters.ForwardGearRatios.Length - 1)];
        float predictedSecondGearRpm =
            simulator.State.SpeedMetersPerSecond /
            MathF.Max(0.05f, parameters.WheelRadiusMeters) /
            MathF.Tau *
            60f *
            secondGearRatio *
            parameters.FinalDriveRatio;
        if (predictedSecondGearRpm <= parameters.RedlineRpm + parameters.DownshiftOverRevToleranceRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-RPM downshift setup did not exceed the old over-rev guard. Predicted {predictedSecondGearRpm:0} RPM.");
        }

        simulator.Update(new VehicleInput(0f, 0f, 0f, shiftDownRequested: true), 1f / 60f);
        if (!simulator.State.IsShifting || simulator.State.Gear != 2)
        {
            throw new InvalidOperationException($"Physics smoke test failed: manual high-RPM downshift was blocked. Gear {simulator.State.Gear}, shifting {simulator.State.IsShifting}.");
        }
    }

    private static void VerifyManualOverRevDownshiftCreatesEngineBraking(VehicleSimulationParameters parameters)
    {
        if (parameters.ForwardGearRatios.Length < 3)
        {
            return;
        }

        SimpleVehicleSimulator downshift = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        SimpleVehicleSimulator reference = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        downshift.SetManualTransmission(true);
        reference.SetManualTransmission(true);

        const float dt = 1f / 120f;
        float secondGearRatio = parameters.ForwardGearRatios[1];
        float targetOverRevRpm = parameters.RedlineRpm + parameters.DownshiftOverRevToleranceRpm + 650f;
        float targetSpeedMetersPerSecond =
            targetOverRevRpm /
            MathF.Max(0.1f, secondGearRatio * parameters.FinalDriveRatio) *
            MathF.Tau *
            MathF.Max(0.05f, parameters.WheelRadiusMeters) /
            60f;
        targetSpeedMetersPerSecond = MathHelper.Clamp(targetSpeedMetersPerSecond, 30f, 44f);

        DriveToHighSpeedWithManualShifts(downshift, parameters, targetSpeedMetersPerSecond, dt);
        DriveToHighSpeedWithManualShifts(reference, parameters, targetSpeedMetersPerSecond, dt);
        downshift.State.Gear = 3;
        reference.State.Gear = 3;

        float predictedSecondGearRpm =
            downshift.State.SpeedMetersPerSecond /
            MathF.Max(0.05f, parameters.WheelRadiusMeters) /
            MathF.Tau *
            60f *
            secondGearRatio *
            parameters.FinalDriveRatio;
        if (predictedSecondGearRpm <= parameters.RedlineRpm + parameters.DownshiftOverRevToleranceRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift setup was too slow. Predicted {predictedSecondGearRpm:0} RPM.");
        }

        float downshiftStartSpeed = downshift.State.SpeedMetersPerSecond;
        float referenceStartSpeed = reference.State.SpeedMetersPerSecond;
        downshift.Update(new VehicleInput(0f, 0f, 0f, shiftDownRequested: true), 1f / 60f);
        reference.Update(new VehicleInput(0f, 0f, 0f), 1f / 60f);

        float maximumRpm = downshift.State.Rpm;
        float maximumForcedOverRevRpm = downshift.State.MechanicalOverRevRpm;
        float maximumEngineBrakeTorque = downshift.State.EngineBrakeTorqueNm;
        float maximumPowertrainShock = downshift.State.PowertrainShockIntensity;
        float maximumLimiterBounce = downshift.State.RevLimiterBounceIntensity;
        float minimumLimiterBounceRpm = float.MaxValue;
        float maximumLimiterBounceRpm = 0f;
        bool overRevReported = downshift.State.MechanicalOverRevActive;
        int steps = Math.Max(2, (int)MathF.Ceiling(parameters.ManualShiftTimeSeconds / dt) + 120);
        for (int i = 0; i < steps; i++)
        {
            downshift.Update(new VehicleInput(0f, 0f, 0f), dt);
            reference.Update(new VehicleInput(0f, 0f, 0f), dt);
            maximumRpm = MathF.Max(maximumRpm, downshift.State.Rpm);
            maximumForcedOverRevRpm = MathF.Max(maximumForcedOverRevRpm, downshift.State.MechanicalOverRevRpm);
            maximumEngineBrakeTorque = MathF.Max(maximumEngineBrakeTorque, downshift.State.EngineBrakeTorqueNm);
            maximumPowertrainShock = MathF.Max(maximumPowertrainShock, downshift.State.PowertrainShockIntensity);
            maximumLimiterBounce = MathF.Max(maximumLimiterBounce, downshift.State.RevLimiterBounceIntensity);
            if (downshift.State.MechanicalOverRevActive && downshift.State.RevLimiterBounceIntensity > 0.05f)
            {
                minimumLimiterBounceRpm = MathF.Min(minimumLimiterBounceRpm, downshift.State.Rpm);
                maximumLimiterBounceRpm = MathF.Max(maximumLimiterBounceRpm, downshift.State.Rpm);
            }

            overRevReported |= downshift.State.MechanicalOverRevActive;
        }

        float downshiftSpeedDrop = downshiftStartSpeed - downshift.State.SpeedMetersPerSecond;
        float referenceSpeedDrop = referenceStartSpeed - reference.State.SpeedMetersPerSecond;
        if (maximumRpm > parameters.RedlineRpm + 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift exceeded displayed engine limiter. Max {maximumRpm:0}, limiter {parameters.RedlineRpm:0}.");
        }

        if (maximumForcedOverRevRpm <= parameters.DownshiftOverRevToleranceRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift did not report forced gearbox RPM. Forced over-rev {maximumForcedOverRevRpm:0}, tolerance {parameters.DownshiftOverRevToleranceRpm:0}.");
        }

        if (!overRevReported || maximumEngineBrakeTorque <= 0f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift was not reported or produced no engine braking. Reported {overRevReported}, torque {maximumEngineBrakeTorque:0} Nm.");
        }

        if (maximumPowertrainShock <= 0.05f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift produced no powertrain shock signal. Max shock {maximumPowertrainShock:0.00}.");
        }

        if (maximumLimiterBounce <= 0.05f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift did not bounce on the limiter. Max bounce {maximumLimiterBounce:0.00}.");
        }

        float limiterBounceSpread = maximumLimiterBounceRpm - minimumLimiterBounceRpm;
        float expectedBounceSpread = MathF.Max(70f, parameters.RevLimiterBounceRpm * 0.45f);
        if (minimumLimiterBounceRpm >= float.MaxValue || limiterBounceSpread < expectedBounceSpread)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift limiter RPM did not visibly bounce. Spread {limiterBounceSpread:0} RPM, expected {expectedBounceSpread:0}.");
        }

        if (downshiftSpeedDrop <= referenceSpeedDrop + 0.55f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: over-rev downshift did not slow the car enough. Downshift {downshiftSpeedDrop:0.00} m/s, reference {referenceSpeedDrop:0.00} m/s.");
        }
    }

    private static void VerifyRevLimiter(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        float maxRpm = 0f;
        bool limiterActivated = false;
        bool limiterBounced = false;
        for (int i = 0; i < 2400; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
            maxRpm = MathF.Max(maxRpm, simulator.State.Rpm);
            limiterActivated |= simulator.State.RevLimiterActive;
            limiterBounced |= simulator.State.RevLimiterBounceIntensity > 0.05f;

            if (simulator.State.Rpm > parameters.RedlineRpm + 0.5f)
            {
                throw new InvalidOperationException($"Physics smoke test failed: RPM exceeded limiter. RPM {simulator.State.Rpm:0}, limiter {parameters.RedlineRpm:0}.");
            }
        }

        if (!limiterActivated)
        {
            throw new InvalidOperationException($"Physics smoke test failed: rev limiter never activated. Max RPM {maxRpm:0}, limiter {parameters.RedlineRpm:0}.");
        }

        if (!limiterBounced)
        {
            throw new InvalidOperationException($"Physics smoke test failed: rev limiter activated without a bounce signal. Max RPM {maxRpm:0}, limiter {parameters.RedlineRpm:0}.");
        }
    }

    private static void VerifyEngineBraking(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator inGear = CreateAcceleratedSimulator(parameters);
        SimpleVehicleSimulator neutral = CreateAcceleratedSimulator(parameters);

        inGear.SetManualTransmission(true);
        neutral.SetManualTransmission(true);
        inGear.State.Gear = 1;
        neutral.State.Gear = 0;

        float inGearStart = inGear.State.SpeedMetersPerSecond;
        float neutralStart = neutral.State.SpeedMetersPerSecond;
        const float dt = 1f / 120f;
        for (int i = 0; i < 360; i++)
        {
            inGear.Update(new VehicleInput(0f, 0f, 0f), dt);
            neutral.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        float inGearDrop = inGearStart - inGear.State.SpeedMetersPerSecond;
        float neutralDrop = neutralStart - neutral.State.SpeedMetersPerSecond;
        if (inGearDrop <= neutralDrop + 0.18f || inGear.State.EngineBrakeTorqueNm <= 0f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: engine braking was not meaningfully stronger than neutral coasting. In gear {inGearDrop:0.00} m/s, neutral {neutralDrop:0.00} m/s.");
        }
    }

    private static void VerifyBrakeHardwareAndAbs(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = CreateAcceleratedSimulator(parameters);
        float speedBeforeBraking = simulator.State.SpeedMetersPerSecond;

        const float dt = 1f / 120f;
        bool absActivated = false;
        int lockedFrames = 0;
        float maxFrontBrakeTorque = 0f;
        float maxRearBrakeTorque = 0f;

        for (int i = 0; i < 300; i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0f), dt);
            absActivated |= simulator.State.AbsActive;
            if (simulator.State.LockedWheelCount > 0)
            {
                lockedFrames++;
            }

            maxFrontBrakeTorque = MathF.Max(maxFrontBrakeTorque, simulator.State.FrontBrakeTorqueNm);
            maxRearBrakeTorque = MathF.Max(maxRearBrakeTorque, simulator.State.RearBrakeTorqueNm);
        }

        if (simulator.State.SpeedMetersPerSecond >= speedBeforeBraking - 4f)
        {
            throw new InvalidOperationException("Physics smoke test failed: hardware brake model did not slow the car enough.");
        }

        if (maxFrontBrakeTorque <= 0f || maxRearBrakeTorque <= 0f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: brake hardware torque was not generated. Front {maxFrontBrakeTorque:0}, rear {maxRearBrakeTorque:0}.");
        }

        if (parameters.Brakes.Abs.Enabled)
        {
            if (!absActivated)
            {
                throw new InvalidOperationException("Physics smoke test failed: ABS-equipped car never activated ABS under full braking.");
            }

            if (lockedFrames > 210)
            {
                throw new InvalidOperationException($"Physics smoke test failed: ABS-equipped car stayed locked too long. Locked frames {lockedFrames}.");
            }
        }
        else if (lockedFrames <= 0)
        {
            throw new InvalidOperationException("Physics smoke test failed: non-ABS car never locked a wheel under full braking.");
        }
    }

    private static void VerifyHardBrakingDoesNotRearLockFirst(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, 34f, dt);

        for (int i = 0; i < 120; i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0f), dt);

            if (simulator.State.Brake < 0.35f || simulator.State.SpeedMetersPerSecond < 9f)
            {
                continue;
            }

            float frontSlip = (simulator.State.FrontLeftSlipRatio + simulator.State.FrontRightSlipRatio) * 0.5f;
            float rearSlip = (simulator.State.RearLeftSlipRatio + simulator.State.RearRightSlipRatio) * 0.5f;
            if (rearSlip < frontSlip - 0.22f)
            {
                throw new InvalidOperationException($"Physics smoke test failed: rear axle locks before the front under hard braking. Front slip {frontSlip:0.00}, rear slip {rearSlip:0.00}.");
            }
        }
    }

    private static void VerifyStraightLineBrakingStability(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new CenterlineOnlyElevationSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, 22f, dt);

        float speedBeforeBraking = simulator.State.SpeedMetersPerSecond;
        float startHeading = simulator.State.HeadingRadians;

        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0f), dt);
        }

        float headingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(simulator.State.HeadingRadians - startHeading)));
        if (simulator.State.SpeedMetersPerSecond >= speedBeforeBraking - 4f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: straight-line braking did not slow the car enough. Before {speedBeforeBraking:0.00} m/s, after {simulator.State.SpeedMetersPerSecond:0.00} m/s.");
        }

        if (MathF.Abs(simulator.State.LateralSpeed) > 0.45f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: straight-line braking created too much lateral speed. Lateral {simulator.State.LateralSpeed:0.00} m/s.");
        }

        if (headingChangeDegrees > 3f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: straight-line braking created too much yaw. Heading change {headingChangeDegrees:0.00} degrees.");
        }
    }

    private static void VerifyDigitalBrakeAssistModulatesLocking(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, 32f, dt);
        float speedBeforeBraking = simulator.State.SpeedMetersPerSecond;
        int lockedFrames = 0;
        float maxBrakeApplied = 0f;

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0.45f, brakeAssistEnabled: true), dt);
            if (simulator.State.LockedWheelCount > 0)
            {
                lockedFrames++;
            }

            maxBrakeApplied = MathF.Max(maxBrakeApplied, simulator.State.Brake);
        }

        if (simulator.State.SpeedMetersPerSecond >= speedBeforeBraking - 7.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: assisted digital braking did not slow the car enough. Before {speedBeforeBraking:0.00} m/s, after {simulator.State.SpeedMetersPerSecond:0.00} m/s.");
        }

        if (lockedFrames > 55)
        {
            throw new InvalidOperationException($"Physics smoke test failed: assisted digital braking spent too long locked. Locked frames {lockedFrames}.");
        }

        if (maxBrakeApplied > 0.98f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: assisted digital braking did not modulate pressure. Max brake {maxBrakeApplied:0.00}.");
        }
    }

    private static void VerifyTrailBrakingKeepsSteeringAuthority(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(simulator, 24f, dt);
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float startHeading = simulator.State.HeadingRadians;
        int lockedFrames = 0;

        for (int i = 0; i < 150; i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0.65f, brakeAssistEnabled: true), dt);
            if (simulator.State.LockedWheelCount > 0)
            {
                lockedFrames++;
            }
        }

        float headingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(simulator.State.HeadingRadians - startHeading)));
        float speedDrop = startSpeed - simulator.State.SpeedMetersPerSecond;
        if (headingChangeDegrees < 16f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: trail braking lost steering authority. Heading change {headingChangeDegrees:0.0} degrees.");
        }

        if (speedDrop < 4.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: trail braking did not slow the car enough. Speed drop {speedDrop:0.00} m/s.");
        }

        if (lockedFrames > 42)
        {
            throw new InvalidOperationException($"Physics smoke test failed: trail braking spent too long locked. Locked frames {lockedFrames}.");
        }
    }

    private static void VerifyHighSpeedTrailBrakingHasBite(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator coast = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(coast, parameters, 42f, dt);
        DriveToHighSpeedWithManualShifts(simulator, parameters, 42f, dt);
        float coastStartHeading = coast.State.HeadingRadians;
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float startHeading = simulator.State.HeadingRadians;
        int lockedFrames = 0;
        float peakBrakeForce = 0f;
        float coastPeakLateralG = 0f;
        float brakedPeakLateralG = 0f;

        for (int i = 0; i < 150; i++)
        {
            coast.Update(new VehicleInput(0f, 0f, -0.75f), dt);
            simulator.Update(new VehicleInput(0f, 1f, -0.75f, brakeAssistEnabled: true), dt);
            coastPeakLateralG = MathF.Max(coastPeakLateralG, MathF.Abs(coast.State.LateralAcceleration) / 9.81f);
            brakedPeakLateralG = MathF.Max(brakedPeakLateralG, MathF.Abs(simulator.State.LateralAcceleration) / 9.81f);
            peakBrakeForce = MathF.Max(peakBrakeForce, simulator.State.BrakeForce);
            if (simulator.State.LockedWheelCount > 0)
            {
                lockedFrames++;
            }
        }

        float coastHeadingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(coast.State.HeadingRadians - coastStartHeading)));
        float speedDrop = startSpeed - simulator.State.SpeedMetersPerSecond;
        float headingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(simulator.State.HeadingRadians - startHeading)));
        if (speedDrop < 9.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed trail braking lacked bite. Speed drop {speedDrop:0.00} m/s.");
        }

        if (peakBrakeForce < parameters.MassKg * 7.2f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed trail braking generated too little brake force. Peak {peakBrakeForce:0} N.");
        }

        if (headingChangeDegrees < coastHeadingChangeDegrees * 0.70f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed trail braking lost too much steering authority. Braked heading {headingChangeDegrees:0.0} degrees, coast heading {coastHeadingChangeDegrees:0.0} degrees.");
        }

        if (brakedPeakLateralG < coastPeakLateralG * 0.70f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed trail braking lost too much lateral grip. Braked {brakedPeakLateralG:0.00} g, coast {coastPeakLateralG:0.00} g.");
        }

        if (lockedFrames > 55)
        {
            throw new InvalidOperationException($"Physics smoke test failed: high-speed trail braking spent too long locked. Locked frames {lockedFrames}.");
        }
    }

    private static void VerifyPostBrakeReleaseTurnKeepsSteeringAuthority(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator braked = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(braked, parameters, 38f, dt);
        for (int i = 0; i < 90; i++)
        {
            braked.Update(new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true), dt);
        }

        float releaseSpeed = braked.State.SpeedMetersPerSecond;
        SimpleVehicleSimulator sameSpeed = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        DriveToHighSpeedWithManualShifts(sameSpeed, parameters, MathF.Max(8f, releaseSpeed), dt);

        float brakedStartHeading = braked.State.HeadingRadians;
        float sameSpeedStartHeading = sameSpeed.State.HeadingRadians;
        float brakedSteerAfterTenth = 0f;
        float peakFrontBrakeSlip = 0f;
        float peakBrakedLateralG = 0f;

        for (int i = 0; i < 144; i++)
        {
            braked.Update(new VehicleInput(0f, 0f, -0.75f, brakeAssistEnabled: true), dt);
            sameSpeed.Update(new VehicleInput(0f, 0f, -0.75f), dt);

            if (i == 12)
            {
                brakedSteerAfterTenth = MathF.Abs(braked.State.Steer);
            }

            peakFrontBrakeSlip = MathF.Max(
                peakFrontBrakeSlip,
                MathF.Max(-braked.State.FrontLeftSlipRatio, -braked.State.FrontRightSlipRatio));
            peakBrakedLateralG = MathF.Max(peakBrakedLateralG, MathF.Abs(braked.State.LateralAcceleration) / 9.81f);
        }

        float brakedHeadingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(braked.State.HeadingRadians - brakedStartHeading)));
        float sameSpeedHeadingChangeDegrees = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(sameSpeed.State.HeadingRadians - sameSpeedStartHeading)));
        if (brakedSteerAfterTenth < 0.62f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: post-brake steering took too long to build. Steer after 0.1s {brakedSteerAfterTenth:0.00}.");
        }

        if (brakedHeadingChangeDegrees < sameSpeedHeadingChangeDegrees * 0.82f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: post-brake turn lost too much steering authority. Braked {brakedHeadingChangeDegrees:0.0} degrees, same-speed {sameSpeedHeadingChangeDegrees:0.0} degrees.");
        }

        if (peakFrontBrakeSlip > 0.13f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: post-brake front wheels kept too much brake slip. Peak slip {peakFrontBrakeSlip:0.0000}.");
        }

        if (peakBrakedLateralG < 0.85f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: post-brake turn did not build lateral grip. Peak {peakBrakedLateralG:0.00} g.");
        }
    }

    private static void VerifyBrakeOverridesThrottleInHighSpeedTurn(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToHighSpeedWithManualShifts(simulator, parameters, 42f, dt);
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float maxEffectiveThrottle = 0f;

        for (int i = 0; i < 152; i++)
        {
            simulator.Update(new VehicleInput(1f, 1f, -0.75f, brakeAssistEnabled: true, throttleAssistEnabled: true), dt);
            maxEffectiveThrottle = MathF.Max(maxEffectiveThrottle, simulator.State.EffectiveThrottle);
        }

        float speedDrop = startSpeed - simulator.State.SpeedMetersPerSecond;
        if (speedDrop < 8.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: braking did not dominate held throttle in a high-speed turn. Speed drop {speedDrop:0.00} m/s.");
        }

        if (maxEffectiveThrottle > 0.18f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: brake override left too much drive throttle. Effective throttle {maxEffectiveThrottle:0.00}.");
        }
    }

    private static void VerifyVehiclePoseTracksWheelGroundContact(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new CenterlineOnlyElevationSampler(),
            new Vector3(12f, 0.6f, 4f),
            MathHelper.ToRadians(8f),
            parameters);

        simulator.Update(new VehicleInput(0f, 0f, 0f), 1f / 120f);
        float expectedCenterHeight = simulator.State.Position.X * 0.08f;
        if (MathF.Abs(simulator.State.Position.Y - expectedCenterHeight) > 0.06f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: visual body pose is not on the tyre contact plane. Y {simulator.State.Position.Y:0.000}, expected {expectedCenterHeight:0.000}.");
        }

        if (MathF.Abs(simulator.State.BodyPitchRadians) <= 0.005f)
        {
            throw new InvalidOperationException("Physics smoke test failed: visual body pose did not follow the sloped tyre contact plane.");
        }
    }

    private static void VerifyVisualSuspensionUsesFourCornerSupport(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator cornering = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        DriveToSpeed(cornering, 20f, dt);
        for (int i = 0; i < 152; i++)
        {
            cornering.Update(new VehicleInput(0.18f, 0f, 0.78f), dt);
        }

        VehicleState cornerState = cornering.State;
        float leftCompression = Average(
            cornerState.FrontLeftVisualSuspensionCompressionMeters,
            cornerState.RearLeftVisualSuspensionCompressionMeters);
        float rightCompression = Average(
            cornerState.FrontRightVisualSuspensionCompressionMeters,
            cornerState.RearRightVisualSuspensionCompressionMeters);
        float frontSplit = cornerState.FrontLeftVisualSuspensionCompressionMeters -
                           cornerState.FrontRightVisualSuspensionCompressionMeters;
        float rearSplit = cornerState.RearLeftVisualSuspensionCompressionMeters -
                          cornerState.RearRightVisualSuspensionCompressionMeters;

        if (MathF.Abs(leftCompression - rightCompression) < 0.009f)
        {
            throw new InvalidOperationException(
                $"Physics smoke test failed: visual suspension did not create side-to-side roll support. " +
                $"Left {leftCompression:0.000}, right {rightCompression:0.000}, " +
                $"FL/FR/RL/RR {cornerState.FrontLeftVisualSuspensionCompressionMeters:0.000}/" +
                $"{cornerState.FrontRightVisualSuspensionCompressionMeters:0.000}/" +
                $"{cornerState.RearLeftVisualSuspensionCompressionMeters:0.000}/" +
                $"{cornerState.RearRightVisualSuspensionCompressionMeters:0.000}, " +
                $"loads {cornerState.FrontLeftLoadN:0}/{cornerState.FrontRightLoadN:0}/" +
                $"{cornerState.RearLeftLoadN:0}/{cornerState.RearRightLoadN:0}.");
        }

        if (MathF.Abs(frontSplit - rearSplit) < 0.0005f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: front/rear visual suspension roll split was identical. Front {frontSplit:0.000}, rear {rearSplit:0.000}.");
        }

        float visibleBodyRollRadians = MathF.Abs(cornerState.BodyRollRadians - cornerState.GroundRollRadians);
        if (visibleBodyRollRadians < MathHelper.ToRadians(0.18f))
        {
            throw new InvalidOperationException($"Physics smoke test failed: body roll did not come from suspension displacement. Body {MathHelper.ToDegrees(cornerState.BodyRollRadians):0.00}, ground {MathHelper.ToDegrees(cornerState.GroundRollRadians):0.00}.");
        }

        if (visibleBodyRollRadians > MathHelper.ToRadians(3.4f))
        {
            throw new InvalidOperationException($"Physics smoke test failed: visual body roll is too large for restrained presentation. Body {MathHelper.ToDegrees(cornerState.BodyRollRadians):0.00}, ground {MathHelper.ToDegrees(cornerState.GroundRollRadians):0.00}.");
        }

        float rollBeforeRelease = visibleBodyRollRadians;
        cornering.Update(new VehicleInput(0f, 0f, 0f), dt);
        float rollAfterOneFrame = MathF.Abs(cornering.State.BodyRollRadians - cornering.State.GroundRollRadians);
        if (rollAfterOneFrame < rollBeforeRelease * 0.55f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: visual body roll snapped back too quickly. Before {MathHelper.ToDegrees(rollBeforeRelease):0.00}, after {MathHelper.ToDegrees(rollAfterOneFrame):0.00} degrees.");
        }

        SimpleVehicleSimulator braking = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);
        DriveToSpeed(braking, 24f, dt);
        for (int i = 0; i < 23; i++)
        {
            braking.Update(new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true), dt);
        }

        VehicleState brakeState = braking.State;
        float frontCompression = Average(
            brakeState.FrontLeftVisualSuspensionCompressionMeters,
            brakeState.FrontRightVisualSuspensionCompressionMeters);
        float rearCompression = Average(
            brakeState.RearLeftVisualSuspensionCompressionMeters,
            brakeState.RearRightVisualSuspensionCompressionMeters);
        if (frontCompression <= rearCompression + 0.015f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: braking did not pitch through suspension compression. Front {frontCompression:0.000}, rear {rearCompression:0.000}.");
        }
    }

    private static void VerifyNeutralFreeRevUsesEngineSimulator(VehicleSimulationParameters parameters)
    {
        if (!parameters.EngineSimulatorDrivesPhysics || !parameters.EngineSimulatorFullDriveline)
        {
            return;
        }

        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters);
        simulator.State.Gear = 0;
        simulator.State.Rpm = parameters.IdleRpm;

        const float dt = 1f / 120f;
        float maximumCrankRpm = 0f;
        float maximumSpeed = 0f;
        float maximumDriveForce = 0f;
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            maximumCrankRpm = MathF.Max(maximumCrankRpm, simulator.State.EngineSimulatorCrankRpm);
            maximumSpeed = MathF.Max(maximumSpeed, simulator.State.SpeedMetersPerSecond);
            maximumDriveForce = MathF.Max(maximumDriveForce, MathF.Abs(simulator.State.DriveForce));
        }

        if (!simulator.State.EngineSimulatorPowerActive)
        {
            throw new InvalidOperationException("Physics smoke test failed: neutral free-rev did not publish Engine Sim power state.");
        }

        float minimumExpectedCrankRpm = MathF.Min(parameters.RedlineRpm * 0.70f, parameters.IdleRpm + 2200f);
        if (maximumCrankRpm < minimumExpectedCrankRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: neutral free-rev did not spin the Engine Sim crank. Max {maximumCrankRpm:0}, expected {minimumExpectedCrankRpm:0}.");
        }

        if (maximumCrankRpm > parameters.RedlineRpm + 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: neutral free-rev exceeded the car rev limiter. Max {maximumCrankRpm:0}, limiter {parameters.RedlineRpm:0}.");
        }

        if (maximumSpeed > 0.05f || maximumDriveForce > 10f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: neutral free-rev fed torque to the tyres. Speed {maximumSpeed:0.00} m/s, drive {maximumDriveForce:0.0} N.");
        }
    }

    private static void VerifyRaceStartHoldAllowsRevsBeforeTraction(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        float maxRpm = 0f;
        for (int i = 0; i < 360; i++)
        {
            simulator.UpdateRaceStartHold(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            maxRpm = MathF.Max(maxRpm, simulator.State.Rpm);
        }

        if (simulator.State.SpeedMetersPerSecond > 0.05f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: race start hold allowed traction before GO. Speed {simulator.State.SpeedMetersPerSecond:0.00} m/s.");
        }

        if (simulator.State.Rpm < parameters.RedlineRpm * 0.82f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: race start hold did not let the engine rev during countdown. RPM {simulator.State.Rpm:0}.");
        }

        if (maxRpm > parameters.RedlineRpm + 0.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: race start hold exceeded the car rev limiter. Max {maxRpm:0}, limiter {parameters.RedlineRpm:0}.");
        }

        if (simulator.State.Throttle < 0.99f || simulator.State.EffectiveThrottle > 0.01f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: race start hold did not separate pedal from traction. Pedal {simulator.State.Throttle:0.00}, effective {simulator.State.EffectiveThrottle:0.00}.");
        }

        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
        }

        if (simulator.State.SpeedMetersPerSecond < 1.0f || simulator.State.EffectiveThrottle < 0.70f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: traction did not begin cleanly at GO. Speed {simulator.State.SpeedMetersPerSecond:0.00} m/s, effective throttle {simulator.State.EffectiveThrottle:0.00}.");
        }
    }

    private static void VerifyPreRevLaunchUsesSlippingClutch(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 360; i++)
        {
            simulator.UpdateRaceStartHold(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
        }

        float preLaunchRpm = simulator.State.Rpm;
        float minimumLaunchRpm = preLaunchRpm;
        float maximumClutchSlipRpm = 0f;
        float oneSecondClutchSlipRpm = 0f;
        float finalClutchSlipRpm = 0f;
        float oneSecondSpeed = 0f;
        float minimumSettledDriveForce = float.MaxValue;
        float finalRoadCoupledRpm = 0f;
        for (int i = 0; i < 300; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f, throttleAssistEnabled: true), dt);
            minimumLaunchRpm = MathF.Min(minimumLaunchRpm, simulator.State.Rpm);
            maximumClutchSlipRpm = MathF.Max(maximumClutchSlipRpm, MathF.Abs(simulator.State.ClutchSlipRpm));
            if (i == 119)
            {
                oneSecondClutchSlipRpm = MathF.Abs(simulator.State.ClutchSlipRpm);
                oneSecondSpeed = simulator.State.SpeedMetersPerSecond;
            }

            if (i >= 96)
            {
                minimumSettledDriveForce = MathF.Min(minimumSettledDriveForce, simulator.State.DriveForce);
            }

            finalClutchSlipRpm = MathF.Abs(simulator.State.ClutchSlipRpm);
            finalRoadCoupledRpm = CalculateRoadCoupledRpm(parameters, simulator.State);
        }

        float minimumAllowedRpm = MathF.Max(parameters.IdleRpm + 1800f, parameters.LaunchSlipTargetRpm * 0.88f);
        if (minimumLaunchRpm < minimumAllowedRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: pre-rev launch bogged below the clutch band. Pre {preLaunchRpm:0}, min {minimumLaunchRpm:0}, allowed {minimumAllowedRpm:0}.");
        }

        if (oneSecondSpeed < 3.6f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: pre-rev launch did not bite hard enough by one second. Speed {oneSecondSpeed * 3.6f:0.0} km/h, drive {minimumSettledDriveForce / 1000f:0.0} kN.");
        }

        if (simulator.State.SpeedMetersPerSecond < 9.0f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: slipping clutch launch did not move the car enough. Speed {simulator.State.SpeedMetersPerSecond:0.00} m/s.");
        }

        if (maximumClutchSlipRpm < 800f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: pre-rev launch did not show clutch slip. Max slip {maximumClutchSlipRpm:0} RPM.");
        }

        if (minimumSettledDriveForce < 3800f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: launch clutch protected RPM but did not feed enough torque. Minimum settled drive {minimumSettledDriveForce / 1000f:0.0} kN.");
        }

        float maximumAllowedFinalClutchSlipRpm = MathF.Max(900f, parameters.LaunchSlipTargetRpm * 0.25f);
        if (finalClutchSlipRpm > maximumAllowedFinalClutchSlipRpm ||
            (oneSecondClutchSlipRpm > 700f && finalClutchSlipRpm > oneSecondClutchSlipRpm * 0.45f))
        {
            throw new InvalidOperationException($"Physics smoke test failed: launch clutch did not progressively hand off to tyre traction. One-second slip {oneSecondClutchSlipRpm:0} RPM, final slip {finalClutchSlipRpm:0} RPM, allowed {maximumAllowedFinalClutchSlipRpm:0}.");
        }

        float maximumAllowedFinalSpinRpm = MathF.Max(1400f, parameters.LaunchSlipTargetRpm * 0.32f);
        float finalExcessRpmOverRoad = simulator.State.Rpm - finalRoadCoupledRpm;
        if (finalExcessRpmOverRoad > maximumAllowedFinalSpinRpm)
        {
            throw new InvalidOperationException($"Physics smoke test failed: launch RPM stayed too far above road speed after clutch handoff. RPM {simulator.State.Rpm:0}, road {finalRoadCoupledRpm:0}, excess {finalExcessRpmOverRoad:0}, allowed {maximumAllowedFinalSpinRpm:0}.");
        }
    }

    private static float CalculateRoadCoupledRpm(VehicleSimulationParameters parameters, VehicleState state)
    {
        if (state.Gear <= 0 || state.Gear > parameters.ForwardGearRatios.Length)
        {
            return 0f;
        }

        float wheelRpm = state.SpeedMetersPerSecond /
                         MathF.Max(0.05f, parameters.WheelRadiusMeters) /
                         MathF.Tau *
                         60f;
        return wheelRpm * parameters.ForwardGearRatios[state.Gear - 1] * parameters.FinalDriveRatio;
    }

    private static void VerifyAcceleratorRegatesReverseToFirst(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, reverse: 1f), dt);
        }

        if (simulator.State.Gear != -1 || simulator.State.SignedForwardSpeed >= -0.25f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: reverse setup did not build backward motion. Gear {simulator.State.Gear}, speed {simulator.State.SignedForwardSpeed:0.00} m/s.");
        }

        float backwardSpeedBeforeThrottle = simulator.State.SignedForwardSpeed;
        simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        if (simulator.State.Gear != 1 || !simulator.State.IsShifting)
        {
            throw new InvalidOperationException($"Physics smoke test failed: accelerator did not re-gate reverse to first. Gear {simulator.State.Gear}, shifting {simulator.State.IsShifting}.");
        }

        for (int i = 0; i < 150; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        if (simulator.State.SignedForwardSpeed <= backwardSpeedBeforeThrottle + 0.75f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: first gear did not pull against reverse momentum. Before {backwardSpeedBeforeThrottle:0.00} m/s, after {simulator.State.SignedForwardSpeed:0.00} m/s.");
        }
    }

    private static void VerifyWallScrapePreservesMomentum(VehicleSimulationParameters parameters)
    {
        const float wallHalfWidth = 5.0f;
        SimpleVehicleSimulator simulator = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - parameters.BodyWidthMeters * 0.5f + 0.05f, 0.06f, 0f),
            0f,
            parameters);

        simulator.State.Velocity = new Vector2(1.8f, 26f);

        const float dt = 1f / 120f;
        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        if (simulator.State.Position.X > wallHalfWidth - parameters.BodyWidthMeters * 0.5f + 0.18f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall scrape let the car cross the boundary. X {simulator.State.Position.X:0.00}.");
        }

        if (simulator.State.Velocity.Y < 19f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall scrape killed too much tangential speed. VY {simulator.State.Velocity.Y:0.00} m/s.");
        }

        if (MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)) > 45f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall scrape injected too much yaw. Yaw {MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond):0.0} deg/s.");
        }
    }

    private static void VerifyWallContactDoesNotTrapCar(VehicleSimulationParameters parameters)
    {
        const float wallHalfWidth = 5.0f;
        float bodyHalfWidth = parameters.BodyWidthMeters * 0.5f;
        SimpleVehicleSimulator simulator = new(
            new StraightWallSampler(wallHalfWidth),
            new Vector3(wallHalfWidth - bodyHalfWidth + 0.04f, 0.06f, 0f),
            MathHelper.ToRadians(8f),
            parameters);

        simulator.State.Velocity = new Vector2(2.4f, 16.5f);
        float startZ = simulator.State.Position.Z;

        const float dt = 1f / 120f;
        float minimumTangentialSpeed = float.MaxValue;
        float maximumCenterX = simulator.State.Position.X;
        for (int i = 0; i < 240; i++)
        {
            simulator.Update(new VehicleInput(0.75f, 0f, -0.45f), dt);
            minimumTangentialSpeed = MathF.Min(minimumTangentialSpeed, simulator.State.Velocity.Y);
            maximumCenterX = MathF.Max(maximumCenterX, simulator.State.Position.X);
        }

        float maximumAllowedCenterX = wallHalfWidth - bodyHalfWidth + 0.18f;
        if (maximumCenterX > maximumAllowedCenterX)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall anti-stick let the car climb through the barrier. X {maximumCenterX:0.00}, allowed {maximumAllowedCenterX:0.00}.");
        }

        float progress = simulator.State.Position.Z - startZ;
        if (progress < 18f || minimumTangentialSpeed < 6.5f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall contact trapped the car instead of letting it scrape forward. Progress {progress:0.0} m, min tangential speed {minimumTangentialSpeed:0.0} m/s.");
        }

        if (MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)) > 110f)
        {
            throw new InvalidOperationException($"Physics smoke test failed: wall anti-stick created excessive spin. Yaw {MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond):0.0} deg/s.");
        }
    }

    private static SimpleVehicleSimulator CreateAcceleratedSimulator(VehicleSimulationParameters parameters)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters);

        const float dt = 1f / 120f;
        for (int i = 0; i < 480; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        return simulator;
    }

    private static void DriveToHighSpeedWithManualShifts(
        SimpleVehicleSimulator simulator,
        VehicleSimulationParameters parameters,
        float targetSpeedMetersPerSecond,
        float dt)
    {
        simulator.SetManualTransmission(true);
        for (int i = 0; i < 3600 && simulator.State.SpeedMetersPerSecond < targetSpeedMetersPerSecond; i++)
        {
            bool shiftUp = !simulator.State.IsShifting &&
                           simulator.State.Gear > 0 &&
                           simulator.State.Gear < parameters.ForwardGearRatios.Length &&
                           simulator.State.Rpm >= parameters.UpshiftRpm - 250f;
            simulator.Update(new VehicleInput(1f, 0f, 0f, shiftUpRequested: shiftUp), dt);
        }

        for (int i = 0; i < 30; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, 0f), dt);
        }
    }

    private static void DriveToSpeed(SimpleVehicleSimulator simulator, float targetSpeedMetersPerSecond, float dt)
    {
        for (int i = 0; i < 1200 && simulator.State.SpeedMetersPerSecond < targetSpeedMetersPerSecond; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        for (int i = 0; i < 30; i++)
        {
            simulator.Update(new VehicleInput(0.2f, 0f, 0f), dt);
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float Average(float a, float b)
    {
        return (a + b) * 0.5f;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private sealed class ScriptedProgressTrack : ITrackProgressSampler
    {
        public float LengthMeters => 1000f;

        public float RoadHalfWidthMeters => 8f;

        public TrackProgress GetProgress(Vector3 position)
        {
            float normalized = position.X % 1f;
            if (normalized < 0f)
            {
                normalized += 1f;
            }

            return new TrackProgress(
                normalized * LengthMeters,
                normalized,
                0f,
                0f,
                Vector2.UnitX,
                0f);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }
    }

    private sealed class StraightWallSampler : ITrackSurfaceSampler
    {
        private readonly float _halfWidth;

        public StraightWallSampler(float halfWidth)
        {
            _halfWidth = halfWidth;
        }

        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }

        public bool TryGetBoundaryHit(Vector2 position, float radiusMeters, out TrackBoundaryHit hit)
        {
            float limit = _halfWidth - MathF.Max(0f, radiusMeters);
            if (position.X > limit)
            {
                hit = new TrackBoundaryHit(new Vector2(_halfWidth, position.Y), -Vector2.UnitX, position.X - limit, 0f);
                return true;
            }

            if (position.X < -limit)
            {
                hit = new TrackBoundaryHit(new Vector2(-_halfWidth, position.Y), Vector2.UnitX, -limit - position.X, 0f);
                return true;
            }

            hit = default;
            return false;
        }
    }

    private sealed class CenterlineOnlyElevationSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1.0f);
        }

        public float GetElevation(Vector2 position)
        {
            return position.X * 0.08f;
        }
    }

    private sealed class MutableSurfaceSampler : ITrackSurfaceSampler
    {
        public MutableSurfaceSampler(SurfaceSample surface)
        {
            Surface = surface;
        }

        public SurfaceSample Surface { get; set; }

        public SurfaceSample Sample(Vector3 position)
        {
            return Surface;
        }
    }
}
