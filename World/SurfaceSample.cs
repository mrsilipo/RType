namespace RType.World;

public readonly struct SurfaceSample
{
    public SurfaceSample(
        string name,
        float grip,
        float rollingResistanceMultiplier = 1f,
        float longitudinalDragCoefficient = 0f,
        float lateralDragCoefficient = 0f,
        float wheelSpinDragCoefficient = 0f)
    {
        Name = name;
        Grip = grip;
        RollingResistanceMultiplier = rollingResistanceMultiplier;
        LongitudinalDragCoefficient = longitudinalDragCoefficient;
        LateralDragCoefficient = lateralDragCoefficient;
        WheelSpinDragCoefficient = wheelSpinDragCoefficient;
    }

    public string Name { get; }

    public float Grip { get; }

    public float RollingResistanceMultiplier { get; }

    public float LongitudinalDragCoefficient { get; }

    public float LateralDragCoefficient { get; }

    public float WheelSpinDragCoefficient { get; }
}
