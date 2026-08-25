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
        float vibrationSecondaryAmplitude = 0f)
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
}
