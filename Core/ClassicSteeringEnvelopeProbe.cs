using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;

namespace RType.Core;

public static class ClassicSteeringEnvelopeProbe
{
    private static readonly float[] SpeedsKmh = [0f, 30f, 60f, 90f, 120f, 150f, 180f, 200f];
    private static readonly float[] LateralGTargets = [0.5f, 0.7f, 0.9f, 1.0f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);

        float estimatedSustainedG = EstimateSustainedLateralG(parameters, tyres);
        Console.WriteLine($"Classic steering envelope probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: no production steering, tyre, yaw, or cleanup values changed");
        Console.WriteLine(
            $"  wheelbase={geometry.WheelbaseMeters:F3}m mass={parameters.MassKg:F0}kg " +
            $"frontWeight={parameters.FrontWeightDistribution:P1} estimatedPeakLat={estimatedSustainedG:F2}g");
        Console.WriteLine(
            $"  current cap curve: linear 0={engine.ClassicFourWheel.Steering.ZeroKmhAngleDegrees:F1}deg " +
            $"400={engine.ClassicFourWheel.Steering.TwoHundredKmhAngleDegrees:F1}deg");

        PrintPhysicalAngleTable(geometry, engine, estimatedSustainedG);
        PrintCurrentDemandExamples(geometry, engine);
        PrintProposedEnvelope(geometry, estimatedSustainedG);
        PrintLayerDesign();

