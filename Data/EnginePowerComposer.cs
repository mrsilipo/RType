using RType.Vehicle;

namespace RType.Data;

internal static class EnginePowerComposer
{
    private const float StockBaselineLowCamFlowMultiplier = 1f;
    private const float StockBaselineHighCamFlowMultiplier = 1.22f;
    private const float FlowModifierAuthority = 0.45f;

    public static float ResolveFuelEffectivePowerMultiplier(EngineFuelCompositionInput input)
    {
        float highCompressionBlend = input.CompressionRatio <= input.HighCompressionStartsAt
            ? 0f
            : Clamp((input.CompressionRatio - input.HighCompressionStartsAt) / 1.5f, 0f, 1f);

        return input.BasePowerMultiplier +
            ((input.HighCompressionPowerMultiplier - input.BasePowerMultiplier) * highCompressionBlend);
    }

    public static TorqueCurvePoint[] ResolveDriveTorqueCurve(EngineTorqueCompositionInput input)
    {
        if (input.SourceCurve.Length == 0)
        {
            return [];
        }

        float displacementScale = input.BaseDisplacementCc > 0f
            ? Clamp(input.DisplacementCc / input.BaseDisplacementCc, 0.65f, 1.55f)
            : 1f;
        float lowCamScale = 1f + ((input.LowCamFlowMultiplier - StockBaselineLowCamFlowMultiplier) * FlowModifierAuthority);
        float highCamScale = 1f + ((input.HighCamFlowMultiplier - StockBaselineHighCamFlowMultiplier) * FlowModifierAuthority);
        float intakeScale = 1f + ((input.IntakeFlowScale - 1f) * FlowModifierAuthority);
        float exhaustScale = 1f + ((input.ExhaustFlowScale - 1f) * FlowModifierAuthority);
        float lowFlowScale = Clamp((lowCamScale + intakeScale + exhaustScale) / 3f, 0.75f, 1.35f);
        float highFlowScale = Clamp((highCamScale + intakeScale + exhaustScale) / 3f, 0.75f, 1.55f);
        float compressionScale = Clamp(1f + ((input.CompressionRatio - input.BaseCompressionRatio) * 0.018f), 0.86f, 1.18f);

        TorqueCurvePoint[] result = new TorqueCurvePoint[input.SourceCurve.Length];
        for (int i = 0; i < input.SourceCurve.Length; i++)
        {
            TorqueCurvePoint point = input.SourceCurve[i];
            float vtecBlend = input.VtecEnabled && input.VtecTransitionWidthRpm > 0f
                ? Clamp((point.Rpm - input.VtecActivationRpm) / input.VtecTransitionWidthRpm, 0f, 1f)
                : 0f;
            float flowScale = lowFlowScale + ((highFlowScale - lowFlowScale) * vtecBlend);
            result[i] = new TorqueCurvePoint(point.Rpm, point.TorqueNm * displacementScale * flowScale * compressionScale * input.FuelEffectivePowerMultiplier);
        }

        return result;
    }

    public static EnginePowerCompositionTrace ResolveCompositionTrace(
        EngineTorqueCompositionInput torqueInput,
        EngineBrakeCompositionInput brakeInput,
        TorqueCurvePoint[] resolvedTorqueCurve,
        TorqueCurvePoint[] resolvedEngineBrakeCurve)
    {
        float displacementScale = CalculateDisplacementScale(torqueInput.BaseDisplacementCc, torqueInput.DisplacementCc, 0.65f, 1.55f);
        float lowCamScale = 1f + ((torqueInput.LowCamFlowMultiplier - StockBaselineLowCamFlowMultiplier) * FlowModifierAuthority);
        float highCamScale = 1f + ((torqueInput.HighCamFlowMultiplier - StockBaselineHighCamFlowMultiplier) * FlowModifierAuthority);
        float intakeScale = 1f + ((torqueInput.IntakeFlowScale - 1f) * FlowModifierAuthority);
        float exhaustScale = 1f + ((torqueInput.ExhaustFlowScale - 1f) * FlowModifierAuthority);
        float lowFlowScale = Clamp((lowCamScale + intakeScale + exhaustScale) / 3f, 0.75f, 1.35f);
        float highFlowScale = Clamp((highCamScale + intakeScale + exhaustScale) / 3f, 0.75f, 1.55f);
        float compressionScale = Clamp(1f + ((torqueInput.CompressionRatio - torqueInput.BaseCompressionRatio) * 0.018f), 0.86f, 1.18f);

        float engineBrakeDisplacementScale = CalculateDisplacementScale(brakeInput.BaseDisplacementCc, brakeInput.DisplacementCc, 0.75f, 1.45f);
        float engineBrakeCompressionScale = Clamp(1f + ((brakeInput.CompressionRatio - brakeInput.BaseCompressionRatio) * 0.022f), 0.85f, 1.22f);
        float engineBrakeInertiaScale = brakeInput.BaseRotationalInertiaKgM2 > 0f
            ? Clamp(brakeInput.BaseRotationalInertiaKgM2 / MathF.Max(0.08f, brakeInput.RotationalInertiaKgM2), 0.75f, 1.28f)
            : 1f;
        float engineBrakeScale = engineBrakeDisplacementScale * engineBrakeCompressionScale * engineBrakeInertiaScale;

        return new EnginePowerCompositionTrace(
            BaselineDisplacementCc: torqueInput.BaseDisplacementCc,
            ResolvedDisplacementCc: torqueInput.DisplacementCc,
            BaseCompressionRatio: torqueInput.BaseCompressionRatio,
            ResolvedCompressionRatio: torqueInput.CompressionRatio,
            DisplacementScale: displacementScale,
            CompressionScale: compressionScale,
            LowCamScale: lowCamScale,
            HighCamScale: highCamScale,
            IntakeScale: intakeScale,
            ExhaustScale: exhaustScale,
            LowFlowScale: lowFlowScale,
            HighFlowScale: highFlowScale,
            FuelEffectivePowerMultiplier: torqueInput.FuelEffectivePowerMultiplier,
            VtecEnabled: torqueInput.VtecEnabled,
            VtecActivationRpm: torqueInput.VtecActivationRpm,
            VtecTransitionWidthRpm: torqueInput.VtecTransitionWidthRpm,
            BaselinePeakTorqueNm: FindPeakTorque(torqueInput.SourceCurve),
            ResolvedPeakTorqueNm: FindPeakTorque(resolvedTorqueCurve),
            BaselinePeakEngineBrakeTorqueNm: FindPeakTorque(brakeInput.SourceCurve),
            ResolvedPeakEngineBrakeTorqueNm: FindPeakTorque(resolvedEngineBrakeCurve),
            EngineBrakeDisplacementScale: engineBrakeDisplacementScale,
            EngineBrakeCompressionScale: engineBrakeCompressionScale,
            EngineBrakeInertiaScale: engineBrakeInertiaScale,
            EngineBrakeScale: engineBrakeScale);
    }

