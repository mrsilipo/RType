namespace RType.Vehicle;

public enum DrivetrainLayout
{
    FF,
    FR,
    AWD
}

public readonly record struct AxleTorqueResult(
    float LeftWheelTorqueNm,
    float RightWheelTorqueNm,
    float ManagedAxleTorqueNm,
    float LowGripAnchorMaxTorqueNm,
    float HighGripWheelMaxTorqueNm,
    string LowGripAnchor);

