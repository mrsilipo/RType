using System.Reflection;
using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class WeightTransferProbe
{
    private const string ShowroomStockBuildPath = "Data/PurchaseCars/2000_Ek9_Stock.json";

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters source = VehicleBuildDefinitionLoader.LoadSimulationParameters(ShowroomStockBuildPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        PrintDataMatrix(source);

        ProbeResult staticCase = RunCase(source, engineParameters, new FlatSurfaceSampler(), "static cruise", PrimeCruise, HoldNeutral, 180);
        ProbeResult braking = RunCase(source, engineParameters, new FlatSurfaceSampler(), "heavy braking", PrimeCruise, HeavyBrake, 30);
        ProbeResult accelFf = RunCase(CreateLayoutParameters(source, DrivetrainLayout.FF, 1f), engineParameters, new FlatSurfaceSampler(), "FF acceleration", PrimeSlowRoll, FullThrottle, 180);
        ProbeResult accelFr = RunCase(CreateLayoutParameters(source, DrivetrainLayout.FR, 0f), engineParameters, new FlatSurfaceSampler(), "FR acceleration", PrimeRollingCruise, FullThrottle, 180);
        ProbeResult accelAwd = RunCase(CreateLayoutParameters(source, DrivetrainLayout.AWD, 0.5f), engineParameters, new FlatSurfaceSampler(), "AWD acceleration", PrimeSlowRoll, FullThrottle, 180);
        ProbeResult corner = RunCase(source, engineParameters, new FlatSurfaceSampler(), "hard cornering", PrimeCruise, MaintenanceTurn, 180);
        ProbeResult aero = RunCase(source, engineParameters, new FlatSurfaceSampler(), "high-speed aero cruise", PrimeHighSpeed, HoldNeutral, 180);
        ProbeResult curb = RunCase(source, engineParameters, new AllCurbSampler(), "curb clipping", PrimeCruise, MaintenanceTurn, 180);

        Print(staticCase);
        Print(braking);
        Print(accelFf);
        Print(accelFr);
        Print(accelAwd);
        Print(corner);
        Print(aero);
        Print(curb);

        Require(braking.FrontAverageLoadN > staticCase.FrontAverageLoadN + 100f, "heavy braking did not load the front axle.");
        Require(braking.RearAverageLoadN < staticCase.RearAverageLoadN - 100f, "heavy braking did not unload the rear axle.");
        Require(accelFf.RearAverageLoadN > accelFf.FrontAverageLoadN * 0.45f, "FF acceleration load state collapsed.");
        Require(accelFr.RearAverageLoadN > accelFr.RearStaticAxleLoadN * 0.5f + 100f, "FR acceleration did not increase rear wheel load above static.");
        Require(accelFr.FrontAverageLoadN < accelFr.FrontStaticAxleLoadN * 0.5f - 100f, "FR acceleration did not unload front wheel load below static.");
        Require(accelAwd.FrontDriveTorqueNm > 1f && accelAwd.RearDriveTorqueNm > 1f, "AWD acceleration did not route torque to both axles.");
        Require(MathF.Abs(corner.FrontLeftLoadN - corner.FrontRightLoadN) > 150f, "hard cornering did not split left/right loads.");
        Require(MathF.Abs(aero.TotalAeroLoadN) > MathF.Abs(staticCase.TotalAeroLoadN), "high-speed aero contribution did not increase relative to static cruise.");
        Require(curb.MinSurfaceLoadMultiplier < 0.99f, "curb clipping did not apply surface load modulation.");
        Require(AllLoadsSafe(braking) && AllLoadsSafe(accelFf) && AllLoadsSafe(accelFr) && AllLoadsSafe(accelAwd) && AllLoadsSafe(corner) && AllLoadsSafe(curb), "a probe case produced unsafe wheel load.");

        ProbeVisualIsolation(source, engineParameters);
        Console.WriteLine("Weight-transfer probe passed: assembled data, FF/FR/AWD acceleration, braking, cornering, aero, curb modulation, and visual isolation are stable.");
    }

    private static void PrintDataMatrix(VehicleSimulationParameters parameters)
    {
        float frontRollStiffness = parameters.FrontSpringRateNPerM * parameters.FrontTrackMeters * parameters.FrontTrackMeters * 0.5f +
                                   parameters.FrontAntiRollBarRateNmPerRad;
        float rearRollStiffness = parameters.RearSpringRateNPerM * parameters.RearTrackMeters * parameters.RearTrackMeters * 0.5f +
                                  parameters.RearAntiRollBarRateNmPerRad;
        float totalRollStiffness = frontRollStiffness + rearRollStiffness;
        float frontRollShare = totalRollStiffness > 0.001f ? frontRollStiffness / totalRollStiffness : 0.5f;
        Console.WriteLine(
            $"weight data: {parameters.Id} {parameters.DisplayName}, layout {parameters.DrivetrainLayout}, mass {parameters.MassKg:0.0}kg, " +
            $"front {parameters.FrontWeightDistribution * 100f:0.0}%, cg {parameters.CenterOfGravityHeightMeters:0.000}m, " +
            $"wheelbase {parameters.WheelbaseMeters:0.000}m, track F/R {parameters.FrontTrackMeters:0.000}/{parameters.RearTrackMeters:0.000}m");
        Console.WriteLine(
            $"roll data: spring F/R {parameters.FrontSpringRateNPerM:0}/{parameters.RearSpringRateNPerM:0}N/m, " +
            $"ARB F/R {parameters.FrontAntiRollBarRateNmPerRad:0}/{parameters.RearAntiRollBarRateNmPerRad:0}Nm/rad, " +
            $"roll stiffness F/R {frontRollStiffness:0}/{rearRollStiffness:0}, front share {frontRollShare:0.000}");
    }

    private static void ProbeVisualIsolation(VehicleSimulationParameters source, SimulationEngineParameters engineParameters)
    {
        ProbeResult normalVisual = RunCase(source, engineParameters, new FlatSurfaceSampler(), "normal visual pseudo", PrimeSlowRoll, MaintenanceTurn, 180);
        ProbeResult doubledVisual = RunCase(CloneWithVisualPseudoScale(source, 2f), engineParameters, new FlatSurfaceSampler(), "double visual pseudo", PrimeSlowRoll, MaintenanceTurn, 180);
        Console.WriteLine(
            $"visual isolation delta loads FL/FR/RL/RR {doubledVisual.FrontLeftLoadN - normalVisual.FrontLeftLoadN:0.0}/" +
            $"{doubledVisual.FrontRightLoadN - normalVisual.FrontRightLoadN:0.0}/" +
            $"{doubledVisual.RearLeftLoadN - normalVisual.RearLeftLoadN:0.0}/" +
            $"{doubledVisual.RearRightLoadN - normalVisual.RearRightLoadN:0.0}N, " +
            $"delta body roll {doubledVisual.BodyRollDegrees - normalVisual.BodyRollDegrees:0.00}deg");
        Require(MathF.Abs(doubledVisual.FrontLeftLoadN - normalVisual.FrontLeftLoadN) < 0.01f, "visual pseudo scale polluted physical front-left load.");
        Require(MathF.Abs(doubledVisual.FrontRightLoadN - normalVisual.FrontRightLoadN) < 0.01f, "visual pseudo scale polluted physical front-right load.");
        Require(MathF.Abs(doubledVisual.RearLeftLoadN - normalVisual.RearLeftLoadN) < 0.01f, "visual pseudo scale polluted physical rear-left load.");
        Require(MathF.Abs(doubledVisual.RearRightLoadN - normalVisual.RearRightLoadN) < 0.01f, "visual pseudo scale polluted physical rear-right load.");
    }

    private static ProbeResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ITrackSurfaceSampler sampler,
        string label,
        Action<SimpleVehicleSimulator, float> prime,
        Func<int, VehicleInput> inputForFrame,
        int frames)
    {
        SimpleVehicleSimulator simulator = new(
            sampler,
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);

        const float dt = 1f / 120f;
        prime(simulator, dt);
        for (int i = 0; i < frames; i++)
        {
            simulator.Update(inputForFrame(i), dt);
        }

        VehicleState state = simulator.State;
        return new ProbeResult(
            label,
            parameters.DrivetrainLayout,
            state.SpeedMetersPerSecond,
            state.FrontLeftLoadN,
            state.FrontRightLoadN,
            state.RearLeftLoadN,
            state.RearRightLoadN,
            state.FrontStaticAxleLoadN,
            state.RearStaticAxleLoadN,
            state.LongitudinalLoadTransferN,
            state.FrontLateralLoadTransferN,
            state.RearLateralLoadTransferN,
            state.FrontAeroLoadN,
            state.RearAeroLoadN,
            state.FrontRollShare,
            MathF.Min(
                MathF.Min(state.FrontLeftSurfaceLoadMultiplier, state.FrontRightSurfaceLoadMultiplier),
                MathF.Min(state.RearLeftSurfaceLoadMultiplier, state.RearRightSurfaceLoadMultiplier)),
            state.FrontLeftFrictionEllipseGripBudgetN,
            state.FrontRightFrictionEllipseGripBudgetN,
            state.RearLeftFrictionEllipseGripBudgetN,
            state.RearRightFrictionEllipseGripBudgetN,
            MathF.Abs(state.FrontLeftDriveTorqueNm) + MathF.Abs(state.FrontRightDriveTorqueNm),
            MathF.Abs(state.RearLeftDriveTorqueNm) + MathF.Abs(state.RearRightDriveTorqueNm),
            state.FrontDifferentialManagedAxleTorqueNm,
            state.RearDifferentialManagedAxleTorqueNm,
            MathHelper.ToDegrees(state.BodyRollRadians - state.GroundRollRadians));
    }

    private static void PrimeSlowRoll(SimpleVehicleSimulator simulator, float dt)
    {
        for (int i = 0; i < 160 && simulator.State.SpeedMetersPerSecond < 12f; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }
    }

    private static void PrimeCruise(SimpleVehicleSimulator simulator, float dt)
    {
        for (int i = 0; i < 420 && simulator.State.SpeedMetersPerSecond < 24f; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }
    }

    private static void PrimeRollingCruise(SimpleVehicleSimulator simulator, float dt)
    {
        simulator.State.Gear = 3;
        simulator.State.Velocity = new Vector2(0f, 24f);
        for (int i = 0; i < 30; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, 0f), dt);
        }
    }

    private static void PrimeHighSpeed(SimpleVehicleSimulator simulator, float dt)
    {
        simulator.State.Gear = 5;
        simulator.State.Velocity = new Vector2(0f, 58f);
        for (int i = 0; i < 30; i++)
        {
            simulator.Update(new VehicleInput(0.20f, 0f, 0f), dt);
        }
    }

    private static VehicleInput HoldNeutral(int frame) => new(0f, 0f, 0f);

    private static VehicleInput FullThrottle(int frame) => new(1f, 0f, 0f);

    private static VehicleInput HeavyBrake(int frame) => new(0f, 1f, 0f);

    private static VehicleInput MaintenanceTurn(int frame) => new(0.35f, 0f, 0.42f);

    private static VehicleSimulationParameters CreateLayoutParameters(
        VehicleSimulationParameters source,
        DrivetrainLayout layout,
        float frontTorqueShare)
    {
        VehicleSimulationParameters clone = CopyInitProperties(source, new VehicleSimulationParameters());
        DifferentialParameters lsd = new()
        {
            TorqueBiasRatio = MathF.Max(3.0f, source.DifferentialTorqueBiasRatio),
            PreloadTorqueNm = MathF.Max(45f, source.DifferentialPreloadTorqueNm)
        };

        Set(clone, nameof(VehicleSimulationParameters.DrivetrainLayout), layout);
        Set(clone, nameof(VehicleSimulationParameters.FrontTorqueShare), frontTorqueShare);
        Set(clone, nameof(VehicleSimulationParameters.DrivenWheels), layout switch
        {
            DrivetrainLayout.FF => new DrivenWheelSet(true, true, false, false),
            DrivetrainLayout.FR => new DrivenWheelSet(false, false, true, true),
            DrivetrainLayout.AWD => new DrivenWheelSet(true, true, true, true),
            _ => new DrivenWheelSet(true, true, false, false)
        });
        Set(clone, nameof(VehicleSimulationParameters.FrontDifferential), layout == DrivetrainLayout.FR ? DifferentialParameters.Open : lsd);
        Set(clone, nameof(VehicleSimulationParameters.RearDifferential), layout == DrivetrainLayout.FF ? DifferentialParameters.Open : lsd);
        return clone;
    }

    private static VehicleSimulationParameters CloneWithVisualPseudoScale(VehicleSimulationParameters source, float multiplier)
    {
        VehicleSimulationParameters clone = CopyInitProperties(source, new VehicleSimulationParameters());
        ArcadeHandlingParameters arcade = CopyInitProperties(source.ArcadeHandling, new ArcadeHandlingParameters());
        typeof(ArcadeHandlingParameters)
            .GetProperty(nameof(ArcadeHandlingParameters.PseudoLateralTransferScale), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(arcade, source.ArcadeHandling.PseudoLateralTransferScale * multiplier);
        Set(clone, nameof(VehicleSimulationParameters.ArcadeHandling), arcade);
        return clone;
    }

    private static T CopyInitProperties<T>(T source, T destination)
    {
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(destination, property.GetValue(source));
            }
        }

        return destination;
    }

    private static void Set<TValue>(VehicleSimulationParameters parameters, string propertyName, TValue value)
    {
        typeof(VehicleSimulationParameters)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parameters, value);
    }

    private static bool AllLoadsSafe(ProbeResult result)
    {
        return result.FrontLeftLoadN >= 50f &&
               result.FrontRightLoadN >= 50f &&
               result.RearLeftLoadN >= 50f &&
               result.RearRightLoadN >= 50f;
    }

    private static void Print(ProbeResult result)
    {
        Console.WriteLine(
            $"{result.Label} [{result.Layout}]: speed {result.SpeedMetersPerSecond * 3.6f:0.0}km/h, " +
            $"loads FL/FR/RL/RR {result.FrontLeftLoadN:0}/{result.FrontRightLoadN:0}/{result.RearLeftLoadN:0}/{result.RearRightLoadN:0}N, " +
            $"static F/R {result.FrontStaticAxleLoadN:0}/{result.RearStaticAxleLoadN:0}N, " +
            $"transfer long/frontLat/rearLat {result.LongitudinalTransferN:0}/{result.FrontLateralTransferN:0}/{result.RearLateralTransferN:0}N, " +
            $"aero F/R {result.FrontAeroLoadN:0}/{result.RearAeroLoadN:0}N, rollShare {result.FrontRollShare:0.000}, " +
            $"surfaceMin {result.MinSurfaceLoadMultiplier:0.00}, gripBudget FL/FR/RL/RR {result.FrontLeftGripBudgetN:0}/{result.FrontRightGripBudgetN:0}/" +
            $"{result.RearLeftGripBudgetN:0}/{result.RearRightGripBudgetN:0}N, drive F/R {result.FrontDriveTorqueNm:0}/{result.RearDriveTorqueNm:0}Nm, " +
            $"managedDiff F/R {result.FrontManagedTorqueNm:0}/{result.RearManagedTorqueNm:0}Nm, bodyRoll {result.BodyRollDegrees:0.00}deg");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Weight-transfer probe failed: {message}");
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }

    private sealed class AllCurbSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample(
                "CURB",
                1.1f,
                staticFrictionCoefficient: 1.1f,
                dynamicFrictionCoefficient: 0.85f,
                optimalSlipRatio: 0.08f,
                vibrationPrimaryFrequency: 4f,
                vibrationPrimaryAmplitude: 0.25f,
                handbrakeScreechFactor: 1f);
        }
    }

    private readonly record struct ProbeResult(
        string Label,
        DrivetrainLayout Layout,
        float SpeedMetersPerSecond,
        float FrontLeftLoadN,
        float FrontRightLoadN,
        float RearLeftLoadN,
        float RearRightLoadN,
        float FrontStaticAxleLoadN,
        float RearStaticAxleLoadN,
        float LongitudinalTransferN,
        float FrontLateralTransferN,
        float RearLateralTransferN,
        float FrontAeroLoadN,
        float RearAeroLoadN,
        float FrontRollShare,
        float MinSurfaceLoadMultiplier,
        float FrontLeftGripBudgetN,
        float FrontRightGripBudgetN,
        float RearLeftGripBudgetN,
        float RearRightGripBudgetN,
        float FrontDriveTorqueNm,
        float RearDriveTorqueNm,
        float FrontManagedTorqueNm,
        float RearManagedTorqueNm,
        float BodyRollDegrees)
    {
        public float FrontAverageLoadN => (FrontLeftLoadN + FrontRightLoadN) * 0.5f;

        public float RearAverageLoadN => (RearLeftLoadN + RearRightLoadN) * 0.5f;

        public float TotalAeroLoadN => FrontAeroLoadN + RearAeroLoadN;
    }
}
