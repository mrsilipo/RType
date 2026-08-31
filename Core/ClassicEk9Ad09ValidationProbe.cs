using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicEk9Ad09ValidationProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float Gravity = 9.81f;

    private static readonly ValidationCase[] Cases =
    [
        new("0.5g steady", 100f, 2.5f, input => new VehicleInput(0.25f, 0f, 0.35f, brakeAssistEnabled: true),
            new TargetBand(0.45f, 0.70f, 1f, 4f, 0.5f, 3.5f, 0f, 2.0f, "clean front-led ordinary corner")),
        new("0.9g steady", 120f, 2.5f, input => new VehicleInput(0.25f, 0f, 0.75f, brakeAssistEnabled: true),
            new TargetBand(0.85f, 1.15f, 4f, 8f, 2.5f, 6f, 1f, 4.5f, "fast committed FF corner")),
        new("power exit", 100f, 2.5f, input => new VehicleInput(input < 0.75f ? 0.20f : 1.0f, 0f, 0.60f, brakeAssistEnabled: true),
            new TargetBand(0.65f, 1.15f, 4f, 9f, 2f, 6f, 1f, 5f, "front slip rises, yaw gain softens, exits cleanly")),
        new("lift off", 120f, 2.5f, input => new VehicleInput(input < 0.80f ? 0.35f : 0f, 0f, 0.65f, brakeAssistEnabled: true),
            new TargetBand(0.70f, 1.15f, 4f, 9f, 3f, 8f, 2f, 6.5f, "line tightens and rear slip/beta rise controllably")),
        new("trail brake", 120f, 2.5f, input => new VehicleInput(0f, BrakeRelease(input), 0.65f, brakeAssistEnabled: true),
            new TargetBand(0.55f, 1.10f, 3f, 9f, 3f, 8f, 2f, 6.5f, "front load/rear unload creates controllable rotation")),
        new("left-right", 100f, 2.5f, input => new VehicleInput(0.25f, 0f, input < 0.85f ? 0.75f : -0.75f, brakeAssistEnabled: true),
            new TargetBand(0.45f, 1.15f, 2f, 9f, 2f, 8f, 1f, 7f, "state unwinds without instant snap or dead response"))
    ];

    private static readonly Profile[] Profiles =
    [
        new("legacy", ClassicFourWheelAssistOptions.Default, true),
        new("off", new ClassicFourWheelAssistOptions { YawRecoveryEnabled = false }, false),
        new("cond", ClassicFourWheelAssistOptions.Default, false)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic EK9/AD09 validation probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  target source: Docs/ek9_ad09_handling_reference_targets.md");
        Console.WriteLine("  targets are engineering envelopes, not claimed instrumented EK9+AD09 measurements.");
        Console.WriteLine("  profile case speed latG avg/max slipF/R beta/maxBeta yaw yawRecMax act/bd/yaw/rear rearLoadMin gripF/R speedDrop verdict");

        foreach (Profile profile in Profiles)
        {
            foreach (ValidationCase validationCase in Cases)
            {
                Result result = RunCase(parameters, engine, profile.AssistOptions, profile.UseLegacyYawRecovery, validationCase);
                Console.WriteLine(
                    $"  {profile.Label,-9} {validationCase.Label,-11} {validationCase.StartSpeedKmh,5:F0} " +
                    $"{result.AverageLateralG,4:F2}/{result.PeakLateralG,4:F2} " +
                    $"{result.FinalFrontSlipDegrees,5:F2}/{result.FinalRearSlipDegrees,5:F2} " +
                    $"{result.FinalBetaDegrees,5:F2}/{result.PeakAbsBetaDegrees,5:F2} " +
                    $"{result.FinalYawRateDegreesPerSecond,6:F1} {result.PeakYawRecoveryDegreesPerSecondSquared,8:F0} " +
                    $"{result.PeakYawRecoveryActivation,4:F2}/{result.PeakYawRecoveryBetaDotGate,4:F2}/{result.PeakYawRecoveryYawExcessGate,4:F2}/{result.PeakYawRecoveryRearSlipGate,4:F2} " +
                    $"{result.MinimumRearWheelLoadN,7:F0} {result.PeakFrontGripUsage,4:F2}/{result.PeakRearGripUsage,4:F2} " +
                    $"{result.SpeedDropKmh,7:F2} {Classify(result, validationCase.Target)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  interpretation:");
        Console.WriteLine("    legacy/off/conditional separates old yaw-servo suppression from base tyre/chassis capability and the new gated recovery.");
        Console.WriteLine("    rearLoadMin shows whether lift/trail braking is creating a real rear-unload event.");
        Console.WriteLine("    lift/trail cases should show rear slip and beta rising without the car becoming a rear-steered mess.");
        Console.WriteLine("Classic EK9/AD09 validation probe complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        ClassicFourWheelAssistOptions assistOptions,
        bool useLegacyYawRecovery,
        ValidationCase validationCase)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            AssistOptions = assistOptions,
            UseLegacyYawRecoveryForProbe = useLegacyYawRecovery
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, validationCase.StartSpeedKmh / 3.6f);

        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        int ticks = Math.Max(1, (int)MathF.Round(validationCase.DurationSeconds / Dt));
        int sampleStart = Math.Max(1, ticks / 3);
        float lateralGSum = 0f;
        int lateralGSamples = 0;
        float peakLateralG = 0f;
        float peakAbsBeta = 0f;
        float peakYawRecovery = 0f;
        float peakYawRecoveryActivation = 0f;
        float peakYawRecoveryBetaDotGate = 0f;
        float peakYawRecoveryYawExcessGate = 0f;
        float peakYawRecoveryRearSlipGate = 0f;
        float minimumRearWheelLoad = float.PositiveInfinity;
        float peakFrontGrip = 0f;
        float peakRearGrip = 0f;

        for (int i = 1; i <= ticks; i++)
        {
            float time = i * Dt;
            simulator.Update(validationCase.Input(time), Dt);
            VehicleState state = simulator.State;

            float lateralG = MathF.Abs(state.LateralAcceleration) / Gravity;
            peakLateralG = MathF.Max(peakLateralG, lateralG);
            peakAbsBeta = MathF.Max(peakAbsBeta, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            peakYawRecovery = MathF.Max(peakYawRecovery, MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared));
            peakYawRecoveryActivation = MathF.Max(peakYawRecoveryActivation, state.ClassicYawRecoveryActivation);
            peakYawRecoveryBetaDotGate = MathF.Max(peakYawRecoveryBetaDotGate, state.ClassicYawRecoveryBetaDotGate);
            peakYawRecoveryYawExcessGate = MathF.Max(peakYawRecoveryYawExcessGate, state.ClassicYawRecoveryYawExcessGate);
            peakYawRecoveryRearSlipGate = MathF.Max(peakYawRecoveryRearSlipGate, state.ClassicYawRecoveryRearSlipGate);
            minimumRearWheelLoad = MathF.Min(minimumRearWheelLoad, MathF.Min(state.RearLeftLoadN, state.RearRightLoadN));
            peakFrontGrip = MathF.Max(peakFrontGrip, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage));
            peakRearGrip = MathF.Max(peakRearGrip, MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));

            if (i >= sampleStart)
            {
                lateralGSum += lateralG;
                lateralGSamples++;
            }
        }

        VehicleState final = simulator.State;
        return new Result(
            lateralGSamples > 0 ? lateralGSum / lateralGSamples : 0f,
            peakLateralG,
            Average(final.FrontLeftSlipAngleDegrees, final.FrontRightSlipAngleDegrees),
            Average(final.RearLeftSlipAngleDegrees, final.RearRightSlipAngleDegrees),
            final.ClassicBodySlipAngleDegrees,
            peakAbsBeta,
            MathHelper.ToDegrees(final.YawRateRadiansPerSecond),
            peakYawRecovery,
            peakYawRecoveryActivation,
            peakYawRecoveryBetaDotGate,
            peakYawRecoveryYawExcessGate,
            peakYawRecoveryRearSlipGate,
            minimumRearWheelLoad,
            peakFrontGrip,
            peakRearGrip,
            startSpeedKmh - final.SpeedMetersPerSecond * 3.6f);
    }

    private static float BrakeRelease(float time)
    {
        if (time < 0.35f)
        {
            return 0.85f;
        }

        return MathHelper.Lerp(0.85f, 0.10f, SmoothStep01((time - 0.35f) / 0.90f));
    }

    private static string Classify(Result result, TargetBand target)
    {
        bool lateralOk = result.AverageLateralG >= target.MinAverageLateralG &&
            result.AverageLateralG <= target.MaxAverageLateralG;
        bool frontSlipOk = MathF.Abs(result.FinalFrontSlipDegrees) >= target.MinFrontSlipDegrees &&
            MathF.Abs(result.FinalFrontSlipDegrees) <= target.MaxFrontSlipDegrees;
        bool rearSlipOk = MathF.Abs(result.FinalRearSlipDegrees) >= target.MinRearSlipDegrees &&
            MathF.Abs(result.FinalRearSlipDegrees) <= target.MaxRearSlipDegrees;
        bool betaOk = MathF.Abs(result.FinalBetaDegrees) <= target.MaxBetaDegrees &&
            result.PeakAbsBetaDegrees <= target.MaxBetaDegrees + 2f;

        if (lateralOk && frontSlipOk && rearSlipOk && betaOk)
        {
            return "inside-target";
        }

        if (result.AverageLateralG < target.MinAverageLateralG)
        {
            return "too-little-cornering";
        }

        if (MathF.Abs(result.FinalFrontSlipDegrees) > target.MaxFrontSlipDegrees ||
            result.PeakFrontGripUsage > 0.98f)
        {
            return "front-overdriven";
        }

        if (MathF.Abs(result.FinalBetaDegrees) > target.MaxBetaDegrees ||
            result.PeakAbsBetaDegrees > target.MaxBetaDegrees + 2f)
        {
            return "too-much-beta";
        }

        if (MathF.Abs(result.FinalRearSlipDegrees) < target.MinRearSlipDegrees)
        {
            return "rear-too-locked";
        }

        return "mixed";
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Average(float a, float b) => (a + b) * 0.5f;

    private readonly record struct Profile(string Label, ClassicFourWheelAssistOptions AssistOptions, bool UseLegacyYawRecovery);

    private readonly record struct ValidationCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        Func<float, VehicleInput> Input,
        TargetBand Target);

    private readonly record struct TargetBand(
        float MinAverageLateralG,
        float MaxAverageLateralG,
        float MinFrontSlipDegrees,
        float MaxFrontSlipDegrees,
        float MinRearSlipDegrees,
        float MaxRearSlipDegrees,
        float MinBetaDegrees,
        float MaxBetaDegrees,
        string Description);

    private readonly record struct Result(
        float AverageLateralG,
        float PeakLateralG,
        float FinalFrontSlipDegrees,
        float FinalRearSlipDegrees,
        float FinalBetaDegrees,
        float PeakAbsBetaDegrees,
        float FinalYawRateDegreesPerSecond,
        float PeakYawRecoveryDegreesPerSecondSquared,
        float PeakYawRecoveryActivation,
        float PeakYawRecoveryBetaDotGate,
        float PeakYawRecoveryYawExcessGate,
        float PeakYawRecoveryRearSlipGate,
        float MinimumRearWheelLoadN,
        float PeakFrontGripUsage,
        float PeakRearGripUsage,
        float SpeedDropKmh);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
