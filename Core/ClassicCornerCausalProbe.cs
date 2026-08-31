using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicCornerCausalProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int MeasurementTicks = 360;
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
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic corner causal probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine("  cleanup=off, throttle=0.25, gear=4, surface=ROAD");
        Console.WriteLine(
            $"  geometry wheelbase={geometry.WheelbaseMeters:0.000}m cgFront={geometry.CgToFrontAxleMeters:0.000}m cgRear={geometry.CgToRearAxleMeters:0.000}m cgH={parameters.CenterOfGravityHeightMeters:0.000}m");

        RunCase(parameters, engineParameters, geometry, "straight", 0f);
        RunCase(parameters, engineParameters, geometry, "medium", 0.35f);
        RunCase(parameters, engineParameters, geometry, "hard", 0.65f);

        Console.WriteLine("Classic corner causal probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        VehicleAxleGeometry geometry,
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

        Console.WriteLine($"  {label} steerInput={steerInput:0.00}");
        PrintHeader();
        for (int i = 0; i < MeasurementTicks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            if (tick is 30 or 60 or 120 or 240 or 360)
            {
                PrintSnapshot(tick * Dt, simulator.State, parameters, geometry);
            }
        }

        RequireFinite(simulator.State);
    }

    private static void PrintHeader()
    {
        Console.WriteLine(
            "    t     speed  steer   beta   yaw/refYaw     slip F/R/refF/refR      latF/R   gripF/R loads FL/FR/RL/RR latV  latPowerW equivDrag");
    }

    private static void PrintSnapshot(
        float timeSeconds,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry)
    {
        float speed = MathF.Max(0.01f, state.SpeedMetersPerSecond);
        ReferenceSnapshot reference = CalculateReference(parameters, geometry, speed, MathHelper.ToRadians((state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f));
        AxleKinematics front = AxleKinematics.FromFront(state);
        AxleKinematics rear = AxleKinematics.FromRear(state);
        float frontLateralForce = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float rearLateralForce = state.RearLeftLateralForceN + state.RearRightLateralForceN;
        float frontGrip = MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage);
        float rearGrip = MathF.Max(state.RearLeftGripUsage, state.RearRightGripUsage);
        float lateralPowerW =
            state.FrontLeftLateralForceN * state.FrontLeftLocalLateralSpeedMetersPerSecond +
            state.FrontRightLateralForceN * state.FrontRightLocalLateralSpeedMetersPerSecond +
            state.RearLeftLateralForceN * state.RearLeftLocalLateralSpeedMetersPerSecond +
            state.RearRightLateralForceN * state.RearRightLocalLateralSpeedMetersPerSecond;
        float equivalentDragN = lateralPowerW / speed;
        float loadTransferExpectedFront = ExpectedLateralTransfer(parameters, geometry.FrontTrackMeters, parameters.FrontWeightDistribution, state.LateralAcceleration);
        float loadTransferExpectedRear = ExpectedLateralTransfer(parameters, geometry.RearTrackMeters, 1f - parameters.FrontWeightDistribution, state.LateralAcceleration);
        float loadTransferMeasuredFront = state.FrontLeftLoadN - state.FrontRightLoadN;
        float loadTransferMeasuredRear = state.RearLeftLoadN - state.RearRightLoadN;

        Console.WriteLine(
            $"    {timeSeconds,4:0.00} {speed * 3.6f,7:0.0} {state.FrontLeftSteerAngleDegrees,6:0.0} {state.ClassicBodySlipAngleDegrees,6:0.0} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),7:0.0}/{reference.YawRateDegreesPerSecond,7:0.0} " +
            $"{front.SlipDegrees,5:0.0}/{rear.SlipDegrees,5:0.0}/{reference.FrontSlipDegrees,5:0.0}/{reference.RearSlipDegrees,5:0.0} " +
            $"{frontLateralForce,7:0}/{rearLateralForce,7:0} {frontGrip,5:0.00}/{rearGrip,5:0.00} " +
            $"{state.FrontLeftLoadN,5:0}/{state.FrontRightLoadN,5:0}/{state.RearLeftLoadN,5:0}/{state.RearRightLoadN,5:0} " +
            $"{state.LateralSpeed,5:0.00} {lateralPowerW,10:0}W {equivalentDragN,8:0}N");
        Console.WriteLine(
            $"         kinLat F/R={front.LocalLateralSpeedMetersPerSecond:+0.00;-0.00;0.00}/{rear.LocalLateralSpeedMetersPerSecond:+0.00;-0.00;0.00}m/s yawLat F/R={front.YawLateralContributionMetersPerSecond:+0.00;-0.00;0.00}/{rear.YawLateralContributionMetersPerSecond:+0.00;-0.00;0.00}m/s " +
            $"loadXfer measured F/R={loadTransferMeasuredFront:+0;-0;0}/{loadTransferMeasuredRear:+0;-0;0}N expected F/R={loadTransferExpectedFront:+0;-0;0}/{loadTransferExpectedRear:+0;-0;0}N");
    }

    private static float ExpectedLateralTransfer(
        VehicleSimulationParameters parameters,
        float axleTrackMeters,
        float axleShare,
        float lateralAcceleration)
    {
        return parameters.MassKg *
            lateralAcceleration *
            MathHelper.Clamp(parameters.CenterOfGravityHeightMeters, 0.05f, 1.5f) /
            MathF.Max(0.1f, axleTrackMeters) *
            axleShare;
    }

    private static ReferenceSnapshot CalculateReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f, 0f, 0f);
        }

        float mass = MathF.Max(1f, parameters.MassKg);
        float cf = MathF.Max(1f, parameters.FrontTyres.CorneringStiffnessNPerRad);
        float cr = MathF.Max(1f, parameters.RearTyres.CorneringStiffnessNPerRad);
        float a = geometry.CgToFrontAxleMeters;
        float b = geometry.CgToRearAxleMeters;
        float safeSpeed = MathF.Max(0.1f, speed);

        float a11 = -cf - cr;
        float a12 = (-cf * a + cr * b) / safeSpeed - mass * safeSpeed;
        float b1 = -cf * steerRadians;
        float a21 = -a * cf + b * cr;
        float a22 = -(a * a * cf + b * b * cr) / safeSpeed;
        float b2 = -a * cf * steerRadians;
        float det = a11 * a22 - a12 * a21;
        if (MathF.Abs(det) <= 0.001f)
        {
            return new ReferenceSnapshot(0f, 0f, 0f);
        }

        float beta = (b1 * a22 - a12 * b2) / det;
        float yawRate = (a11 * b2 - b1 * a21) / det;
        float frontSlip = steerRadians - beta - a * yawRate / safeSpeed;
        float rearSlip = -beta + b * yawRate / safeSpeed;
        return new ReferenceSnapshot(
            MathHelper.ToDegrees(yawRate),
            MathHelper.ToDegrees(frontSlip),
            MathHelper.ToDegrees(rearSlip));
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
            throw new InvalidOperationException("Classic corner causal probe failed: vehicle state became non-finite.");
        }
    }

    private readonly record struct ReferenceSnapshot(
        float YawRateDegreesPerSecond,
        float FrontSlipDegrees,
        float RearSlipDegrees);

    private readonly record struct AxleKinematics(
        float SlipDegrees,
        float LocalLateralSpeedMetersPerSecond,
        float YawLateralContributionMetersPerSecond)
    {
        public static AxleKinematics FromFront(VehicleState state)
        {
            return new AxleKinematics(
                (MathF.Abs(state.FrontLeftSlipAngleDegrees) + MathF.Abs(state.FrontRightSlipAngleDegrees)) * 0.5f,
                (state.FrontLeftLocalLateralSpeedMetersPerSecond + state.FrontRightLocalLateralSpeedMetersPerSecond) * 0.5f,
                (state.FrontLeftYawLateralContributionMetersPerSecond + state.FrontRightYawLateralContributionMetersPerSecond) * 0.5f);
        }

        public static AxleKinematics FromRear(VehicleState state)
        {
            return new AxleKinematics(
                (MathF.Abs(state.RearLeftSlipAngleDegrees) + MathF.Abs(state.RearRightSlipAngleDegrees)) * 0.5f,
                (state.RearLeftLocalLateralSpeedMetersPerSecond + state.RearRightLocalLateralSpeedMetersPerSecond) * 0.5f,
                (state.RearLeftYawLateralContributionMetersPerSecond + state.RearRightYawLateralContributionMetersPerSecond) * 0.5f);
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
