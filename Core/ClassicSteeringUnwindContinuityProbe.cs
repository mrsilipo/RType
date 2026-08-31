using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSteeringUnwindContinuityProbe
{
    private const float Dt = 1f / 120f;
    private const float Throttle = 0.68f;
    private const float ReleaseSpeedKmh = 14.0f;
    private const float RunAfterReleaseSeconds = 0.70f;

    private static readonly UnwindVariant[] Variants =
    [
        new("current", float.NaN, ClassicLowSpeedForceDiagnosticOptions.Default),
        new("slow-return-050", 0.50f, ClassicLowSpeedForceDiagnosticOptions.Default),
        new("slow-return-035", 0.35f, ClassicLowSpeedForceDiagnosticOptions.Default),
        new("no-post-impulse", float.NaN, new ClassicLowSpeedForceDiagnosticOptions
        {
            DisablePostForceRollingContactConstraint = true
        }),
        new("no-relax-below-transition", float.NaN, new ClassicLowSpeedForceDiagnosticOptions
        {
            BypassLateralRelaxationBelowTransition = true
        })
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);

        Console.WriteLine($"Classic steering unwind continuity probe: {parameters.DisplayName}");
        Console.WriteLine("  reproduces live jolt shape: full steering crawl, release near 14km/h, inspect +/-0.25s around release.");
        Console.WriteLine("  variants are diagnostic only; production steering authority and normal handling values are unchanged.");
        Console.WriteLine("  dRoad is road-wheel angular rate, dTarget/dRelax/dFinal are front-axle lateral-force steps.");
        Console.WriteLine();

        foreach (UnwindVariant variant in Variants)
        {
            RunCase(parameters, options, variant, reverse: false);
            RunCase(parameters, options, variant, reverse: true);
        }

        Console.WriteLine("Classic steering unwind continuity probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        GameLaunchOptions options,
        UnwindVariant variant,
        bool reverse)
    {
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = reverse ? -1 : 1;
        simulator.State.Rpm = parameters.IdleRpm;
        simulator.LowSpeedForceDiagnosticOptionsForProbe = variant.Options;
        simulator.LowSpeedSteeringReturnRateMultiplierForProbe = variant.ReturnRateMultiplier;

        List<Sample> samples = [];
        bool released = false;
        float releaseTime = 0f;
        int releaseIndex = -1;
        Sample previous = Sample.From(0f, 0f, simulator.State);

        int maxTicks = SecondsToTicks(8f);
        for (int tick = 1; tick <= maxTicks; tick++)
        {
            float time = tick * Dt;
            float steer = released ? 0f : 1f;
            VehicleInput input = reverse
                ? new VehicleInput(0f, 0f, steer, reverse: Throttle)
                : new VehicleInput(Throttle, 0f, steer);
            simulator.Update(input, Dt);

            Sample sample = Sample.From(time, steer, simulator.State);
            sample = sample with
            {
                RoadWheelRateDegreesPerSecond = (sample.RoadWheelDegrees - previous.RoadWheelDegrees) / Dt,
                FrontTargetStepN = MathF.Abs(sample.FrontTargetForceN - previous.FrontTargetForceN),
                FrontRelaxedStepN = MathF.Abs(sample.FrontRelaxedForceN - previous.FrontRelaxedForceN),
                FrontFinalStepN = MathF.Abs(sample.FrontFinalForceN - previous.FrontFinalForceN),
                FrontYawAccelerationStepDegreesPerSecondSquared = MathF.Abs(sample.FrontYawAccelerationDegreesPerSecondSquared - previous.FrontYawAccelerationDegreesPerSecondSquared),
                YawAccelerationStepDegreesPerSecondSquared = MathF.Abs(sample.YawAccelerationDegreesPerSecondSquared - previous.YawAccelerationDegreesPerSecondSquared)
            };
            samples.Add(sample);

            if (!released && sample.SpeedKmh >= ReleaseSpeedKmh)
            {
                released = true;
                releaseTime = time;
                releaseIndex = samples.Count - 1;
            }

            if (released && time >= releaseTime + RunAfterReleaseSeconds)
            {
                break;
            }

            previous = sample;
        }

        if (releaseIndex < 0)
        {
            Console.WriteLine($"  {variant.Label,-24} {(reverse ? "reverse" : "forward"),-7} did not reach release speed.");
            return;
        }

        int start = Math.Max(0, releaseIndex - SecondsToTicks(0.25f));
        int end = Math.Min(samples.Count - 1, releaseIndex + SecondsToTicks(0.25f));
        Sample worst = samples
            .Skip(start)
            .Take(end - start + 1)
            .MaxBy(s =>
                s.FrontFinalStepN / 35f +
                s.FrontTargetStepN / 40f +
                s.FrontRelaxedStepN / 40f +
                s.YawAccelerationStepDegreesPerSecondSquared * 0.8f +
                MathF.Abs(s.RoadWheelRateDegreesPerSecond) * 0.04f);

        Console.WriteLine(
            $"  {variant.Label,-24} {(reverse ? "reverse" : "forward"),-7} release={releaseTime:0.000}s/{samples[releaseIndex].SpeedKmh:0.00}km/h " +
            $"worst={worst.TimeSeconds:0.000}s/{worst.SpeedKmh:0.00}km/h steer={worst.SteerInput:0.00} road={worst.RoadWheelDegrees,6:0.00}deg " +
            $"dRoad={worst.RoadWheelRateDegreesPerSecond,7:0}deg/s dTarget={worst.FrontTargetStepN,6:0}N dRelax={worst.FrontRelaxedStepN,6:0}N " +
            $"dFinal={worst.FrontFinalStepN,6:0}N dYaw={worst.YawAccelerationStepDegreesPerSecondSquared,6:0}deg/s2");

        PrintSamples(samples, releaseIndex);
    }

    private static void PrintSamples(List<Sample> samples, int releaseIndex)
    {
        int[] offsets = [-24, -12, 0, 1, 2, 4, 8, 12, 18, 24, 30];
        Console.WriteLine("    t      kmh steer road dRoad slipFL/FR targetF relaxF finalF yawAcc frontYaw rollYaw rollW/scale");
        foreach (int offset in offsets)
        {
            int index = releaseIndex + offset;
            if (index < 0 || index >= samples.Count)
            {
                continue;
            }

            Sample s = samples[index];
            Console.WriteLine(
                $"    {s.TimeSeconds,5:0.000} {s.SpeedKmh,5:0.00} {s.SteerInput,5:0.00} {s.RoadWheelDegrees,5:0.00} " +
                $"{s.RoadWheelRateDegreesPerSecond,6:0} {s.FrontLeftSlipDegrees,6:0.00}/{s.FrontRightSlipDegrees,6:0.00} " +
                $"{s.FrontTargetForceN,7:0} {s.FrontRelaxedForceN,7:0} {s.FrontFinalForceN,7:0} " +
                $"{s.YawAccelerationDegreesPerSecondSquared,7:0} {s.FrontYawAccelerationDegreesPerSecondSquared,7:0} " +
                $"{s.RollingContactYawMomentNm,7:0} {s.AverageRollingBlend,4:0.00}/{s.AverageLowSpeedScale,4:0.00}");
        }
    }

    private static int SecondsToTicks(float seconds)
    {
        return (int)MathF.Round(seconds / Dt);
    }

    private readonly record struct UnwindVariant(
        string Label,
        float ReturnRateMultiplier,
        ClassicLowSpeedForceDiagnosticOptions Options);

    private readonly record struct Sample(
        float TimeSeconds,
        float SpeedKmh,
        float SteerInput,
        float RoadWheelDegrees,
        float RoadWheelRateDegreesPerSecond,
        float FrontLeftSlipDegrees,
        float FrontRightSlipDegrees,
        float FrontTargetForceN,
        float FrontRelaxedForceN,
        float FrontFinalForceN,
        float FrontTargetStepN,
        float FrontRelaxedStepN,
        float FrontFinalStepN,
        float FrontYawAccelerationDegreesPerSecondSquared,
        float FrontYawAccelerationStepDegreesPerSecondSquared,
        float YawAccelerationDegreesPerSecondSquared,
        float YawAccelerationStepDegreesPerSecondSquared,
        float RollingContactYawMomentNm,
        float AverageRollingBlend,
        float AverageLowSpeedScale)
    {
        public static Sample From(float timeSeconds, float steerInput, VehicleState state)
        {
            float frontTarget =
                state.FrontLeftRequestedLateralForceN +
                state.FrontRightRequestedLateralForceN;
            float frontRelaxed =
                state.FrontLeftRelaxedLateralForceN +
                state.FrontRightRelaxedLateralForceN;
            float frontFinal =
                state.FrontLeftLowSpeedFinalLateralForceN +
                state.FrontRightLowSpeedFinalLateralForceN;
            float rollingYaw =
                state.FrontLeftLowSpeedRollingContactYawMomentNm +
                state.FrontRightLowSpeedRollingContactYawMomentNm +
                state.RearLeftLowSpeedRollingContactYawMomentNm +
                state.RearRightLowSpeedRollingContactYawMomentNm;
            float averageRollingBlend = (
                state.FrontLeftLowSpeedRollingBlend +
                state.FrontRightLowSpeedRollingBlend +
                state.RearLeftLowSpeedRollingBlend +
                state.RearRightLowSpeedRollingBlend) * 0.25f;
            float averageScale = (
                state.FrontLeftLowSpeedLateralForceScale +
                state.FrontRightLowSpeedLateralForceScale +
                state.RearLeftLowSpeedLateralForceScale +
                state.RearRightLowSpeedLateralForceScale) * 0.25f;

            return new Sample(
                timeSeconds,
                state.SpeedMetersPerSecond * 3.6f,
                steerInput,
                (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f,
                0f,
                state.FrontLeftSlipAngleDegrees,
                state.FrontRightSlipAngleDegrees,
                frontTarget,
                frontRelaxed,
                frontFinal,
                0f,
                0f,
                0f,
                state.ClassicFrontYawAccelerationDegreesPerSecondSquared,
                0f,
                state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
                0f,
                rollingYaw,
                averageRollingBlend,
                averageScale);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 worldPosition)
        {
            _ = worldPosition;
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