        Console.WriteLine("Classic steering envelope probe complete.");
    }

    private static void PrintPhysicalAngleTable(
        VehicleAxleGeometry geometry,
        SimulationEngineParameters engine,
        float estimatedSustainedG)
    {
        Console.WriteLine("  physical road-wheel angle by lateral-g target");
        Console.WriteLine("    speed currentCap capDemandG angle@0.5g angle@0.7g angle@0.9g angle@1.0g angle@estG");
        foreach (float speedKmh in SpeedsKmh)
        {
            float cap = CalculateCurrentMaxSteerAngleDegrees(engine.ClassicFourWheel.Steering, speedKmh);
            float capDemand = CalculateLateralGFromAngle(geometry.WheelbaseMeters, speedKmh, cap);
            Console.WriteLine(
                $"    {speedKmh,5:F0} {cap,10:F2} {capDemand,10:F2} " +
                $"{AngleForG(geometry.WheelbaseMeters, speedKmh, 0.5f),10:F2} " +
                $"{AngleForG(geometry.WheelbaseMeters, speedKmh, 0.7f),10:F2} " +
                $"{AngleForG(geometry.WheelbaseMeters, speedKmh, 0.9f),10:F2} " +
                $"{AngleForG(geometry.WheelbaseMeters, speedKmh, 1.0f),10:F2} " +
                $"{AngleForG(geometry.WheelbaseMeters, speedKmh, estimatedSustainedG),10:F2}");
        }
    }

    private static void PrintCurrentDemandExamples(
        VehicleAxleGeometry geometry,
        SimulationEngineParameters engine)
    {
        float currentCap150 = CalculateCurrentMaxSteerAngleDegrees(engine.ClassicFourWheel.Steering, 150f);
        Console.WriteLine("  150km/h current-demand examples");
        PrintDemand("current cap", geometry.WheelbaseMeters, 150f, currentCap150);
        PrintDemand("medium request", geometry.WheelbaseMeters, 150f, 3.45f);
        PrintDemand("hard request", geometry.WheelbaseMeters, 150f, 6.41f);
    }

    private static void PrintDemand(string label, float wheelbase, float speedKmh, float roadWheelDegrees)
    {
        float radius = CalculateRadius(wheelbase, roadWheelDegrees);
        float g = CalculateLateralGFromAngle(wheelbase, speedKmh, roadWheelDegrees);
        Console.WriteLine(
            $"    {label,-14} angle={roadWheelDegrees:F2}deg radius={radius:F1}m impliedLat={g:F2}g");
    }

    private static void PrintProposedEnvelope(VehicleAxleGeometry geometry, float estimatedSustainedG)
    {
        Console.WriteLine("  proposed physical sanity envelope");
        Console.WriteLine("    speed normalLimit overdriveLimit note");
        foreach (float speedKmh in SpeedsKmh)
        {
            float normalG = MathF.Min(0.75f, estimatedSustainedG * 0.78f);
            float overdriveG = MathF.Min(0.95f, estimatedSustainedG * 0.98f);
            float normal = AngleForG(geometry.WheelbaseMeters, speedKmh, normalG);
            float overdrive = AngleForG(geometry.WheelbaseMeters, speedKmh, overdriveG);
            Console.WriteLine(
                $"    {speedKmh,5:F0} {normal,11:F2}deg {overdrive,13:F2}deg " +
                $"{(speedKmh >= 120f ? "high-speed demand bounded by lateral-g envelope" : "low-speed rack angle can remain larger")}");
        }

        Console.WriteLine(
            "    proposed 150km/h starting point: normal digital hold should live around " +
            $"{AngleForG(geometry.WheelbaseMeters, 150f, MathF.Min(0.75f, estimatedSustainedG * 0.78f)):F2}deg, " +
            $"with deliberate overdrive no higher than about {AngleForG(geometry.WheelbaseMeters, 150f, MathF.Min(0.95f, estimatedSustainedG * 0.98f)):F2}deg before tyre understeer takes over.");
    }

    private static void PrintLayerDesign()
    {
        Console.WriteLine("  proposed architecture");
        Console.WriteLine("    layer 1 physical authority: speed-dependent road-wheel envelope derived from lateral-g capacity, with a normal zone and a limited overdrive zone.");
        Console.WriteLine("    layer 2 digital intent: tap/hold curve maps key hold duration into that envelope; quick taps make small corrections, sustained hold moves into normal authority, longer hold enters overdrive.");
        Console.WriteLine("    release/countersteer: keep separate return/countersteer rates so recovery can stay responsive without increasing high-speed steady steering demand.");
        Console.WriteLine("    important: the envelope is not a hard tyre-grip clamp; overdrive remains possible so the tyre model can still produce progressive understeer.");
    }

    private static float EstimateSustainedLateralG(
        VehicleSimulationParameters parameters,
        ClassicFourWheelTyres tyres)
    {
        float frontLoad = parameters.MassKg * 9.81f * parameters.FrontWeightDistribution;
        float rearLoad = parameters.MassKg * 9.81f * (1f - parameters.FrontWeightDistribution);
        float totalCapacity =
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(frontLoad * 0.5f, 1f, tyres.Front) * 2f +
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(rearLoad * 0.5f, 1f, tyres.Rear) * 2f;
        return totalCapacity / MathF.Max(1f, parameters.MassKg * 9.81f);
    }

    private static float AngleForG(float wheelbaseMeters, float speedKmh, float lateralG)
    {
        if (speedKmh <= 0.1f)
        {
            return 32f;
        }

        float speed = speedKmh / 3.6f;
        float lateralAcceleration = lateralG * 9.81f;
        return MathHelper.ToDegrees(MathF.Atan(wheelbaseMeters * lateralAcceleration / MathF.Max(0.01f, speed * speed)));
    }

    private static float CalculateRadius(float wheelbaseMeters, float roadWheelDegrees)
    {
        float tan = MathF.Tan(MathHelper.ToRadians(MathF.Abs(roadWheelDegrees)));
        return tan <= 0.0001f ? float.PositiveInfinity : wheelbaseMeters / tan;
    }

    private static float CalculateLateralGFromAngle(float wheelbaseMeters, float speedKmh, float roadWheelDegrees)
    {
        if (speedKmh <= 0.1f || MathF.Abs(roadWheelDegrees) <= 0.001f)
        {
            return 0f;
        }

        float speed = speedKmh / 3.6f;
        float radius = CalculateRadius(wheelbaseMeters, roadWheelDegrees);
        return speed * speed / MathF.Max(0.1f, radius) / 9.81f;
    }

    private static float CalculateCurrentMaxSteerAngleDegrees(ClassicBicycleSteeringParameters steering, float speedKmh)
    {
        return MathHelper.Lerp(
            steering.ZeroKmhAngleDegrees,
            steering.TwoHundredKmhAngleDegrees,
            MathHelper.Clamp(speedKmh / 400f, 0f, 1f));
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
