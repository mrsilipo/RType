using RType.Data;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public static class FrictionEllipseProbe
{
    public static void Run(GameLaunchOptions options)
    {
        VehicleSimulationParameters parameters = VehicleRuntimeLoader.LoadSimulationParameters(
            options.VehiclePath,
            options.GarageProfilePath,
            options.GarageVehicleIdOrPath,
            options.GarageSetupIdOrPath);
        SurfaceLibrary surfaces = SurfaceLibraryLoader.Load(options.SurfaceDefinitionPath);

        float wheelLoadN = parameters.MassKg * 9.81f * 0.25f;
        float grip = parameters.FrontTyres.PeakFriction;
        float roadMu = surfaces.Road.StaticFrictionCoefficient;

        ProbeFfUndersteer(wheelLoadN, grip, roadMu);
        ProbeFrPowerOversteer(wheelLoadN, grip, roadMu);
        ProbeAwdSharedSlip(wheelLoadN, grip, roadMu);
        ProbeHandbrakeLock(wheelLoadN, grip, roadMu);
        ProbeHighSpeedStraight(wheelLoadN, grip, roadMu);
        ProbeLowSpeedStop(wheelLoadN, grip, roadMu);

        Console.WriteLine("Friction ellipse probe passed: FF, FR, AWD, handbrake, straight-line, and stopped cases are diagnostic-stable.");
    }

    private static void ProbeFfUndersteer(float loadN, float tyrePeakFriction, float surfaceMu)
    {
        AxleDiagnostics front = CalculateAxle(loadN, tyrePeakFriction, surfaceMu, 0.34f, 0.18f);
        AxleDiagnostics rear = CalculateAxle(loadN, tyrePeakFriction, surfaceMu, 0.02f, 0.18f);
        Require(front.LongitudinalShare > rear.LongitudinalShare + 0.32f, "FF front tyres did not spend more grip longitudinally.");
        Require(front.LateralShare < rear.LateralShare - 0.30f, "FF front tyres did not lose lateral share under drive slip.");
        Console.WriteLine(
            $"FF understeer diagnostic: front long/lat {front.LongitudinalShare:0.00}/{front.LateralShare:0.00}, rear long/lat {rear.LongitudinalShare:0.00}/{rear.LateralShare:0.00}");
    }

    private static void ProbeFrPowerOversteer(float loadN, float tyrePeakFriction, float surfaceMu)
    {
        AxleDiagnostics front = CalculateAxle(loadN, tyrePeakFriction, surfaceMu, 0.02f, 0.20f);
        AxleDiagnostics rear = CalculateAxle(loadN, tyrePeakFriction, surfaceMu, 0.38f, 0.20f);
        Require(rear.LongitudinalShare > front.LongitudinalShare + 0.32f, "FR rear tyres did not spend more grip longitudinally.");
        Require(rear.LateralShare < front.LateralShare - 0.30f, "FR rear tyres did not lose lateral share under power slip.");
        Console.WriteLine(
            $"FR power-oversteer diagnostic: front long/lat {front.LongitudinalShare:0.00}/{front.LateralShare:0.00}, rear long/lat {rear.LongitudinalShare:0.00}/{rear.LateralShare:0.00}");
    }

    private static void ProbeAwdSharedSlip(float loadN, float tyrePeakFriction, float surfaceMu)
    {
        AxleDiagnostics front = CalculateAxle(loadN, tyrePeakFriction, surfaceMu, 0.20f, 0.20f);
        AxleDiagnostics rear = CalculateAxle(loadN, tyrePeakFriction, surfaceMu, 0.18f, 0.20f);
        Require(MathF.Abs(front.LongitudinalShare - rear.LongitudinalShare) < 0.06f, "AWD grip split was not balanced front-to-rear.");
        Require(front.TotalSlip > 0.25f && rear.TotalSlip > 0.25f, "AWD shared slip was too small to represent four-wheel drift.");
        Console.WriteLine(
            $"AWD four-wheel drift diagnostic: front total/share {front.TotalSlip:0.00}/{front.LongitudinalShare:0.00}, rear total/share {rear.TotalSlip:0.00}/{rear.LongitudinalShare:0.00}");
    }

    private static void ProbeHandbrakeLock(float loadN, float tyrePeakFriction, float surfaceMu)
    {
        UnifiedTyreForceDiagnostics rear = UnifiedTyreForceModel.Calculate(loadN, surfaceMu, tyrePeakFriction, -1.10f, 0.12f, 0.10f, 0.12f);
        Require(rear.LongitudinalShare > 0.98f, "Handbrake lock did not consume the rear tyre grip budget longitudinally.");
        Require(rear.LateralShare < 0.12f, "Handbrake lock left too much rear lateral grip share.");
        Require(rear.LongitudinalForceN > 0f, "Negative braking slip did not produce opposing positive longitudinal force.");
        Console.WriteLine(
            $"Handbrake diagnostic: rear long/lat share {rear.LongitudinalShare:0.00}/{rear.LateralShare:0.00}, force {rear.LongitudinalForceN:0}N/{rear.LateralForceN:0}N");
    }

    private static void ProbeHighSpeedStraight(float loadN, float tyrePeakFriction, float surfaceMu)
    {
        UnifiedTyreForceDiagnostics wheel = UnifiedTyreForceModel.Calculate(loadN, surfaceMu, tyrePeakFriction, 0.02f, 0f, 0.10f, 0.12f);
        Require(wheel.TotalSlip < 0.25f, "High-speed straight-line total slip diagnostic was too high.");
        Require(wheel.LateralShare <= 0.001f, "High-speed straight-line diagnostic produced lateral grip demand.");
        Require(wheel.LongitudinalForceN < 0f, "Positive drive slip did not produce opposing negative diagnostic force.");
        Console.WriteLine(
            $"High-speed straight diagnostic: totalSlip {wheel.TotalSlip:0.00}, long/lat share {wheel.LongitudinalShare:0.00}/{wheel.LateralShare:0.00}");
    }

    private static void ProbeLowSpeedStop(float loadN, float tyrePeakFriction, float surfaceMu)
    {
        UnifiedTyreForceDiagnostics wheel = UnifiedTyreForceModel.Calculate(loadN, surfaceMu, tyrePeakFriction, 0f, 0f, 0.10f, 0.12f);
        Require(wheel.TotalForceN == 0f && wheel.GripUsage == 0f, "Stopped wheel diagnostic produced force with no slip.");
        Console.WriteLine("Low-speed stop diagnostic: no slip produces no ellipse force.");
    }

    private static AxleDiagnostics CalculateAxle(
        float loadN,
        float tyrePeakFriction,
        float surfaceMu,
        float longitudinalSlip,
        float lateralSlip)
    {
        UnifiedTyreForceDiagnostics left = UnifiedTyreForceModel.Calculate(loadN, surfaceMu, tyrePeakFriction, longitudinalSlip, lateralSlip, 0.10f, 0.12f);
        UnifiedTyreForceDiagnostics right = UnifiedTyreForceModel.Calculate(loadN, surfaceMu, tyrePeakFriction, longitudinalSlip, lateralSlip, 0.10f, 0.12f);
        return new AxleDiagnostics(
            (left.TotalSlip + right.TotalSlip) * 0.5f,
            (left.LongitudinalShare + right.LongitudinalShare) * 0.5f,
            (left.LateralShare + right.LateralShare) * 0.5f,
            (left.GripUsage + right.GripUsage) * 0.5f);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Friction ellipse probe failed: {message}");
        }
    }

    private readonly record struct AxleDiagnostics(
        float TotalSlip,
        float LongitudinalShare,
        float LateralShare,
        float GripUsage);
}
