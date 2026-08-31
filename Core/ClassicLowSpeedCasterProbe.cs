using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicLowSpeedCasterProbe
{
    private const float Dt = 1f / 120f;
    private const float RunSeconds = 8.0f;

    private static readonly ClassicFourWheelAssistOptions AssistsOff = new()
    {
        BodySlipDampingEnabled = false,
        LateralVelocityDampingEnabled = false,
        RearFollowEnabled = false,
        YawRecoveryEnabled = false,
        SpeedRetentionEnabled = false
    };

    private static readonly float[] CheckpointsSeconds =
    [
        0.05f,
        0.10f,
        0.20f,
        0.35f,
        0.50f,
        0.75f,
        1.00f,
        1.50f,
        2.00f,
        3.00f,
        4.00f,
        5.00f,
        6.00f,
        7.00f,
        8.00f
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic low-speed caster probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine(
            "  reproduction: standstill, steering already held, gentle throttle, observe rear lateral motion through 0-20km/h.");
        Console.WriteLine(
            $"  low-speed guards: slipSpeedFloor={engine.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond:0.00}m/s; " +
            $"rollingDominantEnd={engine.ClassicFourWheel.LowSpeed.RollingDominantEndMetersPerSecond * 3.6f:0.0}km/h; " +
            $"dynamicBlendEnd={engine.ClassicFourWheel.LowSpeed.DynamicBlendEndMetersPerSecond * 3.6f:0.0}km/h; " +
            $"frontRollingScale={engine.ClassicFourWheel.LowSpeed.RollingDominantMaximumLateralScale:0.00}; " +
            $"rearRollingScale={engine.ClassicFourWheel.LowSpeed.RollingDominantRearLateralScale:0.00}; " +
            $"rollingConstraint={engine.ClassicFourWheel.LowSpeed.RollingConstraintGripFraction:0.00}@{engine.ClassicFourWheel.LowSpeed.RollingConstraintLateralSpeedMetersPerSecond:0.00}m/s; " +
            "tyre relaxation uses max(abs(localForward), 3.0m/s).");
        Console.WriteLine(
            "  force convention: rear lateral force should oppose rear local lateral velocity; same sign means caster-like force assistance.");
        Console.WriteLine(
            "  columns: mode case t speed worldVx/Vz bodyU/V yaw kinYaw yaw/kin radius/kinRadius beta steer roadAng latAcc " +
            "front/rearLatF front/rearYaw natural/damp/assistYaw " +
            "frontFy/driveSide lowScaleF/R RL u/v slip targetFy relaxedFy forceRight oppose RR u/v slip targetFy relaxedFy forceRight oppose");

        RunCase(parameters, engine, "assist-on", "fwd-straight", 0f, reverse: false);
        RunCase(parameters, engine, "assist-on", "fwd-left", 1.0f, reverse: false);
        RunCase(parameters, engine, "assist-on", "fwd-right", -1.0f, reverse: false);
        RunCase(parameters, engine, "assist-on", "rev-left", 1.0f, reverse: true);
        RunCase(parameters, engine, "assist-on", "rev-right", -1.0f, reverse: true);
        RunCase(parameters, engine, "assist-on", "fwd-altern", 1.0f, reverse: false, alternatingSteer: true);
        RunCase(parameters, engine, "assist-on", "rev-altern", 1.0f, reverse: true, alternatingSteer: true);
        RunCase(parameters, engine, "assist-off", "fwd-left", 1.0f, assists: AssistsOff, reverse: false);
        RunCase(parameters, engine, "assist-off", "rev-left", 1.0f, assists: AssistsOff, reverse: true);
        RunDiagnosticVariants(parameters, engine);

        Console.WriteLine("Classic low-speed caster probe complete.");
    }

    private static void RunDiagnosticVariants(VehicleSimulationParameters parameters, SimulationEngineParameters engine)
    {
        Console.WriteLine("  diagnostic variants: full steering only; production config remains unchanged.");
        RunCase(parameters, engine, "diag", "frontFy0", 1.0f, reverse: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            FrontSlipLateralMultiplier = 0f
        });
        RunCase(parameters, engine, "diag", "driveSide35", 1.0f, reverse: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            FrontDriveSideMultiplier = 0.35f
        });
        RunCase(parameters, engine, "diag", "rearResist2", 1.0f, reverse: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            RearLateralResistanceMultiplier = 2.0f
        });
        RunCase(parameters, engine, "diag", "kinBlend", 1.0f, reverse: false, diagnostics: new ClassicLowSpeedForceDiagnosticOptions
        {
            KinematicYawBlend = 0.85f,
            KinematicBlendEndSpeedMetersPerSecond = 2.5f
        });
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string mode,
        string label,
        float steer,
        bool reverse,
        ClassicFourWheelAssistOptions? assists = null,
        ClassicLowSpeedForceDiagnosticOptions? diagnostics = null,
        bool alternatingSteer = false)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = reverse ? -1 : 1;
        simulator.State.Rpm = parameters.IdleRpm;
        simulator.State.Velocity = Vector2.Zero;
        if (assists is not null)
        {
            simulator.AssistOptions = assists;
        }
        if (diagnostics is not null)
        {
            simulator.LowSpeedForceDiagnosticOptionsForProbe = diagnostics;
        }

        int checkpointIndex = 0;
        int firstRearSidewaysTick = -1;
        float firstRearSidewaysSpeed = 0f;
        float maximumBetaBelowFiveKmh = 0f;
        float maximumYawRatioBelowFiveKmh = 0f;
        float maximumRearSlipBelowFiveKmh = 0f;
        float maximumYawRateStepDegreesBelowTwentyKmh = 0f;
        float maximumBetaStepDegreesBelowTwentyKmh = 0f;
        float maximumLateralSpeedStepBelowTwentyKmh = 0f;
        float maximumHeadingStepDegreesBelowTwentyKmh = 0f;
        float maximumBodyRollStepDegreesBelowTwentyKmh = 0f;
        float maximumBodyPitchStepDegreesBelowTwentyKmh = 0f;
        float maximumPositionYStepBelowTwentyKmh = 0f;
        float maximumSteerAngleStepDegreesBelowTwentyKmh = 0f;
        int maximumYawRateStepTick = 0;
        int maximumBetaStepTick = 0;
        int maximumLateralSpeedStepTick = 0;
        int maximumHeadingStepTick = 0;
        int maximumBodyRollStepTick = 0;
        int maximumBodyPitchStepTick = 0;
        int maximumPositionYStepTick = 0;
        int maximumSteerAngleStepTick = 0;
        float previousYawRateDegrees = MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond);
        float previousBetaDegrees = simulator.State.ClassicBodySlipAngleDegrees;
        float previousLateralSpeed = simulator.State.LateralSpeed;
        float previousHeadingDegrees = MathHelper.ToDegrees(simulator.State.HeadingRadians);
        float previousBodyRollDegrees = MathHelper.ToDegrees(simulator.State.BodyRollRadians);
        float previousBodyPitchDegrees = MathHelper.ToDegrees(simulator.State.BodyPitchRadians);
        float previousPositionY = simulator.State.Position.Y;
        float previousSteerAngleDegrees = (simulator.State.FrontLeftSteerAngleDegrees + simulator.State.FrontRightSteerAngleDegrees) * 0.5f;
        for (int tick = 1; tick <= SecondsToTicks(RunSeconds); tick++)
        {
            float time = tick * Dt;
            float steerInput = alternatingSteer
                ? (time < 2.20f ? steer : time < 3.20f ? -steer : time < 4.20f ? steer : -steer)
                : steer;
            VehicleInput input = reverse
                ? new VehicleInput(0f, 0f, steerInput, reverse: 0.28f)
                : new VehicleInput(0.28f, 0f, steerInput);
            simulator.Update(input, Dt);
            UpdateLowSpeedRegressionMetrics(
                simulator.State,
                parameters,
                ref maximumBetaBelowFiveKmh,
                ref maximumYawRatioBelowFiveKmh,
                ref maximumRearSlipBelowFiveKmh,
                ref maximumYawRateStepDegreesBelowTwentyKmh,
                ref maximumBetaStepDegreesBelowTwentyKmh,
                ref maximumLateralSpeedStepBelowTwentyKmh,
                ref maximumHeadingStepDegreesBelowTwentyKmh,
                ref maximumBodyRollStepDegreesBelowTwentyKmh,
                ref maximumBodyPitchStepDegreesBelowTwentyKmh,
                ref maximumPositionYStepBelowTwentyKmh,
                ref maximumSteerAngleStepDegreesBelowTwentyKmh,
                ref maximumYawRateStepTick,
                ref maximumBetaStepTick,
                ref maximumLateralSpeedStepTick,
                ref maximumHeadingStepTick,
                ref maximumBodyRollStepTick,
                ref maximumBodyPitchStepTick,
                ref maximumPositionYStepTick,
                ref maximumSteerAngleStepTick,
                ref previousYawRateDegrees,
                ref previousBetaDegrees,
                ref previousLateralSpeed,
                ref previousHeadingDegrees,
                ref previousBodyRollDegrees,
                ref previousBodyPitchDegrees,
                ref previousPositionY,
                ref previousSteerAngleDegrees,
                tick);

            float rearLateral = (MathF.Abs(simulator.State.RearLeftLocalLateralSpeedMetersPerSecond) +
                MathF.Abs(simulator.State.RearRightLocalLateralSpeedMetersPerSecond)) * 0.5f;
            if (firstRearSidewaysTick < 0 && rearLateral > 0.08f)
            {
                firstRearSidewaysTick = tick;
                firstRearSidewaysSpeed = simulator.State.SpeedMetersPerSecond * 3.6f;
            }

            if (checkpointIndex < CheckpointsSeconds.Length &&
                time + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(mode, label, CheckpointsSeconds[checkpointIndex], simulator.State, parameters);
                checkpointIndex++;
            }
        }

        if (firstRearSidewaysTick >= 0)
        {
            Console.WriteLine(
                $"  {mode,-10} {label,-8} first rear lateral motion >0.08m/s at {firstRearSidewaysTick * Dt:0.000}s, speed={firstRearSidewaysSpeed:0.00}km/h");
        }
        else
        {
            Console.WriteLine($"  {mode,-10} {label,-8} rear lateral motion stayed below 0.08m/s");
        }

        Console.WriteLine(
            $"  {mode,-10} {label,-8} max low-speed step: " +
            $"yawRate={maximumYawRateStepDegreesBelowTwentyKmh:0.00}deg/s at {maximumYawRateStepTick * Dt:0.000}s, " +
            $"beta={maximumBetaStepDegreesBelowTwentyKmh:0.00}deg at {maximumBetaStepTick * Dt:0.000}s, " +
            $"latSpeed={maximumLateralSpeedStepBelowTwentyKmh:0.00}m/s at {maximumLateralSpeedStepTick * Dt:0.000}s, " +
            $"heading={maximumHeadingStepDegreesBelowTwentyKmh:0.00}deg at {maximumHeadingStepTick * Dt:0.000}s, " +
            $"steer={maximumSteerAngleStepDegreesBelowTwentyKmh:0.00}deg at {maximumSteerAngleStepTick * Dt:0.000}s");
        Console.WriteLine(
            $"  {mode,-10} {label,-8} max visual step: " +
            $"bodyRoll={maximumBodyRollStepDegreesBelowTwentyKmh:0.00}deg at {maximumBodyRollStepTick * Dt:0.000}s, " +
            $"bodyPitch={maximumBodyPitchStepDegreesBelowTwentyKmh:0.00}deg at {maximumBodyPitchStepTick * Dt:0.000}s, " +
            $"positionY={maximumPositionYStepBelowTwentyKmh * 1000f:0.0}mm at {maximumPositionYStepTick * Dt:0.000}s");

        if (mode == "assist-on" && label == "fwd-left")
        {
            if (maximumBetaBelowFiveKmh > 20f)
            {
                throw new InvalidOperationException(
                    $"Classic low-speed caster probe failed: full-steer CG beta exceeded 20deg below 5km/h ({maximumBetaBelowFiveKmh:0.00}deg).");
            }

            if (maximumYawRatioBelowFiveKmh > 1.50f)
            {
                throw new InvalidOperationException(
                    $"Classic low-speed caster probe failed: full-steer yaw exceeded kinematic expectation below 5km/h ({maximumYawRatioBelowFiveKmh:0.00}x).");
            }

            if (maximumRearSlipBelowFiveKmh > 4.0f && firstRearSidewaysTick >= 0)
            {
                throw new InvalidOperationException(
                    $"Classic low-speed caster probe failed: rear slip exceeded 4deg below 5km/h ({maximumRearSlipBelowFiveKmh:0.00}deg).");
            }

            if (firstRearSidewaysTick >= 0 && firstRearSidewaysTick * Dt < 0.75f)
            {
                throw new InvalidOperationException(
                    $"Classic low-speed caster probe failed: rear lateral motion began too early at {firstRearSidewaysTick * Dt:0.000}s.");
            }

            if (maximumYawRateStepDegreesBelowTwentyKmh > 2.0f)
            {
                throw new InvalidOperationException(
                    $"Classic low-speed caster probe failed: yaw rate jolted by {maximumYawRateStepDegreesBelowTwentyKmh:0.00}deg/s in one tick below 20km/h.");
            }

            if (maximumBetaStepDegreesBelowTwentyKmh > 1.0f)
            {
                throw new InvalidOperationException(
                    $"Classic low-speed caster probe failed: beta jolted by {maximumBetaStepDegreesBelowTwentyKmh:0.00}deg in one tick below 20km/h.");
            }
        }

        if ((label == "fwd-altern" || label == "rev-altern") &&
            maximumYawRateStepDegreesBelowTwentyKmh > 1.60f)
        {
            throw new InvalidOperationException(
                $"Classic low-speed caster probe failed: {label} yaw rate jolted by {maximumYawRateStepDegreesBelowTwentyKmh:0.00}deg/s in one tick below 20km/h.");
        }
    }

    private static void PrintSample(string mode, string label, float time, VehicleState state, VehicleSimulationParameters parameters)
    {
        float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float frontLat = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLat = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float steerRadians = MathHelper.ToRadians(roadAngle);
        float expectedRadius = MathF.Abs(steerRadians) > 0.0001f
            ? parameters.WheelbaseMeters / MathF.Tan(MathF.Abs(steerRadians))
            : float.PositiveInfinity;
        float expectedYawRate = MathF.Abs(steerRadians) > 0.0001f
            ? state.SignedForwardSpeed * MathF.Tan(steerRadians) / MathF.Max(0.25f, parameters.WheelbaseMeters)
            : 0f;
        float actualRadius = MathF.Abs(state.YawRateRadiansPerSecond) > 0.0001f
            ? state.SpeedMetersPerSecond / MathF.Abs(state.YawRateRadiansPerSecond)
            : float.PositiveInfinity;
        float yawRatio = MathF.Abs(expectedYawRate) > 0.0001f
            ? state.YawRateRadiansPerSecond / expectedYawRate
            : 0f;
        float frontSlipDerivedLateral = (state.FrontLeftRelaxedLateralForceN + state.FrontRightRelaxedLateralForceN) *
            MathF.Cos(steerRadians);
        float frontDriveSideForce = (
            state.FrontLeftDriveTorqueNm / MathF.Max(0.05f, parameters.WheelRadiusMeters) +
            state.FrontRightDriveTorqueNm / MathF.Max(0.05f, parameters.WheelRadiusMeters)) *
            MathF.Sin(steerRadians);
        float frontLowSpeedScale = (state.FrontLeftLowSpeedLateralForceScale + state.FrontRightLowSpeedLateralForceScale) * 0.5f;
        float rearLowSpeedScale = (state.RearLeftLowSpeedLateralForceScale + state.RearRightLowSpeedLateralForceScale) * 0.5f;
        RearWheelSample rl = RearWheelSample.From(
            state.RearLeftLocalForwardSpeedMetersPerSecond,
            state.RearLeftLocalLateralSpeedMetersPerSecond,
            state.RearLeftSlipAngleDegrees,
            state.RearLeftRequestedLateralForceN,
            state.RearLeftRelaxedLateralForceN,
            state.RearLeftLateralForceN);
        RearWheelSample rr = RearWheelSample.From(
            state.RearRightLocalForwardSpeedMetersPerSecond,
            state.RearRightLocalLateralSpeedMetersPerSecond,
            state.RearRightSlipAngleDegrees,
            state.RearRightRequestedLateralForceN,
            state.RearRightRelaxedLateralForceN,
            state.RearRightLateralForceN);

        Console.WriteLine(
            $"  {mode,-10} {label,-8} {time,4:F2} {state.SpeedMetersPerSecond * 3.6f,6:F2} " +
            $"{state.Velocity.X,6:F2}/{state.Velocity.Y,6:F2} {state.SignedForwardSpeed,6:F2}/{state.LateralSpeed,6:F2} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:F1} {MathHelper.ToDegrees(expectedYawRate),6:F1} {yawRatio,6:F2} " +
            $"{FormatFinite(actualRadius),6}/{FormatFinite(expectedRadius),6} {state.ClassicBodySlipAngleDegrees,6:F2} " +
            $"{state.Steer,5:F2} {roadAngle,6:F2} {state.LateralAcceleration / 9.81f,6:F2} " +
            $"{frontLat,7:F0}/{rearLat,7:F0} {state.ClassicFrontYawAccelerationDegreesPerSecondSquared,7:F0}/{state.ClassicRearYawAccelerationDegreesPerSecondSquared,7:F0} " +
            $"{state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,7:F0}/{state.ClassicYawDampingAccelerationDegreesPerSecondSquared,7:F0}/{state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared + state.ClassicRearFollowAccelerationDegreesPerSecondSquared,7:F0} " +
            $"{frontSlipDerivedLateral,7:F0}/{frontDriveSideForce,7:F0} {frontLowSpeedScale,4:F2}/{rearLowSpeedScale,4:F2} " +
            $"{rl.LocalForward,6:F2}/{rl.LocalLateral,6:F2} {rl.SlipDegrees,6:F1} {rl.TargetForceN,7:F0} {rl.RelaxedForceN,7:F0} {rl.BodyRightForceN,7:F0} {rl.DirectionLabel,7} " +
            $"{rr.LocalForward,6:F2}/{rr.LocalLateral,6:F2} {rr.SlipDegrees,6:F1} {rr.TargetForceN,7:F0} {rr.RelaxedForceN,7:F0} {rr.BodyRightForceN,7:F0} {rr.DirectionLabel,7}");
    }

    private static string FormatFinite(float value)
    {
        return float.IsFinite(value) ? value.ToString("0.0") : "inf";
    }

    private static void UpdateLowSpeedRegressionMetrics(
        VehicleState state,
        VehicleSimulationParameters parameters,
        ref float maximumBetaBelowFiveKmh,
        ref float maximumYawRatioBelowFiveKmh,
        ref float maximumRearSlipBelowFiveKmh,
        ref float maximumYawRateStepDegreesBelowTwentyKmh,
        ref float maximumBetaStepDegreesBelowTwentyKmh,
        ref float maximumLateralSpeedStepBelowTwentyKmh,
        ref float maximumHeadingStepDegreesBelowTwentyKmh,
        ref float maximumBodyRollStepDegreesBelowTwentyKmh,
        ref float maximumBodyPitchStepDegreesBelowTwentyKmh,
        ref float maximumPositionYStepBelowTwentyKmh,
        ref float maximumSteerAngleStepDegreesBelowTwentyKmh,
        ref int maximumYawRateStepTick,
        ref int maximumBetaStepTick,
        ref int maximumLateralSpeedStepTick,
        ref int maximumHeadingStepTick,
        ref int maximumBodyRollStepTick,
        ref int maximumBodyPitchStepTick,
        ref int maximumPositionYStepTick,
        ref int maximumSteerAngleStepTick,
        ref float previousYawRateDegrees,
        ref float previousBetaDegrees,
        ref float previousLateralSpeed,
        ref float previousHeadingDegrees,
        ref float previousBodyRollDegrees,
        ref float previousBodyPitchDegrees,
        ref float previousPositionY,
        ref float previousSteerAngleDegrees,
        int tick)
    {
        float speedKmh = state.SpeedMetersPerSecond * 3.6f;
        float yawRateDegrees = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
        float betaDegrees = state.ClassicBodySlipAngleDegrees;
        float headingDegrees = MathHelper.ToDegrees(state.HeadingRadians);
        float bodyRollDegrees = MathHelper.ToDegrees(state.BodyRollRadians);
        float bodyPitchDegrees = MathHelper.ToDegrees(state.BodyPitchRadians);
        float steerAngleDegrees = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        if (speedKmh <= 20f)
        {
            float yawRateStep = MathF.Abs(yawRateDegrees - previousYawRateDegrees);
            float betaStep = MathF.Abs(betaDegrees - previousBetaDegrees);
            float lateralSpeedStep = MathF.Abs(state.LateralSpeed - previousLateralSpeed);
            float headingStep = MathF.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(
                MathHelper.ToRadians(headingDegrees - previousHeadingDegrees))));
            float bodyRollStep = MathF.Abs(bodyRollDegrees - previousBodyRollDegrees);
            float bodyPitchStep = MathF.Abs(bodyPitchDegrees - previousBodyPitchDegrees);
            float positionYStep = MathF.Abs(state.Position.Y - previousPositionY);
            float steerAngleStep = MathF.Abs(steerAngleDegrees - previousSteerAngleDegrees);
            if (yawRateStep > maximumYawRateStepDegreesBelowTwentyKmh)
            {
                maximumYawRateStepDegreesBelowTwentyKmh = yawRateStep;
                maximumYawRateStepTick = tick;
            }

            if (betaStep > maximumBetaStepDegreesBelowTwentyKmh)
            {
                maximumBetaStepDegreesBelowTwentyKmh = betaStep;
                maximumBetaStepTick = tick;
            }

            if (lateralSpeedStep > maximumLateralSpeedStepBelowTwentyKmh)
            {
                maximumLateralSpeedStepBelowTwentyKmh = lateralSpeedStep;
                maximumLateralSpeedStepTick = tick;
            }

            if (headingStep > maximumHeadingStepDegreesBelowTwentyKmh)
            {
                maximumHeadingStepDegreesBelowTwentyKmh = headingStep;
                maximumHeadingStepTick = tick;
            }

            if (bodyRollStep > maximumBodyRollStepDegreesBelowTwentyKmh)
            {
                maximumBodyRollStepDegreesBelowTwentyKmh = bodyRollStep;
                maximumBodyRollStepTick = tick;
            }

            if (bodyPitchStep > maximumBodyPitchStepDegreesBelowTwentyKmh)
            {
                maximumBodyPitchStepDegreesBelowTwentyKmh = bodyPitchStep;
                maximumBodyPitchStepTick = tick;
            }

            if (positionYStep > maximumPositionYStepBelowTwentyKmh)
            {
                maximumPositionYStepBelowTwentyKmh = positionYStep;
                maximumPositionYStepTick = tick;
            }

            if (steerAngleStep > maximumSteerAngleStepDegreesBelowTwentyKmh)
            {
                maximumSteerAngleStepDegreesBelowTwentyKmh = steerAngleStep;
                maximumSteerAngleStepTick = tick;
            }
        }

        previousYawRateDegrees = yawRateDegrees;
        previousBetaDegrees = betaDegrees;
        previousLateralSpeed = state.LateralSpeed;
        previousHeadingDegrees = headingDegrees;
        previousBodyRollDegrees = bodyRollDegrees;
        previousBodyPitchDegrees = bodyPitchDegrees;
        previousPositionY = state.Position.Y;
        previousSteerAngleDegrees = steerAngleDegrees;

        if (speedKmh > 5f)
        {
            return;
        }

        float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float steerRadians = MathHelper.ToRadians(roadAngle);
        float expectedYawRate = MathF.Abs(steerRadians) > 0.0001f
            ? state.SignedForwardSpeed * MathF.Tan(steerRadians) / MathF.Max(0.25f, parameters.WheelbaseMeters)
            : 0f;
        if (MathF.Abs(expectedYawRate) > 0.01f)
        {
            maximumYawRatioBelowFiveKmh = MathF.Max(
                maximumYawRatioBelowFiveKmh,
                MathF.Abs(state.YawRateRadiansPerSecond / expectedYawRate));
        }

        maximumBetaBelowFiveKmh = MathF.Max(maximumBetaBelowFiveKmh, MathF.Abs(state.ClassicBodySlipAngleDegrees));
        maximumRearSlipBelowFiveKmh = MathF.Max(
            maximumRearSlipBelowFiveKmh,
            MathF.Max(MathF.Abs(state.RearLeftSlipAngleDegrees), MathF.Abs(state.RearRightSlipAngleDegrees)));
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct RearWheelSample(
        float LocalForward,
        float LocalLateral,
        float SlipDegrees,
        float TargetForceN,
        float RelaxedForceN,
        float BodyRightForceN,
        string DirectionLabel)
    {
        public static RearWheelSample From(
            float localForward,
            float localLateral,
            float slipDegrees,
            float targetForce,
            float relaxedForce,
            float bodyRightForce)
        {
            string direction = MathF.Abs(localLateral) < 0.03f || MathF.Abs(bodyRightForce) < 1f
                ? "neutral"
                : MathF.Sign(localLateral) == MathF.Sign(bodyRightForce)
                    ? "assists"
                    : "opposes";
            return new RearWheelSample(
                localForward,
                localLateral,
                slipDegrees,
                targetForce,
                relaxedForce,
                bodyRightForce,
                direction);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
