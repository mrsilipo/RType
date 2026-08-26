using RType.Data;
using RType.Vehicle;

namespace RType.Core;

internal static class EnginePowerComposerProbe
{
    public static void Run()
    {
        TorqueCurvePoint[] source =
        [
            new TorqueCurvePoint(3500f, 120f),
            new TorqueCurvePoint(6200f, 150f),
            new TorqueCurvePoint(8400f, 145f)
        ];

        float fuelMultiplier = EnginePowerComposer.ResolveFuelEffectivePowerMultiplier(new EngineFuelCompositionInput(
            CompressionRatio: 12.4f,
            BasePowerMultiplier: 1.0f,
            HighCompressionPowerMultiplier: 1.08f,
            HighCompressionStartsAt: 12.0f));

        TorqueCurvePoint[] drive = EnginePowerComposer.ResolveDriveTorqueCurve(new EngineTorqueCompositionInput(
            SourceCurve: source,
            BaseDisplacementCc: 1595f,
            DisplacementCc: 1715f,
            BaseCompressionRatio: 10.8f,
            CompressionRatio: 12.4f,
            VtecEnabled: true,
            VtecActivationRpm: 5800f,
            VtecTransitionWidthRpm: 350f,
            LowCamFlowMultiplier: 1.0f,
            HighCamFlowMultiplier: 1.34f,
            IntakeFlowScale: 1.18f,
            ExhaustFlowScale: 1.16f,
            FuelEffectivePowerMultiplier: fuelMultiplier));

        TorqueCurvePoint[] brake = EnginePowerComposer.ResolveEngineBrakeTorqueCurve(new EngineBrakeCompositionInput(
            SourceCurve:
            [
                new TorqueCurvePoint(1000f, 14f),
                new TorqueCurvePoint(8400f, 85f)
            ],
            BaseDisplacementCc: 1595f,
            DisplacementCc: 1715f,
            BaseCompressionRatio: 10.8f,
            CompressionRatio: 12.4f,
            BaseRotationalInertiaKgM2: 0.18f,
            RotationalInertiaKgM2: 0.132f,
            IdleRpm: 900f,
            PowerRedlineRpm: 8200f,
            LimiterHardCutRpm: 8400f));
        EnginePowerCompositionTrace trace = EnginePowerComposer.ResolveCompositionTrace(
            new EngineTorqueCompositionInput(
                SourceCurve: source,
                BaseDisplacementCc: 1595f,
                DisplacementCc: 1715f,
                BaseCompressionRatio: 10.8f,
                CompressionRatio: 12.4f,
                VtecEnabled: true,
                VtecActivationRpm: 5800f,
                VtecTransitionWidthRpm: 350f,
                LowCamFlowMultiplier: 1.0f,
                HighCamFlowMultiplier: 1.34f,
                IntakeFlowScale: 1.18f,
                ExhaustFlowScale: 1.16f,
                FuelEffectivePowerMultiplier: fuelMultiplier),
            new EngineBrakeCompositionInput(
                SourceCurve:
                [
                    new TorqueCurvePoint(1000f, 14f),
                    new TorqueCurvePoint(8400f, 85f)
                ],
                BaseDisplacementCc: 1595f,
                DisplacementCc: 1715f,
                BaseCompressionRatio: 10.8f,
                CompressionRatio: 12.4f,
                BaseRotationalInertiaKgM2: 0.18f,
                RotationalInertiaKgM2: 0.132f,
                IdleRpm: 900f,
                PowerRedlineRpm: 8200f,
                LimiterHardCutRpm: 8400f),
            drive,
            brake);

        Console.WriteLine("Engine power composer probe");
        Console.WriteLine($"  fuel multiplier: {fuelMultiplier:0.000}");
        Console.WriteLine($"  drive points: {drive.Length}, low {drive[0].TorqueNm:0.0}Nm, high {drive[1].TorqueNm:0.0}Nm");
        Console.WriteLine($"  brake points: {brake.Length}, peak {brake.Max(point => point.TorqueNm):0.0}Nm");
        Console.WriteLine($"  trace: baseline peak {trace.BaselinePeakTorqueNm:0.0}Nm -> resolved peak {trace.ResolvedPeakTorqueNm:0.0}Nm, displacement x{trace.DisplacementScale:0.000}, compression x{trace.CompressionScale:0.000}, high flow x{trace.HighFlowScale:0.000}, fuel x{trace.FuelEffectivePowerMultiplier:0.000}, engine brake x{trace.EngineBrakeScale:0.000}");

        Require(Math.Abs(fuelMultiplier - 1.0213333f) < 0.001f, "fuel high-compression blend changed unexpectedly");
        Require(drive.Length == source.Length, "drive curve point count changed");
        Require(drive[1].TorqueNm > drive[0].TorqueNm, "high-rpm VTEC/flow torque should exceed low-rpm torque in this fixture");
        Require(brake.Length == 2 && brake[1].TorqueNm > 100f, "engine braking should scale up with compression/displacement/lower inertia");
        Require(Math.Abs(trace.BaselinePeakTorqueNm - 150f) < 0.001f, "trace should preserve baseline peak torque");
        Require(Math.Abs(trace.ResolvedPeakTorqueNm - drive.Max(point => point.TorqueNm)) < 0.001f, "trace resolved peak torque should match resolved curve");
        Require(trace.DisplacementScale > 1f && trace.HighFlowScale > trace.LowFlowScale, "trace should expose displacement and high-cam flow gains");
        Require(Math.Abs(trace.EngineBrakeScale - (brake.Max(point => point.TorqueNm) / 85f)) < 0.001f, "trace engine brake scale should match resolved engine-brake curve");

        Console.WriteLine("  result: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Engine power composer probe failed: {message}");
        }
    }
}