    public static TorqueCurvePoint[] ResolveEngineBrakeTorqueCurve(EngineBrakeCompositionInput input)
    {
        TorqueCurvePoint[] source = input.SourceCurve.Length > 0
            ? input.SourceCurve
            : CreateDefaultEngineBrakeTorqueCurve(input);

        float displacementScale = CalculateDisplacementScale(input.BaseDisplacementCc, input.DisplacementCc, 0.75f, 1.45f);
        float compressionScale = Clamp(1f + ((input.CompressionRatio - input.BaseCompressionRatio) * 0.022f), 0.85f, 1.22f);
        float inertiaScale = input.BaseRotationalInertiaKgM2 > 0f
            ? Clamp(input.BaseRotationalInertiaKgM2 / MathF.Max(0.08f, input.RotationalInertiaKgM2), 0.75f, 1.28f)
            : 1f;
        float engineBrakeScale = displacementScale * compressionScale * inertiaScale;

        TorqueCurvePoint[] result = new TorqueCurvePoint[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            TorqueCurvePoint point = source[i];
            result[i] = new TorqueCurvePoint(point.Rpm, point.TorqueNm * engineBrakeScale);
        }

        return result;
    }

    private static TorqueCurvePoint[] CreateDefaultEngineBrakeTorqueCurve(EngineBrakeCompositionInput input)
    {
        float idleRpm = MathF.Max(650f, input.IdleRpm);
        float redlineRpm = MathF.Max(idleRpm + 1200f, input.PowerRedlineRpm);
        float limiterRpm = MathF.Max(redlineRpm, input.LimiterHardCutRpm);
        float displacementLiters = MathF.Max(1.0f, input.DisplacementCc / 1000f);
        float baseTorque = 7.5f * displacementLiters;

        return
        [
            new TorqueCurvePoint(idleRpm, baseTorque),
            new TorqueCurvePoint((idleRpm + redlineRpm) * 0.45f, baseTorque * 2.35f),
            new TorqueCurvePoint(redlineRpm * 0.78f, baseTorque * 4.2f),
            new TorqueCurvePoint(limiterRpm, baseTorque * 5.9f)
        ];
    }

    private static float CalculateDisplacementScale(float baseDisplacementCc, float displacementCc, float min, float max)
    {
        return baseDisplacementCc > 0f
            ? Clamp(displacementCc / baseDisplacementCc, min, max)
            : 1f;
    }

    private static float FindPeakTorque(TorqueCurvePoint[] curve)
    {
        return curve.Length == 0 ? 0f : curve.Max(point => point.TorqueNm);
    }

    private static float Clamp(float value, float min, float max)
    {
        return MathF.Min(max, MathF.Max(min, value));
    }
}

internal readonly record struct EngineFuelCompositionInput(
    float CompressionRatio,
    float BasePowerMultiplier,
    float HighCompressionPowerMultiplier,
    float HighCompressionStartsAt);

internal readonly record struct EngineTorqueCompositionInput(
    TorqueCurvePoint[] SourceCurve,
    float BaseDisplacementCc,
    float DisplacementCc,
    float BaseCompressionRatio,
    float CompressionRatio,
    bool VtecEnabled,
    float VtecActivationRpm,
    float VtecTransitionWidthRpm,
    float LowCamFlowMultiplier,
    float HighCamFlowMultiplier,
    float IntakeFlowScale,
    float ExhaustFlowScale,
    float FuelEffectivePowerMultiplier);

internal readonly record struct EngineBrakeCompositionInput(
    TorqueCurvePoint[] SourceCurve,
    float BaseDisplacementCc,
    float DisplacementCc,
    float BaseCompressionRatio,
    float CompressionRatio,
    float BaseRotationalInertiaKgM2,
    float RotationalInertiaKgM2,
    float IdleRpm,
    float PowerRedlineRpm,
    float LimiterHardCutRpm);
