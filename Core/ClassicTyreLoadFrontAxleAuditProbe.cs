using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTyreLoadFrontAxleAuditProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float Gravity = 9.81f;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engine.ClassicFourWheel);

        Console.WriteLine($"Classic tyre-load/front-axle audit probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        PrintLoadSensitivity("front", parameters.MassKg * Gravity * parameters.FrontWeightDistribution * 0.5f, tyres.Front);
        PrintLoadSensitivity("rear", parameters.MassKg * Gravity * (1f - parameters.FrontWeightDistribution) * 0.5f, tyres.Rear);
        PrintAxleCapacitySplit(parameters, tyres);
        PrintSensitivityVariantSplit(parameters, tyres, 0.20f);
        PrintSensitivityVariantSplit(parameters, tyres, 0.28f);

        Console.WriteLine();
        Console.WriteLine("  dynamic brake-turn: 150km/h, gear=4, brake at t0, steer=1 from 0.15s, sample=1.2s");
        Console.WriteLine("  case sens speedLoss frontSlipZero frontLatUseAvg/max rearLatUseAvg/max frontLongUseAvg/max rearLongUseAvg/max frontLatForceAvg rearLatForceAvg frontYawAvg rearYawAvg betaMax yawMax classification");
        RunDynamicCase("stateful-reg-on", parameters, engine, staticLoads: false, disableRegulator: false, brakeScale: 1f, loadSensitivityOverride: null);
        RunDynamicCase("instant-reg-on", parameters, CloneLoadTransfer(engine, enabled: false), staticLoads: false, disableRegulator: false, brakeScale: 1f, loadSensitivityOverride: null);
        RunDynamicCase("static-reg-on", parameters, engine, staticLoads: true, disableRegulator: false, brakeScale: 1f, loadSensitivityOverride: null);
        RunDynamicCase("stateful-raw45", parameters, engine, staticLoads: false, disableRegulator: true, brakeScale: 0.45f, loadSensitivityOverride: null);
        RunDynamicCase("stateful-sens20", parameters, engine, staticLoads: false, disableRegulator: false, brakeScale: 1f, loadSensitivityOverride: 0.20f);
        RunDynamicCase("stateful-sens28", parameters, engine, staticLoads: false, disableRegulator: false, brakeScale: 1f, loadSensitivityOverride: 0.28f);

        Console.WriteLine("Classic tyre-load/front-axle audit probe complete.");
    }

    private static void PrintLoadSensitivity(string label, float staticWheelLoad, ClassicBicycleTyreParameters tyre)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"  {label} load sensitivity: staticWheelLoad={staticWheelLoad:0}N referenceLoad={tyre.ReferenceLoadN:0}N " +
            $"peakSlip={tyre.PeakSlipAngleDegrees:0.0}deg maxGrip={tyre.MaxGrip:0.00} loadSensitivity={tyre.LoadSensitivity:0.000}");
        Console.WriteLine("    loadScale loadN peakForceN effectiveMu forceVsStatic");
        float staticForce = MathF.Abs(ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(
            MathHelper.ToRadians(tyre.PeakSlipAngleDegrees),
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(staticWheelLoad, 1f, tyre),
            tyre));
        foreach (float scale in new[] { 0.5f, 0.75f, 1.0f, 1.25f, 1.5f })
        {
            float load = staticWheelLoad * scale;
            float maxForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(load, 1f, tyre);
            float force = MathF.Abs(ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(
                MathHelper.ToRadians(tyre.PeakSlipAngleDegrees),
                maxForce,
                tyre));
            Console.WriteLine(
                $"    {scale,8:0.00} {load,6:0} {force,10:0} {force / MathF.Max(1f, load),10:0.000} " +
                $"{force / MathF.Max(1f, staticForce),13:0.000}");
        }
    }

    private static void PrintAxleCapacitySplit(VehicleSimulationParameters parameters, ClassicFourWheelTyres tyres)
    {
        float frontStaticWheel = parameters.MassKg * Gravity * parameters.FrontWeightDistribution * 0.5f;
        float rearStaticWheel = parameters.MassKg * Gravity * (1f - parameters.FrontWeightDistribution) * 0.5f;
        Console.WriteLine();
        Console.WriteLine("  lateral transfer axle-capacity check, constant total axle load");
        Console.WriteLine("    axle split loaded/unloaded totalPeakN vs50_50 delta");
        foreach (float loadedShare in new[] { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f })
        {
            PrintAxleSplit("front", frontStaticWheel * 2f, loadedShare, tyres.Front);
        }

        foreach (float loadedShare in new[] { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f })
        {
            PrintAxleSplit("rear", rearStaticWheel * 2f, loadedShare, tyres.Rear);
        }
    }

    private static void PrintSensitivityVariantSplit(
        VehicleSimulationParameters parameters,
        ClassicFourWheelTyres tyres,
        float sensitivity)
    {
        ClassicFourWheelTyres variant = new(
            ClassicFourWheelVehicleSimulator.CopyTyreWithLoadSensitivity(tyres.Front, sensitivity),
            ClassicFourWheelVehicleSimulator.CopyTyreWithLoadSensitivity(tyres.Rear, sensitivity));
        Console.WriteLine();
        Console.WriteLine($"  diagnostic split check with loadSensitivity override={sensitivity:0.000}");
        Console.WriteLine("    axle split loaded/unloaded totalPeakN vs50_50 delta");
        float frontStaticWheel = parameters.MassKg * Gravity * parameters.FrontWeightDistribution * 0.5f;
        float rearStaticWheel = parameters.MassKg * Gravity * (1f - parameters.FrontWeightDistribution) * 0.5f;
        foreach (float loadedShare in new[] { 0.50f, 0.70f, 0.80f, 0.90f })
        {
            PrintAxleSplit("front", frontStaticWheel * 2f, loadedShare, variant.Front);
        }

        foreach (float loadedShare in new[] { 0.50f, 0.70f, 0.80f, 0.90f })
        {
            PrintAxleSplit("rear", rearStaticWheel * 2f, loadedShare, variant.Rear);
        }
    }

    private static void PrintAxleSplit(string label, float staticAxleLoad, float loadedShare, ClassicBicycleTyreParameters tyre)
    {
        float loaded = MathF.Max(50f, staticAxleLoad * loadedShare);
        float unloaded = MathF.Max(50f, staticAxleLoad - loaded);
        float loadedForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(loaded, 1f, tyre);
        float unloadedForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(unloaded, 1f, tyre);
        float total = loadedForce + unloadedForce;
        float equalTotal =
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(staticAxleLoad * 0.5f, 1f, tyre) * 2f;
        Console.WriteLine(
            $"    {label,-5} {loadedShare * 100f:00}/{(1f - loadedShare) * 100f:00} " +
            $"{loaded,6:0}/{unloaded,6:0} {total,10:0} {total / MathF.Max(1f, equalTotal),7:0.000} {total - equalTotal,7:0}");
    }

    private static void RunDynamicCase(
        string label,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        bool staticLoads,
        bool disableRegulator,
        float brakeScale,
        float? loadSensitivityOverride)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, StartSpeedKmh / 3.6f);
        simulator.UseStaticWheelLoadsForProbe = staticLoads;
        simulator.DisableBrakePressureRegulatorForProbe = disableRegulator;
        if (loadSensitivityOverride.HasValue)
        {
            simulator.TyreLoadSensitivityOverrideForProbe = loadSensitivityOverride.Value;
        }

        float startSpeed = simulator.State.SpeedMetersPerSecond * 3.6f;
        Accumulator acc = new();
        float? frontSlipZero = null;
        float previousFrontSlip = 0f;
        for (int i = 1; i <= SecondsToTicks(1.2f); i++)
        {
            float elapsed = i * Dt;
            float steer = elapsed >= 0.15f ? 1f : 0f;
            simulator.Update(new VehicleInput(0f, brakeScale, steer, brakeAssistEnabled: true), Dt);
            VehicleState state = simulator.State;
            float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
            if (elapsed > 0.16f && frontSlipZero is null && previousFrontSlip > 0.05f && frontSlip <= 0f)
            {
                frontSlipZero = elapsed;
            }

            previousFrontSlip = frontSlip;
            acc.Add(state, parameters);
        }

        VehicleState end = simulator.State;
        string classification = Classify(acc, frontSlipZero);
        string frontSlipZeroText = frontSlipZero.HasValue
            ? frontSlipZero.Value.ToString("0.00")
            : "never";
        Console.WriteLine(
            $"  {label,-15} {FormatSensitivity(loadSensitivityOverride),4} {startSpeed - end.SpeedMetersPerSecond * 3.6f,8:0.0} " +
            $"{frontSlipZeroText,13} " +
            $"{acc.FrontLatUseAverage,7:0.00}/{acc.FrontLatUseMax:0.00} " +
            $"{acc.RearLatUseAverage,7:0.00}/{acc.RearLatUseMax:0.00} " +
            $"{acc.FrontLongUseAverage,8:0.00}/{acc.FrontLongUseMax:0.00} " +
            $"{acc.RearLongUseAverage,7:0.00}/{acc.RearLongUseMax:0.00} " +
            $"{acc.FrontLateralForceAverage,15:0} {acc.RearLateralForceAverage,14:0} " +
            $"{acc.FrontYawMomentAverage,11:0} {acc.RearYawMomentAverage,10:0} " +
            $"{acc.BetaMax,7:0.0} {acc.YawMax,6:0.0} {classification}");
    }

    private static string FormatSensitivity(float? loadSensitivityOverride)
    {
        return loadSensitivityOverride.HasValue
            ? loadSensitivityOverride.Value.ToString("0.00")
            : "data";
    }

    private static string Classify(Accumulator acc, float? frontSlipZero)
    {
        if (acc.FrontLatUseAverage < 0.18f && acc.RearLatUseAverage > acc.FrontLatUseAverage * 2.5f)
        {
            return "front-asleep-rear-dominant";
        }

        if (frontSlipZero.HasValue)
        {
            return "front-slip-reversal";
        }

        if (acc.FrontLongUseAverage > 0.75f)
        {
            return "front-brake-budget-heavy";
        }

        return "balanced-enough-for-next-audit";
    }

    private static SimulationEngineParameters CloneLoadTransfer(SimulationEngineParameters source, bool enabled)
    {
        return new SimulationEngineParameters
        {
            HandlingModel = source.HandlingModel,
            Timing = source.Timing,
            VehicleSafety = source.VehicleSafety,
            StabilityAssist = source.StabilityAssist,
            DigitalThrottleAssist = source.DigitalThrottleAssist,
            DigitalBrakeAssist = source.DigitalBrakeAssist,
            BrakeThrottlePriority = source.BrakeThrottlePriority,
            SteeringAssist = source.SteeringAssist,
            TyreForce = source.TyreForce,
            RpmResponse = source.RpmResponse,
            ClassicBicycle = source.ClassicBicycle,
            ClassicFourWheel = new ClassicBicycleParameters
            {
                Steering = source.ClassicFourWheel.Steering,
                FrontTyres = source.ClassicFourWheel.FrontTyres,
                RearTyres = source.ClassicFourWheel.RearTyres,
                Yaw = source.ClassicFourWheel.Yaw,
                GripBudget = source.ClassicFourWheel.GripBudget,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance,
                ChassisLoadTransfer = new ClassicChassisLoadTransferParameters
                {
                    Enabled = enabled,
                    LongitudinalNaturalFrequencyHz = source.ClassicFourWheel.ChassisLoadTransfer.LongitudinalNaturalFrequencyHz,
                    LongitudinalDampingRatio = source.ClassicFourWheel.ChassisLoadTransfer.LongitudinalDampingRatio,
                    LateralNaturalFrequencyHz = source.ClassicFourWheel.ChassisLoadTransfer.LateralNaturalFrequencyHz,
                    LateralDampingRatio = source.ClassicFourWheel.ChassisLoadTransfer.LateralDampingRatio
                }
            }
        };
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private sealed class Accumulator
    {
        private int _count;
        private float _frontLatUseSum;
        private float _rearLatUseSum;
        private float _frontLongUseSum;
        private float _rearLongUseSum;
        private float _frontLateralForceSum;
        private float _rearLateralForceSum;
        private float _frontYawMomentSum;
        private float _rearYawMomentSum;

        public float FrontLatUseMax { get; private set; }
        public float RearLatUseMax { get; private set; }
        public float FrontLongUseMax { get; private set; }
        public float RearLongUseMax { get; private set; }
        public float BetaMax { get; private set; }
        public float YawMax { get; private set; }
        public float FrontLatUseAverage => _count > 0 ? _frontLatUseSum / _count : 0f;
        public float RearLatUseAverage => _count > 0 ? _rearLatUseSum / _count : 0f;
        public float FrontLongUseAverage => _count > 0 ? _frontLongUseSum / _count : 0f;
        public float RearLongUseAverage => _count > 0 ? _rearLongUseSum / _count : 0f;
        public float FrontLateralForceAverage => _count > 0 ? _frontLateralForceSum / _count : 0f;
        public float RearLateralForceAverage => _count > 0 ? _rearLateralForceSum / _count : 0f;
        public float FrontYawMomentAverage => _count > 0 ? _frontYawMomentSum / _count : 0f;
        public float RearYawMomentAverage => _count > 0 ? _rearYawMomentSum / _count : 0f;

        public void Add(VehicleState state, VehicleSimulationParameters parameters)
        {
            VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
            float frontLat = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
            float rearLat = state.RearLeftLateralForceN + state.RearRightLateralForceN;
            float frontYaw = -geometry.CgToFrontAxleMeters * frontLat;
            float rearYaw = geometry.CgToRearAxleMeters * rearLat;
            _count++;
            _frontLatUseSum += state.ClassicFrontLateralGripUsage;
            _rearLatUseSum += state.ClassicRearLateralGripUsage;
            _frontLongUseSum += state.ClassicFrontLongitudinalGripUsage;
            _rearLongUseSum += state.ClassicRearLongitudinalGripUsage;
            _frontLateralForceSum += frontLat;
            _rearLateralForceSum += rearLat;
            _frontYawMomentSum += frontYaw;
            _rearYawMomentSum += rearYaw;
            FrontLatUseMax = MathF.Max(FrontLatUseMax, state.ClassicFrontLateralGripUsage);
            RearLatUseMax = MathF.Max(RearLatUseMax, state.ClassicRearLateralGripUsage);
            FrontLongUseMax = MathF.Max(FrontLongUseMax, state.ClassicFrontLongitudinalGripUsage);
            RearLongUseMax = MathF.Max(RearLongUseMax, state.ClassicRearLongitudinalGripUsage);
            BetaMax = MathF.Max(BetaMax, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            YawMax = MathF.Max(YawMax, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
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
