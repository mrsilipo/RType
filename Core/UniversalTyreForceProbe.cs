using Microsoft.Xna.Framework;
using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class UniversalTyreForceProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SimulationEngineParameters engineParameters = SimulationEngineDefinitionLoader.Load(options.SimulationEngineDefinitionPath);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(options.SurfaceDefinitionPath);

        float wheelLoadN = parameters.MassKg * 9.81f * 0.25f;
        float gripBudgetN = wheelLoadN * parameters.FrontTyres.PeakFriction * surfaces.Road.StaticFrictionCoefficient;

        float slidingForceFloor = engineParameters.TyreForce.SlidingForceFloor;
        float lateralLongitudinalGripCoupling = engineParameters.TyreForce.LateralLongitudinalGripCoupling;
        ProbeRequestClamp(gripBudgetN, slidingForceFloor, lateralLongitudinalGripCoupling);
        ProbeLateralBudgetCost(gripBudgetN, slidingForceFloor, lateralLongitudinalGripCoupling);
        ProbeBrakeDirection(gripBudgetN, slidingForceFloor, lateralLongitudinalGripCoupling);
        ProbeNoRequestNoPropulsion(gripBudgetN, slidingForceFloor, lateralLongitudinalGripCoupling);
        ProbeSlidingPlateau(gripBudgetN, slidingForceFloor);
        ProbeLiveHighSpeedPowerBalance(parameters, engineParameters);

        Console.WriteLine("Universal tyre force probe passed: request clamp, lateral budget, braking direction, sliding plateau, no-request propulsion guard, and live high-speed balance are stable.");
    }

    private static void ProbeRequestClamp(float gripBudgetN, float slidingForceFloor, float lateralLongitudinalGripCoupling)
    {
        UnifiedTyreForceResult result = UnifiedTyreForceModel.CalculateFromRequest(
            new TyreForceRequest(gripBudgetN, gripBudgetN * 1.75f, 0.08f, 0f, 0.10f, 0.12f),
            tyreShape: 12.5f,
            slidingCurveFloor: slidingForceFloor,
            lateralLongitudinalGripCoupling: lateralLongitudinalGripCoupling);
        Require(result.LongitudinalForceN <= gripBudgetN + 0.1f, "drive request exceeded grip budget.");
        Require(result.LongitudinalForceN > gripBudgetN * 0.98f, "drive request did not use available straight-line grip.");
        Console.WriteLine($"Request clamp: requested={gripBudgetN * 1.75f:0}N actual={result.LongitudinalForceN:0}N budget={gripBudgetN:0}N");
    }

    private static void ProbeLateralBudgetCost(float gripBudgetN, float slidingForceFloor, float lateralLongitudinalGripCoupling)
    {
        UnifiedTyreForceResult straight = UnifiedTyreForceModel.CalculateFromRequest(
            new TyreForceRequest(gripBudgetN, gripBudgetN * 0.75f, 0.08f, 0f, 0.10f, 0.12f),
            tyreShape: 12.5f,
            slidingCurveFloor: slidingForceFloor,
            lateralLongitudinalGripCoupling: lateralLongitudinalGripCoupling);
        UnifiedTyreForceResult cornering = UnifiedTyreForceModel.CalculateFromRequest(
            new TyreForceRequest(gripBudgetN, gripBudgetN * 0.75f, 0.08f, 0.18f, 0.10f, 0.12f),
            tyreShape: 12.5f,
            slidingCurveFloor: slidingForceFloor,
            lateralLongitudinalGripCoupling: lateralLongitudinalGripCoupling);
        Require(cornering.LongitudinalForceN <= straight.LongitudinalForceN + 0.1f, "cornering increased longitudinal force.");
        Require(MathF.Abs(cornering.LateralForceN) > MathF.Abs(straight.LateralForceN) + 1f, "cornering did not consume lateral grip.");
        Require(cornering.LongitudinalForceN >= straight.LongitudinalForceN * 0.72f, "cornering consumed too much longitudinal drive for the classic tyre foundation.");
        Require(cornering.GripUsage <= 1.01f, "coupled tyre force exceeded the configured grip budget.");
        Console.WriteLine($"Lateral budget: straightLong={straight.LongitudinalForceN:0}N cornerLong={cornering.LongitudinalForceN:0}N cornerLat={cornering.LateralForceN:0}N coupling={lateralLongitudinalGripCoupling:0.00}");
    }

    private static void ProbeBrakeDirection(float gripBudgetN, float slidingForceFloor, float lateralLongitudinalGripCoupling)
    {
        UnifiedTyreForceResult result = UnifiedTyreForceModel.CalculateFromRequest(
            new TyreForceRequest(gripBudgetN, -gripBudgetN * 0.55f, -0.12f, 0.04f, 0.10f, 0.12f),
            tyreShape: 12.5f,
            slidingCurveFloor: slidingForceFloor,
            lateralLongitudinalGripCoupling: lateralLongitudinalGripCoupling);
        Require(result.LongitudinalForceN < 0f, "brake request did not create braking force.");
        Require(result.GripUsage <= 1.01f, "braking case exceeded the configured coupled grip budget.");
        Console.WriteLine($"Brake direction: long={result.LongitudinalForceN:0}N lat={result.LateralForceN:0}N usage={result.GripUsage:0.00}");
    }

    private static void ProbeNoRequestNoPropulsion(float gripBudgetN, float slidingForceFloor, float lateralLongitudinalGripCoupling)
    {
        UnifiedTyreForceResult result = UnifiedTyreForceModel.CalculateFromRequest(
            new TyreForceRequest(gripBudgetN, 0f, 0.04f, 0.16f, 0.10f, 0.12f),
            tyreShape: 12.5f,
            slidingCurveFloor: slidingForceFloor,
            lateralLongitudinalGripCoupling: lateralLongitudinalGripCoupling);
        Require(MathF.Abs(result.LongitudinalForceN) <= 0.001f, "free-rolling tyre generated longitudinal propulsion.");
        Console.WriteLine($"No-request propulsion guard: long={result.LongitudinalForceN:0.000}N lat={result.LateralForceN:0}N");
    }

    private static void ProbeSlidingPlateau(float gripBudgetN, float slidingForceFloor)
    {
        UnifiedTyreForceDiagnostics diagnostics = UnifiedTyreForceModel.CalculateFromGripBudget(
            gripBudgetN,
            relaxedLongitudinalSlipRatio: 0f,
            relaxedLateralSlip: 1.4f,
            longitudinalPeakSlipRatio: 0.10f,
            lateralPeakSlip: 0.12f,
            tyreShape: 1f,
            slidingCurveFloor: slidingForceFloor);
        float minimumExpectedForce = gripBudgetN * MathHelper.Clamp(slidingForceFloor, 0f, 1f) - 0.1f;
        Require(diagnostics.TotalForceN >= minimumExpectedForce, "sliding plateau dropped below configured force floor.");
        Console.WriteLine($"Sliding plateau: slip={diagnostics.TotalSlip:0.00} force={diagnostics.TotalForceN:0}N floor={slidingForceFloor:0.00}");
    }

    private static void ProbeLiveHighSpeedPowerBalance(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters)
    {
        const float dt = 1f / 120f;
        LiveCase straight = RunLiveCase(parameters, engineParameters, 33.3f, 0f, dt);
        LiveCase corner = RunLiveCase(parameters, engineParameters, 33.3f, 0.28f, dt);
        Require(corner.AverageLongitudinalAcceleration <= straight.AverageLongitudinalAcceleration + 0.35f, "cornering created more drive acceleration than the straight case.");
        Require(MathF.Abs(corner.AverageLateralForceN) > 1200f, "high-speed corner did not retain useful lateral tyre force.");
        Console.WriteLine(
            $"Live 120km/h balance: straightAccel={straight.AverageLongitudinalAcceleration:0.00}m/s2 " +
            $"cornerAccel={corner.AverageLongitudinalAcceleration:0.00}m/s2 cornerLat={corner.AverageLateralForceN:0}N");
    }

    private static LiveCase RunLiveCase(
        VehicleSimulationParameters parameters,
        SimulationEngineParameters engineParameters,
        float targetSpeedMetersPerSecond,
        float steer,
        float dt)
    {
        SimpleVehicleSimulator simulator = new(
            new FlatSurfaceSampler(),
            new Vector3(0f, 0.06f, 0f),
            0f,
            parameters,
            engineParameters);

        for (int i = 0; i < 7200 && simulator.State.SpeedMetersPerSecond < targetSpeedMetersPerSecond; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, 0f), dt);
        }

        for (int i = 0; i < 60; i++)
        {
            simulator.Update(new VehicleInput(0.25f, 0f, 0f), dt);
        }

        float sumLongAcceleration = 0f;
        float sumLateralForce = 0f;
        for (int i = 0; i < 180; i++)
        {
            simulator.Update(new VehicleInput(1f, 0f, steer), dt);
            VehicleState state = simulator.State;
            sumLongAcceleration += state.LongitudinalAcceleration;
            sumLateralForce +=
                state.FrontLeftLateralForceN +
                state.FrontRightLateralForceN +
                state.RearLeftLateralForceN +
                state.RearRightLateralForceN;
        }

        return new LiveCase(sumLongAcceleration / 180f, sumLateralForce / 180f);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Universal tyre force probe failed: {message}");
        }
    }

    private readonly record struct LiveCase(float AverageLongitudinalAcceleration, float AverageLateralForceN);

    private sealed class FlatSurfaceSampler : ITrackSurfaceSampler
    {
        public SurfaceSample Sample(Vector3 position)
        {
            return new SurfaceSample("ROAD", 1f);
        }
    }
}
