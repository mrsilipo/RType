using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicFfLimitStateProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float Gravity = 9.81f;

    private static readonly LimitCase[] Cases =
    [
        new("steady-low", 100f, 2.5f, time => new VehicleInput(0.25f, 0f, 0.35f, brakeAssistEnabled: true), 0.35f, "ordinary 70-80% corner"),
        new("steady-near", 120f, 2.5f, time => new VehicleInput(0.25f, 0f, 0.75f, brakeAssistEnabled: true), 0.75f, "near-limit steady sweeper"),
        new("power-exit", 100f, 2.5f, time => new VehicleInput(time < 0.75f ? 0.20f : 1.0f, 0f, 0.60f, brakeAssistEnabled: true), 0.60f, "front tyres share drive and cornering"),
        new("lift-mid", 120f, 2.5f, time => new VehicleInput(time < 0.80f ? 0.35f : 0f, 0f, 0.65f, brakeAssistEnabled: true), 0.65f, "lift should tighten line and wake rear"),
        new("trail-release", 120f, 2.5f, time => new VehicleInput(0f, TrailBrake(time), 0.65f, brakeAssistEnabled: true), 0.65f, "brake release should restore front lateral authority"),
        new("bad-lift", 120f, 2.5f, time => new VehicleInput(time < 0.75f ? 0.35f : 0f, 0f, 0.82f, brakeAssistEnabled: true), 0.82f, "poor driver: abrupt lift with no correction"),
        new("catch-lift", 120f, 2.5f, time => new VehicleInput(time < 0.75f ? 0.35f : 0f, 0f, time < 1.05f ? 0.82f : -0.35f, brakeAssistEnabled: true), 0.82f, "same lift, then corrective steering"),
        new("left-right", 100f, 2.5f, time => new VehicleInput(0.25f, 0f, time < 0.85f ? 0.75f : -0.75f, brakeAssistEnabled: true), 0.75f, "rapid reversal should carry tyre/chassis state")
    ];

    private static readonly float[] CheckpointsSeconds = [0.50f, 0.80f, 1.05f, 1.35f, 1.80f, 2.50f];
    private static readonly ProbeTyreVariant[] TyreVariants =
    [
        new("current-load/current-peak", float.NaN, float.NaN, float.NaN),
        new("current-load/progressive-peak", float.NaN, 0.78f, 18f),
        new("current-load/stronger-peak", float.NaN, 0.68f, 16f),
        new("stronger-load/current-peak", 0.22f, float.NaN, float.NaN),
        new("stronger-load/progressive-peak", 0.22f, 0.78f, 18f),
        new("stronger-load/stronger-peak", 0.22f, 0.68f, 16f)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engine.ClassicFourWheel.Yaw.InertiaScale));

        Console.WriteLine($"Classic FF limit-state probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  frozen baseline: no production tuning changed by this probe.");
        Console.WriteLine("  summary columns: case latG avg/max slipF/R beta final/max betaDotMax yaw final yawAccMax loadRearMin capRearLoss yawF/Rmax gripF/Rmax assistYawMax cleanupMax brakeRegMin state verdict");

        foreach (ProbeTyreVariant variant in TyreVariants)
        {
            Console.WriteLine();
            Console.WriteLine($"  tyre variant {variant.Label}: loadSens={(float.IsFinite(variant.LoadSensitivity) ? variant.LoadSensitivity.ToString("F2") : "current")} slidingGrip={(float.IsFinite(variant.SlidingGrip) ? variant.SlidingGrip.ToString("F2") : "current")} falloff={(float.IsFinite(variant.FalloffSlipDegrees) ? variant.FalloffSlipDegrees.ToString("F1") : "current")}");
            foreach (LimitCase limitCase in Cases)
            {
                Result result = RunCase(parameters, engine, yawInertia, limitCase, variant, printSamples: variant.Label == "current-load/current-peak");
                Console.WriteLine(
                    $"  summary {variant.Label,-29} {limitCase.Label,-13} " +
                    $"{result.AverageLateralG,4:F2}/{result.PeakLateralG,4:F2} " +
                    $"{result.FinalFrontSlipDegrees,5:F2}/{result.FinalRearSlipDegrees,5:F2} " +
                    $"{result.FinalBetaDegrees,5:F2}/{result.PeakAbsBetaDegrees,5:F2} " +
                    $"{result.PeakAbsBetaDotDegreesPerSecond,6:F1} {result.FinalYawRateDegreesPerSecond,6:F1} {result.PeakAbsYawAccelerationDegreesPerSecondSquared,6:F0} " +
                    $"{result.MinimumRearWheelLoadN,6:F0} {result.PeakRearCapacityLossFraction * 100f,5:F1}% " +
                    $"{result.PeakAbsFrontYawMomentNm,6:F0}/{result.PeakAbsRearYawMomentNm,6:F0} " +
                    $"{result.PeakFrontGripUsage,4:F2}/{result.PeakRearGripUsage,4:F2} " +
                    $"{result.PeakAbsYawRecoveryDegreesPerSecondSquared,6:F0} {result.PeakAbsCleanupForceN,6:F0} " +
                    $"{result.MinimumBrakePressureRatio,4:F2} {result.PeakLimitState,-11} {Classify(result)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  classification guide:");
        Console.WriteLine("    rear-load-no-capacity-loss = rear unloads, but tyre load sensitivity is too weak to matter.");
        Console.WriteLine("    capacity-loss-no-yaw = rear capacity changes, but yaw/beta barely respond.");
        Console.WriteLine("    assist-leash = yaw recovery/cleanup is materially active during the intended limit event.");
        Console.WriteLine("    combined-slip-front = braking/drive consumes front budget before useful cornering balance appears.");
        Console.WriteLine("    too-safe = loads/capacity/yaw all move, but never enough to enter a readable near-limit state.");
        Console.WriteLine("Classic FF limit-state probe complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float yawInertia,
        LimitCase limitCase,
        ProbeTyreVariant variant,
        bool printSamples)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        if (float.IsFinite(variant.LoadSensitivity))
        {
            simulator.TyreLoadSensitivityOverrideForProbe = variant.LoadSensitivity;
        }

        if (float.IsFinite(variant.SlidingGrip) || float.IsFinite(variant.FalloffSlipDegrees))
        {
            simulator.TyrePostPeakSlidingGripOverrideForProbe = variant.SlidingGrip;
            simulator.TyrePostPeakFalloffSlipDegreesOverrideForProbe = variant.FalloffSlipDegrees;
        }

        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, limitCase.StartSpeedKmh / 3.6f);

        float staticRearCapacity = 0f;
        float lateralGSum = 0f;
        int lateralGSamples = 0;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        int sampleStart = SecondsToTicks(0.65f);
        int checkpointIndex = 0;
        Accumulator accumulator = new();

        if (printSamples)
        {
            Console.WriteLine();
            Console.WriteLine($"  case {limitCase.Label}: {limitCase.Description}");
            Console.WriteLine("    t speed throttle brake steer latG slipF/R beta betaDot yaw yawAcc loads FL/FR/RL/RR capF/R latF/R yawF/R gripF/R yawRec cleanup brakeRatio limit");
        }

        int ticks = SecondsToTicks(limitCase.DurationSeconds);
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            VehicleInput input = limitCase.Input(time);
            simulator.Update(input, Dt);
            VehicleState state = simulator.State;
            Sample sample = BuildSample(state, previousBeta, yawInertia, limitCase.ReferenceSteerSign, parameters, engine);
            previousBeta = state.ClassicBodySlipAngleDegrees;

            if (staticRearCapacity <= 0f && state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN > 1f)
            {
                staticRearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
            }

            accumulator.Add(sample, staticRearCapacity);

            if (tick >= sampleStart)
            {
                lateralGSum += MathF.Abs(state.LateralAcceleration) / Gravity;
                lateralGSamples++;
            }

            if (printSamples &&
                checkpointIndex < CheckpointsSeconds.Length &&
                time + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintSample(CheckpointsSeconds[checkpointIndex], state, sample);
                checkpointIndex++;
            }
        }

        VehicleState final = simulator.State;
        Sample finalSample = BuildSample(final, previousBeta, yawInertia, limitCase.ReferenceSteerSign, parameters, engine);
        return accumulator.ToResult(
            finalSample,
            lateralGSamples > 0 ? lateralGSum / lateralGSamples : 0f);
    }

    private static Sample BuildSample(
        VehicleState state,
        float previousBetaDegrees,
        float yawInertia,
        float referenceSteerSign,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        float turnSign = MathF.Sign(referenceSteerSign);
        if (turnSign == 0f)
        {
            turnSign = MathF.Sign(state.Steer);
        }

        if (turnSign == 0f)
        {
            turnSign = 1f;
        }

        float frontSlip = Average(state.FrontLeftSlipAngleDegrees, state.FrontRightSlipAngleDegrees) * turnSign;
        float rearSlip = Average(state.RearLeftSlipAngleDegrees, state.RearRightSlipAngleDegrees) * turnSign;
        float beta = state.ClassicBodySlipAngleDegrees * turnSign;
        float betaDot = (state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnSign;
        float frontCapacity = state.FrontLeftFrictionEllipseGripBudgetN + state.FrontRightFrictionEllipseGripBudgetN;
        float rearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
        float frontLateral = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateral = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontYawMoment = MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * yawInertia;
        float rearYawMoment = MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * yawInertia;
        float yawAcceleration = state.ClassicNaturalYawAccelerationDegreesPerSecondSquared +
            state.ClassicYawDampingAccelerationDegreesPerSecondSquared +
            state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared +
            state.ClassicRearFollowAccelerationDegreesPerSecondSquared;
        float cleanupForce = state.ClassicBodySlipDampingForceN + state.ClassicLateralVelocityDampingForceN;
        float brakeRatio = Minimum(
            state.FrontLeftBrakePressureRatio,
            state.FrontRightBrakePressureRatio,
            state.RearLeftBrakePressureRatio,
            state.RearRightBrakePressureRatio);

        return new Sample(
            MathF.Abs(state.LateralAcceleration) / Gravity,
            frontSlip,
            rearSlip,
            beta,
            betaDot,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond) * turnSign,
            yawAcceleration * turnSign,
            MathF.Min(state.RearLeftLoadN, state.RearRightLoadN),
            frontCapacity,
            rearCapacity,
            frontLateral,
            rearLateral,
            frontYawMoment * turnSign,
            rearYawMoment * turnSign,
            MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
            MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage),
            MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared),
            MathF.Abs(cleanupForce),
            brakeRatio,
            ClassifyLimitState(frontSlip, rearSlip, beta, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage), MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage)));
    }

    private static void PrintSample(float time, VehicleState state, Sample sample)
    {
        Console.WriteLine(
            $"    {time,4:F2} {state.SpeedMetersPerSecond * 3.6f,6:F1} {state.Throttle,5:F2} {state.Brake,5:F2} {state.Steer,5:F2} " +
            $"{sample.LateralG,4:F2} {sample.FrontSlipDegrees,5:F2}/{sample.RearSlipDegrees,5:F2} " +
            $"{sample.BetaDegrees,5:F2} {sample.BetaDotDegreesPerSecond,7:F1} {sample.YawRateDegreesPerSecond,6:F1} {sample.YawAccelerationDegreesPerSecondSquared,7:F0} " +
            $"{state.FrontLeftLoadN,5:F0}/{state.FrontRightLoadN,5:F0}/{state.RearLeftLoadN,5:F0}/{state.RearRightLoadN,5:F0} " +
            $"{sample.FrontCapacityN,6:F0}/{sample.RearCapacityN,6:F0} " +
            $"{sample.FrontLateralForceN,6:F0}/{sample.RearLateralForceN,6:F0} " +
            $"{sample.FrontYawMomentNm,7:F0}/{sample.RearYawMomentNm,7:F0} " +
            $"{sample.FrontGripUsage,4:F2}/{sample.RearGripUsage,4:F2} " +
            $"{sample.YawRecoveryDegreesPerSecondSquared,6:F0} {sample.CleanupForceN,6:F0} {sample.BrakePressureRatio,4:F2} {sample.LimitState}");
    }

    private static string Classify(Result result)
    {
        if (result.MinimumRearWheelLoadN < 900f && result.PeakRearCapacityLossFraction < 0.10f)
        {
            return "rear-load-no-capacity-loss";
        }

        if (result.PeakRearCapacityLossFraction >= 0.10f &&
            result.PeakAbsBetaDegrees < 3.0f &&
            result.PeakAbsRearYawMomentNm < result.PeakAbsFrontYawMomentNm * 0.35f)
        {
            return "capacity-loss-no-yaw";
        }

        if (result.PeakAbsYawRecoveryDegreesPerSecondSquared > 20f ||
            result.PeakAbsCleanupForceN > 2500f)
        {
            return "assist-leash";
        }

        if (result.PeakFrontGripUsage > 0.95f &&
            MathF.Abs(result.FinalFrontSlipDegrees) > 9f)
        {
            return "combined-slip-front";
        }

        if (result.PeakLimitState is "GRIP" or "LOADED")
        {
            return "too-safe";
        }

        if (result.PeakAbsBetaDegrees > 7f)
        {
            return "runaway-beta";
        }

        return "limit-state-present";
    }

    private static string ClassifyLimitState(float frontSlip, float rearSlip, float beta, float frontGrip, float rearGrip)
    {
        float peakSlip = MathF.Max(MathF.Abs(frontSlip), MathF.Abs(rearSlip));
        float peakGrip = MathF.Max(frontGrip, rearGrip);
        float absBeta = MathF.Abs(beta);

        if (peakSlip >= 12f || absBeta >= 8f)
        {
            return "SLIDING";
        }

        if (peakSlip >= 8f || peakGrip >= 0.95f || absBeta >= 5f)
        {
            return "OVERDRIVEN";
        }

        if (peakSlip >= 5f || peakGrip >= 0.75f || absBeta >= 3f)
        {
            return "NEAR_LIMIT";
        }

        if (peakSlip >= 2f || peakGrip >= 0.35f)
        {
            return "LOADED";
        }

        return "GRIP";
    }

    private static float TrailBrake(float time)
    {
        if (time < 0.35f)
        {
            return 0.85f;
        }

        return MathHelper.Lerp(0.85f, 0.10f, SmoothStep01((time - 0.35f) / 0.90f));
    }

    private static int SecondsToTicks(float seconds) => Math.Max(1, (int)MathF.Round(seconds / Dt));

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Average(float a, float b) => (a + b) * 0.5f;

    private static float Minimum(float a, float b, float c, float d) => MathF.Min(MathF.Min(a, b), MathF.Min(c, d));

    private readonly record struct LimitCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        Func<float, VehicleInput> Input,
        float ReferenceSteerSign,
        string Description);

    private readonly record struct ProbeTyreVariant(
        string Label,
        float LoadSensitivity,
        float SlidingGrip,
        float FalloffSlipDegrees);

    private readonly record struct Sample(
        float LateralG,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        float BetaDegrees,
        float BetaDotDegreesPerSecond,
        float YawRateDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float MinimumRearWheelLoadN,
        float FrontCapacityN,
        float RearCapacityN,
        float FrontLateralForceN,
        float RearLateralForceN,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float FrontGripUsage,
        float RearGripUsage,
        float YawRecoveryDegreesPerSecondSquared,
        float CleanupForceN,
        float BrakePressureRatio,
        string LimitState);

    private readonly record struct Result(
        float AverageLateralG,
        float PeakLateralG,
        float FinalFrontSlipDegrees,
        float FinalRearSlipDegrees,
        float FinalBetaDegrees,
        float PeakAbsBetaDegrees,
        float PeakAbsBetaDotDegreesPerSecond,
        float FinalYawRateDegreesPerSecond,
        float PeakAbsYawAccelerationDegreesPerSecondSquared,
        float MinimumRearWheelLoadN,
        float PeakRearCapacityLossFraction,
        float PeakAbsFrontYawMomentNm,
        float PeakAbsRearYawMomentNm,
        float PeakFrontGripUsage,
        float PeakRearGripUsage,
        float PeakAbsYawRecoveryDegreesPerSecondSquared,
        float PeakAbsCleanupForceN,
        float MinimumBrakePressureRatio,
        string PeakLimitState);

    private sealed class Accumulator
    {
        private float _peakLateralG;
        private float _peakAbsBeta;
        private float _peakAbsBetaDot;
        private float _peakAbsYawAcceleration;
        private float _minimumRearLoad = float.PositiveInfinity;
        private float _peakRearCapacityLoss;
        private float _peakAbsFrontYawMoment;
        private float _peakAbsRearYawMoment;
        private float _peakFrontGrip;
        private float _peakRearGrip;
        private float _peakYawRecovery;
        private float _peakCleanup;
        private float _minimumBrakePressure = 1f;
        private string _peakLimitState = "GRIP";

        public void Add(Sample sample, float staticRearCapacity)
        {
            _peakLateralG = MathF.Max(_peakLateralG, sample.LateralG);
            _peakAbsBeta = MathF.Max(_peakAbsBeta, MathF.Abs(sample.BetaDegrees));
            _peakAbsBetaDot = MathF.Max(_peakAbsBetaDot, MathF.Abs(sample.BetaDotDegreesPerSecond));
            _peakAbsYawAcceleration = MathF.Max(_peakAbsYawAcceleration, MathF.Abs(sample.YawAccelerationDegreesPerSecondSquared));
            _minimumRearLoad = MathF.Min(_minimumRearLoad, sample.MinimumRearWheelLoadN);
            if (staticRearCapacity > 1f)
            {
                _peakRearCapacityLoss = MathF.Max(_peakRearCapacityLoss, MathHelper.Clamp((staticRearCapacity - sample.RearCapacityN) / staticRearCapacity, 0f, 1f));
            }

            _peakAbsFrontYawMoment = MathF.Max(_peakAbsFrontYawMoment, MathF.Abs(sample.FrontYawMomentNm));
            _peakAbsRearYawMoment = MathF.Max(_peakAbsRearYawMoment, MathF.Abs(sample.RearYawMomentNm));
            _peakFrontGrip = MathF.Max(_peakFrontGrip, sample.FrontGripUsage);
            _peakRearGrip = MathF.Max(_peakRearGrip, sample.RearGripUsage);
            _peakYawRecovery = MathF.Max(_peakYawRecovery, sample.YawRecoveryDegreesPerSecondSquared);
            _peakCleanup = MathF.Max(_peakCleanup, sample.CleanupForceN);
            _minimumBrakePressure = MathF.Min(_minimumBrakePressure, sample.BrakePressureRatio);
            if (Rank(sample.LimitState) > Rank(_peakLimitState))
            {
                _peakLimitState = sample.LimitState;
            }
        }

        public Result ToResult(Sample final, float averageLateralG)
        {
            return new Result(
                averageLateralG,
                _peakLateralG,
                final.FrontSlipDegrees,
                final.RearSlipDegrees,
                final.BetaDegrees,
                _peakAbsBeta,
                _peakAbsBetaDot,
                final.YawRateDegreesPerSecond,
                _peakAbsYawAcceleration,
                _minimumRearLoad,
                _peakRearCapacityLoss,
                _peakAbsFrontYawMoment,
                _peakAbsRearYawMoment,
                _peakFrontGrip,
                _peakRearGrip,
                _peakYawRecovery,
                _peakCleanup,
                _minimumBrakePressure,
                _peakLimitState);
        }

        private static int Rank(string state)
        {
            return state switch
            {
                "LOADED" => 1,
                "NEAR_LIMIT" => 2,
                "OVERDRIVEN" => 3,
                "SLIDING" => 4,
                _ => 0
            };
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
