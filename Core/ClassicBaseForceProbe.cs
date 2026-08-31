using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicBaseForceProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int MeasurementTicks = 360;
    private const float MeasurementSeconds = MeasurementTicks * Dt;
    private const int Gear = 4;

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

        Console.WriteLine($"Classic base-force probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine($"  entry={EntrySpeedKmh:0}km/h duration={MeasurementSeconds:0.0}s gear={Gear} throttle=0.25 cleanup=off");
        Console.WriteLine("  force buckets: request -> wheel-frame after grip clamp -> car-forward after steer projection -> net after roll/aero");

        BaseForceSample straight = MeasureCase(parameters, engineParameters, 0f, "straight");
        BaseForceSample small = MeasureCase(parameters, engineParameters, 0.18f, "small");
        BaseForceSample medium = MeasureCase(parameters, engineParameters, 0.35f, "medium");
        BaseForceSample hard = MeasureCase(parameters, engineParameters, 0.65f, "hard");

        PrintSample(straight, straight);
        PrintSample(small, straight);
        PrintSample(medium, straight);
        PrintSample(hard, straight);

        Console.WriteLine("Classic base-force probe complete.");
    }

    private static BaseForceSample MeasureCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float steer,
        string label)
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
        float startRpm = simulator.State.Rpm;
        BaseForceAccumulator accumulator = new(parameters);

        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steer), Dt);
            accumulator.Add(simulator.State);
        }

        RequireFinite(simulator.State);
        return accumulator.ToSample(label, steer, startSpeedKmh, simulator.State.SpeedMetersPerSecond * 3.6f, startRpm, simulator.State.Rpm);
    }

    private static void PrintSample(BaseForceSample sample, BaseForceSample straight)
    {
        float extraLoss = sample.SpeedDropKmh - straight.SpeedDropKmh;
        Console.WriteLine(
            $"  {sample.Label,-8} steer={sample.SteerInput:0.00} drop={sample.SpeedDropKmh:+0.0;-0.0;0.0}km/h extra={extraLoss:+0.0;-0.0;0.0}km/h " +
            $"rpmDrop={sample.RpmDrop:+0;-0;0} accel={sample.AverageNetForwardAcceleration:+0.00;-0.00;0.00}m/s2 lat={sample.AverageAbsLateralAcceleration / 9.81f:0.00}g");
        Console.WriteLine(
            $"    reqLong={sample.AverageRequestedLongitudinalForceN:+0;-0;0}N wheelLong={sample.AverageWheelFrameLongitudinalForceN:+0;-0;0}N " +
            $"carFwd={sample.AverageCarForwardWheelForceN:+0;-0;0}N netFwd={sample.AverageNetForwardForceN:+0;-0;0}N");
        Console.WriteLine(
            $"    speedAxis={sample.AverageSpeedAxisForceN:+0;-0;0}N fwdPower={sample.AverageForwardPowerForceN:+0;-0;0}N " +
            $"latPower={sample.AverageLateralPowerForceN:+0;-0;0}N speedAccel={sample.AverageSpeedAxisAcceleration:+0.00;-0.00;0.00}m/s2");
        Console.WriteLine(
            $"    gripLoss={sample.AverageGripClampLossN:+0;-0;0}N steerProjectionLoss={sample.AverageSteeringProjectionLossN:+0;-0;0}N " +
            $"engineBrake={sample.AverageEngineBrakeForceN:+0;-0;0}N roll={sample.AverageRollingResistanceForceN:0}N aero={sample.AverageAeroDragForceN:0}N");
        Console.WriteLine(
            $"    slipF/R={sample.AverageFrontSlipDegrees:0.0}/{sample.AverageRearSlipDegrees:0.0}deg gripF/R={sample.AverageFrontGripUsage:0.00}/{sample.AverageRearGripUsage:0.00} " +
            $"body={sample.PeakBodySlipDegrees:0.0}deg yaw={sample.PeakYawRateDegreesPerSecond:0.0}deg/s");
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
            throw new InvalidOperationException("Classic base-force probe failed: vehicle state became non-finite.");
        }
    }

    private sealed class BaseForceAccumulator
    {
        private readonly float _massKg;
        private readonly float _wheelRadiusMeters;
        private int _samples;
        private float _sumRequestedLongitudinalForce;
        private float _sumWheelFrameLongitudinalForce;
        private float _sumCarForwardWheelForce;
        private float _sumNetForwardForce;
        private float _sumSpeedAxisForce;
        private float _sumForwardPowerForce;
        private float _sumLateralPowerForce;
        private float _sumSpeedAxisAcceleration;
        private float _sumGripClampLoss;
        private float _sumSteeringProjectionLoss;
        private float _sumEngineBrakeForce;
        private float _sumRollingResistanceForce;
        private float _sumAeroDragForce;
        private float _sumAbsLateralAcceleration;
        private float _sumFrontSlip;
        private float _sumRearSlip;
        private float _sumFrontGripUsage;
        private float _sumRearGripUsage;
        private float _peakBodySlip;
        private float _peakYawRate;

        public BaseForceAccumulator(VehicleSimulationParameters parameters)
        {
            _massKg = MathF.Max(1f, parameters.MassKg);
            _wheelRadiusMeters = MathF.Max(0.05f, parameters.WheelRadiusMeters);
        }

        public void Add(VehicleState state)
        {
            float requestedLongitudinal =
                state.FrontLeftRequestedLongitudinalForceN +
                state.FrontRightRequestedLongitudinalForceN +
                state.RearLeftRequestedLongitudinalForceN +
                state.RearRightRequestedLongitudinalForceN;
            float wheelFrameLongitudinal =
                state.FrontLeftDriveTorqueNm / _wheelRadiusMeters +
                state.FrontRightDriveTorqueNm / _wheelRadiusMeters +
                state.RearLeftDriveTorqueNm / _wheelRadiusMeters +
                state.RearRightDriveTorqueNm / _wheelRadiusMeters;
            float carForwardWheelForce =
                state.FrontLeftLongitudinalForceN +
                state.FrontRightLongitudinalForceN +
                state.RearLeftLongitudinalForceN +
                state.RearRightLongitudinalForceN;
            float netForwardForce =
                carForwardWheelForce -
                state.ClassicRollingResistanceForceN -
                state.ClassicAeroDragForceN;
            float netLateralForce = state.LateralAcceleration * _massKg;
            float speed = MathF.Max(0.01f, state.SpeedMetersPerSecond);
            float forwardWeight = state.SignedForwardSpeed / speed;
            float lateralWeight = state.LateralSpeed / speed;
            float forwardPowerForce = netForwardForce * forwardWeight;
            float lateralPowerForce = netLateralForce * lateralWeight;
            float speedAxisForce = forwardPowerForce + lateralPowerForce;
            float frontSlip = (
                MathF.Abs(state.FrontLeftSlipAngleDegrees) +
                MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f;
            float rearSlip = (
                MathF.Abs(state.RearLeftSlipAngleDegrees) +
                MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f;

            _samples++;
            _sumRequestedLongitudinalForce += requestedLongitudinal;
            _sumWheelFrameLongitudinalForce += wheelFrameLongitudinal;
            _sumCarForwardWheelForce += carForwardWheelForce;
            _sumNetForwardForce += netForwardForce;
            _sumSpeedAxisForce += speedAxisForce;
            _sumForwardPowerForce += forwardPowerForce;
            _sumLateralPowerForce += lateralPowerForce;
            _sumSpeedAxisAcceleration += speedAxisForce / _massKg;
            _sumGripClampLoss += requestedLongitudinal - wheelFrameLongitudinal;
            _sumSteeringProjectionLoss += wheelFrameLongitudinal - carForwardWheelForce;
            _sumEngineBrakeForce += state.ClassicEngineBrakeForceRequestN;
            _sumRollingResistanceForce += state.ClassicRollingResistanceForceN;
            _sumAeroDragForce += state.ClassicAeroDragForceN;
            _sumAbsLateralAcceleration += MathF.Abs(state.LateralAcceleration);
            _sumFrontSlip += frontSlip;
            _sumRearSlip += rearSlip;
            _sumFrontGripUsage += MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage);
            _sumRearGripUsage += MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);
            _peakBodySlip = MathF.Max(_peakBodySlip, MathF.Abs(state.ClassicBodySlipAngleDegrees));
            _peakYawRate = MathF.Max(_peakYawRate, MathF.Abs(MathHelper.ToDegrees(state.YawRateRadiansPerSecond)));
        }

        public BaseForceSample ToSample(string label, float steer, float startSpeedKmh, float endSpeedKmh, float startRpm, float endRpm)
        {
            float count = MathF.Max(1f, _samples);
            float netForwardForce = _sumNetForwardForce / count;
            return new BaseForceSample(
                label,
                steer,
                startSpeedKmh - endSpeedKmh,
                startRpm - endRpm,
                netForwardForce / _massKg,
                _sumRequestedLongitudinalForce / count,
                _sumWheelFrameLongitudinalForce / count,
                _sumCarForwardWheelForce / count,
                netForwardForce,
                _sumSpeedAxisForce / count,
                _sumForwardPowerForce / count,
                _sumLateralPowerForce / count,
                _sumSpeedAxisAcceleration / count,
                _sumGripClampLoss / count,
                _sumSteeringProjectionLoss / count,
                _sumEngineBrakeForce / count,
                _sumRollingResistanceForce / count,
                _sumAeroDragForce / count,
                _sumAbsLateralAcceleration / count,
                _sumFrontSlip / count,
                _sumRearSlip / count,
                _sumFrontGripUsage / count,
                _sumRearGripUsage / count,
                _peakBodySlip,
                _peakYawRate);
        }
    }

    private readonly record struct BaseForceSample(
        string Label,
        float SteerInput,
        float SpeedDropKmh,
        float RpmDrop,
        float AverageNetForwardAcceleration,
        float AverageRequestedLongitudinalForceN,
        float AverageWheelFrameLongitudinalForceN,
        float AverageCarForwardWheelForceN,
        float AverageNetForwardForceN,
        float AverageSpeedAxisForceN,
        float AverageForwardPowerForceN,
        float AverageLateralPowerForceN,
        float AverageSpeedAxisAcceleration,
        float AverageGripClampLossN,
        float AverageSteeringProjectionLossN,
        float AverageEngineBrakeForceN,
        float AverageRollingResistanceForceN,
        float AverageAeroDragForceN,
        float AverageAbsLateralAcceleration,
        float AverageFrontSlipDegrees,
        float AverageRearSlipDegrees,
        float AverageFrontGripUsage,
        float AverageRearGripUsage,
        float PeakBodySlipDegrees,
        float PeakYawRateDegreesPerSecond);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
