using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicFrontBrakeCombinedGripProbe
{
    private const float Dt = 1f / 120f;
    private const int Gear = 4;
    private const float StartSpeedKmh = 150f;
    private const float SteerCommand = 1.0f;
    private const float PreBrakeSeconds = 0.50f;
    private const float SampleSeconds = 0.50f;

    private static readonly float[] CheckpointsSeconds = [0.10f, 0.25f, 0.50f];
    private static readonly float[] BrakeSweep = [1.0f, 0.75f, 0.50f, 0.25f, 0f];

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        VehicleAxleGeometry geometry = VehicleAxleGeometry.FromParameters(parameters);
        ClassicBicycleGripBudgetParameters grip = engine.ClassicFourWheel.GripBudget;

        Console.WriteLine($"Classic front brake/combined-grip probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine("  diagnostic-only: production handling values unchanged");
        Console.WriteLine(
            $"  combined-grip equation: demand=(abs(Fx/Fmax)^p + abs(Fy/Fmax)^p), p={grip.CombinedGripExponent:0.00}; " +
            "demand>1 scales the force vector unless brake+steer lateral priority is active.");
        Console.WriteLine(
            $"  brake+steer priority: lateralPriority={grip.BrakingSteeringLateralPriority:0.00}, " +
            $"frontBrakeMultiplier={grip.BrakingSteeringFrontBrakeMultiplier:0.00}, rearBrakeMultiplier={grip.BrakingSteeringRearBrakeMultiplier:0.00}, " +
            $"frontPressureTarget={grip.BrakePressureFrontTargetGripUsage:0.00}, rearPressureTarget={grip.BrakePressureRearTargetGripUsage:0.00}");

        RunTimeline(parameters, engine, geometry);
        RunBrakeSweep(parameters, engine, geometry);

        Console.WriteLine("Classic front brake/combined-grip probe complete.");
    }

    private static void RunTimeline(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry)
    {
        ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine);
        for (int i = 0; i < SecondsToTicks(PreBrakeSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true), Dt);
        }

        Console.WriteLine();
        Console.WriteLine("  front-wheel timeline: 150km/h -> 0.5s hard brake -> hard brake + steer=1");
        Console.WriteLine("    t wheel load steer vLong vLat slip reqFx actFx reqFy cap longUse latUse usage yaw actualFy variants(sym/latFirst/frontPri)");
        int checkpointIndex = 0;
        for (int i = 1; i <= SecondsToTicks(SampleSeconds); i++)
        {
            simulator.Update(new VehicleInput(0f, 1f, SteerCommand, brakeAssistEnabled: true), Dt);
            float elapsed = i * Dt;
            if (checkpointIndex < CheckpointsSeconds.Length &&
                elapsed + Dt * 0.5f >= CheckpointsSeconds[checkpointIndex])
            {
                PrintWheelSample(CheckpointsSeconds[checkpointIndex], "FL", BuildWheelSample(simulator.State, geometry, left: true, engine));
                PrintWheelSample(CheckpointsSeconds[checkpointIndex], "FR", BuildWheelSample(simulator.State, geometry, left: false, engine));
                checkpointIndex++;
            }
        }
    }

    private static void RunBrakeSweep(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        VehicleAxleGeometry geometry)
    {
        Console.WriteLine();
        Console.WriteLine("  brake-release sweep: same 0.5s pre-brake, then hold steer=1 and fixed brake level for 0.5s");
        Console.WriteLine("    brake speed angle slipF/R pressF/R reqFxF actFxF reqFyF actFyF capF longUseF latUseF usageF yawF/R/net idealBrakeF/R actualBrakeF/R class");
        foreach (float brake in BrakeSweep)
        {
            ClassicFourWheelVehicleSimulator simulator = CreateSimulator(parameters, engine);
            for (int i = 0; i < SecondsToTicks(PreBrakeSeconds); i++)
            {
                simulator.Update(new VehicleInput(0f, 1f, 0f, brakeAssistEnabled: true), Dt);
            }

            for (int i = 0; i < SecondsToTicks(SampleSeconds); i++)
            {
                simulator.Update(new VehicleInput(0f, brake, SteerCommand, brakeAssistEnabled: true), Dt);
            }

            PrintSweepSample(brake, simulator.State, parameters, geometry);
        }
    }

    private static ClassicFourWheelVehicleSimulator CreateSimulator(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine)
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
        return simulator;
    }

    private static WheelSample BuildWheelSample(
        VehicleState state,
        VehicleAxleGeometry geometry,
        bool left,
        SimulationEngineParameters engine)
    {
        float steer = left ? state.FrontLeftSteerAngleDegrees : state.FrontRightSteerAngleDegrees;
        float steerRadians = MathHelper.ToRadians(steer);
        float sin = MathF.Sin(steerRadians);
        float cos = MathF.Cos(steerRadians);
        float reqFx = left ? state.FrontLeftRequestedLongitudinalForceN : state.FrontRightRequestedLongitudinalForceN;
        float actBodyFx = left ? state.FrontLeftLongitudinalForceN : state.FrontRightLongitudinalForceN;
        float actBodyFy = left ? state.FrontLeftLateralForceN : state.FrontRightLateralForceN;
        float actualWheelFx = actBodyFx * cos + actBodyFy * sin;
        float actualWheelFy = actBodyFy * cos - actBodyFx * sin;
        float reqFy = left ? state.FrontLeftRequestedLateralForceN : state.FrontRightRequestedLateralForceN;
        float cap = left ? state.FrontLeftFrictionEllipseGripBudgetN : state.FrontRightFrictionEllipseGripBudgetN;
        float exponent = engine.ClassicFourWheel.GripBudget.CombinedGripExponent;
        Allocation symmetric = AllocateSymmetric(reqFx, reqFy, cap, exponent);
        Allocation lateralFirst = AllocateLateralFirst(reqFx, reqFy, cap, exponent);
        Allocation frontPriority = AllocateWithLateralPriority(reqFx, reqFy, cap, exponent, 0.65f);
        float right = left ? -geometry.FrontTrackMeters * 0.5f : geometry.FrontTrackMeters * 0.5f;
        float yawMoment = right * actBodyFx - geometry.CgToFrontAxleMeters * actBodyFy;

        return new WheelSample(
            left ? "FL" : "FR",
            left ? state.FrontLeftLoadN : state.FrontRightLoadN,
            steer,
            left ? state.FrontLeftLocalForwardSpeedMetersPerSecond : state.FrontRightLocalForwardSpeedMetersPerSecond,
            left ? state.FrontLeftLocalLateralSpeedMetersPerSecond : state.FrontRightLocalLateralSpeedMetersPerSecond,
            left ? state.FrontLeftSlipAngleDegrees : state.FrontRightSlipAngleDegrees,
            reqFx,
            actualWheelFx,
            reqFy,
            cap,
            MathF.Abs(actualWheelFx) / MathF.Max(1f, cap),
            MathF.Abs(actualWheelFy) / MathF.Max(1f, cap),
            left ? state.FrontLeftGripUsage : state.FrontRightGripUsage,
            yawMoment,
            actualWheelFy,
            symmetric,
            lateralFirst,
            frontPriority);
    }

    private static void PrintWheelSample(float elapsed, string wheel, WheelSample sample)
    {
        Console.WriteLine(
            $"    {elapsed,4:0.00} {wheel,-2} {sample.LoadN,5:0} {sample.SteerDegrees,5:0.00} " +
            $"{sample.LocalForwardSpeed,6:0.0} {sample.LocalLateralSpeed,6:0.0} {sample.SlipDegrees,6:0.00} " +
            $"{sample.RequestedFxN,7:0} {sample.ActualWheelFxN,7:0} {sample.RequestedFyN,7:0} {sample.CapacityN,6:0} " +
            $"{sample.LongitudinalUsage,5:0.00} {sample.LateralUsage,5:0.00} {sample.CombinedUsage,5:0.00} " +
            $"{sample.YawMomentNm,7:0} {sample.ActualWheelFyN,8:0} " +
            $"{sample.Symmetric.FyN,6:0}/{sample.LateralFirst.FyN,6:0}/{sample.FrontPriority.FyN,6:0}");
    }

    private static void PrintSweepSample(
        float brake,
        VehicleState state,
        VehicleSimulationParameters parameters,
        VehicleAxleGeometry geometry)
    {
        float frontSlip = (state.FrontLeftSlipAngleDegrees + state.FrontRightSlipAngleDegrees) * 0.5f;
        float rearSlip = (state.RearLeftSlipAngleDegrees + state.RearRightSlipAngleDegrees) * 0.5f;
        float frontReqFx = state.FrontLeftRequestedLongitudinalForceN + state.FrontRightRequestedLongitudinalForceN;
        float frontActFx = state.FrontLeftLongitudinalForceN + state.FrontRightLongitudinalForceN;
        float frontReqFy = state.FrontLeftRequestedLateralForceN + state.FrontRightRequestedLateralForceN;
        float frontActFy = state.FrontLeftLateralForceN + state.FrontRightLateralForceN;
        float frontCap = state.FrontLeftFrictionEllipseGripBudgetN + state.FrontRightFrictionEllipseGripBudgetN;
        float frontLongUse = MathF.Abs(frontActFx) / MathF.Max(1f, frontCap);
        float frontLatUse = MathF.Abs(frontActFy) / MathF.Max(1f, frontCap);
        float frontUse = MathF.Max(state.FrontLeftGripUsage, state.FrontRightGripUsage);
        float frontPressure = (state.FrontLeftBrakePressureRatio + state.FrontRightBrakePressureRatio) * 0.5f;
        float rearPressure = (state.RearLeftBrakePressureRatio + state.RearRightBrakePressureRatio) * 0.5f;
        float frontYaw =
            Moment(-geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontLeftLongitudinalForceN, state.FrontLeftLateralForceN) +
            Moment(geometry.FrontTrackMeters * 0.5f, geometry.CgToFrontAxleMeters, state.FrontRightLongitudinalForceN, state.FrontRightLateralForceN);
        float rearYaw =
            Moment(-geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearLeftLongitudinalForceN, state.RearLeftLateralForceN) +
            Moment(geometry.RearTrackMeters * 0.5f, -geometry.CgToRearAxleMeters, state.RearRightLongitudinalForceN, state.RearRightLateralForceN);
        float totalLoad = state.FrontLeftLoadN + state.FrontRightLoadN + state.RearLeftLoadN + state.RearRightLoadN;
        float idealFrontBrakeShare = (state.FrontLeftLoadN + state.FrontRightLoadN) / MathF.Max(1f, totalLoad);
        float requestedFrontBrakeShare = parameters.BrakeBiasFront;
        float actualFrontBrakeShare = MathF.Abs(frontActFx) / MathF.Max(
            1f,
            MathF.Abs(frontActFx) + MathF.Abs(state.RearLeftLongitudinalForceN + state.RearRightLongitudinalForceN));
        string classification = Classify(frontLongUse, frontLatUse, frontReqFy, frontActFy, frontSlip);

        Console.WriteLine(
            $"    {brake,4:0.00} {state.SpeedMetersPerSecond * 3.6f,5:0.0} " +
            $"{((state.FrontLeftSteerAngleDegrees + state.FrontRightSteerAngleDegrees) * 0.5f),5:0.00} " +
            $"{frontSlip,6:0.00}/{rearSlip,6:0.00} {frontPressure,4:0.00}/{rearPressure,4:0.00} " +
            $"{frontReqFx,7:0} {frontActFx,7:0} {frontReqFy,7:0} {frontActFy,7:0} {frontCap,6:0} " +
            $"{frontLongUse,5:0.00} {frontLatUse,5:0.00} {frontUse,5:0.00} " +
            $"{frontYaw,7:0}/{rearYaw,7:0}/{frontYaw + rearYaw,7:0} " +
            $"{idealFrontBrakeShare,4:0.00}/{requestedFrontBrakeShare,4:0.00} {actualFrontBrakeShare,4:0.00} {classification}");
    }

    private static string Classify(float frontLongUse, float frontLatUse, float frontReqFy, float frontActFy, float frontSlip)
    {
        if (frontLatUse < 0.12f && frontLongUse > 0.70f)
        {
            return "front-budget-spent-on-brake";
        }

        if (MathF.Abs(frontReqFy) > 1000f && MathF.Abs(frontActFy) < MathF.Abs(frontReqFy) * 0.35f)
        {
            return "combined-clamp-removes-lateral";
        }

        if (frontSlip <= 0f)
        {
            return "front-slip-collapsed";
        }

        return "front-participating";
    }

    private static Allocation AllocateSymmetric(float longitudinal, float lateral, float maxForce, float exponent)
    {
        float fx = longitudinal;
        float fy = lateral;
        float usage = ClampCombined(ref fx, ref fy, maxForce, exponent);
        return new Allocation(fx, fy, usage);
    }

    private static Allocation AllocateLateralFirst(float longitudinal, float lateral, float maxForce, float exponent)
    {
        maxForce = MathF.Max(1f, maxForce);
        exponent = MathHelper.Clamp(exponent, 1.2f, 4f);
        float latRatio = MathHelper.Clamp(MathF.Abs(lateral) / maxForce, 0f, 1f);
        float remainingLong = MathF.Pow(MathF.Max(0f, 1f - MathF.Pow(latRatio, exponent)), 1f / exponent) * maxForce;
        float fx = MathF.Sign(longitudinal) * MathF.Min(MathF.Abs(longitudinal), remainingLong);
        float fy = MathF.Sign(lateral) * latRatio * maxForce;
        float usage = CalculateUsage(fx, fy, maxForce, exponent);
        return new Allocation(fx, fy, usage);
    }

    private static Allocation AllocateWithLateralPriority(float longitudinal, float lateral, float maxForce, float exponent, float priority)
    {
        Allocation symmetric = AllocateSymmetric(longitudinal, lateral, maxForce, exponent);
        float prioritizedFy = MathHelper.Lerp(symmetric.FyN, lateral, MathHelper.Clamp(priority, 0f, 0.85f));
        float latRatio = MathHelper.Clamp(MathF.Abs(prioritizedFy) / MathF.Max(1f, maxForce), 0f, 1f);
        float p = MathHelper.Clamp(exponent, 1.2f, 4f);
        float remainingLongitudinalRatio = MathF.Pow(MathF.Max(0f, 1f - MathF.Pow(latRatio, p)), 1f / p);
        float fx = MathF.Sign(longitudinal) * MathF.Min(MathF.Abs(longitudinal), remainingLongitudinalRatio * MathF.Max(1f, maxForce));
        float fy = MathF.Sign(lateral) * latRatio * MathF.Max(1f, maxForce);
        return new Allocation(fx, fy, CalculateUsage(fx, fy, maxForce, exponent));
    }

    private static float ClampCombined(ref float longitudinal, ref float lateral, float maxForce, float exponent)
    {
        float demand = CalculateDemand(longitudinal, lateral, maxForce, exponent);
        if (demand <= 1f)
        {
            return MathF.Pow(MathF.Max(0f, demand), 1f / MathHelper.Clamp(exponent, 1.2f, 4f));
        }

        float scale = MathF.Pow(demand, -1f / MathHelper.Clamp(exponent, 1.2f, 4f));
        longitudinal *= scale;
        lateral *= scale;
        return 1f;
    }

    private static float CalculateUsage(float longitudinal, float lateral, float maxForce, float exponent)
    {
        return MathF.Pow(MathF.Max(0f, CalculateDemand(longitudinal, lateral, maxForce, exponent)), 1f / MathHelper.Clamp(exponent, 1.2f, 4f));
    }

    private static float CalculateDemand(float longitudinal, float lateral, float maxForce, float exponent)
    {
        maxForce = MathF.Max(1f, maxForce);
        exponent = MathHelper.Clamp(exponent, 1.2f, 4f);
        return MathF.Pow(MathF.Abs(longitudinal / maxForce), exponent) +
            MathF.Pow(MathF.Abs(lateral / maxForce), exponent);
    }

    private static float Moment(float right, float forward, float forwardForce, float rightForce)
    {
        return right * forwardForce - forward * rightForce;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private readonly record struct WheelSample(
        string Wheel,
        float LoadN,
        float SteerDegrees,
        float LocalForwardSpeed,
        float LocalLateralSpeed,
        float SlipDegrees,
        float RequestedFxN,
        float ActualWheelFxN,
        float RequestedFyN,
        float CapacityN,
        float LongitudinalUsage,
        float LateralUsage,
        float CombinedUsage,
        float YawMomentNm,
        float ActualWheelFyN,
        Allocation Symmetric,
        Allocation LateralFirst,
        Allocation FrontPriority);

    private readonly record struct Allocation(float FxN, float FyN, float Usage);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
