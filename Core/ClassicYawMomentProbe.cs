using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicYawMomentProbe
{
    private const float EntrySpeedKmh = 150f;
    private const float Dt = 1f / 120f;
    private const int Ticks = 60;
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
        ClassicFourWheelTyres tyres = ClassicFourWheelVehicleSimulator.ResolveClassicTyres(parameters, engineParameters.ClassicFourWheel);

        float scaledYawInertia = MathF.Max(1f, parameters.YawInertiaKgM2 * MathF.Max(0.1f, engineParameters.ClassicFourWheel.Yaw.InertiaScale));
        float referenceYawInertia = EstimateReferenceYawInertia(parameters);
        Console.WriteLine($"Classic yaw moment probe: {parameters.DisplayName}, model={engineParameters.HandlingModel}");
        Console.WriteLine($"  cleanup=off throttle=0.25 gear={Gear} first={Ticks * Dt:0.00}s");
        Console.WriteLine(
            $"  scaledYawInertia = configuredYawInertia * classicFourWheel.yaw.inertiaScale = " +
            $"{parameters.YawInertiaKgM2:0} * {engineParameters.ClassicFourWheel.Yaw.InertiaScale:0.00} = {scaledYawInertia:0}kgm2");
        Console.WriteLine($"  simpleRefYawInertia~={referenceYawInertia:0}kgm2 scaleVsRef={scaledYawInertia / MathF.Max(1f, referenceYawInertia):0.00}x");

        RunCase(parameters, engineParameters, geometry, tyres, scaledYawInertia, referenceYawInertia, "medium", 0.35f);
        RunCase(parameters, engineParameters, geometry, tyres, scaledYawInertia, referenceYawInertia, "hard", 0.65f);

        Console.WriteLine("Classic yaw moment probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        VehicleAxleGeometry geometry,
        ClassicFourWheelTyres tyres,
        float yawInertia,
        float referenceYawInertia,
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
        simulator.State.Velocity = new Vector2(0f, EntrySpeedKmh / 3.6f);

        Console.WriteLine($"  {label} steerInput={steerInput:0.00}");
        Console.WriteLine("    t speed steer yaw/ref rawMoment dampMoment dampAcc removed netMoment acc cur/ri/zd/both gain inertia/damp/both");

        float previousYawRate = simulator.State.YawRateRadiansPerSecond;
        for (int i = 0; i < Ticks; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, steerInput), Dt);
            float measuredYawAcceleration = (simulator.State.YawRateRadiansPerSecond - previousYawRate) / Dt;
            previousYawRate = simulator.State.YawRateRadiansPerSecond;

            int tick = i + 1;
            if (tick is 12 or 30 or 60)
            {
                PrintSnapshot(tick * Dt, simulator.State, parameters, engineParameters, geometry, tyres, yawInertia, referenceYawInertia, measuredYawAcceleration);
            }
        }

        RequireFinite(simulator.State);
    }

    private static void PrintSnapshot(
        float timeSeconds,
        VehicleState state,
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        VehicleAxleGeometry geometry,
        ClassicFourWheelTyres tyres,
        float yawInertia,
        float referenceYawInertia,
        float measuredYawAcceleration)
    {
        WheelMoment fl = CalculateWheelMoment(
            -geometry.FrontTrackMeters * 0.5f,
            geometry.CgToFrontAxleMeters,
            state.FrontLeftLongitudinalForceN,
            state.FrontLeftLateralForceN,
            state.FrontLeftSlipAngleDegrees,
            state.FrontLeftLoadN,
            state.FrontLeftFrictionEllipseGripBudgetN,
            tyres.Front);
        WheelMoment fr = CalculateWheelMoment(
            geometry.FrontTrackMeters * 0.5f,
            geometry.CgToFrontAxleMeters,
            state.FrontRightLongitudinalForceN,
            state.FrontRightLateralForceN,
            state.FrontRightSlipAngleDegrees,
            state.FrontRightLoadN,
            state.FrontRightFrictionEllipseGripBudgetN,
            tyres.Front);
        WheelMoment rl = CalculateWheelMoment(
            -geometry.RearTrackMeters * 0.5f,
            -geometry.CgToRearAxleMeters,
            state.RearLeftLongitudinalForceN,
            state.RearLeftLateralForceN,
            state.RearLeftSlipAngleDegrees,
            state.RearLeftLoadN,
            state.RearLeftFrictionEllipseGripBudgetN,
            tyres.Rear);
        WheelMoment rr = CalculateWheelMoment(
            geometry.RearTrackMeters * 0.5f,
            -geometry.CgToRearAxleMeters,
            state.RearRightLongitudinalForceN,
            state.RearRightLateralForceN,
            state.RearRightSlipAngleDegrees,
            state.RearRightLoadN,
            state.RearRightFrictionEllipseGripBudgetN,
            tyres.Rear);

        float frontMoment = fl.TotalMomentNm + fr.TotalMomentNm;
        float rearMoment = rl.TotalMomentNm + rr.TotalMomentNm;
        float netMoment = frontMoment + rearMoment;
        float naturalYawAcceleration = netMoment / yawInertia;
        float yawDampingAcceleration = -state.YawRateRadiansPerSecond * MathF.Max(0f, engineParameters.ClassicFourWheel.Yaw.Damping);
        float yawDampingMoment = yawDampingAcceleration * yawInertia;
        float netMomentAfterDamping = netMoment + yawDampingMoment;
        float dampingRemovedPercent = CalculateDampingRemovedPercent(netMoment, yawDampingMoment);
        float totalExpectedYawAcceleration = naturalYawAcceleration + yawDampingAcceleration;
        ReferenceSnapshot reference = CalculateReference(parameters, geometry, state.SpeedMetersPerSecond, MathHelper.ToRadians(state.FrontLeftSteerAngleDegrees));
        CounterfactualYaw counterfactual = CalculateCounterfactuals(netMoment, yawDampingAcceleration, yawInertia, referenceYawInertia);

        Console.WriteLine(
            $"    {timeSeconds,4:0.00} {state.SpeedMetersPerSecond * 3.6f,6:0.0} {state.FrontLeftSteerAngleDegrees,5:0.0} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:0.0}/{reference.YawRateDegreesPerSecond,5:0.0} " +
            $"{netMoment,9:0} {yawDampingMoment,10:0} {MathHelper.ToDegrees(yawDampingAcceleration),7:0.0} {dampingRemovedPercent,6:0}% {netMomentAfterDamping,9:0} " +
            $"{MathHelper.ToDegrees(counterfactual.CurrentInertiaCurrentDamping),6:0}/{MathHelper.ToDegrees(counterfactual.ReferenceInertiaCurrentDamping),6:0}/" +
            $"{MathHelper.ToDegrees(counterfactual.CurrentInertiaZeroDamping),6:0}/{MathHelper.ToDegrees(counterfactual.ReferenceInertiaZeroDamping),6:0} " +
            $"{MathHelper.ToDegrees(counterfactual.InertiaOnlyGain),6:0}/{MathHelper.ToDegrees(counterfactual.DampingOnlyGain),6:0}/{MathHelper.ToDegrees(counterfactual.BothGain),6:0}");
        Console.WriteLine(
            $"         measuredYawAcc={MathHelper.ToDegrees(measuredYawAcceleration):0.0}deg/s2 calcYawAcc={MathHelper.ToDegrees(totalExpectedYawAcceleration):0.0}deg/s2 " +
            $"frontMoment={frontMoment:0}Nm rearMoment={rearMoment:0}Nm wheelMoments FL/FR/RL/RR={fl.TotalMomentNm:0}/{fr.TotalMomentNm:0}/{rl.TotalMomentNm:0}/{rr.TotalMomentNm:0}Nm");
        Console.WriteLine(
            $"         latF FL/FR/RL/RR={state.FrontLeftLateralForceN,6:0}/{state.FrontRightLateralForceN,6:0}/{state.RearLeftLateralForceN,6:0}/{state.RearRightLateralForceN,6:0}N " +
            $"forceExpected FL/FR={fl.ExpectedLateralForceN,6:0}/{fr.ExpectedLateralForceN,6:0}N actual/expected FL={fl.LateralForceRatio,4:0.00} FR={fr.LateralForceRatio,4:0.00}");
        Console.WriteLine(
            $"         arms z FL/FR/RL/RR={geometry.CgToFrontAxleMeters:0.000}/{geometry.CgToFrontAxleMeters:0.000}/{-geometry.CgToRearAxleMeters:0.000}/{-geometry.CgToRearAxleMeters:0.000}m " +
            $"grip FL/FR/RL/RR={state.FrontLeftGripUsage:0.00}/{state.FrontRightGripUsage:0.00}/{state.RearLeftGripUsage:0.00}/{state.RearRightGripUsage:0.00} " +
            $"load FL/FR/RL/RR={state.FrontLeftLoadN:0}/{state.FrontRightLoadN:0}/{state.RearLeftLoadN:0}/{state.RearRightLoadN:0}N");
    }

    private static CounterfactualYaw CalculateCounterfactuals(
        float rawTyreYawMoment,
        float yawDampingAcceleration,
        float currentYawInertia,
        float referenceYawInertia)
    {
        float currentCurrent = rawTyreYawMoment / MathF.Max(1f, currentYawInertia) + yawDampingAcceleration;
        float referenceCurrent = rawTyreYawMoment / MathF.Max(1f, referenceYawInertia) + yawDampingAcceleration;
        float currentZero = rawTyreYawMoment / MathF.Max(1f, currentYawInertia);
        float referenceZero = rawTyreYawMoment / MathF.Max(1f, referenceYawInertia);

        return new CounterfactualYaw(
            currentCurrent,
            referenceCurrent,
            currentZero,
            referenceZero,
            referenceCurrent - currentCurrent,
            currentZero - currentCurrent,
            referenceZero - currentCurrent);
    }

    private static float CalculateDampingRemovedPercent(float rawTyreYawMoment, float yawDampingMoment)
    {
        if (MathF.Abs(rawTyreYawMoment) <= 1f ||
            MathF.Sign(rawTyreYawMoment) == MathF.Sign(yawDampingMoment))
        {
            return 0f;
        }

        return MathF.Abs(yawDampingMoment) / MathF.Abs(rawTyreYawMoment) * 100f;
    }

    private static WheelMoment CalculateWheelMoment(
        float localRightMeters,
        float localForwardMeters,
        float localForwardForceN,
        float localRightForceN,
        float slipAngleDegrees,
        float loadN,
        float gripBudgetN,
        ClassicBicycleTyreParameters tyre)
    {
        float longMoment = localRightMeters * localForwardForceN;
        float lateralMoment = -localForwardMeters * localRightForceN;
        float expected = ClassicFourWheelVehicleSimulator.CalculateDiagnosticTyreLateralForce(
            MathHelper.ToRadians(slipAngleDegrees),
            MathF.Max(1f, gripBudgetN),
            tyre);
        float ratio = MathF.Abs(expected) > 1f
            ? localRightForceN / expected
            : 0f;
        return new WheelMoment(
            longMoment,
            lateralMoment,
            longMoment + lateralMoment,
            expected,
            ratio,
            loadN);
    }

    private static float EstimateReferenceYawInertia(VehicleSimulationParameters parameters)
    {
        float length = MathF.Max(parameters.WheelbaseMeters * 1.45f, parameters.BodyLengthMeters);
        float width = MathF.Max(parameters.FrontTrackMeters, parameters.BodyWidthMeters);
        return parameters.MassKg * (length * length + width * width) / 12f;
    }

    private static ReferenceSnapshot CalculateReference(
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry,
        float speed,
        float steerRadians)
    {
        if (MathF.Abs(steerRadians) <= 0.0001f)
        {
            return new ReferenceSnapshot(0f);
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
            return new ReferenceSnapshot(0f);
        }

        float yawRate = (a11 * b2 - b1 * a21) / det;
        return new ReferenceSnapshot(MathHelper.ToDegrees(yawRate));
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
            throw new InvalidOperationException("Classic yaw moment probe failed: vehicle state became non-finite.");
        }
    }

    private readonly record struct WheelMoment(
        float LongitudinalMomentNm,
        float LateralMomentNm,
        float TotalMomentNm,
        float ExpectedLateralForceN,
        float LateralForceRatio,
        float LoadN);

    private readonly record struct ReferenceSnapshot(float YawRateDegreesPerSecond);

    private readonly record struct CounterfactualYaw(
        float CurrentInertiaCurrentDamping,
        float ReferenceInertiaCurrentDamping,
        float CurrentInertiaZeroDamping,
        float ReferenceInertiaZeroDamping,
        float InertiaOnlyGain,
        float DampingOnlyGain,
        float BothGain);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
