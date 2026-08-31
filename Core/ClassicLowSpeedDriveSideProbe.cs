using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicLowSpeedDriveSideProbe
{
    private const float Dt = 1f / 120f;
    private const float Throttle = 0.68f;

    private static readonly Variant[] Variants =
    [
        new("current", 1f, float.NaN),
        new("drive-side-35-to-15kmh", 0.35f, 15f / 3.6f),
        new("drive-side-00-to-15kmh", 0f, 15f / 3.6f),
        new("drive-side-00-to-20kmh", 0f, 20f / 3.6f)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);

        Console.WriteLine($"Classic low-speed drive-side probe: {parameters.DisplayName}");
        Console.WriteLine("  diagnostic only: isolates steered front longitudinal force rotating into lateral/yaw force.");
        Console.WriteLine("  driveSide is body-side force from front longitudinal force through steered wheels.");
        Console.WriteLine();

        foreach (Variant variant in Variants)
        {
            RunCase(parameters, options, variant, reverse: false, alternating: false);
            RunCase(parameters, options, variant, reverse: true, alternating: false);
            RunCase(parameters, options, variant, reverse: false, alternating: true);
        }

        Console.WriteLine("Classic low-speed drive-side probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        GameLaunchOptions options,
        Variant variant,
        bool reverse,
        bool alternating)
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
        simulator.LowSpeedForceDiagnosticOptionsForProbe = new ClassicLowSpeedForceDiagnosticOptions
        {
            FrontDriveSideMultiplier = variant.FrontDriveSideMultiplier
        };
        simulator.FrontDriveSideSuppressionEndSpeedMetersPerSecondForProbe = variant.SuppressEndSpeedMetersPerSecond;

        List<Sample> samples = [];
        Sample previous = Sample.From(0f, 0f, simulator.State);
        int ticks = SecondsToTicks(4.0f);
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            float steer = alternating
                ? (time < 1.20f ? 1f : time < 1.85f ? -1f : time < 2.50f ? 1f : -1f)
                : 1f;
            VehicleInput input = reverse
                ? new VehicleInput(0f, 0f, steer, reverse: Throttle)
                : new VehicleInput(Throttle, 0f, steer);

            simulator.Update(input, Dt);
            Sample sample = Sample.From(time, steer, simulator.State);
            sample = sample with
            {
                YawAccelStep = MathF.Abs(sample.YawAccelerationDegreesPerSecondSquared - previous.YawAccelerationDegreesPerSecondSquared),
                FrontDriveSideStepN = MathF.Abs(sample.FrontDriveSideForceN - previous.FrontDriveSideForceN),
                FrontLocalRightStepN = MathF.Abs(sample.FrontLocalRightForceN - previous.FrontLocalRightForceN),
                FrontTargetStepN = MathF.Abs(sample.FrontTargetForceN - previous.FrontTargetForceN)
            };
            samples.Add(sample);
            previous = sample;
        }

        IEnumerable<Sample> window = samples.Where(s => s.SpeedKmh >= 5f && s.SpeedKmh <= 15f);
        Sample worst = window.MaxBy(s =>
            s.YawAccelStep +
            s.FrontLocalRightStepN / 20f +
            s.FrontDriveSideStepN / 20f +
            s.FrontTargetStepN / 40f);
        Sample firstTurn = samples.FirstOrDefault(s => MathF.Abs(s.YawRateDegreesPerSecond) >= 2f);
        Sample near12 = samples.MinBy(s => MathF.Abs(s.SpeedKmh - 12f));

        string direction = reverse ? "reverse" : "forward";
        string mode = alternating ? "alternating" : "full-lock";
        Console.WriteLine(
            $"  {variant.Label,-24} {direction,-7} {mode,-11} " +
            $"firstYaw2={FormatEvent(firstTurn)} near12={near12.TimeSeconds:0.000}s yaw={near12.YawRateDegreesPerSecond,6:0.0}deg/s " +
            $"driveSide={near12.FrontDriveSideForceN,7:0}N localRight={near12.FrontLocalRightForceN,7:0}N " +
            $"worst={worst.TimeSeconds:0.000}s/{worst.SpeedKmh:0.00}kmh dYaw={worst.YawAccelStep,6:0} dDrive={worst.FrontDriveSideStepN,6:0}N dLocal={worst.FrontLocalRightStepN,6:0}N dTarget={worst.FrontTargetStepN,6:0}N");

        PrintSamples(samples, worst.TimeSeconds);
    }

    private static void PrintSamples(List<Sample> samples, float centerTime)
    {
        Console.WriteLine("    t     kmh steer road yawAcc yawRate tgtFy relaxFy finalFy localRight driveSide reqLong rollW scale");
        foreach (Sample sample in samples.Where(s => MathF.Abs(s.TimeSeconds - centerTime) <= 0.05f))
        {
            Console.WriteLine(
                $"    {sample.TimeSeconds,5:0.000} {sample.SpeedKmh,5:0.00} {sample.SteerInput,5:0.00} {sample.RoadWheelDegrees,5:0.1} " +
                $"{sample.YawAccelerationDegreesPerSecondSquared,6:0} {sample.YawRateDegreesPerSecond,7:0.0} " +
                $"{sample.FrontTargetForceN,6:0} {sample.FrontRelaxedForceN,7:0} {sample.FrontFinalForceN,7:0} " +
                $"{sample.FrontLocalRightForceN,10:0} {sample.FrontDriveSideForceN,9:0} {sample.FrontRequestedLongitudinalForceN,7:0} " +
                $"{sample.AverageRollingBlend,5:0.00} {sample.AverageLowSpeedScale,5:0.00}");
        }
    }

    private static string FormatEvent(Sample sample)
    {
        return sample.TimeSeconds > 0f
            ? $"{sample.TimeSeconds:0.000}s/{sample.SpeedKmh:0.00}kmh"
            : "never";
    }

    private static int SecondsToTicks(float seconds)
    {
        return (int)MathF.Round(seconds / Dt);
    }

    private readonly record struct Variant(
        string Label,
        float FrontDriveSideMultiplier,
        float SuppressEndSpeedMetersPerSecond);

    private readonly record struct Sample(
        float TimeSeconds,
        float SpeedKmh,
        float SteerInput,
        float RoadWheelDegrees,
        float YawRateDegreesPerSecond,
        float YawAccelerationDegreesPerSecondSquared,
        float YawAccelStep,
        float FrontTargetForceN,
        float FrontTargetStepN,
        float FrontRelaxedForceN,
        float FrontFinalForceN,
        float FrontLocalRightForceN,
        float FrontLocalRightStepN,
        float FrontDriveSideForceN,
        float FrontDriveSideStepN,
        float FrontRequestedLongitudinalForceN,
        float AverageRollingBlend,
        float AverageLowSpeedScale)
    {
        public static Sample From(float timeSeconds, float steerInput, VehicleState state)
        {
            float roadWheel = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
            float frontTarget = state.FrontLeftRequestedLateralForceN + state.FrontRightRequestedLateralForceN;
            float frontRelaxed = state.FrontLeftRelaxedLateralForceN + state.FrontRightRelaxedLateralForceN;
            float frontFinal = state.FrontLeftLowSpeedFinalLateralForceN + state.FrontRightLowSpeedFinalLateralForceN;
            float frontLocalRight = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
            float steerRadians = MathHelper.ToRadians(roadWheel);
            float frontDriveSide = frontLocalRight - frontFinal * MathF.Cos(steerRadians);
            float frontLongitudinal = state.FrontLeftRequestedLongitudinalForceN + state.FrontRightRequestedLongitudinalForceN;
            float averageRollingBlend = (
                state.FrontLeftLowSpeedRollingBlend +
                state.FrontRightLowSpeedRollingBlend) * 0.5f;
            float averageScale = (
                state.FrontLeftLowSpeedLateralForceScale +
                state.FrontRightLowSpeedLateralForceScale) * 0.5f;

            return new Sample(
                timeSeconds,
                state.SpeedMetersPerSecond * 3.6f,
                steerInput,
                roadWheel,
                MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
                state.ClassicNaturalYawAccelerationDegreesPerSecondSquared,
                0f,
                frontTarget,
                0f,
                frontRelaxed,
                frontFinal,
                frontLocalRight,
                0f,
                frontDriveSide,
                0f,
                frontLongitudinal,
                averageRollingBlend,
                averageScale);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 worldPosition)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
