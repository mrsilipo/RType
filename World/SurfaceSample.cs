using Microsoft.Xna.Framework;

namespace RType.World;

public readonly struct SurfaceSample
{
    public SurfaceSample(
        string name,
        float grip,
        float rollingResistanceMultiplier = 1f,
        float longitudinalDragCoefficient = 0f,
        float lateralDragCoefficient = 0f,
        float wheelSpinDragCoefficient = 0f,
        float staticFrictionCoefficient = 0f,
        float dynamicFrictionCoefficient = 0f,
        float optimalSlipRatio = 0f,
        float displacementDragCoefficient = 0f,
        float vibrationPrimaryFrequency = 0f,
        float vibrationPrimaryAmplitude = 0f,
        float vibrationSecondaryFrequency = 0f,
        float vibrationSecondaryAmplitude = 0f,
        float handbrakeScreechFactor = 1f,
        float handbrakeWheelSpinRecoveryRate = 18f,
        float blendWeight = 0f)
    {
        Name = name;
        Grip = grip;
        RollingResistanceMultiplier = rollingResistanceMultiplier;
        LongitudinalDragCoefficient = longitudinalDragCoefficient;
        LateralDragCoefficient = lateralDragCoefficient;
        WheelSpinDragCoefficient = wheelSpinDragCoefficient;
        StaticFrictionCoefficient = staticFrictionCoefficient > 0f ? staticFrictionCoefficient : grip;
        DynamicFrictionCoefficient = dynamicFrictionCoefficient > 0f ? dynamicFrictionCoefficient : MathF.Max(0.05f, grip * 0.78f);
        OptimalSlipRatio = optimalSlipRatio > 0f ? optimalSlipRatio : 0.10f;
        DisplacementDragCoefficient = displacementDragCoefficient;
        VibrationPrimaryFrequency = MathF.Max(0f, vibrationPrimaryFrequency);
        VibrationPrimaryAmplitude = MathHelper.Clamp(vibrationPrimaryAmplitude, 0f, 0.95f);
        VibrationSecondaryFrequency = MathF.Max(0f, vibrationSecondaryFrequency);
        VibrationSecondaryAmplitude = MathHelper.Clamp(vibrationSecondaryAmplitude, 0f, 0.95f);
        HandbrakeScreechFactor = MathHelper.Clamp(handbrakeScreechFactor, 0f, 1.5f);
        HandbrakeWheelSpinRecoveryRate = MathF.Max(0f, handbrakeWheelSpinRecoveryRate);
        BlendWeight = MathHelper.Clamp(blendWeight, 0f, 1f);
    }

    public string Name { get; }

    public float Grip { get; }

    public float RollingResistanceMultiplier { get; }

    public float LongitudinalDragCoefficient { get; }

    public float LateralDragCoefficient { get; }

    public float WheelSpinDragCoefficient { get; }

    public float StaticFrictionCoefficient { get; }

    public float DynamicFrictionCoefficient { get; }

    public float OptimalSlipRatio { get; }

    public float DisplacementDragCoefficient { get; }

    public float VibrationPrimaryFrequency { get; }

    public float VibrationPrimaryAmplitude { get; }

    public float VibrationSecondaryFrequency { get; }

    public float VibrationSecondaryAmplitude { get; }

    public float HandbrakeScreechFactor { get; }

    public float HandbrakeWheelSpinRecoveryRate { get; }

    public float BlendWeight { get; }

    public static SurfaceSample Blend(string name, SurfaceSample from, SurfaceSample to, float amount)
    {
        float t = SmoothStep(MathHelper.Clamp(amount, 0f, 1f));
        return new SurfaceSample(
            name,
            MathHelper.Lerp(from.Grip, to.Grip, t),
            MathHelper.Lerp(from.RollingResistanceMultiplier, to.RollingResistanceMultiplier, t),
            MathHelper.Lerp(from.LongitudinalDragCoefficient, to.LongitudinalDragCoefficient, t),
            MathHelper.Lerp(from.LateralDragCoefficient, to.LateralDragCoefficient, t),
            MathHelper.Lerp(from.WheelSpinDragCoefficient, to.WheelSpinDragCoefficient, t),
            MathHelper.Lerp(from.StaticFrictionCoefficient, to.StaticFrictionCoefficient, t),
            MathHelper.Lerp(from.DynamicFrictionCoefficient, to.DynamicFrictionCoefficient, t),
            MathHelper.Lerp(from.OptimalSlipRatio, to.OptimalSlipRatio, t),
            MathHelper.Lerp(from.DisplacementDragCoefficient, to.DisplacementDragCoefficient, t),
            MathHelper.Lerp(from.VibrationPrimaryFrequency, to.VibrationPrimaryFrequency, t),
            MathHelper.Lerp(from.VibrationPrimaryAmplitude, to.VibrationPrimaryAmplitude, t),
            MathHelper.Lerp(from.VibrationSecondaryFrequency, to.VibrationSecondaryFrequency, t),
            MathHelper.Lerp(from.VibrationSecondaryAmplitude, to.VibrationSecondaryAmplitude, t),
            MathHelper.Lerp(from.HandbrakeScreechFactor, to.HandbrakeScreechFactor, t),
            MathHelper.Lerp(from.HandbrakeWheelSpinRecoveryRate, to.HandbrakeWheelSpinRecoveryRate, t),
            t);
    }

    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }
}
