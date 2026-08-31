using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicFourWheelProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic four-wheel probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        ProbeSteeringSignConvention(parameters, engineParameters);
        ProbeSnapSpinStability(parameters, engineParameters);
        ProbeSteeringReleaseRecovery(parameters, engineParameters);
        ProbeTrailBrakingTurnIn(parameters, engineParameters);
        ProbeAlternatingSteeringResponse(parameters, engineParameters);
        ProbeCornerSequenceCarryover(parameters, engineParameters);
        ProbeCorneringSpeedLoss(parameters, engineParameters);
        ProbeYawContributionSplit(parameters, engineParameters);
        ProbeYawContributionSweep(parameters, engineParameters);
        ProbeReleaseDecayTime(parameters, engineParameters);
        ProbeIndependentWheelLoads(parameters, engineParameters);
        ProbeManualShiftLatch(parameters, engineParameters);
        ProbeReverse(parameters, engineParameters);
        ProbeDecelerationAndBrakeLoads(parameters, engineParameters);
        ProbeFfThrottleSaturation(parameters, engineParameters);
        Console.WriteLine("Classic four-wheel probe passed.");
    }

    private static void ProbeYawContributionSweep(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        YawContributionSample[] samples =
        [
            RunYawContributionCase(parameters, engineParameters, 45f, 0.45f, 0.70f, 0.0f),
            RunYawContributionCase(parameters, engineParameters, 60f, 0.45f, 0.70f, 0.0f),
            RunYawContributionCase(parameters, engineParameters, 80f, 0.35f, 0.70f, 0.0f),
            RunYawContributionCase(parameters, engineParameters, 100f, 0.28f, 0.70f, 0.0f)
        ];

        foreach (YawContributionSample sample in samples)
        {
            float front = MathF.Abs(sample.FrontYawAcceleration);
            float rear = MathF.Abs(sample.RearYawAcceleration);
            float ratio = front > 0.001f ? rear / front : 0f;
            float slipGap = MathF.Abs(sample.FrontSlipAngleDegrees) - MathF.Abs(sample.RearSlipAngleDegrees);
            Console.WriteLine(
                $"  yaw sweep {sample.SpeedKmh:0}kmh steer={sample.SteerInput:0.00}: bodySlip={sample.BodySlipAngleDegrees:0.0}deg slipF/R={sample.FrontSlipAngleDegrees:0.0}/{sample.RearSlipAngleDegrees:0.0}deg slipGap={slipGap:+0.0;-0.0}deg frontYaw={sample.FrontYawAcceleration:0} rearYaw={sample.RearYawAcceleration:0} rear/front={ratio:0.00}");
        }
    }

    private static void ProbeTrailBrakingTurnIn(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        float[] speedsKmh = [100f, 120f, 140f];
        foreach (float speedKmh in speedsKmh)
        {
            TrailBrakeSample recoveryOn = RunTrailBrakeCase(parameters, engineParameters, speedKmh, disableYawRecovery: false);
            TrailBrakeSample recoveryOff = RunTrailBrakeCase(parameters, engineParameters, speedKmh, disableYawRecovery: true);

            Console.WriteLine(
                $"  trail {speedKmh:0}kmh recovery=on steer={recoveryOn.SteerAngleDegrees:0.0}/{recoveryOn.MaxSteerAngleDegrees:0.0}deg slipF/R={recoveryOn.FrontSlipAngleDegrees:0.0}/{recoveryOn.RearSlipAngleDegrees:0.0}deg yaw={recoveryOn.YawRateDegreesPerSecond:0.0}deg/s radius={recoveryOn.TurnRadiusMeters:0}m frontGrip={recoveryOn.PeakFrontGripUsage:0.00} frontLat/Long={recoveryOn.FrontLateralGripUsage:0.00}/{recoveryOn.FrontLongitudinalGripUsage:0.00} frontF lat/long={recoveryOn.FrontLateralForceN:0}/{recoveryOn.FrontLongitudinalForceN:0}N frontYaw={recoveryOn.FrontYawAcceleration:0} rearYaw={recoveryOn.RearYawAcceleration:0} natural={recoveryOn.NaturalYawAcceleration:0} damping={recoveryOn.DampingYawAcceleration:0} recovery={recoveryOn.RecoveryYawAcceleration:0} bodySlip={recoveryOn.BodySlipAngleDegrees:0.0}deg");
            Console.WriteLine(
                $"  trail {speedKmh:0}kmh recovery=off steer={recoveryOff.SteerAngleDegrees:0.0}/{recoveryOff.MaxSteerAngleDegrees:0.0}deg slipF/R={recoveryOff.FrontSlipAngleDegrees:0.0}/{recoveryOff.RearSlipAngleDegrees:0.0}deg yaw={recoveryOff.YawRateDegreesPerSecond:0.0}deg/s radius={recoveryOff.TurnRadiusMeters:0}m frontGrip={recoveryOff.PeakFrontGripUsage:0.00} frontLat/Long={recoveryOff.FrontLateralGripUsage:0.00}/{recoveryOff.FrontLongitudinalGripUsage:0.00} frontF lat/long={recoveryOff.FrontLateralForceN:0}/{recoveryOff.FrontLongitudinalForceN:0}N frontYaw={recoveryOff.FrontYawAcceleration:0} rearYaw={recoveryOff.RearYawAcceleration:0} natural={recoveryOff.NaturalYawAcceleration:0} damping={recoveryOff.DampingYawAcceleration:0} recovery={recoveryOff.RecoveryYawAcceleration:0} bodySlip={recoveryOff.BodySlipAngleDegrees:0.0}deg");
        }
    }

    private static void ProbeAlternatingSteeringResponse(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        foreach (float speedKmh in new[] { 80f, 100f, 120f })
        {
            AlternatingSteerSample sample = RunAlternatingSteerCase(parameters, engineParameters, speedKmh);
            Console.WriteLine(
                $"  flick {speedKmh:0}kmh peakYaw={sample.PeakYawRateDegreesPerSecond:0.0}deg/s residualYaw={sample.ResidualYawRateDegreesPerSecond:0.0}deg/s peakLat={sample.PeakLateralSpeedMetersPerSecond:0.00}m/s residualLat={sample.ResidualLateralSpeedMetersPerSecond:0.00}m/s maxRecovery={sample.PeakRecoveryYawAcceleration:0}deg/s2 maxDamping={sample.PeakDampingYawAcceleration:0}deg/s2 rearSlip={sample.PeakRearSlipAngleDegrees:0.0}deg steerEnd={sample.SteerAngleAfterReleaseDegrees:0.0}deg");
        }
    }

    private static void ProbeCorneringSpeedLoss(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        float originalLateralDamping = engineParameters.ClassicFourWheel.Yaw.LateralVelocityDamping;
        try
        {
            foreach (float throttle in new[] { 0f, 0.35f })
            {
                CorneringSpeedLossSample baseline = RunCorneringSpeedLossCase(
                    parameters,
                    engineParameters,
                    originalLateralDamping,
                    throttle,
                    steer: 0f);
                CorneringSpeedLossSample current = RunCorneringSpeedLossCase(
                    parameters,
                    engineParameters,
                    originalLateralDamping,
                    throttle,
                    steer: 0.85f);
                CorneringSpeedLossSample noLinearDamping = RunCorneringSpeedLossCase(
                    parameters,
                    engineParameters,
                    0f,
                    throttle,
                    steer: 0.85f);

                Console.WriteLine(
                    $"  speed loss throttle={throttle:0.00}: straightDrop={baseline.SpeedDropKmh:0.0}kmh cornerDrop={current.SpeedDropKmh:0.0}kmh noLinearLatDamp={noLinearDamping.SpeedDropKmh:0.0}kmh extra={current.SpeedDropKmh - baseline.SpeedDropKmh:0.0}kmh linear/bodyEst={current.LinearDampingSpeedDropKmh:0.0}/{current.BodySlipDampingSpeedDropKmh:0.0}kmh slipF/R={current.PeakFrontSlipDegrees:0.0}/{current.PeakRearSlipDegrees:0.0}deg bodySlip={current.PeakBodySlipDegrees:0.0}deg");
            }
        }
        finally
        {
            engineParameters.ClassicFourWheel.Yaw.LateralVelocityDamping = originalLateralDamping;
        }
    }

    private static CorneringSpeedLossSample RunCorneringSpeedLossCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float lateralVelocityDamping,
        float throttle,
        float steer)
    {
        engineParameters.ClassicFourWheel.Yaw.LateralVelocityDamping = lateralVelocityDamping;
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 4;
        simulator.State.Velocity = new Vector2(0f, 135f / 3.6f);

        const float dt = 1f / 120f;
        const int ticks = 144;
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float linearDampingSpeedDrop = 0f;
        float bodySlipDampingSpeedDrop = 0f;
        float peakFrontSlip = 0f;
        float peakRearSlip = 0f;
        float peakBodySlip = 0f;

        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, steer), dt);
            VehicleState state = simulator.State;
            float speed = MathF.Max(0.1f, state.SpeedMetersPerSecond);
            float mass = MathF.Max(1f, state.FrontLeftLoadN + state.FrontRightLoadN + state.RearLeftLoadN + state.RearRightLoadN) / 9.81f;
            float lateralSpeedShare = MathF.Abs(state.LateralSpeed) / speed;
            float linearDampingForce = MathF.Abs(state.LateralSpeed * mass * lateralVelocityDamping);
            float bodySlipDampingForce = MathF.Abs(state.ClassicBodySlipDampingForceN);

            linearDampingSpeedDrop += linearDampingForce / mass * lateralSpeedShare * dt;
            bodySlipDampingSpeedDrop += bodySlipDampingForce / mass * lateralSpeedShare * dt;
            peakFrontSlip = MathF.Max(
                peakFrontSlip,
                (MathF.Abs(state.FrontLeftSlipAngleDegrees) + MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f);
            peakRearSlip = MathF.Max(
                peakRearSlip,
                (MathF.Abs(state.RearLeftSlipAngleDegrees) + MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f);
            peakBodySlip = MathF.Max(peakBodySlip, MathF.Abs(state.ClassicBodySlipAngleDegrees));
        }

        VehicleState final = simulator.State;
        RequireFinite(final);
        return new CorneringSpeedLossSample(
            (startSpeed - final.SpeedMetersPerSecond) * 3.6f,
            linearDampingSpeedDrop * 3.6f,
            bodySlipDampingSpeedDrop * 3.6f,
            peakFrontSlip,
            peakRearSlip,
            peakBodySlip);
    }

    private static void ProbeCornerSequenceCarryover(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 4;
        simulator.State.Velocity = new Vector2(0f, 130f / 3.6f);

        CornerSequenceSample t1 = RunCornerSequencePhase(simulator, 0.08f, 0f, 0.32f, 1.0f, gear: 4);
        CornerSequenceSample t2 = RunCornerSequencePhase(simulator, 0f, 0.32f, 0.95f, 1.0f, gear: 3);
        CornerSequenceSample t2Exit = RunCornerSequencePhase(simulator, 0.25f, 0f, 0.15f, 0.55f, gear: 3);
        CornerSequenceSample t3 = RunCornerSequencePhase(simulator, 0f, 0.24f, -0.85f, 1.0f, gear: 3);
        CornerSequenceSample t4 = RunCornerSequencePhase(simulator, 0.12f, 0f, 0.70f, 1.0f, gear: 3);

        Console.WriteLine(FormatCornerSequence("chain T1", t1));
        Console.WriteLine(FormatCornerSequence("chain T2", t2));
        Console.WriteLine(FormatCornerSequence("chain T2exit", t2Exit));
        Console.WriteLine(FormatCornerSequence("chain T3", t3));
        Console.WriteLine(FormatCornerSequence("chain T4", t4));
    }

    private static void ProbeReleaseDecayTime(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ReleaseDecaySample medium = RunReleaseDecayCase(parameters, engineParameters, 60f, 0.85f, 0.55f, 1.25f);
        ReleaseDecaySample fast = RunReleaseDecayCase(parameters, engineParameters, 85f, 0.65f, 0.45f, 1.25f);

        Console.WriteLine(
            $"  decay: 60kmh yawPeak={medium.PeakYawRateDegreesPerSecond:0.0}deg/s yaw10%={medium.YawTenPercentSeconds:0.00}s lateral10%={medium.LateralTenPercentSeconds:0.00}s extraHeading={medium.ExtraHeadingAfterReleaseDegrees:0.0}deg");
        Console.WriteLine(
            $"  decay: 85kmh yawPeak={fast.PeakYawRateDegreesPerSecond:0.0}deg/s yaw10%={fast.YawTenPercentSeconds:0.00}s lateral10%={fast.LateralTenPercentSeconds:0.00}s extraHeading={fast.ExtraHeadingAfterReleaseDegrees:0.0}deg");

        Require(medium.YawTenPercentSeconds <= 0.95f, "medium-speed yaw decay is too floaty after release.");
        Require(fast.YawTenPercentSeconds <= 1.05f, "fast-road yaw decay is too floaty after release.");
    }

    private static void ProbeYawContributionSplit(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        YawContributionSample steady = RunYawContributionCase(parameters, engineParameters, 70f, 0.55f, 0.80f, 0.0f);
        YawContributionSample release = RunYawContributionCase(parameters, engineParameters, 70f, 0.85f, 0.45f, 0.55f);

        Console.WriteLine(
            $"  yaw split steady: bodySlip={steady.BodySlipAngleDegrees:0.0}deg frontSlip={steady.FrontSlipAngleDegrees:0.0}deg rearSlip={steady.RearSlipAngleDegrees:0.0}deg frontYaw={steady.FrontYawAcceleration:0} rearYaw={steady.RearYawAcceleration:0} natural={steady.NaturalYawAcceleration:0} damping={steady.DampingYawAcceleration:0} recovery={steady.RecoveryYawAcceleration:0} rearFollow={steady.RearFollowYawAcceleration:0} followDeficit={steady.RearFollowForceDeficitN:0}N bodySlipDamp={steady.BodySlipDampingForceN:0}N");
        Console.WriteLine(
            $"  yaw split release: bodySlip={release.BodySlipAngleDegrees:0.0}deg frontSlip={release.FrontSlipAngleDegrees:0.0}deg rearSlip={release.RearSlipAngleDegrees:0.0}deg frontYaw={release.FrontYawAcceleration:0} rearYaw={release.RearYawAcceleration:0} natural={release.NaturalYawAcceleration:0} damping={release.DampingYawAcceleration:0} recovery={release.RecoveryYawAcceleration:0} rearFollow={release.RearFollowYawAcceleration:0} followDeficit={release.RearFollowForceDeficitN:0}N bodySlipDamp={release.BodySlipDampingForceN:0}N");

        Require(MathF.Abs(steady.RearFollowYawAcceleration) < MathF.Abs(steady.NaturalYawAcceleration) * 0.45f + 80f,
            "rear-follow assist is dominating steady-state cornering instead of leaving the rear tyres to track naturally.");
        Require(MathF.Abs(release.RearFollowYawAcceleration) < MathF.Abs(release.NaturalYawAcceleration) * 0.75f + 140f,
            "rear-follow assist is dominating release recovery instead of acting as a limited rear force deficit correction.");
    }

    private static void ProbeSteeringReleaseRecovery(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        SteeringReleaseResult medium = RunSteeringReleaseCase(parameters, engineParameters, 60f, 0.85f, 0.55f, 1.25f);
        SteeringReleaseResult fast = RunSteeringReleaseCase(parameters, engineParameters, 85f, 0.65f, 0.45f, 1.25f);

        Console.WriteLine(
            $"  release: 60kmh steerEnd={medium.SteerAngleAfterReleaseDegrees:0.0}deg yawBefore={medium.YawBeforeReleaseDegreesPerSecond:0.0}deg/s yawAfter={medium.YawAfterReleaseDegreesPerSecond:0.0}deg/s extraHeading={medium.ExtraHeadingAfterReleaseDegrees:0.0}deg");
        Console.WriteLine(
            $"  release: 85kmh steerEnd={fast.SteerAngleAfterReleaseDegrees:0.0}deg yawBefore={fast.YawBeforeReleaseDegreesPerSecond:0.0}deg/s yawAfter={fast.YawAfterReleaseDegreesPerSecond:0.0}deg/s extraHeading={fast.ExtraHeadingAfterReleaseDegrees:0.0}deg");

        Require(MathF.Abs(medium.SteerAngleAfterReleaseDegrees) < 2.0f, "medium-speed steering rack did not return close to center after release.");
        Require(MathF.Abs(medium.YawAfterReleaseDegreesPerSecond) < MathF.Abs(medium.YawBeforeReleaseDegreesPerSecond) * 0.38f + 2f, "medium-speed yaw did not recover after steering release.");
        Require(MathF.Abs(medium.ExtraHeadingAfterReleaseDegrees) < 34f, "medium-speed steering release kept rotating too far.");
        Require(MathF.Abs(fast.SteerAngleAfterReleaseDegrees) < 2.0f, "fast-road steering rack did not return close to center after release.");
        Require(MathF.Abs(fast.YawAfterReleaseDegreesPerSecond) < MathF.Abs(fast.YawBeforeReleaseDegreesPerSecond) * 0.38f + 2f, "fast-road yaw did not recover after steering release.");
        Require(MathF.Abs(fast.ExtraHeadingAfterReleaseDegrees) < 26f, "fast-road steering release kept rotating too far.");
    }

    private static void ProbeSteeringSignConvention(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.State.Velocity = new Vector2(0f, 25f);
        float startHeading = simulator.State.HeadingRadians;

        for (int i = 0; i < 72; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0.55f), 1f / 120f);
        }

        VehicleState state = simulator.State;
        Console.WriteLine(
            $"  sign: rightInput forceLatAccel={state.PhysicalLoadTransferLateralAcceleration:0.00}m/s2 bodyLatAccel={state.LateralAcceleration:0.00}m/s2 yaw={MathHelper.ToDegrees(state.YawRateRadiansPerSecond):0.00}deg/s headingDelta={MathHelper.ToDegrees(state.HeadingRadians - startHeading):0.00}deg");
        Require(state.PhysicalLoadTransferLateralAcceleration > 0.05f, "right steering must generate right-turn physical lateral acceleration in the game +X convention.");
        Require(state.YawRateRadiansPerSecond > 0.01f, "right steering must generate rightward positive yaw in the game +X convention.");
        RequireFinite(state);
    }

    private static void ProbeSnapSpinStability(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        CornerStabilityResult lowSpeedFullLock = RunCornerStabilityCase(parameters, engineParameters, 40f, 1.0f, 0f, 1.2f);
        CornerStabilityResult mediumSpeed = RunCornerStabilityCase(parameters, engineParameters, 60f, 0.55f, 0f, 1.5f);
        CornerStabilityResult fastRoad = RunCornerStabilityCase(parameters, engineParameters, 80f, 0.45f, 0f, 1.5f);
        CornerStabilityResult highSpeed = RunCornerStabilityCase(parameters, engineParameters, 120f, 0.30f, 0f, 1.5f);

        Console.WriteLine(
            $"  stability: 40kmh full hdg={lowSpeedFullLock.HeadingDeltaDegrees:0.0}deg yaw={lowSpeedFullLock.PeakYawRateDegreesPerSecond:0.0}deg/s roll={lowSpeedFullLock.PeakBodyRollDegrees:0.0}deg speedDrop={lowSpeedFullLock.SpeedDropKmh:0.0}km/h");
        Console.WriteLine(
            $"  stability: 60kmh hdg={mediumSpeed.HeadingDeltaDegrees:0.0}deg yaw={mediumSpeed.PeakYawRateDegreesPerSecond:0.0}deg/s roll={mediumSpeed.PeakBodyRollDegrees:0.0}deg speedDrop={mediumSpeed.SpeedDropKmh:0.0}km/h");
        Console.WriteLine(
            $"  stability: 80kmh hdg={fastRoad.HeadingDeltaDegrees:0.0}deg yaw={fastRoad.PeakYawRateDegreesPerSecond:0.0}deg/s roll={fastRoad.PeakBodyRollDegrees:0.0}deg speedDrop={fastRoad.SpeedDropKmh:0.0}km/h");
        Console.WriteLine(
            $"  stability: 120kmh hdg={highSpeed.HeadingDeltaDegrees:0.0}deg yaw={highSpeed.PeakYawRateDegreesPerSecond:0.0}deg/s roll={highSpeed.PeakBodyRollDegrees:0.0}deg speedDrop={highSpeed.SpeedDropKmh:0.0}km/h");

        Require(MathF.Abs(lowSpeedFullLock.HeadingDeltaDegrees) < 145f, "low-speed full-lock steering snapped into a spin.");
        Require(lowSpeedFullLock.PeakYawRateDegreesPerSecond < 165f, "low-speed full-lock yaw rate is unstable.");
        Require(MathF.Abs(mediumSpeed.HeadingDeltaDegrees) < 115f, "medium-speed steering snapped into a spin.");
        Require(mediumSpeed.PeakYawRateDegreesPerSecond < 120f, "medium-speed steering yaw rate is unstable.");
        Require(mediumSpeed.SpeedDropKmh < 45f, "medium-speed steering scrubbed too much speed.");
        Require(MathF.Abs(fastRoad.HeadingDeltaDegrees) < 95f, "fast-road steering snapped into a spin.");
        Require(fastRoad.PeakYawRateDegreesPerSecond < 105f, "fast-road steering yaw rate is unstable.");
        Require(fastRoad.SpeedDropKmh < 40f, "fast-road steering scrubbed too much speed.");
        Require(MathF.Abs(highSpeed.HeadingDeltaDegrees) < 70f, "high-speed steering snapped into a spin.");
        Require(highSpeed.PeakYawRateDegreesPerSecond < 85f, "high-speed steering yaw rate is unstable.");
        Require(highSpeed.SpeedDropKmh < 35f, "high-speed steering scrubbed too much speed.");
    }

    private static void ProbeIndependentWheelLoads(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.State.Velocity = new Vector2(0f, 30f);

        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0.70f), 1f / 120f);
        }

        VehicleState state = simulator.State;
        Console.WriteLine(
            $"  loads FL/FR/RL/RR={state.FrontLeftLoadN:0}/{state.FrontRightLoadN:0}/{state.RearLeftLoadN:0}/{state.RearRightLoadN:0}N slip FL/FR/RL/RR={state.FrontLeftSlipAngleDegrees:0.0}/{state.FrontRightSlipAngleDegrees:0.0}/{state.RearLeftSlipAngleDegrees:0.0}/{state.RearRightSlipAngleDegrees:0.0}");
        Require(MathF.Abs(state.FrontLeftLoadN - state.FrontRightLoadN) > 75f, "front left/right loads did not split while cornering.");
        Require(MathF.Abs(state.RearLeftLoadN - state.RearRightLoadN) > 40f, "rear left/right loads did not split while cornering.");
        Require(state.FrontLeftLoadN > state.FrontRightLoadN, "right steering should load the outside/front-left tyre more than the front-right tyre.");
        Require(state.RearLeftLoadN > state.RearRightLoadN, "right steering should load the outside/rear-left tyre more than the rear-right tyre.");
        Require(MathF.Abs(state.FrontLeftSlipAngleDegrees - state.FrontRightSlipAngleDegrees) > 0.01f ||
                MathF.Abs(state.FrontLeftLongitudinalForceN - state.FrontRightLongitudinalForceN) > 0.01f,
            "front wheels remained identical under a four-wheel cornering case.");
        RequireFinite(state);
    }

    private static void ProbeManualShiftLatch(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 1;

        simulator.Update(new VehicleInput(0.5f, 0f, 0f, shiftUpRequested: true), 1f / 30f);
        Console.WriteLine($"  shift latch: after one upshift press gear={simulator.State.Gear}");
        Require(simulator.State.Gear == 2, "one upshift press must advance exactly one gear.");

        simulator.Update(new VehicleInput(0.5f, 0f, 0f, shiftDownRequested: true), 1f / 30f);
        Require(simulator.State.Gear == 1, "one downshift press must reduce exactly one gear.");
        RequireFinite(simulator.State);
    }

    private static void ProbeReverse(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f, reverse: 1f), 1f / 120f);
        }

        Console.WriteLine($"  reverse: gear={simulator.State.Gear} signedSpeed={simulator.State.SignedForwardSpeed:0.00}m/s rpm={simulator.State.Rpm:0}");
        Require(simulator.State.Gear == -1, "Y/reverse input did not select reverse.");
        Require(simulator.State.SignedForwardSpeed < -0.4f, "reverse input did not move backward.");
        RequireFinite(simulator.State);
    }

    private static void ProbeDecelerationAndBrakeLoads(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator coast = CreateSimulator(parameters, engineParameters);
        ClassicFourWheelVehicleSimulator brake = CreateSimulator(parameters, engineParameters);
        coast.SetManualTransmission(true);
        brake.SetManualTransmission(true);
        coast.State.Gear = 4;
        brake.State.Gear = 4;
        coast.State.Velocity = new Vector2(0f, 100f / 3.6f);
        brake.State.Velocity = new Vector2(0f, 100f / 3.6f);

        float coastStart = coast.State.SpeedMetersPerSecond;
        float brakeStart = brake.State.SpeedMetersPerSecond;
        for (int i = 0; i < 120; i++)
        {
            coast.Update(new VehicleInput(0f, 0f, 0f), 1f / 120f);
            brake.Update(new VehicleInput(0f, 0.8f, 0f), 1f / 120f);
        }

        Console.WriteLine(
            $"  decel coast/brake drop={(coastStart - coast.State.SpeedMetersPerSecond) * 3.6f:0.0}/{(brakeStart - brake.State.SpeedMetersPerSecond) * 3.6f:0.0}km/h brakeLoadF={brake.State.ClassicDynamicFrontAxleLoadN:0}N staticF={brake.State.ClassicStaticFrontAxleLoadN:0}N");
        Require(brake.State.SpeedMetersPerSecond < coast.State.SpeedMetersPerSecond - 1f, "service brakes did not decelerate more than coast.");
        Require(brake.State.ClassicDynamicFrontAxleLoadN > brake.State.ClassicStaticFrontAxleLoadN + 100f, "braking did not transfer load forward.");
        RequireFinite(coast.State);
        RequireFinite(brake.State);
    }

    private static void ProbeFfThrottleSaturation(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator coast = CreateSimulator(parameters, engineParameters);
        ClassicFourWheelVehicleSimulator throttle = CreateSimulator(parameters, engineParameters);
        coast.State.Gear = Math.Min(3, parameters.ForwardGearRatios.Length);
        throttle.State.Gear = coast.State.Gear;
        coast.State.Velocity = new Vector2(0f, 24f);
        throttle.State.Velocity = new Vector2(0f, 24f);

        for (int i = 0; i < 180; i++)
        {
            coast.Update(new VehicleInput(0f, 0f, 0.65f), 1f / 120f);
            throttle.Update(new VehicleInput(1f, 0f, 0.65f), 1f / 120f);
        }

        float coastFrontGrip = MathF.Max(coast.State.FrontLeftGripUsage, coast.State.FrontRightGripUsage);
        float throttleFrontGrip = MathF.Max(throttle.State.FrontLeftGripUsage, throttle.State.FrontRightGripUsage);
        Console.WriteLine(
            $"  FF saturation: coastFront={coastFrontGrip:0.00} throttleFront={throttleFrontGrip:0.00} driveF={throttle.State.ClassicDriveForceRequestN:0}N");
        Require(throttleFrontGrip >= coastFrontGrip, "FF throttle did not consume additional front wheel grip budget.");
        Require(throttle.State.ClassicDriveForceRequestN > 1f, "FF throttle did not request drive force.");
        RequireFinite(coast.State);
        RequireFinite(throttle.State);
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

    private static CornerStabilityResult RunCornerStabilityCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh,
        float steer,
        float throttle,
        float durationSeconds)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 4;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        float startHeading = simulator.State.HeadingRadians;
        float startSpeed = simulator.State.SpeedMetersPerSecond;
        float peakYawRate = 0f;
        float peakBodyRoll = 0f;
        int ticks = Math.Max(1, (int)MathF.Round(durationSeconds * 120f));

        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, steer), 1f / 120f);
            peakYawRate = MathF.Max(peakYawRate, MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)));
            peakBodyRoll = MathF.Max(peakBodyRoll, MathF.Abs(MathHelper.ToDegrees(simulator.State.BodyRollRadians - simulator.State.GroundRollRadians)));
        }

        VehicleState state = simulator.State;
        RequireFinite(state);
        return new CornerStabilityResult(
            MathHelper.ToDegrees(MathHelper.WrapAngle(state.HeadingRadians - startHeading)),
            peakYawRate,
            peakBodyRoll,
            MathF.Max(0f, startSpeed - state.SpeedMetersPerSecond) * 3.6f);
    }

    private static SteeringReleaseResult RunSteeringReleaseCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh,
        float steer,
        float holdSeconds,
        float releaseSeconds)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 3;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        const float dt = 1f / 120f;

        int holdTicks = Math.Max(1, (int)MathF.Round(holdSeconds * 120f));
        for (int i = 0; i < holdTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, steer), dt);
        }

        float yawBeforeRelease = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float headingAtRelease = simulator.State.HeadingRadians;
        int releaseTicks = Math.Max(1, (int)MathF.Round(releaseSeconds * 120f));
        for (int i = 0; i < releaseTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        VehicleState state = simulator.State;
        RequireFinite(state);
        return new SteeringReleaseResult(
            MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees)),
            yawBeforeRelease,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            MathHelper.ToDegrees(MathHelper.WrapAngle(state.HeadingRadians - headingAtRelease)));
    }

    private static YawContributionSample RunYawContributionCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh,
        float steer,
        float holdSeconds,
        float releaseSeconds)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 3;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        const float dt = 1f / 120f;

        int holdTicks = Math.Max(1, (int)MathF.Round(holdSeconds * 120f));
        for (int i = 0; i < holdTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, steer), dt);
        }

        int releaseTicks = Math.Max(0, (int)MathF.Round(releaseSeconds * 120f));
        for (int i = 0; i < releaseTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
        }

        VehicleState state = simulator.State;
        RequireFinite(state);
        return new YawContributionSample(
            speedKmh,
            steer,
            state.ClassicBodySlipAngleDegrees,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            state.ClassicFrontYawAccelerationDegreesPerSecondSquared,
            state.ClassicRearYawAccelerationDegreesPerSecondSquared,
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared,
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared,
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared,
            state.ClassicRearFollowForceDeficitN,
            state.ClassicBodySlipDampingForceN);
    }

    private static TrailBrakeSample RunTrailBrakeCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh,
        bool disableYawRecovery)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.DisableYawRecoveryForProbe = disableYawRecovery;
        simulator.SetManualTransmission(true);
        simulator.State.Gear = speedKmh >= 130f ? 5 : 4;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        const float dt = 1f / 120f;
        const float brake = 0.35f;
        const float steer = 1.0f;

        for (int i = 0; i < 18; i++)
        {
            simulator.Update(new VehicleInput(0f, brake, 0f), dt);
        }

        float peakFrontGrip = 0f;
        float peakYawRate = 0f;
        for (int i = 0; i < 84; i++)
        {
            simulator.Update(new VehicleInput(0f, brake, steer), dt);
            peakFrontGrip = MathF.Max(peakFrontGrip, MathF.Max(simulator.State.FrontLeftGripUsage, simulator.State.FrontRightGripUsage));
            peakYawRate = MathF.Max(peakYawRate, MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)));
        }

        VehicleState state = simulator.State;
        RequireFinite(state);
        float yawRateRadians = MathF.Abs(state.YawRateRadiansPerSecond);
        float turnRadius = yawRateRadians > 0.001f
            ? state.SpeedMetersPerSecond / yawRateRadians
            : float.PositiveInfinity;

        return new TrailBrakeSample(
            MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees)),
            state.SteeringSpeedMatchedMaxAngleDegrees,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            peakYawRate,
            turnRadius,
            peakFrontGrip,
            state.ClassicFrontLateralGripUsage,
            state.ClassicFrontLongitudinalGripUsage,
            (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f,
            (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f,
            state.FrontLeftLateralForceN + state.FrontRightLateralForceN,
            state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN,
            state.ClassicFrontYawAccelerationDegreesPerSecondSquared,
            state.ClassicRearYawAccelerationDegreesPerSecondSquared,
            state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared,
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared,
            state.ClassicBodySlipAngleDegrees);
    }

    private static AlternatingSteerSample RunAlternatingSteerCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = speedKmh >= 115f ? 4 : 3;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        const float dt = 1f / 120f;
        float peakYawRate = 0f;
        float peakLateralSpeed = 0f;
        float peakRecovery = 0f;
        float peakDamping = 0f;
        float peakRearSlip = 0f;

        for (int i = 0; i < 336; i++)
        {
            int segment = i / 36;
            float steer = (segment & 1) == 0 ? 0.85f : -0.85f;
            simulator.Update(new VehicleInput(0.08f, 0f, steer), dt);
            VehicleState state = simulator.State;
            peakYawRate = MathF.Max(peakYawRate, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
            peakLateralSpeed = MathF.Max(peakLateralSpeed, MathF.Abs(state.LateralSpeed));
            peakRecovery = MathF.Max(peakRecovery, MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared));
            peakDamping = MathF.Max(peakDamping, MathF.Abs(state.ClassicYawDampingAccelerationDegreesPerSecondSquared));
            peakRearSlip = MathF.Max(peakRearSlip, MathF.Max(MathF.Abs(state.RearLeftSlipAngleDegrees), MathF.Abs(state.RearRightSlipAngleDegrees)));
        }

        for (int i = 0; i < 90; i++)
        {
            simulator.Update(new VehicleInput(0.08f, 0f, 0f), dt);
        }

        VehicleState final = simulator.State;
        RequireFinite(final);
        return new AlternatingSteerSample(
            peakYawRate,
            MathHelper.ToDegrees(final.YawRateRadiansPerSecond),
            peakLateralSpeed,
            final.LateralSpeed,
            peakRecovery,
            peakDamping,
            peakRearSlip,
            MathF.Max(MathF.Abs(final.FrontLeftSteerAngleDegrees), MathF.Abs(final.FrontRightSteerAngleDegrees)));
    }

    private static CornerSequenceSample RunCornerSequencePhase(
        ClassicFourWheelVehicleSimulator simulator,
        float throttle,
        float brake,
        float steer,
        float seconds,
        int gear)
    {
        simulator.State.Gear = gear;
        const float dt = 1f / 120f;
        int ticks = Math.Max(1, (int)MathF.Round(seconds * 120f));
        float peakFrontGrip = 0f;
        float peakFrontSlip = 0f;
        float peakRearSlip = 0f;
        float peakRearMinusFrontSlip = 0f;
        float peakBodySlip = 0f;
        float peakRecovery = 0f;

        for (int i = 0; i < ticks; i++)
        {
            simulator.Update(new VehicleInput(throttle, brake, steer), dt);
            VehicleState state = simulator.State;
            float frontSlip = (
                MathF.Abs(state.FrontLeftSlipAngleDegrees) +
                MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f;
            float rearSlip = (
                MathF.Abs(state.RearLeftSlipAngleDegrees) +
                MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f;
            peakFrontGrip = MathF.Max(peakFrontGrip, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage));
            peakFrontSlip = MathF.Max(peakFrontSlip, frontSlip);
            peakRearSlip = MathF.Max(peakRearSlip, rearSlip);
            peakRearMinusFrontSlip = MathF.Max(peakRearMinusFrontSlip, rearSlip - frontSlip);
            peakBodySlip = MathF.Max(peakBodySlip, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            peakRecovery = MathF.Max(peakRecovery, MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared));
        }

        VehicleState final = simulator.State;
        RequireFinite(final);
        return new CornerSequenceSample(
            final.SpeedMetersPerSecond * 3.6f,
            final.Gear,
            final.Steer,
            MathF.Max(MathF.Abs(final.FrontLeftSteerAngleDegrees), MathF.Abs(final.FrontRightSteerAngleDegrees)),
            final.SteeringSpeedMatchedMaxAngleDegrees,
            MathHelper.ToDegrees(final.YawRateRadiansPerSecond),
            final.LateralSpeed,
            peakFrontGrip,
            final.ClassicFrontLateralGripUsage,
            final.ClassicFrontLongitudinalGripUsage,
            final.ClassicEngineBrakeForceRequestN,
            final.ClassicServiceBrakeForceRequestN,
            final.ClassicFrontYawAccelerationDegreesPerSecondSquared,
            final.ClassicRearYawAccelerationDegreesPerSecondSquared,
            final.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
            final.ClassicYawDampingAccelerationDegreesPerSecondSquared,
            final.ClassicYawRecoveryAccelerationDegreesPerSecondSquared,
            peakFrontSlip,
            peakRearSlip,
            peakRearMinusFrontSlip,
            peakBodySlip,
            peakRecovery);
    }

    private static string FormatCornerSequence(string label, CornerSequenceSample sample)
    {
        return
            $"  {label}: speed={sample.SpeedKmh:0}kmh gear={sample.Gear} steer={sample.SteerInput:0.00} angle={sample.SteerAngleDegrees:0.0}/{sample.MaxSteerAngleDegrees:0.0}deg yaw={sample.YawRateDegreesPerSecond:0.0}deg/s latSpeed={sample.LateralSpeedMetersPerSecond:0.00}m/s frontGrip={sample.PeakFrontGripUsage:0.00} frontLat/Long={sample.FrontLateralGripUsage:0.00}/{sample.FrontLongitudinalGripUsage:0.00} engineBrake={sample.EngineBrakeForceN:0}N serviceBrake={sample.ServiceBrakeForceN:0}N natural={sample.NaturalYawAcceleration:0} damping={sample.DampingYawAcceleration:0} recovery={sample.RecoveryYawAcceleration:0} slipPeakF/R={sample.PeakFrontSlipAngleDegrees:0.0}/{sample.PeakRearSlipAngleDegrees:0.0}deg rearGap={sample.PeakRearMinusFrontSlipDegrees:+0.0;-0.0}deg bodySlipPeak={sample.PeakBodySlipAngleDegrees:0.0}deg maxRecovery={sample.PeakRecoveryYawAcceleration:0}";
    }

    private static ReleaseDecaySample RunReleaseDecayCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float speedKmh,
        float steer,
        float holdSeconds,
        float releaseSeconds)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 3;
        simulator.State.Velocity = new Vector2(0f, speedKmh / 3.6f);
        const float dt = 1f / 120f;

        int holdTicks = Math.Max(1, (int)MathF.Round(holdSeconds * 120f));
        float peakYawRate = 0f;
        for (int i = 0; i < holdTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, steer), dt);
            peakYawRate = MathF.Max(peakYawRate, MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)));
        }

        float releaseYawRate = MathF.Max(0.001f, MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)));
        float releaseLateralSpeed = MathF.Max(0.001f, MathF.Abs(simulator.State.LateralSpeed));
        float headingAtRelease = simulator.State.HeadingRadians;
        float yawTenPercentSeconds = releaseSeconds;
        float lateralTenPercentSeconds = releaseSeconds;
        bool foundYaw = false;
        bool foundLateral = false;

        int releaseTicks = Math.Max(1, (int)MathF.Round(releaseSeconds * 120f));
        for (int i = 0; i < releaseTicks; i++)
        {
            simulator.Update(new VehicleInput(0f, 0f, 0f), dt);
            float seconds = (i + 1) * dt;
            if (!foundYaw && MathF.Abs(MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond)) <= releaseYawRate * 0.10f)
            {
                yawTenPercentSeconds = seconds;
                foundYaw = true;
            }

            if (!foundLateral && MathF.Abs(simulator.State.LateralSpeed) <= releaseLateralSpeed * 0.10f)
            {
                lateralTenPercentSeconds = seconds;
                foundLateral = true;
            }
        }

        VehicleState state = simulator.State;
        RequireFinite(state);
        return new ReleaseDecaySample(
            peakYawRate,
            yawTenPercentSeconds,
            lateralTenPercentSeconds,
            MathHelper.ToDegrees(MathHelper.WrapAngle(state.HeadingRadians - headingAtRelease)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Classic four-wheel probe failed: {message}");
        }
    }

    private static void RequireFinite(VehicleState state)
    {
        Require(float.IsFinite(state.Position.X) && float.IsFinite(state.Position.Z), "position became non-finite.");
        Require(float.IsFinite(state.Velocity.X) && float.IsFinite(state.Velocity.Y), "velocity became non-finite.");
        Require(float.IsFinite(state.HeadingRadians), "heading became non-finite.");
        Require(float.IsFinite(state.YawRateRadiansPerSecond), "yaw rate became non-finite.");
        Require(float.IsFinite(state.FrontLeftLoadN) && float.IsFinite(state.FrontRightLoadN), "front load became non-finite.");
        Require(float.IsFinite(state.RearLeftLoadN) && float.IsFinite(state.RearRightLoadN), "rear load became non-finite.");
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private readonly record struct CornerStabilityResult(
        float HeadingDeltaDegrees,
        float PeakYawRateDegreesPerSecond,
        float PeakBodyRollDegrees,
        float SpeedDropKmh);

    private readonly record struct SteeringReleaseResult(
        float SteerAngleAfterReleaseDegrees,
        float YawBeforeReleaseDegreesPerSecond,
        float YawAfterReleaseDegreesPerSecond,
        float ExtraHeadingAfterReleaseDegrees);

    private readonly record struct YawContributionSample(
        float SpeedKmh,
        float SteerInput,
        float BodySlipAngleDegrees,
        float FrontSlipAngleDegrees,
        float RearSlipAngleDegrees,
        float FrontYawAcceleration,
        float RearYawAcceleration,
        float NaturalYawAcceleration,
        float DampingYawAcceleration,
        float RecoveryYawAcceleration,
        float RearFollowYawAcceleration,
        float RearFollowForceDeficitN,
        float BodySlipDampingForceN);

    private readonly record struct TrailBrakeSample(
        float SteerAngleDegrees,
        float MaxSteerAngleDegrees,
        float YawRateDegreesPerSecond,
        float PeakYawRateDegreesPerSecond,
        float TurnRadiusMeters,
        float PeakFrontGripUsage,
        float FrontLateralGripUsage,
        float FrontLongitudinalGripUsage,
        float FrontSlipAngleDegrees,
        float RearSlipAngleDegrees,
        float FrontLateralForceN,
        float FrontLongitudinalForceN,
        float FrontYawAcceleration,
        float RearYawAcceleration,
        float NaturalYawAcceleration,
        float DampingYawAcceleration,
        float RecoveryYawAcceleration,
        float BodySlipAngleDegrees);

    private readonly record struct AlternatingSteerSample(
        float PeakYawRateDegreesPerSecond,
        float ResidualYawRateDegreesPerSecond,
        float PeakLateralSpeedMetersPerSecond,
        float ResidualLateralSpeedMetersPerSecond,
        float PeakRecoveryYawAcceleration,
        float PeakDampingYawAcceleration,
        float PeakRearSlipAngleDegrees,
        float SteerAngleAfterReleaseDegrees);

    private readonly record struct CorneringSpeedLossSample(
        float SpeedDropKmh,
        float LinearDampingSpeedDropKmh,
        float BodySlipDampingSpeedDropKmh,
        float PeakFrontSlipDegrees,
        float PeakRearSlipDegrees,
        float PeakBodySlipDegrees);

    private readonly record struct CornerSequenceSample(
        float SpeedKmh,
        int Gear,
        float SteerInput,
        float SteerAngleDegrees,
        float MaxSteerAngleDegrees,
        float YawRateDegreesPerSecond,
        float LateralSpeedMetersPerSecond,
        float PeakFrontGripUsage,
        float FrontLateralGripUsage,
        float FrontLongitudinalGripUsage,
        float EngineBrakeForceN,
        float ServiceBrakeForceN,
        float FrontYawAcceleration,
        float RearYawAcceleration,
        float NaturalYawAcceleration,
        float DampingYawAcceleration,
        float RecoveryYawAcceleration,
        float PeakFrontSlipAngleDegrees,
        float PeakRearSlipAngleDegrees,
        float PeakRearMinusFrontSlipDegrees,
        float PeakBodySlipAngleDegrees,
        float PeakRecoveryYawAcceleration);

    private readonly record struct ReleaseDecaySample(
        float PeakYawRateDegreesPerSecond,
        float YawTenPercentSeconds,
        float LateralTenPercentSeconds,
        float ExtraHeadingAfterReleaseDegrees);
}
