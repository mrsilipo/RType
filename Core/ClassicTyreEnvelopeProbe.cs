using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;

namespace RType.Core;

public static class ClassicTyreEnvelopeProbe
{
    private static readonly float[] LoadRatios = [0.50f, 0.75f, 1.00f, 1.25f, 1.50f];
    private static readonly float[] SlipAnglesDegrees = [0f, 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f, 12f, 15f, 20f, 25f];
    private static readonly float[] LongitudinalUsage = [0f, 0.35f, 0.65f, 0.85f];
    private static readonly TyreCurveVariant[] CurveVariants =
    [
        new("current", float.NaN, float.NaN),
        new("progressive-A", 0.78f, 18f),
        new("stronger-B", 0.68f, 16f)
    ];

    private static readonly float[] LoadSensitivityVariants = [0.12f, 0.20f, 0.28f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);

        Console.WriteLine($"Classic tyre envelope probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: prints the active classic tyre force law from EK9/AD09 resolved data.");
        foreach (TyreCurveVariant variant in CurveVariants)
        {
            ClassicBicycleTyreParameters front = variant.Apply(tyres.Front);
            ClassicBicycleTyreParameters rear = variant.Apply(tyres.Rear);
            Console.WriteLine();
            Console.WriteLine($"curve variant {variant.Label}: slidingGrip={(float.IsFinite(variant.SlidingGrip) ? variant.SlidingGrip.ToString("F2") : "current")} falloff={(float.IsFinite(variant.FalloffSlipDegrees) ? variant.FalloffSlipDegrees.ToString("F1") : "current")}deg");
            PrintTyre("front", front);
            PrintTyre("rear", rear);
            PrintAxleSplit("front axle", front, parameters.MassKg * 9.81f * MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f));
            PrintAxleSplit("rear axle", rear, parameters.MassKg * 9.81f * (1f - MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f)));
        }

        PrintLoadSensitivitySweep("front axle load sensitivity sweep", tyres.Front, parameters.MassKg * 9.81f * MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f));
        PrintLoadSensitivitySweep("rear axle load sensitivity sweep", tyres.Rear, parameters.MassKg * 9.81f * (1f - MathHelper.Clamp(parameters.FrontWeightDistribution, 0.05f, 0.95f)));
        Console.WriteLine("Classic tyre envelope probe complete.");
    }

    private static void PrintTyre(string label, ClassicBicycleTyreParameters tyre)
    {
        Console.WriteLine();
        Console.WriteLine($"  {label}: muRef={tyre.MaxGrip:F3} peakSlip={tyre.PeakSlipAngleDegrees:F1}deg falloff={tyre.FalloffSlipAngleDegrees:F1}deg slide={tyre.SlidingGrip:F2} loadSens={tyre.LoadSensitivity:F3} refLoad={tyre.ReferenceLoadN:F0}N relax={tyre.RelaxationLengthMeters:F2}m");
        Console.WriteLine("    loadRatio loadN muEff peakFy slip:forceUsage");

        foreach (float loadRatio in LoadRatios)
        {
            float load = tyre.ReferenceLoadN * loadRatio;
            float peakForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(load, 1f, tyre);
            float muEffective = peakForce / MathF.Max(1f, load);
            Console.Write($"    {loadRatio,5:F2} {load,6:F0} {muEffective,5:F3} {peakForce,6:F0}");

            foreach (float slipDegrees in SlipAnglesDegrees)
            {
                float force = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(slipDegrees), peakForce, tyre);
                Console.Write($" {slipDegrees:00}:{force / peakForce,4:F2}");
            }

            Console.WriteLine();
        }

        Console.WriteLine("    reference-load slope: slip forceUsage dFy/dSlip(N/deg)");
        float referencePeak = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(tyre.ReferenceLoadN, 1f, tyre);
        foreach (float slipDegrees in SlipAnglesDegrees)
        {
            float force = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(slipDegrees), referencePeak, tyre);
            float slope = CalculateSlopeNPerDegree(slipDegrees, referencePeak, tyre);
            Console.WriteLine($"    {slipDegrees,5:F1} {force / referencePeak,8:F3} {slope,11:F0}");
        }

        Console.WriteLine("    combined usage at reference load: longUsage slip:availableLatUsage");
        foreach (float longUsage in LongitudinalUsage)
        {
            float longitudinal = referencePeak * longUsage;
            float latBudget = MathF.Sqrt(MathF.Max(0f, referencePeak * referencePeak - longitudinal * longitudinal));
            Console.Write($"    {longUsage,5:F2}");
            foreach (float slipDegrees in SlipAnglesDegrees)
            {
                float requested = MathF.Abs(ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(slipDegrees), referencePeak, tyre));
                float allowed = MathF.Min(requested, latBudget);
                Console.Write($" {slipDegrees:00}:{allowed / referencePeak,4:F2}");
            }

            Console.WriteLine();
        }
    }

    private static void PrintLoadSensitivitySweep(string label, ClassicBicycleTyreParameters tyre, float axleLoad)
    {
        Console.WriteLine();
        Console.WriteLine($"  {label}:");
        foreach (float loadSensitivity in LoadSensitivityVariants)
        {
            ClassicBicycleTyreParameters adjusted = ClassicFourWheelVehicleSimulator.CopyTyreWithLoadSensitivity(tyre, loadSensitivity);
            float evenCapacity = CapacityForSplit(adjusted, axleLoad, 0.50f);
            Console.Write($"    loadSens={loadSensitivity:F2}");
            foreach (float outsideShare in new[] { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f })
            {
                float capacity = CapacityForSplit(adjusted, axleLoad, outsideShare);
                Console.Write($" {outsideShare * 100f:F0}/{(1f - outsideShare) * 100f:F0}:{capacity / MathF.Max(1f, evenCapacity):F3}");
            }

            Console.WriteLine();
        }
    }

    private static void PrintAxleSplit(string label, ClassicBicycleTyreParameters tyre, float axleLoad)
    {
        Console.WriteLine();
        Console.WriteLine($"  {label} capacity by fixed total load split:");
        Console.WriteLine("    split outside/inside outsideLoad insideLoad axleCapacity relativeTo50");
        float evenCapacity = CapacityForSplit(tyre, axleLoad, 0.50f);
        foreach (float outsideShare in new[] { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f })
        {
            float outsideLoad = axleLoad * outsideShare;
            float insideLoad = axleLoad - outsideLoad;
            float capacity = CapacityForSplit(tyre, axleLoad, outsideShare);
            Console.WriteLine($"    {outsideShare * 100f,4:F0}/{(1f - outsideShare) * 100f,2:F0} {outsideLoad,8:F0} {insideLoad,8:F0} {capacity,10:F0} {capacity / MathF.Max(1f, evenCapacity),8:F3}");
        }
    }

    private static float CapacityForSplit(ClassicBicycleTyreParameters tyre, float axleLoad, float outsideShare)
    {
        float outsideLoad = axleLoad * outsideShare;
        float insideLoad = axleLoad - outsideLoad;
        return ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(outsideLoad, 1f, tyre) +
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(insideLoad, 1f, tyre);
    }

    private static float CalculateSlopeNPerDegree(float slipDegrees, float maxForceN, ClassicBicycleTyreParameters tyre)
    {
        const float DeltaDegrees = 0.1f;
        float lower = MathF.Max(0f, slipDegrees - DeltaDegrees);
        float upper = slipDegrees + DeltaDegrees;
        float lowerForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(lower), maxForceN, tyre);
        float upperForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(MathHelper.ToRadians(upper), maxForceN, tyre);
        return (upperForce - lowerForce) / MathF.Max(0.001f, upper - lower);
    }

    private readonly record struct TyreCurveVariant(string Label, float SlidingGrip, float FalloffSlipDegrees)
    {
        public ClassicBicycleTyreParameters Apply(ClassicBicycleTyreParameters source) =>
            ClassicFourWheelVehicleSimulator.CopyTyreWithPostPeakShape(source, SlidingGrip, FalloffSlipDegrees);
    }
}
