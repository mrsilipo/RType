using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTyreCorneringStiffnessAuditProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float Gravity = 9.81f;

    private static readonly float[] RearLoadsN = [350f, 700f, 900f, 1500f, 2000f];
    private static readonly float[] ForceFractions = [0.25f, 0.50f, 0.75f, 0.90f];
    private static readonly StiffnessVariant[] StiffnessVariants =
    [
        new("current", float.NaN),
        new("mild-stiff-load", 0.25f),
        new("strong-stiff-load", 0.45f)
    ];

    private static readonly AuditCase[] Cases =
    [
        new("lift-mid", 120f, 2.2f, time => new VehicleInput(time < 0.80f ? 0.35f : 0f, 0f, 0.65f, brakeAssistEnabled: true), 0.65f),
        new("left-right", 100f, 2.2f, time => new VehicleInput(0.25f, 0f, time < 0.85f ? 0.75f : -0.75f, brakeAssistEnabled: true), 0.75f),
        new("trail-release", 120f, 2.2f, time => new VehicleInput(0f, TrailBrake(time), 0.65f, brakeAssistEnabled: true), 0.65f)
    ];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);
        float yawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engine.ClassicFourWheel.Yaw.InertiaScale));

        Console.WriteLine($"Classic tyre cornering-stiffness audit: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  frozen baseline: probe-only stiffness-load candidates; no production tuning changed.");
        PrintRearTyreLoadTable(tyres.Rear);
        PrintDynamicCases(parameters, engine, yawInertia);
        Console.WriteLine("Classic tyre cornering-stiffness audit complete.");
    }

    private static void PrintRearTyreLoadTable(ClassicBicycleTyreParameters rearTyre)
    {
        Console.WriteLine();
        Console.WriteLine("  rear tyre load table");
        Console.WriteLine("    variant loadN cornerStiffN/deg stiffPerLoad peakFy effMu reqSlip25/50/75/90 peakSlip");

        foreach (StiffnessVariant variant in StiffnessVariants)
        {
            foreach (float load in RearLoadsN)
            {
                ClassicBicycleTyreParameters effectiveTyre = ApplyVariant(rearTyre, load, variant);
                float peakForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(load, 1f, effectiveTyre);
                float stiffnessNPerDegree = CalculateSlopeAtSlipDegrees(0.75f, peakForce, effectiveTyre);
                Console.Write(
                    $"    {variant.Label,-17} {load,5:F0} {stiffnessNPerDegree,16:F0} {stiffnessNPerDegree / MathF.Max(1f, load),12:F3} " +
                    $"{peakForce,6:F0} {peakForce / MathF.Max(1f, load),5:F2}");

                foreach (float fraction in ForceFractions)
                {
                    Console.Write($" {FindSlipForForceFraction(fraction, peakForce, effectiveTyre),5:F2}");
                }

                Console.WriteLine($" {effectiveTyre.PeakSlipAngleDegrees,7:F2}");
            }
        }
    }

    private static void PrintDynamicCases(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float yawInertia)
    {
        Console.WriteLine();
        Console.WriteLine("  dynamic manoeuvres");
        Console.WriteLine("    variant case minRearN rearCapLoss rearSlipMax betaMax yawF/Rmax yawRec cleanup verdict");

        foreach (StiffnessVariant variant in StiffnessVariants)
        {
            foreach (AuditCase auditCase in Cases)
            {
                Summary summary = RunDynamicCase(parameters, engine, yawInertia, variant, auditCase);
                Console.WriteLine(
                    $"    {variant.Label,-17} {auditCase.Label,-13} {summary.MinInsideRearLoadN,8:F0} " +
                    $"{summary.MaxRearCapacityLoss * 100f,10:F1}% {summary.MaxRearSlipDegrees,10:F2} {summary.MaxAbsBetaDegrees,7:F2} " +
                    $"{summary.MaxAbsFrontYawMomentNm,6:F0}/{summary.MaxAbsRearYawMomentNm,6:F0} " +
                    $"{summary.MaxYawRecoveryDegreesPerSecondSquared,6:F0} {summary.MaxCleanupForceN,7:F0} {summary.Classify()}");
            }
        }
    }

    private static Summary RunDynamicCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float yawInertia,
        StiffnessVariant variant,
        AuditCase auditCase)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, auditCase.StartSpeedKmh / 3.6f);
        if (float.IsFinite(variant.StiffnessLoadExponent))
        {
            simulator.TyreCorneringStiffnessLoadExponentOverrideForProbe = variant.StiffnessLoadExponent;
        }

        float staticRearCapacity = 0f;
        float previousBeta = simulator.State.ClassicBodySlipAngleDegrees;
        Summary summary = new();
        int ticks = Math.Max(1, (int)MathF.Round(auditCase.DurationSeconds / Dt));
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            simulator.Update(auditCase.Input(time), Dt);
            VehicleState state = simulator.State;
            if (staticRearCapacity <= 0f && state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN > 1f)
            {
                staticRearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
            }

            summary.Add(BuildSample(state, previousBeta, staticRearCapacity, yawInertia, auditCase.ReferenceSteerSign, parameters, engine));
            previousBeta = state.ClassicBodySlipAngleDegrees;
        }

        return summary;
    }

    private static Sample BuildSample(
        VehicleState state,
        float previousBetaDegrees,
        float staticRearCapacityN,
        float yawInertia,
        float referenceSteerSign,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
    {
        float turnSign = MathF.Sign(referenceSteerSign);
        if (turnSign == 0f)
        {
            turnSign = 1f;
        }

        float rearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
        return new Sample(
            MathF.Min(state.RearLeftLoadN, state.RearRightLoadN),
            staticRearCapacityN > 1f ? MathHelper.Clamp((staticRearCapacityN - rearCapacity) / staticRearCapacityN, 0f, 1f) : 0f,
            MathF.Abs(Average(state.RearLeftSlipAngleDegrees, state.RearRightSlipAngleDegrees) * turnSign),
            MathF.Abs(state.ClassicBodySlipAngleDegrees * turnSign),
            MathF.Abs(MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * yawInertia),
            MathF.Abs(MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * yawInertia),
            MathF.Abs(state.ClassicYawRecoveryAccelerationDegreesPerSecondSquared),
            MathF.Abs(state.ClassicBodySlipDampingForceN + state.ClassicLateralVelocityDampingForceN),
            MathF.Abs((state.ClassicBodySlipAngleDegrees - previousBetaDegrees) / Dt * turnSign));
    }

    private static ClassicBicycleTyreParameters ApplyVariant(
        ClassicBicycleTyreParameters tyre,
        float loadN,
        StiffnessVariant variant)
    {
        return float.IsFinite(variant.StiffnessLoadExponent)
            ? ClassicFourWheelVehicleSimulator.CopyTyreWithCorneringStiffnessLoadSensitivity(tyre, loadN, variant.StiffnessLoadExponent)
            : tyre;
    }

    private static float CalculateSlopeAtSlipDegrees(float slipDegrees, float peakForce, ClassicBicycleTyreParameters tyre)
    {
        const float DeltaDegrees = 0.1f;
        float lowerForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(slipDegrees - DeltaDegrees), peakForce, tyre);
        float upperForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(slipDegrees + DeltaDegrees), peakForce, tyre);
        return (upperForce - lowerForce) / (DeltaDegrees * 2f);
    }

    private static float FindSlipForForceFraction(float fraction, float peakForce, ClassicBicycleTyreParameters tyre)
    {
        float target = peakForce * MathHelper.Clamp(fraction, 0f, 1f);
        float previousSlip = 0f;
        float previousForce = 0f;
        for (int step = 1; step <= 600; step++)
        {
            float slip = step * 0.05f;
            float force = MathF.Abs(ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(slip), peakForce, tyre));
            if (force >= target)
            {
                float span = MathF.Max(0.001f, force - previousForce);
                float t = MathHelper.Clamp((target - previousForce) / span, 0f, 1f);
                return MathHelper.Lerp(previousSlip, slip, t);
            }

            previousSlip = slip;
            previousForce = force;
        }

        return float.NaN;
    }

    private static float TrailBrake(float time)
    {
        if (time < 0.35f)
        {
            return 0.85f;
        }

        return MathHelper.Lerp(0.85f, 0.10f, SmoothStep01((time - 0.35f) / 0.90f));
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Average(float a, float b) => (a + b) * 0.5f;

    private readonly record struct StiffnessVariant(string Label, float StiffnessLoadExponent);

    private readonly record struct AuditCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        Func<float, VehicleInput> Input,
        float ReferenceSteerSign);

    private readonly record struct Sample(
        float InsideRearLoadN,
        float RearCapacityLoss,
        float RearSlipDegrees,
        float BetaDegrees,
        float FrontYawMomentNm,
        float RearYawMomentNm,
        float YawRecoveryDegreesPerSecondSquared,
        float CleanupForceN,
        float BetaDotDegreesPerSecond);

    private sealed class Summary
    {
        public float MinInsideRearLoadN { get; private set; } = float.PositiveInfinity;
        public float MaxRearCapacityLoss { get; private set; }
        public float MaxRearSlipDegrees { get; private set; }
        public float MaxAbsBetaDegrees { get; private set; }
        public float MaxAbsFrontYawMomentNm { get; private set; }
        public float MaxAbsRearYawMomentNm { get; private set; }
        public float MaxYawRecoveryDegreesPerSecondSquared { get; private set; }
        public float MaxCleanupForceN { get; private set; }
        public float MaxAbsBetaDotDegreesPerSecond { get; private set; }

        public void Add(Sample sample)
        {
            MinInsideRearLoadN = MathF.Min(MinInsideRearLoadN, sample.InsideRearLoadN);
            MaxRearCapacityLoss = MathF.Max(MaxRearCapacityLoss, sample.RearCapacityLoss);
            MaxRearSlipDegrees = MathF.Max(MaxRearSlipDegrees, sample.RearSlipDegrees);
            MaxAbsBetaDegrees = MathF.Max(MaxAbsBetaDegrees, sample.BetaDegrees);
            MaxAbsFrontYawMomentNm = MathF.Max(MaxAbsFrontYawMomentNm, sample.FrontYawMomentNm);
            MaxAbsRearYawMomentNm = MathF.Max(MaxAbsRearYawMomentNm, sample.RearYawMomentNm);
            MaxYawRecoveryDegreesPerSecondSquared = MathF.Max(MaxYawRecoveryDegreesPerSecondSquared, sample.YawRecoveryDegreesPerSecondSquared);
            MaxCleanupForceN = MathF.Max(MaxCleanupForceN, sample.CleanupForceN);
            MaxAbsBetaDotDegreesPerSecond = MathF.Max(MaxAbsBetaDotDegreesPerSecond, MathF.Abs(sample.BetaDotDegreesPerSecond));
        }

        public string Classify()
        {
            if (MaxRearSlipDegrees < 6f && MaxRearCapacityLoss < 0.10f)
            {
                return "rear-stays-pre-limit";
            }

            if (MaxRearSlipDegrees >= 6f && MaxAbsBetaDegrees < 4.5f)
            {
                return "slip-rises-little-attitude";
            }

            if (MaxYawRecoveryDegreesPerSecondSquared > 15f || MaxCleanupForceN > 3000f)
            {
                return "assist-active";
            }

            return "chain-responsive";
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
