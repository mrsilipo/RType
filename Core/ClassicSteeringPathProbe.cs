using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSteeringPathProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int MeasurementTicks = 120;
    private const int Gear = 4;
    private const float UnrestrictedCapDegrees = 32f;
    private const float InstantSteerRateDegreesPerSecond = 5000f;

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

        Console.WriteLine($"Classic steering path probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, surface=ROAD");
        PrintSpeedCapCurve(engineParameters.ClassicFourWheel.Steering);
        RunComparison(parameters, engineParameters, "medium", 0.35f);
        RunComparison(parameters, engineParameters, "hard", 0.65f);
        Console.WriteLine("Classic steering path probe complete.");
    }

    private static void RunComparison(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters baselineEngineParameters,
        string label,
        float steerInput)
    {
        SimulationEngineParameters current = CloneWithSteering(
            baselineEngineParameters,
            baselineEngineParameters.ClassicFourWheel.Steering);
        SimulationEngineParameters unrestrictedCap = CloneWithSteering(
            baselineEngineParameters,
            CloneSteering(
                baselineEngineParameters.ClassicFourWheel.Steering,
                zeroKmh: UnrestrictedCapDegrees,
                sixtyKmh: UnrestrictedCapDegrees,
                oneTwentyKmh: UnrestrictedCapDegrees,
                twoHundredKmh: UnrestrictedCapDegrees));
        SimulationEngineParameters instantRamp = CloneWithSteering(
            baselineEngineParameters,
            CloneSteering(
                baselineEngineParameters.ClassicFourWheel.Steering,
                steerSpeed: InstantSteerRateDegreesPerSecond));

        Console.WriteLine($"  {label} rawInput={steerInput:F2}");
        ProbeResult currentResult = RunCase(parameters, current, "current cap + current ramp", steerInput);
        ProbeResult unrestrictedResult = RunCase(parameters, unrestrictedCap, "unrestricted cap + current ramp", steerInput);
        ProbeResult instantResult = RunCase(parameters, instantRamp, "current cap + instant ramp", steerInput);

        PrintResult(currentResult);
        PrintResult(unrestrictedResult);
        PrintResult(instantResult);
        PrintComparison(currentResult, unrestrictedResult, instantResult);
    }

    private static ProbeResult RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
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

        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        List<SteeringSample> checkpoints = [];
        SteeringSample? frontSlipZeroCrossing = null;
        float previousAverageFrontSlip = 0f;
        float previousActualSteer = 0f;

        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            SteeringSample sample = BuildSample(
                tick * Dt,
                simulator.State,
                engineParameters,
                steerInput,
                previousActualSteer);
            float averageFrontSlip = sample.FrontSlipDegrees;

            if (frontSlipZeroCrossing is null &&
                tick > 1 &&
                MathF.Sign(previousAverageFrontSlip) != 0f &&
                MathF.Sign(averageFrontSlip) != 0f &&
                MathF.Sign(previousAverageFrontSlip) != MathF.Sign(averageFrontSlip))
            {
                frontSlipZeroCrossing = sample;
            }

            if (tick is 12 or 30 or 60 or 120)
            {
                checkpoints.Add(sample);
            }

            previousAverageFrontSlip = averageFrontSlip;
            previousActualSteer = sample.ActualSteerAngleDegrees;
        }

        return new ProbeResult(
            label,
            engineParameters.ClassicFourWheel.Steering.SteerSpeedDegreesPerSecond,
            simulator.State.SteeringSpeedMatchedMaxAngleDegrees,
            startSpeedKmh - simulator.State.SpeedMetersPerSecond * 3.6f,
            MathHelper.ToDegrees(simulator.State.YawRateRadiansPerSecond),
            simulator.State.ClassicBodySlipAngleDegrees,
            MathF.Max(simulator.State.RearLeftGripUsage, simulator.State.RearRightGripUsage),
            frontSlipZeroCrossing,
            checkpoints);
    }

    private static SteeringSample BuildSample(
        float timeSeconds,
        VehicleState state,
        SimulationEngineParameters engineParameters,
        float rawSteerInput,
        float previousActualSteerDegrees)
    {
        float speedKmh = state.SpeedMetersPerSecond * 3.6f;
        float speedCap = CalculateMaxSteerAngleDegrees(engineParameters.ClassicFourWheel.Steering, speedKmh);
        float targetAfterCap = MathHelper.Clamp(rawSteerInput, -1f, 1f) * speedCap;
        float targetUncapped = MathHelper.Clamp(rawSteerInput, -1f, 1f) * UnrestrictedCapDegrees;
        float actualSteer = (state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f;
        float rampedInput = speedCap > 0.001f ? actualSteer / speedCap : 0f;
        float rampRate = (actualSteer - previousActualSteerDegrees) / Dt;
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float frontLocalVelocityAngle = CalculateFrontLocalVelocityAngleDegrees(state);
        float requiredForOneDegree = frontLocalVelocityAngle + 1f;
        float requiredForTwoDegrees = frontLocalVelocityAngle + 2f;
        float rearGrip = MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);

        return new SteeringSample(
            timeSeconds,
            speedKmh,
            rawSteerInput,
            rampedInput,
            engineParameters.ClassicFourWheel.Steering.SteerSpeedDegreesPerSecond,
            rampRate,
            speedCap,
            targetUncapped,
            targetAfterCap,
            actualSteer,
            actualSteer,
            frontSlip,
            state.ClassicBodySlipAngleDegrees,
            frontLocalVelocityAngle,
            requiredForOneDegree,
            requiredForTwoDegrees,
            MathHelper.ToDegrees(state.YawRateRadiansPerSecond),
            rearGrip,
            state.SpeedMetersPerSecond * 3.6f);
    }

    private static float CalculateFrontLocalVelocityAngleDegrees(VehicleState state)
    {
        float frontForward = (state.FrontLeftLocalForwardSpeedMetersPerSecond + state.FrontRightLocalForwardSpeedMetersPerSecond) * 0.5f;
        float frontLateral = (state.FrontLeftLocalLateralSpeedMetersPerSecond + state.FrontRightLocalLateralSpeedMetersPerSecond) * 0.5f;
        return MathHelper.ToDegrees(MathF.Atan2(frontLateral, MathF.Max(2f, MathF.Abs(frontForward))));
    }

    private static void PrintSpeedCapCurve(ClassicBicycleSteeringParameters steering)
    {
        float cap150 = CalculateMaxSteerAngleDegrees(steering, EntrySpeedKmh);
        Console.WriteLine(
            $"  speed cap curve: 0={steering.ZeroKmhAngleDegrees:F1}deg 60={steering.SixtyKmhAngleDegrees:F1}deg " +
            $"120={steering.OneTwentyKmhAngleDegrees:F1}deg 200={steering.TwoHundredKmhAngleDegrees:F1}deg; " +
            $"cap@150={cap150:F2}deg, steerSpeed={steering.SteerSpeedDegreesPerSecond:F1}deg/s");
        Console.WriteLine(
            $"  cap@150 interpolation: t=smoothstep((150-120)/80)={SmoothStep01((EntrySpeedKmh - 120f) / 80f):F3}; " +
            $"lerp({steering.OneTwentyKmhAngleDegrees:F1}, {steering.TwoHundredKmhAngleDegrees:F1})={cap150:F2}deg");
    }

    private static void PrintResult(ProbeResult result)
    {
        Console.WriteLine($"    case: {result.Label}");
        Console.WriteLine("      t speed raw ramped rate cfgRate cap reqNoCap reqCap actual roadAngle slipF beta velAng need+1/+2 yaw rearGrip");
        foreach (SteeringSample sample in result.Checkpoints)
        {
            PrintSample("      ", sample);
        }

        if (result.FrontSlipZeroCrossing is null)
        {
            Console.WriteLine("      front-slip zero crossing: none in first 1.00s");
        }
        else
        {
            SteeringSample zero = result.FrontSlipZeroCrossing.Value;
            Console.WriteLine(
                $"      front-slip zero crossing: t={zero.TimeSeconds:F3}s actual={zero.ActualSteerAngleDegrees:F2}deg " +
                $"beta={zero.BodySlipDegrees:F2}deg frontVelAngle={zero.FrontLocalVelocityAngleDegrees:F2}deg " +
                $"needed(+1/+2)={zero.RequiredSteerForOneDegreeSlip:F2}/{zero.RequiredSteerForTwoDegreesSlip:F2}deg " +
                $"cap={zero.SpeedCapDegrees:F2}deg");
        }

        Console.WriteLine(
            $"      summary: speedDrop={result.SpeedDropKmh:F1}km/h yaw@1s={result.YawRateAtOneSecondDegreesPerSecond:F1}deg/s " +
            $"beta@1s={result.BodySlipAtOneSecondDegrees:F1}deg rearGrip@1s={result.RearGripAtOneSecond:F2}");
    }

    private static void PrintSample(string indent, SteeringSample sample)
    {
        Console.WriteLine(
            $"{indent}{sample.TimeSeconds,4:F2} {sample.SpeedKmh,6:F1} {sample.RawInput,4:F2} {sample.RampedInput,6:F2} " +
            $"{sample.RampRateDegreesPerSecond,6:F0} {sample.ConfiguredRampRateDegreesPerSecond,7:F0} {sample.SpeedCapDegrees,5:F2} " +
            $"{sample.RequestedBeforeSpeedCapDegrees,8:F2} {sample.RequestedAfterSpeedCapDegrees,6:F2} {sample.ActualSteerAngleDegrees,6:F2} " +
            $"{sample.FrontWheelRoadAngleDegrees,8:F2} {sample.FrontSlipDegrees,6:F2} {sample.BodySlipDegrees,5:F2} " +
            $"{sample.FrontLocalVelocityAngleDegrees,6:F2} {sample.RequiredSteerForOneDegreeSlip,6:F2}/{sample.RequiredSteerForTwoDegreesSlip,5:F2} " +
            $"{sample.YawRateDegreesPerSecond,6:F1} {sample.RearGripUsage,5:F2}");
    }

    private static void PrintComparison(
        ProbeResult current,
        ProbeResult unrestrictedCap,
        ProbeResult instantRamp)
    {
        Console.WriteLine(
            $"    comparison: zeroCross current={FormatZeroCross(current)} unrestrictedCap={FormatZeroCross(unrestrictedCap)} instantRamp={FormatZeroCross(instantRamp)}");
        Console.WriteLine(
            $"    comparison: speedDrop current={current.SpeedDropKmh:F1} unrestrictedCap={unrestrictedCap.SpeedDropKmh:F1} instantRamp={instantRamp.SpeedDropKmh:F1}km/h; " +
            $"beta@1s current={current.BodySlipAtOneSecondDegrees:F1} unrestrictedCap={unrestrictedCap.BodySlipAtOneSecondDegrees:F1} instantRamp={instantRamp.BodySlipAtOneSecondDegrees:F1}deg");
        Console.WriteLine($"    classification: {Classify(current, unrestrictedCap, instantRamp)}");
    }

    private static string FormatZeroCross(ProbeResult result)
    {
        return result.FrontSlipZeroCrossing is null
            ? "none"
            : $"{result.FrontSlipZeroCrossing.Value.TimeSeconds:F3}s";
    }

    private static string Classify(ProbeResult current, ProbeResult unrestrictedCap, ProbeResult instantRamp)
    {
        bool unrestrictedPreventsZero = current.FrontSlipZeroCrossing is not null && unrestrictedCap.FrontSlipZeroCrossing is null;
        bool instantPreventsZero = current.FrontSlipZeroCrossing is not null && instantRamp.FrontSlipZeroCrossing is null;
        bool unrestrictedDelaysZero = DelaysZeroCross(current, unrestrictedCap, 0.08f);
        bool instantDelaysZero = DelaysZeroCross(current, instantRamp, 0.08f);

        if ((unrestrictedPreventsZero || unrestrictedDelaysZero) && !(instantPreventsZero || instantDelaysZero))
        {
            return "speed-sensitive cap is the primary front-authority limiter";
        }

        if ((instantPreventsZero || instantDelaysZero) && !(unrestrictedPreventsZero || unrestrictedDelaysZero))
        {
            return "digital steering ramp is the primary front-authority limiter";
        }

        if ((unrestrictedPreventsZero || unrestrictedDelaysZero) && (instantPreventsZero || instantDelaysZero))
        {
            return "both cap and ramp materially reduce front authority";
        }

        return "front slip reversal persists despite isolated cap/ramp counterfactuals; underlying vehicle state overwhelms available steering";
    }

    private static bool DelaysZeroCross(ProbeResult baseline, ProbeResult counterfactual, float minimumDelaySeconds)
    {
        if (baseline.FrontSlipZeroCrossing is null)
        {
            return false;
        }

        if (counterfactual.FrontSlipZeroCrossing is null)
        {
            return true;
        }

        return counterfactual.FrontSlipZeroCrossing.Value.TimeSeconds -
            baseline.FrontSlipZeroCrossing.Value.TimeSeconds >= minimumDelaySeconds;
    }

    private static float CalculateMaxSteerAngleDegrees(ClassicBicycleSteeringParameters steering, float speedKmh)
    {
        if (speedKmh <= 60f)
        {
            return MathHelper.Lerp(steering.ZeroKmhAngleDegrees, steering.SixtyKmhAngleDegrees, SmoothStep01(speedKmh / 60f));
        }

        if (speedKmh <= 120f)
        {
            return MathHelper.Lerp(steering.SixtyKmhAngleDegrees, steering.OneTwentyKmhAngleDegrees, SmoothStep01((speedKmh - 60f) / 60f));
        }

        return MathHelper.Lerp(steering.OneTwentyKmhAngleDegrees, steering.TwoHundredKmhAngleDegrees, SmoothStep01((speedKmh - 120f) / 80f));
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static SimulationEngineParameters CloneWithSteering(
        SimulationEngineParameters source,
        ClassicBicycleSteeringParameters steering)
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
                Steering = steering,
                FrontTyres = source.ClassicFourWheel.FrontTyres,
                RearTyres = source.ClassicFourWheel.RearTyres,
                Yaw = source.ClassicFourWheel.Yaw,
                GripBudget = source.ClassicFourWheel.GripBudget,
                ChassisLoadTransfer = source.ClassicFourWheel.ChassisLoadTransfer,
                LowSpeed = source.ClassicFourWheel.LowSpeed,
                Resistance = source.ClassicFourWheel.Resistance
            }
        };
    }

    private static ClassicBicycleSteeringParameters CloneSteering(
        ClassicBicycleSteeringParameters source,
        float? zeroKmh = null,
        float? sixtyKmh = null,
        float? oneTwentyKmh = null,
        float? twoHundredKmh = null,
        float? steerSpeed = null)
    {
        return new ClassicBicycleSteeringParameters
        {
            ZeroKmhAngleDegrees = zeroKmh ?? source.ZeroKmhAngleDegrees,
            SixtyKmhAngleDegrees = sixtyKmh ?? source.SixtyKmhAngleDegrees,
            OneTwentyKmhAngleDegrees = oneTwentyKmh ?? source.OneTwentyKmhAngleDegrees,
            TwoHundredKmhAngleDegrees = twoHundredKmh ?? source.TwoHundredKmhAngleDegrees,
            SteerSpeedDegreesPerSecond = steerSpeed ?? source.SteerSpeedDegreesPerSecond,
            ReturnSpeedDegreesPerSecond = source.ReturnSpeedDegreesPerSecond,
            PhysicalEnvelopeBlendStartKmh = source.PhysicalEnvelopeBlendStartKmh,
            PhysicalEnvelopeFullKmh = source.PhysicalEnvelopeFullKmh,
            NormalLateralAccelerationG = source.NormalLateralAccelerationG,
            OverdriveLateralAccelerationG = source.OverdriveLateralAccelerationG,
            NormalCommand = source.NormalCommand,
            MinimumHighSpeedAngleDegrees = source.MinimumHighSpeedAngleDegrees,
            NormalPeakSlipFraction = source.NormalPeakSlipFraction,
            OverdrivePeakSlipFraction = source.OverdrivePeakSlipFraction,
            TransientPeakSlipFraction = source.TransientPeakSlipFraction,
            TransientBoostSeconds = source.TransientBoostSeconds,
            DigitalInitialCommandRatePerSecond = source.DigitalInitialCommandRatePerSecond,
            DigitalSustainedCommandRatePerSecond = source.DigitalSustainedCommandRatePerSecond,
            DigitalRiseAccelerationSeconds = source.DigitalRiseAccelerationSeconds,
            DigitalReleaseCommandRatePerSecond = source.DigitalReleaseCommandRatePerSecond,
            DigitalCounterSteerRateMultiplier = source.DigitalCounterSteerRateMultiplier
        };
    }

    private readonly record struct ProbeResult(
        string Label,
        float SteeringRateDegreesPerSecond,
        float FinalSpeedCapDegrees,
        float SpeedDropKmh,
        float YawRateAtOneSecondDegreesPerSecond,
        float BodySlipAtOneSecondDegrees,
        float RearGripAtOneSecond,
        SteeringSample? FrontSlipZeroCrossing,
        IReadOnlyList<SteeringSample> Checkpoints);

    private readonly record struct SteeringSample(
        float TimeSeconds,
        float SpeedKmh,
        float RawInput,
        float RampedInput,
        float ConfiguredRampRateDegreesPerSecond,
        float RampRateDegreesPerSecond,
        float SpeedCapDegrees,
        float RequestedBeforeSpeedCapDegrees,
        float RequestedAfterSpeedCapDegrees,
        float ActualSteerAngleDegrees,
        float FrontWheelRoadAngleDegrees,
        float FrontSlipDegrees,
        float BodySlipDegrees,
        float FrontLocalVelocityAngleDegrees,
        float RequiredSteerForOneDegreeSlip,
        float RequiredSteerForTwoDegreesSlip,
        float YawRateDegreesPerSecond,
        float RearGripUsage,
        float EndSpeedKmh);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
