using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicSlipKinematicsProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float EntrySpeedMetersPerSecond = EntrySpeedKmh / 3.6f;
    private const float Dt = 1f / 120f;
    private const int Ticks = 120;
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
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);

        Console.WriteLine($"Classic slip kinematics probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: cleanup=off, throttle=0.25, gear=4, production values unchanged");
        Console.WriteLine("  body axes: +forward is car nose, +right is car right side; positive yaw rotates heading toward +right in code, positive steer produces negative yaw in this turn test.");
        Console.WriteLine("  four-wheel slip convention: slip = wheelSteerAngle - atan2(localLateralVelocity, effectiveLocalForwardVelocity).");
        Console.WriteLine("  rigid-body reconstruction used here: Vwheel_body = Vcg_body + omega x r -> uWheel=u+r*xRight, vWheel=v-r*zForward.");

        RunCase(parameters, engine, geometry, "medium", 0.35f);
        RunCase(parameters, engine, geometry, "hard", 0.65f);

        Console.WriteLine("Classic slip kinematics probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry,
        string label,
        float steerInput)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            AssistOptions = CleanupOff
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = Gear;
        simulator.State.Velocity = new Vector2(0f, EntrySpeedMetersPerSecond);

        Console.WriteLine($"  {label} rawInput={steerInput:F2}");
        Console.WriteLine("    t wheel posR/Z cgU/V yaw steer wheelAng localU sim/recon localV sim/recon yawLat sim/recon travelAng slip sim/recon/axleRef delta");

        AxleSignTracker rearTracker = new();
        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            int tick = i + 1;
            VehicleState state = simulator.State;
            WheelSlipKinematics fl = BuildWheel("FL", -geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftSteerAngleDegrees, state.FrontLeftLocalForwardSpeedMetersPerSecond, state.FrontLeftLocalLateralSpeedMetersPerSecond, state.FrontLeftYawLateralContributionMetersPerSecond, state.FrontLeftSlipAngleDegrees, state, engine);
            WheelSlipKinematics fr = BuildWheel("FR", geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightSteerAngleDegrees, state.FrontRightLocalForwardSpeedMetersPerSecond, state.FrontRightLocalLateralSpeedMetersPerSecond, state.FrontRightYawLateralContributionMetersPerSecond, state.FrontRightSlipAngleDegrees, state, engine);
            WheelSlipKinematics rl = BuildWheel("RL", -geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, 0f, state.RearLeftLocalForwardSpeedMetersPerSecond, state.RearLeftLocalLateralSpeedMetersPerSecond, state.RearLeftYawLateralContributionMetersPerSecond, state.RearLeftSlipAngleDegrees, state, engine);
            WheelSlipKinematics rr = BuildWheel("RR", geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, 0f, state.RearRightLocalForwardSpeedMetersPerSecond, state.RearRightLocalLateralSpeedMetersPerSecond, state.RearRightYawLateralContributionMetersPerSecond, state.RearRightSlipAngleDegrees, state, engine);

            float rearAxleSlip = (rl.SimSlipDegrees + rr.SimSlipDegrees) * 0.5f;
            bool rearCrossed = rearTracker.Observe(rearAxleSlip);
            if (tick is 12 or 30 || rearCrossed)
            {
                PrintWheel(tick * Dt, fl, parameters, geometry, state);
                PrintWheel(tick * Dt, fr, parameters, geometry, state);
                PrintWheel(tick * Dt, rl, parameters, geometry, state);
                PrintWheel(tick * Dt, rr, parameters, geometry, state);
                PrintAxleReference(tick * Dt, state, parameters, geometry);
            }
        }
    }

    private static WheelSlipKinematics BuildWheel(
        string wheel,
        float localRightMeters,
        float localForwardMeters,
        float steerDegrees,
        float simLocalForward,
        float simLocalLateral,
        float simYawLateral,
        float simSlipDegrees,
        VehicleState state,
        SimulationEngineParameters engine)
    {
        Vector2 forward = new(state.Forward.X, state.Forward.Z);
        Vector2 right = new(state.Right.X, state.Right.Z);
        float cgForward = Vector2.Dot(state.Velocity, forward);
        float cgLateral = Vector2.Dot(state.Velocity, right);
        float yawRate = state.YawRateRadiansPerSecond;
        float reconstructedForward = cgForward + yawRate * localRightMeters;
        float reconstructedYawLateral = -yawRate * localForwardMeters;
        float reconstructedLateral = cgLateral + reconstructedYawLateral;
        float denominator = WheelKinematics.EffectiveSlipSpeed(
            reconstructedForward,
            engine.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond);
        float travelAngle = MathHelper.ToDegrees(MathF.Atan2(reconstructedLateral, denominator));
        float reconstructedSlip = steerDegrees - travelAngle;

        return new WheelSlipKinematics(
            wheel,
            localRightMeters,
            localForwardMeters,
            cgForward,
            cgLateral,
            MathHelper.ToDegrees(yawRate),
            steerDegrees,
            steerDegrees,
            simLocalForward,
            reconstructedForward,
            simLocalLateral,
            reconstructedLateral,
            simYawLateral,
            reconstructedYawLateral,
            travelAngle,
            simSlipDegrees,
            reconstructedSlip,
            simSlipDegrees - reconstructedSlip);
    }

    private static void PrintWheel(
        float time,
        WheelSlipKinematics sample,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        VehicleState state)
    {
        float axleReference = CalculateInstantAxleSlipReference(sample, parameters, geometry, state);
        Console.WriteLine(
            $"    {time,4:F2} {sample.Wheel,-2} {sample.LocalRightMeters,5:F2}/{sample.LocalForwardMeters,5:F2} " +
            $"{sample.CgForwardSpeed,6:F2}/{sample.CgLateralSpeed,6:F2} {sample.YawRateDegreesPerSecond,6:F1} " +
            $"{sample.SteerDegrees,5:F2} {sample.WheelHeadingDegrees,7:F2} " +
            $"{sample.SimLocalForwardSpeed,6:F2}/{sample.ReconstructedLocalForwardSpeed,6:F2} " +
            $"{sample.SimLocalLateralSpeed,6:F2}/{sample.ReconstructedLocalLateralSpeed,6:F2} " +
            $"{sample.SimYawLateralContribution,6:F2}/{sample.ReconstructedYawLateralContribution,6:F2} " +
            $"{sample.WheelTravelAngleDegrees,7:F2} {sample.SimSlipDegrees,7:F2}/{sample.ReconstructedSlipDegrees,7:F2}/{axleReference,7:F2} " +
            $"{sample.SlipDeltaDegrees,6:F3}");
    }

    private static void PrintAxleReference(
        float time,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry)
    {
        ReferenceSnapshot reference = CalculateSteadyReference(
            parameters,
            geometry,
            state.SpeedMetersPerSecond,
            MathHelper.ToRadians(state.FrontLeftSteerAngleDegrees));
        float frontInstant = CalculateInstantAxleSlipReference(
            state,
            geometry.CgToFrontAxleMeters,
            state.FrontLeftSteerAngleDegrees,
            parameters,
            geometry);
        float rearInstant = CalculateInstantAxleSlipReference(
            state,
            -geometry.CgToRearAxleMeters,
            0f,
            parameters,
            geometry);

        Console.WriteLine(
            $"         axle comparison t={time:F2}: instantKinematicSlip F/R={frontInstant:F2}/{rearInstant:F2}deg " +
            $"fourWheelAvg F/R={((state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f):F2}/{((state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f):F2}deg " +
            $"steadyReference F/R={reference.FrontSlipDegrees:F2}/{reference.RearSlipDegrees:F2}deg betaRef={reference.BetaDegrees:F2} yawRef={reference.YawRateDegreesPerSecond:F1}");
    }

    private static float CalculateInstantAxleSlipReference(
        WheelSlipKinematics sample,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        VehicleState state)
    {
        return CalculateInstantAxleSlipReference(state, sample.LocalForwardMeters, sample.SteerDegrees, parameters, geometry);
    }

    private static float CalculateInstantAxleSlipReference(
        VehicleState state,
        float localForwardMeters,
        float steerDegrees,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry)
    {
        _ = parameters;
        _ = geometry;
        Vector2 forward = new(state.Forward.X, state.Forward.Z);
        Vector2 right = new(state.Right.X, state.Right.Z);
        float cgForward = Vector2.Dot(state.Velocity, forward);
        float cgLateral = Vector2.Dot(state.Velocity, right);
        float lateral = cgLateral - state.YawRateRadiansPerSecond * localForwardMeters;
        float denominator = WheelKinematics.EffectiveSlipSpeed(cgForward, 3f);
        return steerDegrees - MathHelper.ToDegrees(MathF.Atan2(lateral, denominator));
    }

    private static ReferenceSnapshot CalculateSteadyReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f, 0f, 0f, 0f, false);
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
            return new ReferenceSnapshot(0f, 0f, 0f, 0f, false);
        }

        float beta = (b1 * a22 - a12 * b2) / det;
        float yawRate = (a11 * b2 - b1 * a21) / det;
        float frontSlip = steerRadians - beta - a * yawRate / safeSpeed;
        float rearSlip = -beta + b * yawRate / safeSpeed;
        return new ReferenceSnapshot(
            MathHelper.ToDegrees(yawRate),
            MathHelper.ToDegrees(beta),
            MathHelper.ToDegrees(frontSlip),
            MathHelper.ToDegrees(rearSlip),
            true);
    }

    private sealed class AxleSignTracker
    {
        private float _previousSign;

        public bool Observe(float slip)
        {
            float sign = MathF.Sign(slip);
            bool crossed = _previousSign != 0f && sign != 0f && sign != _previousSign;
            if (sign != 0f)
            {
                _previousSign = sign;
            }

            return crossed;
        }
    }

    private readonly record struct WheelSlipKinematics(
        string Wheel,
        float LocalRightMeters,
        float LocalForwardMeters,
        float CgForwardSpeed,
        float CgLateralSpeed,
        float YawRateDegreesPerSecond,
        float SteerDegrees,
        float WheelHeadingDegrees,
        float SimLocalForwardSpeed,
        float ReconstructedLocalForwardSpeed,
        float SimLocalLateralSpeed,
        float ReconstructedLocalLateralSpeed,
        float SimYawLateralContribution,
        float ReconstructedYawLateralContribution,
        float WheelTravelAngleDegrees,
        float SimSlipDegrees,
        float ReconstructedSlipDegrees,
        float SlipDeltaDegrees);

    private readonly record struct ReferenceSnapshot(
        float YawRateDegreesPerSecond,
        float BetaDegrees,
        float FrontSlipDegrees,
        float RearSlipDegrees,
        bool IsValid);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
