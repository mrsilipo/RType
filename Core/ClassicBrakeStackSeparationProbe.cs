using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBrakeStackSeparationProbe
{
    private const float Dt = 1f / 120f;
    private const float Gravity = 9.81f;
    private const int Gear = 4;
    private const float DigitalBrakeInitialPressure = 0.40f;
    private const float DigitalBrakeFullPressureSeconds = 0.18f;

    private static readonly BrakeCase[] Cases =
    [
        new("straight-line", 120f, 1.20f, _ => new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true)),
        new("fixed-steer", 120f, 1.20f, _ => new VehicleInput(0f, 1f, 0.65f, brakeAssistEnabled: true))
    ];

    private static readonly BrakeLayer[] Layers =
    [
        new("raw-mechanical", true, 0f, 1f, 1f),
        new("regulator-only", false, 0f, 1f, 1f),
        new("full-production", false, float.NaN, float.NaN, float.NaN)
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

        Console.WriteLine($"Classic brake-stack separation probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  production physics frozen. Layers isolate raw mechanical braking, regulator-only braking, and full brake-steer production.");
        Console.WriteLine("  physical columns: layer case decelPeak resultantPeak latPeak t025/t050/t080/t100 jerkG/s reqBrake serviceReq actualFx removedReq% longThreatF/R gripF/R pressF/R regActF/R steerDeg slipF/R beta yawRate yawMomentF/R class");

        foreach (BrakeCase brakeCase in Cases)
        {
            Console.WriteLine();
            Console.WriteLine($"  case {brakeCase.Label}");
            foreach (BrakeLayer layer in Layers)
            {
                Result result = RunCase(parameters, engine, yawInertia, brakeCase, layer);
                Print(layer, brakeCase, result);
            }
        }

        PrintControllerModeA();
        Console.WriteLine("Classic brake-stack separation probe complete.");
    }

    private static Result RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        float yawInertia,
        BrakeCase brakeCase,
        BrakeLayer layer)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            DisableBrakePressureRegulatorForProbe = layer.DisableRegulator
        };
        simulator.BrakingSteeringLateralPriorityOverrideForProbe = layer.LateralPriority;
        simulator.BrakingSteeringFrontBrakeMultiplierOverrideForProbe = layer.FrontBrakeMultiplier;
        simulator.BrakingSteeringRearBrakeMultiplierOverrideForProbe = layer.RearBrakeMultiplier;
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, brakeCase.StartSpeedKmh / 3.6f);

        Accumulator acc = new(parameters, yawInertia);
        int ticks = Math.Max(1, (int)MathF.Round(brakeCase.DurationSeconds / Dt));
        for (int tick = 1; tick <= ticks; tick++)
        {
            float time = tick * Dt;
            VehicleInput input = brakeCase.Input(time);
            simulator.Update(input, Dt);
            acc.Add(time, input, simulator.State);
        }

        return acc.ToResult();
    }

    private static void Print(BrakeLayer layer, BrakeCase brakeCase, Result result)
    {
        Console.WriteLine(
            $"    {layer.Label,-15} {brakeCase.Label,-12} " +
            $"{result.PeakDecelG,8:F2} {result.PeakResultantG,13:F2} {result.PeakLateralG,7:F2} " +
            $"{FormatTime(result.TimeTo025G),5}/{FormatTime(result.TimeTo050G),5}/{FormatTime(result.TimeTo080G),5}/{FormatTime(result.TimeTo100G),5} " +
            $"{result.PeakJerkGPerSecond,7:F1} {result.PeakRequestedBrakeForceN,8:F0} {result.PeakServiceBrakeRequestN,10:F0} " +
            $"{result.PeakActualLongitudinalForceN,8:F0} {result.PeakRequestedBrakeRemovedFraction * 100f,10:F1}% " +
            $"{result.PeakFrontLongitudinalThreat,6:F2}/{result.PeakRearLongitudinalThreat,5:F2} " +
            $"{result.PeakFrontGripUsage,5:F2}/{result.PeakRearGripUsage,5:F2} " +
            $"{result.MinimumFrontPressureRatio,5:F2}/{result.MinimumRearPressureRatio,5:F2} " +
            $"{result.FrontRegulatorActiveTicks,3}/{result.RearRegulatorActiveTicks,3} " +
            $"{result.PeakRoadWheelAngleDegrees,8:F2} {result.FinalFrontSlipDegrees,6:F2}/{result.FinalRearSlipDegrees,5:F2} " +
            $"{result.PeakAbsBetaDegrees,5:F2} {result.FinalYawRateDegreesPerSecond,7:F1} " +
            $"{result.PeakFrontYawMomentNm,8:F0}/{result.PeakRearYawMomentNm,7:F0} " +
            $"{Classify(result)}");
    }

    private static void PrintControllerModeA()
    {
        Console.WriteLine();
        Console.WriteLine("  Controller Mode A digital-brake shape, matching RacingInputReader constants:");
        Console.WriteLine(
            "    initialValue={0:F2} fullRamp={1:F2}s; columns: t shapedBrake",
            DigitalBrakeInitialPressure,
            DigitalBrakeFullPressureSeconds);
        for (float time = 0f; time <= 0.70f; time += 0.05f)
        {
            Console.WriteLine($"    {time,4:F2} {CalculateModeADigitalBrake(time),10:F3}");
        }

        Console.WriteLine(
            "    timeTo25/50/75/100 = {0}/{1}/{2}/{3}",
            FormatTime(TimeToModeABrake(0.25f)),
            FormatTime(TimeToModeABrake(0.50f)),
            FormatTime(TimeToModeABrake(0.75f)),
            FormatTime(TimeToModeABrake(1.00f)));
        Console.WriteLine("    digital release to zero = immediate on button release unless an analogue trigger is still held");
    }

    private static float TimeToModeABrake(float threshold)
    {
        for (int tick = 0; tick <= 600; tick++)
        {
            float time = tick / 600f;
            if (CalculateModeADigitalBrake(time) >= threshold)
            {
                return time;
            }
        }

        return float.NaN;
    }

    private static float CalculateModeADigitalBrake(float holdSeconds)
    {
        float holdT = MathHelper.Clamp(holdSeconds / DigitalBrakeFullPressureSeconds, 0f, 1f);
        float shapedHold = holdT * holdT * (3f - 2f * holdT);
        return MathHelper.Clamp(MathHelper.Lerp(DigitalBrakeInitialPressure, 1f, shapedHold), 0f, 1f);
    }

    private static string Classify(Result result)
    {
        if (result.PeakRequestedBrakeRemovedFraction > 0.20f)
        {
            return "brake-steer-removes-conflict";
        }

        if (result.PeakFrontLongitudinalThreat < 0.85f && result.PeakRearLongitudinalThreat < 0.85f)
        {
            return "raw-brake-does-not-threaten-tyres";
        }

        if (result.FrontRegulatorActiveTicks + result.RearRegulatorActiveTicks > 20 &&
            result.MinimumFrontPressureRatio < 0.25f)
        {
            return "regulator-civilises-event";
        }

        if (result.PeakFrontGripUsage > 0.95f || result.PeakRearGripUsage > 0.95f)
        {
            return "tyre-budget-threat-present";
        }

        return "brake-stack-benign";
    }

    private static string FormatTime(float time) => float.IsFinite(time) ? time.ToString("F2") : "--";

    private static float Average(float a, float b) => (a + b) * 0.5f;

    private static float MaxAbs(float a, float b) => MathF.Max(MathF.Abs(a), MathF.Abs(b));

    private readonly record struct BrakeCase(
        string Label,
        float StartSpeedKmh,
        float DurationSeconds,
        Func<float, VehicleInput> Input);

    private readonly record struct BrakeLayer(
        string Label,
        bool DisableRegulator,
        float LateralPriority,
        float FrontBrakeMultiplier,
        float RearBrakeMultiplier);

    private readonly record struct Result(
        float PeakDecelG,
        float PeakResultantG,
        float PeakLateralG,
        float TimeTo025G,
        float TimeTo050G,
        float TimeTo080G,
        float TimeTo100G,
        float PeakJerkGPerSecond,
        float PeakRequestedBrakeForceN,
        float PeakServiceBrakeRequestN,
        float PeakActualLongitudinalForceN,
        float PeakRequestedBrakeRemovedFraction,
        float PeakFrontLongitudinalThreat,
        float PeakRearLongitudinalThreat,
        float PeakFrontGripUsage,
        float PeakRearGripUsage,
        float MinimumFrontPressureRatio,
        float MinimumRearPressureRatio,
        int FrontRegulatorActiveTicks,
        int RearRegulatorActiveTicks,
        float PeakRoadWheelAngleDegrees,
        float FinalFrontSlipDegrees,
        float FinalRearSlipDegrees,
        float PeakAbsBetaDegrees,
        float FinalYawRateDegreesPerSecond,
        float PeakFrontYawMomentNm,
        float PeakRearYawMomentNm);

    private sealed class Accumulator
    {
        private readonly VehicleSimulationParameters _parameters;
        private readonly float _yawInertia;
        private float _previousLongitudinalAccelerationG;
        private float _peakDecelG;
        private float _peakResultantG;
        private float _peakLateralG;
        private float _timeTo025G = float.NaN;
        private float _timeTo050G = float.NaN;
        private float _timeTo080G = float.NaN;
        private float _timeTo100G = float.NaN;
        private float _peakJerkGPerSecond;
        private float _peakRequestedBrakeForceN;
        private float _peakServiceBrakeRequestN;
        private float _peakActualLongitudinalForceN;
        private float _peakRemovedFraction;
        private float _peakFrontLongitudinalThreat;
        private float _peakRearLongitudinalThreat;
        private float _peakFrontGripUsage;
        private float _peakRearGripUsage;
        private float _minimumFrontPressure = 1f;
        private float _minimumRearPressure = 1f;
        private int _frontRegulatorActiveTicks;
        private int _rearRegulatorActiveTicks;
        private float _peakRoadWheelAngleDegrees;
        private float _finalFrontSlipDegrees;
        private float _finalRearSlipDegrees;
        private float _peakAbsBetaDegrees;
        private float _finalYawRateDegreesPerSecond;
        private float _peakFrontYawMomentNm;
        private float _peakRearYawMomentNm;

        public Accumulator(VehicleSimulationParameters parameters, float yawInertia)
        {
            _parameters = parameters;
            _yawInertia = yawInertia;
        }

        public void Add(float time, VehicleInput input, VehicleState state)
        {
            float decelG = MathF.Max(0f, -state.LongitudinalAcceleration / Gravity);
            float lateralG = MathF.Abs(state.LateralAcceleration) / Gravity;
            float resultantG = MathF.Sqrt(
                state.LongitudinalAcceleration * state.LongitudinalAcceleration +
                state.LateralAcceleration * state.LateralAcceleration) / Gravity;
            _peakDecelG = MathF.Max(_peakDecelG, decelG);
            _peakLateralG = MathF.Max(_peakLateralG, lateralG);
            _peakResultantG = MathF.Max(_peakResultantG, resultantG);

            if (!float.IsFinite(_timeTo025G) && decelG >= 0.25f) _timeTo025G = time;
            if (!float.IsFinite(_timeTo050G) && decelG >= 0.50f) _timeTo050G = time;
            if (!float.IsFinite(_timeTo080G) && decelG >= 0.80f) _timeTo080G = time;
            if (!float.IsFinite(_timeTo100G) && decelG >= 1.00f) _timeTo100G = time;

            float longitudinalAccelerationG = state.LongitudinalAcceleration / Gravity;
            _peakJerkGPerSecond = MathF.Max(
                _peakJerkGPerSecond,
                MathF.Abs((longitudinalAccelerationG - _previousLongitudinalAccelerationG) / Dt));
            _previousLongitudinalAccelerationG = longitudinalAccelerationG;

            float requestedBrakeForce = input.Brake * MathF.Max(0f, _parameters.MaxBrakeForceN);
            float serviceBrakeRequest = MathF.Abs(state.ClassicServiceBrakeForceRequestN);
            float actualLongitudinalForce = MathF.Abs(
                state.FrontLeftLongitudinalForceN +
                state.FrontRightLongitudinalForceN +
                state.RearLeftLongitudinalForceN +
                state.RearRightLongitudinalForceN);
            _peakRequestedBrakeForceN = MathF.Max(_peakRequestedBrakeForceN, requestedBrakeForce);
            _peakServiceBrakeRequestN = MathF.Max(_peakServiceBrakeRequestN, serviceBrakeRequest);
            _peakActualLongitudinalForceN = MathF.Max(_peakActualLongitudinalForceN, actualLongitudinalForce);
            if (requestedBrakeForce > 1f)
            {
                _peakRemovedFraction = MathF.Max(
                    _peakRemovedFraction,
                    MathHelper.Clamp((requestedBrakeForce - serviceBrakeRequest) / requestedBrakeForce, 0f, 1f));
            }

            _peakFrontLongitudinalThreat = MathF.Max(
                _peakFrontLongitudinalThreat,
                MathF.Max(
                    MathF.Abs(state.FrontLeftRequestedLongitudinalForceN) / MathF.Max(1f, state.FrontLeftFrictionEllipseGripBudgetN),
                    MathF.Abs(state.FrontRightRequestedLongitudinalForceN) / MathF.Max(1f, state.FrontRightFrictionEllipseGripBudgetN)));
            _peakRearLongitudinalThreat = MathF.Max(
                _peakRearLongitudinalThreat,
                MathF.Max(
                    MathF.Abs(state.RearLeftRequestedLongitudinalForceN) / MathF.Max(1f, state.RearLeftFrictionEllipseGripBudgetN),
                    MathF.Abs(state.RearRightRequestedLongitudinalForceN) / MathF.Max(1f, state.RearRightFrictionEllipseGripBudgetN)));
            _peakFrontGripUsage = MathF.Max(_peakFrontGripUsage, MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage));
            _peakRearGripUsage = MathF.Max(_peakRearGripUsage, MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage));

            float frontPressure = Average(state.FrontLeftBrakePressureRatio, state.FrontRightBrakePressureRatio);
            float rearPressure = Average(state.RearLeftBrakePressureRatio, state.RearRightBrakePressureRatio);
            _minimumFrontPressure = MathF.Min(_minimumFrontPressure, frontPressure);
            _minimumRearPressure = MathF.Min(_minimumRearPressure, rearPressure);
            if (state.FrontLeftBrakePressureRegulatorActive || state.FrontRightBrakePressureRegulatorActive)
            {
                _frontRegulatorActiveTicks++;
            }

            if (state.RearLeftBrakePressureRegulatorActive || state.RearRightBrakePressureRegulatorActive)
            {
                _rearRegulatorActiveTicks++;
            }

            float roadWheelAngle = Average(state.FrontLeftSteerAngleDegrees, state.FrontRightSteerAngleDegrees);
            _peakRoadWheelAngleDegrees = MathF.Max(_peakRoadWheelAngleDegrees, MathF.Abs(roadWheelAngle));
            _finalFrontSlipDegrees = Average(state.FrontLeftSlipAngleDegrees, state.FrontRightSlipAngleDegrees);
            _finalRearSlipDegrees = Average(state.RearLeftSlipAngleDegrees, state.RearRightSlipAngleDegrees);
            _peakAbsBetaDegrees = MathF.Max(_peakAbsBetaDegrees, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            _finalYawRateDegreesPerSecond = MathHelper.ToDegrees(state.YawRateRadiansPerSecond);
            _peakFrontYawMomentNm = MathF.Max(
                _peakFrontYawMomentNm,
                MathF.Abs(MathHelper.ToRadians(state.ClassicFrontYawAccelerationDegreesPerSecondSquared) * _yawInertia));
            _peakRearYawMomentNm = MathF.Max(
                _peakRearYawMomentNm,
                MathF.Abs(MathHelper.ToRadians(state.ClassicRearYawAccelerationDegreesPerSecondSquared) * _yawInertia));
        }

        public Result ToResult()
        {
            return new Result(
                _peakDecelG,
                _peakResultantG,
                _peakLateralG,
                _timeTo025G,
                _timeTo050G,
                _timeTo080G,
                _timeTo100G,
                _peakJerkGPerSecond,
                _peakRequestedBrakeForceN,
                _peakServiceBrakeRequestN,
                _peakActualLongitudinalForceN,
                _peakRemovedFraction,
                _peakFrontLongitudinalThreat,
                _peakRearLongitudinalThreat,
                _peakFrontGripUsage,
                _peakRearGripUsage,
                _minimumFrontPressure,
                _minimumRearPressure,
                _frontRegulatorActiveTicks,
                _rearRegulatorActiveTicks,
                _peakRoadWheelAngleDegrees,
                _finalFrontSlipDegrees,
                _finalRearSlipDegrees,
                _peakAbsBetaDegrees,
                _finalYawRateDegreesPerSecond,
                _peakFrontYawMomentNm,
                _peakRearYawMomentNm);
        }
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position) => new("ROAD", 1f);
    }
}
