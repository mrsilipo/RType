using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicTyreResponseProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int MeasurementTicks = 120;
    private const int Gear = 4;

    private static readonly float[] StaticSlipDegrees =
    [
        0f, 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f, 12f, 15f, 20f, 25f
    ];

    private static readonly ClassicFourWheelAssistOptions CleanupOff = new()
    {
        BodySlipDampingEnabled = false,
        LateralVelocityDampingEnabled = false,
        RearFollowEnabled = false,
        YawRecoveryEnabled = false,
        SpeedRetentionEnabled = false
    };

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(
            parameters,
            engineParameters.ClassicFourWheel);

        Console.WriteLine($"Classic tyre response probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, surface=ROAD");
        Console.WriteLine("  signed convention: positive slip requests positive wheel-frame lateral force; force should oppose the slip-generating contact motion once projected onto the tyre contact velocity.");
        Console.WriteLine(
            $"  resolved front tyre: stiffness={tyres.Front.CorneringStiffness:F2} shape, peak={tyres.Front.PeakSlipAngleDegrees:F1}deg, falloff={tyres.Front.FalloffSlipAngleDegrees:F1}deg, maxGrip={tyres.Front.MaxGrip:F2}, slidingGrip={tyres.Front.SlidingGrip:F2}");
        Console.WriteLine(
            $"  resolved rear tyre:  stiffness={tyres.Rear.CorneringStiffness:F2} shape, peak={tyres.Rear.PeakSlipAngleDegrees:F1}deg, falloff={tyres.Rear.FalloffSlipAngleDegrees:F1}deg, maxGrip={tyres.Rear.MaxGrip:F2}, slidingGrip={tyres.Rear.SlidingGrip:F2}");
        Console.WriteLine("  classicFourWheel applies tyre LoadSensitivity to the grip limit before lateral force and combined-grip clamping.");

        PrintStaticCurves(parameters, tyres);
        RunDynamicCase(parameters, engineParameters, tyres, "medium", 0.35f);
        RunDynamicCase(parameters, engineParameters, tyres, "hard", 0.65f);
        Console.WriteLine("Classic tyre response probe complete.");
    }

    private static void PrintStaticCurves(VehicleSimulationParameters parameters, ClassicFourWheelTyres tyres)
    {
        float frontStaticLoad = parameters.MassKg * 9.81f * parameters.FrontWeightDistribution * 0.5f;
        float rearStaticLoad = parameters.MassKg * 9.81f * (1f - parameters.FrontWeightDistribution) * 0.5f;
        float frontRepresentativeHighLoad = frontStaticLoad * 1.45f;
        float rearRepresentativeHighLoad = rearStaticLoad * 1.45f;

        PrintStaticCurve("front static", tyres.Front, frontStaticLoad);
        PrintStaticCurve("front high-load", tyres.Front, frontRepresentativeHighLoad);
        PrintStaticCurve("rear static", tyres.Rear, rearStaticLoad);
        PrintStaticCurve("rear high-load", tyres.Rear, rearRepresentativeHighLoad);
    }

    private static void PrintStaticCurve(string label, ClassicBicycleTyreParameters tyre, float loadN)
    {
        float maxForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(loadN, 1f, tyre);
        Console.WriteLine($"  static curve {label}: load={loadN:F0}N maxForce={maxForce:F0}N");
        Console.WriteLine("    slip rawFy scaledFy usage muEff stage multiplier restoring?");
        foreach (float slipDegrees in StaticSlipDegrees)
        {
            CurvePoint point = SampleCurve(tyre, loadN, slipDegrees);
            Console.WriteLine(
                $"    {slipDegrees,5:F1} {point.RawForceN,7:F0} {point.ScaledForceN,8:F0} " +
                $"{point.GripUsage,5:F2} {point.EffectiveMu,5:F2} {point.Stage,-8} {point.Multiplier,8:F3} {point.Restoring}");
        }
    }

    private static void RunDynamicCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        ClassicFourWheelTyres tyres,
        string label,
        float steerInput)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters)
        {
            AssistOptions = CleanupOff
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);

        Console.WriteLine($"  dynamic {label} steerInput={steerInput:F2}");
        Console.WriteLine("    t wheel speed steer slip load rawFy expPost actualWheelFy bodyRight grip stage dir latVelPower");

        WheelEventTracker fl = new("FL");
        WheelEventTracker fr = new("FR");
        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            VehicleState state = simulator.State;
            DynamicWheelSample left = BuildDynamicSample(tick * Dt, "FL", tyres.Front, state.FrontLeftSteerAngleDegrees, state.FrontLeftSlipAngleDegrees, state.FrontLeftLoadN, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN, state.FrontLeftGripUsage, state.FrontLeftLocalLateralSpeedMetersPerSecond, engineParameters, state.SpeedMetersPerSecond);
            DynamicWheelSample right = BuildDynamicSample(tick * Dt, "FR", tyres.Front, state.FrontRightSteerAngleDegrees, state.FrontRightSlipAngleDegrees, state.FrontRightLoadN, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN, state.FrontRightGripUsage, state.FrontRightLocalLateralSpeedMetersPerSecond, engineParameters, state.SpeedMetersPerSecond);

            fl.Observe(left);
            fr.Observe(right);

            if (tick is 12 or 30 or 60 or 120)
            {
                PrintDynamicSample(left);
                PrintDynamicSample(right);
            }
        }

        PrintWheelEvents(fl);
        PrintWheelEvents(fr);
    }

    private static DynamicWheelSample BuildDynamicSample(
        float timeSeconds,
        string wheel,
        ClassicBicycleTyreParameters tyre,
        float steerAngleDegrees,
        float slipAngleDegrees,
        float loadN,
        float bodyForwardForce,
        float bodyRightForce,
        float actualGripUsage,
        float localLateralSpeed,
        SimulationEngineParameters engineParameters,
        float speedMetersPerSecond)
    {
        float steerRadians = MathHelper.ToRadians(steerAngleDegrees);
        float sin = MathF.Sin(steerRadians);
        float cos = MathF.Cos(steerRadians);
        float actualWheelLongitudinal = bodyForwardForce * cos + bodyRightForce * sin;
        float actualWheelLateral = bodyRightForce * cos - bodyForwardForce * sin;
        CurvePoint curve = SampleCurve(tyre, loadN, slipAngleDegrees);
        float expectedLongitudinal = actualWheelLongitudinal;
        float expectedLateral = curve.RawForceN;
        float expectedGripUsage = ClampCombinedForce(
            ref expectedLongitudinal,
            ref expectedLateral,
            ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(loadN, 1f, tyre),
            engineParameters.ClassicFourWheel.GripBudget.CombinedGripExponent);
        string direction = ClassifyDirection(slipAngleDegrees, curve.RawForceN, actualWheelLateral, localLateralSpeed);

        return new DynamicWheelSample(
            timeSeconds,
            wheel,
            speedMetersPerSecond * 3.6f,
            steerAngleDegrees,
            slipAngleDegrees,
            loadN,
            curve.RawForceN,
            expectedLateral,
            actualWheelLateral,
            bodyRightForce,
            actualGripUsage,
            expectedGripUsage,
            curve.Stage,
            direction,
            actualWheelLateral * localLateralSpeed);
    }

    private static CurvePoint SampleCurve(ClassicBicycleTyreParameters tyre, float loadN, float slipAngleDegrees)
    {
        float maxForce = ClassicFourWheelVehicleSimulator.CalculateClassicTyreGripLimit(loadN, 1f, tyre);
        float slipRadians = MathHelper.ToRadians(slipAngleDegrees);
        float rawForce = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(slipRadians, maxForce, tyre);
        float absSlip = MathF.Abs(slipAngleDegrees);
        float peakSlip = MathF.Max(0.1f, tyre.PeakSlipAngleDegrees);
        float falloffSlip = MathF.Max(peakSlip + 0.1f, tyre.FalloffSlipAngleDegrees);
        string stage;
        float multiplier;
        if (absSlip <= 0.0001f)
        {
            stage = "zero";
            multiplier = 0f;
        }
        else if (absSlip <= peakSlip)
        {
            float t = absSlip / peakSlip;
            float stiffnessShape = MathHelper.Clamp(tyre.CorneringStiffness / 7.5f, 0.45f, 2.25f);
            multiplier = 1f - MathF.Pow(1f - SmoothStep01(t), stiffnessShape);
            stage = "linear";
        }
        else if (absSlip <= falloffSlip)
        {
            float t = SmoothStep01((absSlip - peakSlip) / (falloffSlip - peakSlip));
            multiplier = MathHelper.Lerp(1f, MathHelper.Clamp(tyre.SlidingGrip, 0f, 1.2f), t);
            stage = "falloff";
        }
        else
        {
            multiplier = MathHelper.Clamp(tyre.SlidingGrip, 0f, 1.2f);
            stage = "slide";
        }

        float gripUsage = maxForce > 1f ? MathF.Abs(rawForce) / maxForce : 0f;
        float effectiveMu = loadN > 1f ? rawForce / loadN : 0f;
        bool restoring = MathF.Abs(slipAngleDegrees) < 0.001f || MathF.Sign(rawForce) == MathF.Sign(slipAngleDegrees);
        return new CurvePoint(rawForce, rawForce, gripUsage, effectiveMu, stage, multiplier, restoring ? "yes" : "no");
    }

    private static string ClassifyDirection(
        float slipAngleDegrees,
        float rawWheelLateralForce,
        float actualWheelLateralForce,
        float localLateralSpeed)
    {
        if (MathF.Abs(slipAngleDegrees) < 0.01f)
        {
            return "neutral";
        }

        bool forceFollowsSlip = MathF.Sign(rawWheelLateralForce) == MathF.Sign(slipAngleDegrees);
        bool opposesContactVelocity = MathF.Abs(localLateralSpeed) < 0.01f ||
            MathF.Sign(actualWheelLateralForce) != MathF.Sign(localLateralSpeed);
        return $"{(forceFollowsSlip ? "slip-ok" : "slip-bad")}/{(opposesContactVelocity ? "vel-ok" : "vel-add")}";
    }

    private static void PrintDynamicSample(DynamicWheelSample sample)
    {
        Console.WriteLine(
            $"    {sample.TimeSeconds,4:F2} {sample.Wheel,-2} {sample.SpeedKmh,6:F1} {sample.SteerAngleDegrees,5:F1} " +
            $"{sample.SlipAngleDegrees,6:F1} {sample.LoadN,5:F0} {sample.RawForceN,7:F0} {sample.ExpectedPostClampForceN,7:F0} " +
            $"{sample.ActualWheelLateralForceN,7:F0} {sample.BodyRightForceN,8:F0} {sample.ActualGripUsage,4:F2}/{sample.ExpectedGripUsage,4:F2} " +
            $"{sample.Stage,-7} {sample.Direction,-15} {sample.LateralVelocityPowerW,9:F0}W");
    }

    private static void PrintWheelEvents(WheelEventTracker tracker)
    {
        Console.WriteLine(
            $"    {tracker.Wheel} events: peak={FormatEvent(tracker.Peak)} fallStart={FormatEvent(tracker.FallStart)} " +
            $"zeroOrReverse={FormatEvent(tracker.ZeroOrReverse)} signMismatch={FormatEvent(tracker.SignMismatch)}");
    }

    private static string FormatEvent(DynamicWheelSample? sample)
    {
        if (sample is null)
        {
            return "none";
        }

        DynamicWheelSample s = sample.Value;
        return $"t{s.TimeSeconds:F3}s slip{s.SlipAngleDegrees:F1} Fy{s.ActualWheelLateralForceN:F0}N";
    }

    private static float ClampCombinedForce(ref float longitudinal, ref float lateral, float maxForce, float exponent)
    {
        maxForce = MathF.Max(1f, maxForce);
        exponent = MathHelper.Clamp(exponent, 1.2f, 4f);
        float demand =
            MathF.Pow(MathF.Abs(longitudinal / maxForce), exponent) +
            MathF.Pow(MathF.Abs(lateral / maxForce), exponent);
        if (demand <= 1f)
        {
            return demand;
        }

        float scale = MathF.Pow(demand, -1f / exponent);
        longitudinal *= scale;
        lateral *= scale;
        return 1f;
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private sealed class WheelEventTracker(string wheel)
    {
        private float _previousSlipSign;
        private readonly List<DynamicWheelSample> _samples = [];

        public string Wheel { get; } = wheel;

        public DynamicWheelSample? Peak => _samples.Count == 0
            ? null
            : _samples.MaxBy(sample => MathF.Abs(sample.ActualWheelLateralForceN));

        public DynamicWheelSample? FallStart
        {
            get
            {
                DynamicWheelSample? peak = Peak;
                if (peak is null)
                {
                    return null;
                }

                float peakTime = peak.Value.TimeSeconds;
                float peakForce = MathF.Abs(peak.Value.ActualWheelLateralForceN);
                foreach (DynamicWheelSample sample in _samples)
                {
                    if (sample.TimeSeconds > peakTime &&
                        MathF.Abs(sample.SlipAngleDegrees) > MathF.Abs(peak.Value.SlipAngleDegrees) + 0.1f &&
                        MathF.Abs(sample.ActualWheelLateralForceN) < peakForce - 25f)
                    {
                        return sample;
                    }
                }

                return null;
            }
        }

        public DynamicWheelSample? ZeroOrReverse { get; private set; }

        public DynamicWheelSample? SignMismatch { get; private set; }

        public void Observe(DynamicWheelSample sample)
        {
            _samples.Add(sample);

            float slipSign = MathF.Sign(sample.SlipAngleDegrees);
            float forceSign = MathF.Sign(sample.ActualWheelLateralForceN);
            if (SignMismatch is null &&
                MathF.Abs(sample.SlipAngleDegrees) > 0.1f &&
                forceSign != 0f &&
                forceSign != slipSign)
            {
                SignMismatch = sample;
            }

            if (ZeroOrReverse is null &&
                _previousSlipSign != 0f &&
                slipSign != 0f &&
                slipSign != _previousSlipSign)
            {
                ZeroOrReverse = sample;
            }

            if (slipSign != 0f)
            {
                _previousSlipSign = slipSign;
            }
        }
    }

    private readonly record struct CurvePoint(
        float RawForceN,
        float ScaledForceN,
        float GripUsage,
        float EffectiveMu,
        string Stage,
        float Multiplier,
        string Restoring);

    private readonly record struct DynamicWheelSample(
        float TimeSeconds,
        string Wheel,
        float SpeedKmh,
        float SteerAngleDegrees,
        float SlipAngleDegrees,
        float LoadN,
        float RawForceN,
        float ExpectedPostClampForceN,
        float ActualWheelLateralForceN,
        float BodyRightForceN,
        float ActualGripUsage,
        float ExpectedGripUsage,
        string Stage,
        string Direction,
        float LateralVelocityPowerW);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
