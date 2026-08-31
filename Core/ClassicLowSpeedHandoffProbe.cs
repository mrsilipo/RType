using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicLowSpeedHandoffProbe
{
    private const float Dt = 1f / 120f;
    private const float RunSeconds = 12.0f;
    private const float AuditSpeedMinKmh = 7.0f;
    private const float AuditSpeedMaxKmh = 13.0f;
    private const float Throttle = 0.68f;

    private static readonly HandoffVariant[] Variants =
    [
        new("current", null, null, null, false),
        new("early", 3.0f / 3.6f, 6.5f / 3.6f, null, false),
        new("late", 10.0f / 3.6f, 18.0f / 3.6f, null, false),
        new("floor-low", null, null, 1.5f, false),
        new("floor-high", null, null, 5.0f, false),
        new("floor-smooth", null, null, 3.0f, true)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);

        Console.WriteLine($"Classic low-speed handoff probe: {parameters.DisplayName}");
        Console.WriteLine("  goal: prove whether the 7-13km/h jolt follows the rolling/dynamic tyre handoff.");
        Console.WriteLine("  production physics is unchanged; transition ranges below are diagnostic overrides only.");
        Console.WriteLine("  abbreviations: rollW=rolling blend weight, scale=low-speed lateral force scale, slipFy=dynamic target after low-speed scale, targetFy=blended target before relaxation, relaxedFy=actual relaxed target, finalFy=post grip clamp, contactFy=actual post-force rolling-contact impulse as equivalent force.");

        foreach (HandoffVariant variant in Variants)
        {
            RunCase(parameters, options, variant, "steady-fwd", reverse: false, alternating: false, diagnostics: null);
            RunCase(parameters, options, variant, "altern-fwd", reverse: false, alternating: true, diagnostics: null);
            RunCase(parameters, options, variant, "steady-rev", reverse: true, alternating: false, diagnostics: null);
            RunCase(parameters, options, variant, "altern-rev", reverse: true, alternating: true, diagnostics: null);
        }

        ClassicLowSpeedForceDiagnosticOptions noPostImpulse = new()
        {
            DisablePostForceRollingContactConstraint = true
        };
        ClassicLowSpeedForceDiagnosticOptions rollingTargetOnly = new()
        {
            DisablePostForceRollingContactConstraint = true,
            RollingConstraintOnlyBelowTransition = true
        };
        ClassicLowSpeedForceDiagnosticOptions forceUnwind = new()
        {
            UnwindLateralForceBeforeSignChange = true
        };
        ClassicLowSpeedForceDiagnosticOptions contactPatchSlip = new()
        {
            UseContactPatchSlipRelaxation = true
        };
        ClassicLowSpeedForceDiagnosticOptions slipRate180 = new()
        {
            LimitLowSpeedSlipRate = true,
            MaxLowSpeedSlipRateDegreesPerSecond = 180f
        };
        ClassicLowSpeedForceDiagnosticOptions slipRate120 = new()
        {
            LimitLowSpeedSlipRate = true,
            MaxLowSpeedSlipRateDegreesPerSecond = 120f
        };
        ClassicLowSpeedForceDiagnosticOptions slipRate80 = new()
        {
            LimitLowSpeedSlipRate = true,
            MaxLowSpeedSlipRateDegreesPerSecond = 80f
        };
        HandoffVariant current = Variants[0];
        RunCase(parameters, options, current, "A-no-post-fwd", reverse: false, alternating: true, noPostImpulse);
        RunCase(parameters, options, current, "A-no-post-rev", reverse: true, alternating: true, noPostImpulse);
        RunCase(parameters, options, current, "B-target-fwd", reverse: false, alternating: true, rollingTargetOnly);
        RunCase(parameters, options, current, "B-target-rev", reverse: true, alternating: true, rollingTargetOnly);
        RunCase(parameters, options, current, "C-unwind-fwd", reverse: false, alternating: true, forceUnwind);
        RunCase(parameters, options, current, "C-unwind-rev", reverse: true, alternating: true, forceUnwind);
        RunCase(parameters, options, current, "D-contact-fwd", reverse: false, alternating: true, contactPatchSlip);
        RunCase(parameters, options, current, "D-contact-rev", reverse: true, alternating: true, contactPatchSlip);
        RunCase(parameters, options, current, "E-rate180-fwd", reverse: false, alternating: true, slipRate180);
        RunCase(parameters, options, current, "E-rate180-rev", reverse: true, alternating: true, slipRate180);
        RunCase(parameters, options, current, "F-rate120-fwd", reverse: false, alternating: true, slipRate120);
        RunCase(parameters, options, current, "F-rate120-rev", reverse: true, alternating: true, slipRate120);
        RunCase(parameters, options, current, "G-rate080-fwd", reverse: false, alternating: true, slipRate80);
        RunCase(parameters, options, current, "G-rate080-rev", reverse: true, alternating: true, slipRate80);

        Console.WriteLine("Classic low-speed handoff probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        GameLaunchOptions options,
        HandoffVariant variant,
        string label,
        bool reverse,
        bool alternating,
        ClassicLowSpeedForceDiagnosticOptions? diagnostics)
    {
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        float originalRollingEnd = engine.ClassicFourWheel.LowSpeed.RollingDominantEndMetersPerSecond;
        float originalDynamicEnd = engine.ClassicFourWheel.LowSpeed.DynamicBlendEndMetersPerSecond;
        if (variant.RollingEndMetersPerSecond.HasValue)
        {
            engine.ClassicFourWheel.LowSpeed.RollingDominantEndMetersPerSecond = variant.RollingEndMetersPerSecond.Value;
        }

        if (variant.DynamicEndMetersPerSecond.HasValue)
        {
            engine.ClassicFourWheel.LowSpeed.DynamicBlendEndMetersPerSecond = variant.DynamicEndMetersPerSecond.Value;
        }

        float rollingEndKmh = engine.ClassicFourWheel.LowSpeed.RollingDominantEndMetersPerSecond * 3.6f;
        float dynamicEndKmh = engine.ClassicFourWheel.LowSpeed.DynamicBlendEndMetersPerSecond * 3.6f;
        float slipFloorKmh = engine.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond * 3.6f;
        string relaxationFloorText = variant.RelaxationSpeedFloorMetersPerSecond.HasValue
            ? $"{variant.RelaxationSpeedFloorMetersPerSecond.Value * 3.6f:0.0}km/h{(variant.SmoothRelaxationSpeedFloor ? " smooth" : " max")}"
            : "current";

        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        if (diagnostics is not null)
        {
            simulator.LowSpeedForceDiagnosticOptionsForProbe = diagnostics;
        }
        if (variant.RelaxationSpeedFloorMetersPerSecond.HasValue)
        {
            simulator.LateralRelaxationSpeedFloorOverrideForProbe = variant.RelaxationSpeedFloorMetersPerSecond.Value;
        }

        simulator.SmoothLateralRelaxationSpeedFloorForProbe = variant.SmoothRelaxationSpeedFloor;

        simulator.State.Gear = reverse ? -1 : 1;
        simulator.State.Rpm = parameters.IdleRpm;

        List<Sample> samples = [];
        Sample previous = Sample.From(0f, 0f, simulator.State, parameters);
        Sample worst = previous;
        int worstIndex = -1;
        float worstScore = float.NegativeInfinity;

        for (int tick = 1; tick <= SecondsToTicks(RunSeconds); tick++)
        {
            float time = tick * Dt;
            float steer = alternating
                ? (time < 1.60f ? 1f : time < 2.60f ? -1f : time < 3.60f ? 1f : -1f)
                : 1f;
            VehicleInput input = reverse
                ? new VehicleInput(0f, 0f, steer, reverse: Throttle)
                : new VehicleInput(Throttle, 0f, steer);
            simulator.Update(input, Dt);

            Sample sample = Sample.From(time, steer, simulator.State, parameters);
            sample = sample with
            {
                YawRateStepDegreesPerSecond = MathF.Abs(sample.YawRateDegreesPerSecond - previous.YawRateDegreesPerSecond),
                YawAccelerationStepDegreesPerSecondSquared = MathF.Abs(sample.YawAccelerationDegreesPerSecondSquared - previous.YawAccelerationDegreesPerSecondSquared),
                BetaStepDegrees = MathF.Abs(sample.BetaDegrees - previous.BetaDegrees),
                BlendStep = MaxWheelStep(sample, previous, wheel => wheel.RollingBlend),
                ScaleStep = MaxWheelStep(sample, previous, wheel => wheel.LowSpeedForceScale),
                TargetStepN = MaxWheelStep(sample, previous, wheel => wheel.TargetLateralForceN),
                SlipTargetStepN = MaxWheelStep(sample, previous, wheel => wheel.SlipLateralForceN),
                RelaxedStepN = MaxWheelStep(sample, previous, wheel => wheel.RelaxedLateralForceN),
                FinalStepN = MaxWheelStep(sample, previous, wheel => wheel.FinalLateralForceN),
                RollingStepN = MaxWheelStep(sample, previous, wheel => wheel.RollingConstraintForceN),
                RollingContactStepN = MaxWheelStep(sample, previous, wheel => wheel.RollingContactForceN),
                RollingContactYawMomentStepNm = MaxWheelStep(sample, previous, wheel => wheel.RollingContactYawMomentNm)
            };

            samples.Add(sample);

            if (sample.SpeedKmh >= AuditSpeedMinKmh && sample.SpeedKmh <= AuditSpeedMaxKmh)
            {
                float score =
                    sample.YawAccelerationStepDegreesPerSecondSquared * 0.8f +
                    sample.YawRateStepDegreesPerSecond * 8f +
                    sample.BetaStepDegrees * 20f +
                    sample.BlendStep * 350f +
                    sample.ScaleStep * 300f +
                    sample.TargetStepN / 18f +
                    sample.RelaxedStepN / 20f +
                    sample.FinalStepN / 20f +
                    sample.RollingStepN / 30f +
                    sample.RollingContactStepN / 20f +
                    sample.RollingContactYawMomentStepNm / 35f;
                if (score > worstScore)
                {
                    worstScore = score;
                    worst = sample;
                    worstIndex = samples.Count - 1;
                }
            }

            previous = sample;
        }

        if (worstIndex < 0)
        {
            Sample last = samples.Count > 0 ? samples[^1] : previous;
            Console.WriteLine(
                $"  {variant.Label,-7} {label,-11} transition={rollingEndKmh:0.0}-{dynamicEndKmh:0.0}km/h " +
                $"slipFloor={slipFloorKmh:0.0}km/h relaxFloor={relaxationFloorText} did not reach {AuditSpeedMinKmh:0}-{AuditSpeedMaxKmh:0}km/h; final speed={last.SpeedKmh:0.00}km/h.");
            return;
        }

        int firstBandIndex = samples.FindIndex(sample => sample.SpeedKmh >= AuditSpeedMinKmh);
        int halfBlendIndex = samples.FindIndex(sample => Average(sample.Wheels, wheel => wheel.RollingBlend) <= 0.50f);
        int firstDynamicIndex = samples.FindIndex(sample => Average(sample.Wheels, wheel => wheel.RollingBlend) <= 0.01f);
        float firstBandTime = firstBandIndex >= 0 ? samples[firstBandIndex].TimeSeconds : 0f;
        string halfBlendText = halfBlendIndex >= 0 ? $"{samples[halfBlendIndex].TimeSeconds:0.000}s/{samples[halfBlendIndex].SpeedKmh:0.00}km/h" : "not reached";
        string dynamicText = firstDynamicIndex >= 0 ? $"{samples[firstDynamicIndex].TimeSeconds:0.000}s/{samples[firstDynamicIndex].SpeedKmh:0.00}km/h" : "not reached";

        Console.WriteLine(
            $"  {variant.Label,-7} {label,-11} transition={rollingEndKmh:0.0}-{dynamicEndKmh:0.0}km/h " +
            $"slipFloor={slipFloorKmh:0.0}km/h relaxFloor={relaxationFloorText} first7={firstBandTime:0.000}s halfBlend={halfBlendText} dynFull={dynamicText}");
        Console.WriteLine(
            $"    worst t={worst.TimeSeconds:0.000}s speed={worst.SpeedKmh:0.00}km/h steer={worst.SteerInput:0.00} road={worst.RoadWheelAngleDegrees:0.00}deg " +
            $"yaw={worst.YawRateDegreesPerSecond:0.00}deg/s dYaw={worst.YawRateStepDegreesPerSecond:0.000}deg/s yawAcc={worst.YawAccelerationDegreesPerSecondSquared:0.0} beta={worst.BetaDegrees:0.00} " +
            $"rollW={Average(worst.Wheels, wheel => wheel.RollingBlend):0.000} dRollW={worst.BlendStep:0.000} scale={Average(worst.Wheels, wheel => wheel.LowSpeedForceScale):0.000} dScale={worst.ScaleStep:0.000} " +
            $"dTarget={worst.TargetStepN:0}N dRelax={worst.RelaxedStepN:0}N dFinal={worst.FinalStepN:0}N dRolling={worst.RollingStepN:0}N dContact={worst.RollingContactStepN:0}N dContactYaw={worst.RollingContactYawMomentStepNm:0}Nm");

        PrintWindow(samples, worstIndex);
        PrintWheelDetail(worst);
        if (halfBlendIndex >= 0)
        {
            Console.WriteLine("    half-blend boundary:");
            PrintWindow(samples, halfBlendIndex);
            PrintWheelDetail(samples[halfBlendIndex]);
        }

        if (firstDynamicIndex >= 0)
        {
            Console.WriteLine("    dynamic-full boundary:");
            PrintWindow(samples, firstDynamicIndex);
            PrintWheelDetail(samples[firstDynamicIndex]);
        }

        Console.WriteLine();
    }

    private static void PrintWindow(IReadOnlyList<Sample> samples, int centerIndex)
    {
        int start = Math.Max(0, centerIndex - 6);
        int end = Math.Min(samples.Count - 1, centerIndex + 6);
        Console.WriteLine("    t      spd  steer road  yaw   dYaw  yawAcc beta rollW dRW  scale dSc  tgtF dTgt relF dRel finF dFin rollF dRoll contact dCon cYaw dCYaw");
        for (int i = start; i <= end; i++)
        {
            Sample s = samples[i];
            Console.WriteLine(
                $"    {s.TimeSeconds,5:0.000} {s.SpeedKmh,5:0.00} {s.SteerInput,5:0.00} {s.RoadWheelAngleDegrees,4:0.0} " +
                $"{s.YawRateDegreesPerSecond,5:F1} {s.YawRateStepDegreesPerSecond,5:F2} {s.YawAccelerationDegreesPerSecondSquared,6:F0} {s.BetaDegrees,5:F1} " +
                $"{Average(s.Wheels, w => w.RollingBlend),5:0.00} {s.BlendStep,4:0.00} {Average(s.Wheels, w => w.LowSpeedForceScale),5:0.00} {s.ScaleStep,4:0.00} " +
                $"{SumAbs(s.Wheels, w => w.TargetLateralForceN),5:0} {s.TargetStepN,5:0} " +
                $"{SumAbs(s.Wheels, w => w.RelaxedLateralForceN),5:0} {s.RelaxedStepN,5:0} " +
                $"{SumAbs(s.Wheels, w => w.FinalLateralForceN),5:0} {s.FinalStepN,5:0} " +
                $"{SumAbs(s.Wheels, w => w.RollingConstraintForceN),5:0} {s.RollingStepN,5:0} " +
                $"{SumAbs(s.Wheels, w => w.RollingContactForceN),7:0} {s.RollingContactStepN,5:0} " +
                $"{SumAbs(s.Wheels, w => w.RollingContactYawMomentNm),5:0} {s.RollingContactYawMomentStepNm,5:0}");
        }
    }

    private static void PrintWheelDetail(Sample sample)
    {
        Console.WriteLine("    wheel  vLong effSlip  tau  vLat slip relSlip rollW scale  slipFy targetFy relaxedFy finalFy rollingFy contactFy contactYaw yawNm");
        foreach (WheelSample wheel in sample.Wheels)
        {
            Console.WriteLine(
                $"    {wheel.Label,-2} {wheel.LocalForwardSpeedMetersPerSecond,6:0.00} {wheel.EffectiveSlipSpeedMetersPerSecond,7:0.00} {wheel.LateralRelaxationTimeSeconds,4:0.000} {wheel.LocalLateralSpeedMetersPerSecond,6:0.00} {wheel.SlipAngleDegrees,5:F1} {wheel.RelaxedSlipAngleDegrees,7:F1} " +
                $"{wheel.RollingBlend,5:0.00} {wheel.LowSpeedForceScale,5:0.00} " +
                $"{wheel.SlipLateralForceN,7:0} {wheel.TargetLateralForceN,8:0} {wheel.RelaxedLateralForceN,9:0} {wheel.FinalLateralForceN,7:0} " +
                $"{wheel.RollingConstraintForceN,9:0} {wheel.RollingContactForceN,9:0} {wheel.RollingContactYawMomentNm,10:0} {wheel.YawMomentNm,6:0}");
        }
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private static float SumAbs(IReadOnlyList<WheelSample> wheels, Func<WheelSample, float> selector)
    {
        float sum = 0f;
        for (int i = 0; i < wheels.Count; i++)
        {
            sum += MathF.Abs(selector(wheels[i]));
        }

        return sum;
    }

    private static float Average(IReadOnlyList<WheelSample> wheels, Func<WheelSample, float> selector)
    {
        float sum = 0f;
        for (int i = 0; i < wheels.Count; i++)
        {
            sum += selector(wheels[i]);
        }

        return sum / Math.Max(1, wheels.Count);
    }

    private static float MaxWheelStep(Sample current, Sample previous, Func<WheelSample, float> selector)
    {
        float max = 0f;
        for (int i = 0; i < current.Wheels.Count; i++)
        {
            max = MathF.Max(max, MathF.Abs(selector(current.Wheels[i]) - selector(previous.Wheels[i])));
        }

        return max;
    }

    private static float Moment(float localRightMeters, float localForwardMeters, float localForwardForceN, float localRightForceN)
    {
        return localRightMeters * localForwardForceN - localForwardMeters * localRightForceN;
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private readonly record struct HandoffVariant(
        string Label,
        float? RollingEndMetersPerSecond,
        float? DynamicEndMetersPerSecond,
        float? RelaxationSpeedFloorMetersPerSecond,
        bool SmoothRelaxationSpeedFloor);

    private readonly record struct Sample(
        float TimeSeconds,
        float SpeedKmh,
        float SteerInput,
        float RoadWheelAngleDegrees,
        float YawRateDegreesPerSecond,
        float YawRateStepDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float YawAccelerationStepDegreesPerSecondSquared,
        float BetaDegrees,
        float BetaStepDegrees,
        float BlendStep,
        float ScaleStep,
        float TargetStepN,
        float SlipTargetStepN,
        float RelaxedStepN,
        float FinalStepN,
        float RollingStepN,
        float RollingContactStepN,
        float RollingContactYawMomentStepNm,
        IReadOnlyList<WheelSample> Wheels)
    {
        public static Sample From(float timeSeconds, float steerInput, VehicleState state, VehicleSimulationParameters parameters)
        {
            _ = parameters;
            float roadAngle = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
            VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
            WheelSample[] wheels =
            [
                new(
                    "FL",
                    -geometry.FrontTrackMeters * 0.5f,
                    geometry.CgToFrontAxleMeters,
                    state.FrontLeftLocalForwardSpeedMetersPerSecond,
                    state.FrontLeftLocalLateralSpeedMetersPerSecond,
                    state.FrontLeftLateralRelaxationTimeSeconds,
                    state.FrontLeftSlipAngleDegrees,
                    MathHelper.ToDegrees(state.FrontLeftRelaxedLateralSlip),
                    state.FrontLeftLowSpeedRollingBlend,
                    state.FrontLeftLowSpeedLateralForceScale,
                    state.FrontLeftLowSpeedSlipLateralForceN,
                    state.FrontLeftLowSpeedFinalLateralForceN,
                    state.FrontLeftRelaxedLateralForceN,
                    state.FrontLeftLateralForceN,
                    state.FrontLeftLowSpeedRollingConstraintForceN,
                    state.FrontLeftLowSpeedRollingContactForceN,
                    state.FrontLeftLowSpeedRollingContactYawMomentNm,
                    Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN)),
                new(
                    "FR",
                    geometry.FrontTrackMeters * 0.5f,
                    geometry.CgToFrontAxleMeters,
                    state.FrontRightLocalForwardSpeedMetersPerSecond,
                    state.FrontRightLocalLateralSpeedMetersPerSecond,
                    state.FrontRightLateralRelaxationTimeSeconds,
                    state.FrontRightSlipAngleDegrees,
                    MathHelper.ToDegrees(state.FrontRightRelaxedLateralSlip),
                    state.FrontRightLowSpeedRollingBlend,
                    state.FrontRightLowSpeedLateralForceScale,
                    state.FrontRightLowSpeedSlipLateralForceN,
                    state.FrontRightLowSpeedFinalLateralForceN,
                    state.FrontRightRelaxedLateralForceN,
                    state.FrontRightLateralForceN,
                    state.FrontRightLowSpeedRollingConstraintForceN,
                    state.FrontRightLowSpeedRollingContactForceN,
                    state.FrontRightLowSpeedRollingContactYawMomentNm,
                    Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN)),
                new(
                    "RL",
                    -geometry.RearTrackMeters * 0.5f,
                    -geometry.CgToRearAxleMeters,
                    state.RearLeftLocalForwardSpeedMetersPerSecond,
                    state.RearLeftLocalLateralSpeedMetersPerSecond,
                    state.RearLeftLateralRelaxationTimeSeconds,
                    state.RearLeftSlipAngleDegrees,
                    MathHelper.ToDegrees(state.RearLeftRelaxedLateralSlip),
                    state.RearLeftLowSpeedRollingBlend,
                    state.RearLeftLowSpeedLateralForceScale,
                    state.RearLeftLowSpeedSlipLateralForceN,
                    state.RearLeftLowSpeedFinalLateralForceN,
                    state.RearLeftRelaxedLateralForceN,
                    state.RearLeftLateralForceN,
                    state.RearLeftLowSpeedRollingConstraintForceN,
                    state.RearLeftLowSpeedRollingContactForceN,
                    state.RearLeftLowSpeedRollingContactYawMomentNm,
                    Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN)),
                new(
                    "RR",
                    geometry.RearTrackMeters * 0.5f,
                    -geometry.CgToRearAxleMeters,
                    state.RearRightLocalForwardSpeedMetersPerSecond,
                    state.RearRightLocalLateralSpeedMetersPerSecond,
                    state.RearRightLateralRelaxationTimeSeconds,
                    state.RearRightSlipAngleDegrees,
                    MathHelper.ToDegrees(state.RearRightRelaxedLateralSlip),
                    state.RearRightLowSpeedRollingBlend,
                    state.RearRightLowSpeedLateralForceScale,
                    state.RearRightLowSpeedSlipLateralForceN,
                    state.RearRightLowSpeedFinalLateralForceN,
                    state.RearRightRelaxedLateralForceN,
                    state.RearRightLateralForceN,
                    state.RearRightLowSpeedRollingConstraintForceN,
                    state.RearRightLowSpeedRollingContactForceN,
                    state.RearRightLowSpeedRollingContactYawMomentNm,
                    Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN))
            ];

            return new Sample(
                timeSeconds,
                state.SpeedMetersPerSecond * 3.6f,
                steerInput,
                roadAngle,
                MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
                0f,
                state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
                0f,
                state.ClassicBodySlipAngleDegrees,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                wheels);
        }
    }

    private readonly record struct WheelSample(
        string Label,
        float LocalRightMeters,
        float LocalForwardMeters,
        float LocalForwardSpeedMetersPerSecond,
        float LocalLateralSpeedMetersPerSecond,
        float LateralRelaxationTimeSeconds,
        float SlipAngleDegrees,
        float RelaxedSlipAngleDegrees,
        float RollingBlend,
        float LowSpeedForceScale,
        float SlipLateralForceN,
        float TargetLateralForceN,
        float RelaxedLateralForceN,
        float FinalLateralForceN,
        float RollingConstraintForceN,
        float RollingContactForceN,
        float RollingContactYawMomentNm,
        float YawMomentNm)
    {
        public float EffectiveSlipSpeedMetersPerSecond =>
            MathF.Sqrt(LocalForwardSpeedMetersPerSecond * LocalForwardSpeedMetersPerSecond + 3.0f * 3.0f);
    }
}
