using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class CorneringSpeedLossProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int SettlingTicks = 45;
    private const int MeasurementTicks = 360;
    private const float MeasurementSeconds = MeasurementTicks * Dt;
    private const int Gear = 4;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Cornering speed loss probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine($"  entry={EntrySpeedKmh:0}km/h duration={MeasurementSeconds:0.0}s gear={Gear} surface=ROAD");
        PrintBodySlipDampingCurve();

        foreach (float throttle in new[] { 0f, 0.25f })
        {
            RunThrottleSet(parameters, engineParameters, throttle);
        }

        Console.WriteLine("Cornering speed loss probe complete.");
    }

    private static void PrintBodySlipDampingCurve()
    {
        Console.WriteLine("  body-slip damping curve:");
        foreach (float bodySlipDegrees in new[] { 3.5f, 5.0f, 8.0f, 12.0f, 20.0f })
        {
            float baseGate = ClassicFourWheelVehicleSimulator.CalculateBodySlipDampingGate(bodySlipDegrees);
            float baseRate = ClassicFourWheelVehicleSimulator.BodySlipDampingRateCeiling * baseGate;
            float settleGate = ClassicFourWheelVehicleSimulator.CalculateBodySlipSettleGate(bodySlipDegrees);
            float settleRate = ClassicFourWheelVehicleSimulator.BodySlipSettleRateCeiling * settleGate;
            float rearSettleGate = ClassicFourWheelVehicleSimulator.CalculateRearSlipSettleBodyGate(bodySlipDegrees);
            float rearSettleRate = ClassicFourWheelVehicleSimulator.RearSlipSettleRateCeiling * rearSettleGate;
            Console.WriteLine(
                $"    slip={bodySlipDegrees:0.0}deg baseGate={baseGate:0.00} baseRate={baseRate:0.00}/s " +
                $"settleGate={settleGate:0.00} settleRate={settleRate:0.00}/s rearGate={rearSettleGate:0.00} rearRate={rearSettleRate:0.00}/s");
        }
    }

    private static void RunThrottleSet(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float throttle)
    {
        CorneringLossSample straight = MeasureCase(parameters, engineParameters, throttle, 0f, "straight");
        CorneringLossSample small = MeasureCase(parameters, engineParameters, throttle, 0.18f, "small");
        CorneringLossSample medium = MeasureCase(parameters, engineParameters, throttle, 0.35f, "medium");
        CorneringLossSample nearLimit = MeasureCase(parameters, engineParameters, throttle, 0.65f, "near-limit");

        Console.WriteLine($"  throttle={throttle:0.00}");
        PrintSample(straight, straight);
        PrintSample(small, straight);
        PrintSample(medium, straight);
        PrintSample(nearLimit, straight);
        PrintWarnings(throttle, straight, small, medium, nearLimit);
    }

    private static CorneringLossSample MeasureCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float throttle,
        float steer,
        string label)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engineParameters);
        for (int i = 0; i < SettlingTicks; i++)
        {
            simulator.Update(new VehicleInput(0.18f, 0f, 0f), Dt);
        }

        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);

        float startSpeedKmh = simulator.State.SpeedMetersPerSecond * 3.6f;
        CorneringLossAccumulator accumulator = new();

        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(throttle, 0f, steer), Dt);
            accumulator.Add(simulator.State);
        }

        VehicleState final = simulator.State;
        RequireFinite(final);

        return accumulator.ToSample(
            label,
            throttle,
            steer,
            startSpeedKmh,
            final.SpeedMetersPerSecond * 3.6f);
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);
        return simulator;
    }

    private static void PrintSample(CorneringLossSample sample, CorneringLossSample straight)
    {
        float extraLoss = sample.SpeedDropKmh - straight.SpeedDropKmh;
        float speedAcceleration = -sample.SpeedDropKmh / 3.6f / MeasurementSeconds;
        float extraSpeedAcceleration = speedAcceleration - (-straight.SpeedDropKmh / 3.6f / MeasurementSeconds);
        Console.WriteLine(
            $"    {sample.Label,-10} steer={sample.SteerInput:0.00} angle={sample.PeakRoadWheelAngleDegrees:0.0}deg " +
            $"speed={sample.StartSpeedKmh:0.0}->{sample.EndSpeedKmh:0.0}km/h drop={sample.SpeedDropKmh:+0.0;-0.0;0.0}km/h " +
            $"extra={extraLoss:+0.0;-0.0;0.0}km/h speedAccel={speedAcceleration:+0.00;-0.00;0.00}m/s2 " +
            $"extraSpeedAccel={extraSpeedAcceleration:+0.00;-0.00;0.00}m/s2 localLong/lat={sample.AverageLongitudinalAcceleration:+0.00;-0.00;0.00}/{sample.AverageLateralAcceleration:+0.00;-0.00;0.00}m/s2 " +
            $"driveReq={sample.AverageDriveRequestForceN:0}N drive={sample.AverageDriveForceN:0}N long={sample.AverageLongitudinalWheelForceN:0}N " +
            $"engineBrake={sample.AverageEngineBrakeForceN:0}N roll={sample.AverageRollingResistanceForceN:0}N aero={sample.AverageAeroDragForceN:0}N " +
            $"bodyDamp={sample.AverageBodySlipDampingForceN:0}N retain={sample.AverageSpeedRetentionForceN:0}N " +
            $"slipF/R={sample.PeakFrontSlipDegrees:0.0}/{sample.PeakRearSlipDegrees:0.0}deg body={sample.PeakBodySlipDegrees:0.0}deg grip={sample.PeakGripUsage:0.00}");
    }

    private static void PrintWarnings(
        float throttle,
        CorneringLossSample straight,
        CorneringLossSample small,
        CorneringLossSample medium,
        CorneringLossSample nearLimit)
    {
        WarnIf(small.SpeedDropKmh > straight.SpeedDropKmh + 3.0f,
            $"small steer costs {(small.SpeedDropKmh - straight.SpeedDropKmh):0.0}km/h more than straight over {MeasurementSeconds:0.0}s.");
        WarnIf(medium.SpeedDropKmh > straight.SpeedDropKmh + 7.0f,
            $"medium steer costs {(medium.SpeedDropKmh - straight.SpeedDropKmh):0.0}km/h more than straight over {MeasurementSeconds:0.0}s.");
        WarnIf(throttle > 0f && straight.SpeedDropKmh < 0f && medium.SpeedDropKmh > 0f,
            $"medium steer loses speed under {throttle * 100f:0}% throttle while straight-line throttle gains speed.");
        WarnIf(throttle > 0f &&
               medium.AverageDriveRequestForceN > 500f &&
               medium.AverageLongitudinalWheelForceN < straight.AverageLongitudinalWheelForceN - 650f,
            "medium steer delivered longitudinal wheel force falls well below the straight throttle baseline.");
        WarnIf(medium.PeakBodySlipDegrees < 7.5f &&
               MathF.Abs(medium.AverageBodySlipDampingForceN) > MathF.Abs(medium.AverageSpeedRetentionForceN) + 450f,
            "body-slip damping dominates speed-retention while body slip is still moderate.");
        WarnIf(nearLimit.SpeedDropKmh <= medium.SpeedDropKmh + 1.0f && nearLimit.PeakGripUsage > medium.PeakGripUsage + 0.10f,
            "near-limit case does not show progressive extra scrub versus medium steer.");
    }

    private static void WarnIf(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine($"    warning: {message}");
        }
    }

    private static void RequireFinite(VehicleState state)
    {
        if (!float.IsFinite(state.Position.X) ||
            !float.IsFinite(state.Position.Z) ||
            !float.IsFinite(state.Velocity.X) ||
            !float.IsFinite(state.Velocity.Y) ||
            !float.IsFinite(state.HeadingRadians) ||
            !float.IsFinite(state.YawRateRadiansPerSecond))
        {
            throw new InvalidOperationException("Cornering speed loss probe failed: vehicle state became non-finite.");
        }
    }

    private sealed class CorneringLossAccumulator
    {
        private int _samples;
        private float _sumLongitudinalAcceleration;
        private float _sumLateralAcceleration;
        private float _sumDriveRequestForce;
        private float _sumDriveForce;
        private float _sumLongitudinalWheelForce;
        private float _sumRequestedLongitudinalWheelForce;
        private float _sumLateralWheelForce;
        private float _sumEngineBrakeForce;
        private float _sumServiceBrakeForce;
        private float _sumRollingResistanceForce;
        private float _sumAeroDragForce;
        private float _sumBodySlipDampingForce;
        private float _sumSpeedRetentionForce;
        private float _sumTyreScrubForce;
        private float _sumProjectionForce;
        private float _peakRoadWheelAngle;
        private float _peakFrontSlip;
        private float _peakRearSlip;
        private float _peakBodySlip;
        private float _peakGripUsage;

        public void Add(VehicleState state)
        {
            _samples++;
            _sumLongitudinalAcceleration += state.LongitudinalAcceleration;
            _sumLateralAcceleration += state.LateralAcceleration;
            _sumDriveRequestForce += state.ClassicDriveForceRequestN;
            _sumDriveForce += state.DriveForce;
            _sumLongitudinalWheelForce +=
                state.FrontLeftLongitudinalForceN +
                state.FrontRightLongitudinalForceN +
                state.RearLeftLongitudinalForceN +
                state.RearRightLongitudinalForceN;
            _sumRequestedLongitudinalWheelForce +=
                state.FrontLeftRequestedLongitudinalForceN +
                state.FrontRightRequestedLongitudinalForceN +
                state.RearLeftRequestedLongitudinalForceN +
                state.RearRightRequestedLongitudinalForceN;
            _sumLateralWheelForce +=
                state.FrontLeftLateralForceN +
                state.FrontRightLateralForceN +
                state.RearLeftLateralForceN +
                state.RearRightLateralForceN;
            _sumEngineBrakeForce += state.ClassicEngineBrakeForceRequestN;
            _sumServiceBrakeForce += state.ClassicServiceBrakeForceRequestN;
            _sumRollingResistanceForce += state.ClassicRollingResistanceForceN;
            _sumAeroDragForce += state.ClassicAeroDragForceN;
            _sumBodySlipDampingForce += state.ClassicBodySlipDampingForceN;
            _sumSpeedRetentionForce += state.ClassicCorneringCleanupSpeedRetentionForceN;
            _sumTyreScrubForce += state.PeakTyreScrubForceN;
            _sumProjectionForce += state.PeakSteeringProjectionForceN;
            _peakRoadWheelAngle = MathF.Max(
                _peakRoadWheelAngle,
                MathF.Max(MathF.Abs(state.FrontLeftSteerAngleDegrees), MathF.Abs(state.FrontRightSteerAngleDegrees)));
            _peakFrontSlip = MathF.Max(
                _peakFrontSlip,
                (MathF.Abs(state.FrontLeftSlipAngleDegrees) + MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f);
            _peakRearSlip = MathF.Max(
                _peakRearSlip,
                (MathF.Abs(state.RearLeftSlipAngleDegrees) + MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f);
            _peakBodySlip = MathF.Max(_peakBodySlip, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            _peakGripUsage = MathF.Max(
                _peakGripUsage,
                MathF.Max(
                    MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage),
                    MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage)));
        }

        public CorneringLossSample ToSample(
            string label,
            float throttle,
            float steer,
            float startSpeedKmh,
            float endSpeedKmh)
        {
            float count = MathF.Max(1f, _samples);
            return new CorneringLossSample(
                label,
                throttle,
                steer,
                startSpeedKmh,
                endSpeedKmh,
                startSpeedKmh - endSpeedKmh,
                _sumLongitudinalAcceleration / count,
                _sumLateralAcceleration / count,
                _sumDriveRequestForce / count,
                _sumDriveForce / count,
                _sumLongitudinalWheelForce / count,
                _sumRequestedLongitudinalWheelForce / count,
                _sumLateralWheelForce / count,
                _sumEngineBrakeForce / count,
                _sumServiceBrakeForce / count,
                _sumRollingResistanceForce / count,
                _sumAeroDragForce / count,
                _sumBodySlipDampingForce / count,
                _sumSpeedRetentionForce / count,
                _sumTyreScrubForce / count,
                _sumProjectionForce / count,
                _peakRoadWheelAngle,
                _peakFrontSlip,
                _peakRearSlip,
                _peakBodySlip,
                _peakGripUsage);
        }
    }

    private readonly record struct CorneringLossSample(
        string Label,
        float Throttle,
        float SteerInput,
        float StartSpeedKmh,
        float EndSpeedKmh,
        float SpeedDropKmh,
        float AverageLongitudinalAcceleration,
        float AverageLateralAcceleration,
        float AverageDriveRequestForceN,
        float AverageDriveForceN,
        float AverageLongitudinalWheelForceN,
        float AverageRequestedLongitudinalWheelForceN,
        float AverageLateralWheelForceN,
        float AverageEngineBrakeForceN,
        float AverageServiceBrakeForceN,
        float AverageRollingResistanceForceN,
        float AverageAeroDragForceN,
        float AverageBodySlipDampingForceN,
        float AverageSpeedRetentionForceN,
        float AverageTyreScrubForceN,
        float AverageProjectionForceN,
        float PeakRoadWheelAngleDegrees,
        float PeakFrontSlipDegrees,
        float PeakRearSlipDegrees,
        float PeakBodySlipDegrees,
        float PeakGripUsage);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
