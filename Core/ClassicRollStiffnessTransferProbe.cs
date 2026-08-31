using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class ClassicRollStiffnessTransferProbe
{
    private const float Dt = 1f / 120f;

    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engine = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);

        Console.WriteLine($"Classic roll-stiffness transfer probe: {parameters.DisplayName}, model={engine.HandlingModel}");
        Console.WriteLine(
            $"  suspension data: spring F/R={parameters.FrontSpringRateNPerM:0}/{parameters.RearSpringRateNPerM:0}N/m " +
            $"ARB F/R={parameters.FrontAntiRollBarRateNmPerRad:0}/{parameters.RearAntiRollBarRateNmPerRad:0}Nm/rad");
        Console.WriteLine(
            "  modes: old static-weight lateral split versus suspension-roll-stiffness split; production now uses suspension split");
        Console.WriteLine(
            "  columns: mode case t speed steer brake latG longG share static/roll totalLatTgt F/R latTgt F/R loads FL/FR/RL/RR insideRear rearCap frontCap yawF/R beta yaw");

        RunCase(parameters, engine, "steady-turn", (tick, time) => new VehicleInput(0.25f, 0f, 0.85f));
        RunCase(parameters, engine, "lift-turn", (tick, time) => new VehicleInput(time < 0.25f ? 0.25f : 0f, 0f, 0.85f));
        RunCase(parameters, engine, "brake-turn", (tick, time) => new VehicleInput(0f, time < 0.65f ? 0.75f : 0.15f, 0.85f));

        Console.WriteLine("Classic roll-stiffness transfer probe complete.");
    }

    private static void RunCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string label,
        Func<int, float, VehicleInput> inputForTick)
    {
        RunMode(parameters, engine, label, "old-static", true, inputForTick);
        RunMode(parameters, engine, label, "susp-roll", false, inputForTick);
    }

    private static void RunMode(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engine,
        string label,
        string mode,
        bool useStaticWeightSplit,
        Func<int, float, VehicleInput> inputForTick)
    {
        ClassicFourWheelVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engine)
        {
            UseStaticWeightLateralTransferSplitForProbe = useStaticWeightSplit
        };
        simulator.SetManualTransmission(true);
        simulator.State.Gear = 4;
        simulator.State.Velocity = new Vector2(0f, 150f / 3.6f);

        for (int tick = 1; tick <= SecondsToTicks(1.2f); tick++)
        {
            float time = tick * Dt;
            simulator.Update(inputForTick(tick, time), Dt);
            if (IsCheckpoint(time))
            {
                PrintSample(mode, label, time, simulator.State, parameters);
            }
        }
    }

    private static void PrintSample(
        string mode,
        string label,
        float time,
        VehicleState state,
        VehicleSimulationParameters parameters)
    {
        float staticShare = state.ClassicStaticWeightFrontLateralTransferShare;
        float rollShare = state.ClassicFrontRollStiffnessShare;
        float totalTarget = MathF.Abs(state.ClassicTargetFrontLateralLoadTransferN) +
            MathF.Abs(state.ClassicTargetRearLateralLoadTransferN);
        float insideRearLoad = state.RearLeftLoadN < state.RearRightLoadN
            ? state.RearLeftLoadN
            : state.RearRightLoadN;
        float outsideRearLoad = MathF.Max(state.RearLeftLoadN, state.RearRightLoadN);
        float rearCapacity = state.RearLeftFrictionEllipseGripBudgetN + state.RearRightFrictionEllipseGripBudgetN;
        float frontCapacity = state.FrontLeftFrictionEllipseGripBudgetN + state.FrontRightFrictionEllipseGripBudgetN;
        float frontYaw = state.ClassicFrontYawAccelerationDegreesPerSecondSquared;
        float rearYaw = state.ClassicRearYawAccelerationDegreesPerSecondSquared;

        Console.WriteLine(
            $"  {mode,-10} {label,-11} {time,4:0.00} {state.SpeedMetersPerSecond * 3.6f,6:0.0} " +
            $"{state.Steer,5:0.00} {state.Brake,5:0.00} {state.LateralAcceleration / 9.81f,5:0.00} {state.LongitudinalAcceleration / 9.81f,5:0.00} " +
            $"{staticShare,4:0.00}/{rollShare,4:0.00} {totalTarget,7:0} " +
            $"{state.ClassicTargetFrontLateralLoadTransferN,6:0}/{state.ClassicTargetRearLateralLoadTransferN,6:0} " +
            $"{state.FrontLeftLoadN,5:0}/{state.FrontRightLoadN,5:0}/{state.RearLeftLoadN,5:0}/{state.RearRightLoadN,5:0} " +
            $"{insideRearLoad,5:0}/{outsideRearLoad,5:0} {rearCapacity,6:0} {frontCapacity,6:0} " +
            $"{frontYaw,6:0}/{rearYaw,6:0} {state.ClassicBodySlipAngleDegrees,5:0.00} " +
            $"{MathHelper.ToDegrees(state.YawRateRadiansPerSecond),6:0.0}");
    }

    private static bool IsCheckpoint(float time)
    {
        return MathF.Abs(time - 0.25f) < Dt * 0.5f ||
               MathF.Abs(time - 0.50f) < Dt * 0.5f ||
               MathF.Abs(time - 0.75f) < Dt * 0.5f ||
               MathF.Abs(time - 1.00f) < Dt * 0.5f ||
               MathF.Abs(time - 1.20f) < Dt * 0.5f;
    }

    private static int SecondsToTicks(float seconds)
    {
        return Math.Max(1, (int)MathF.Round(seconds / Dt));
    }

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
