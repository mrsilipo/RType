using Microsoft.Xna.Framework;

namespace RType.Vehicle;

internal sealed class TorqueCurveEnginePowerUnit : IEnginePowerUnit
{
    private readonly VehicleSimulationParameters _parameters;

    public TorqueCurveEnginePowerUnit(VehicleSimulationParameters parameters)
    {
        _parameters = parameters;
        State = EnginePowerUnitState.Disabled;
    }

    public bool Enabled => true;

    public bool UsesEngineSimulator => false;

    public bool OwnsDriveline => false;

    public EnginePowerUnitState State { get; private set; }

    public EnginePowerUnitState Advance(EnginePowerUnitRequest request)
    {
        float rpm = MathHelper.Clamp(
            request.Rpm,
            450f,
            MathF.Max(450f, _parameters.RedlineRpm + _parameters.RevLimiterBounceRpm));
        float throttle = MathHelper.Clamp(request.Throttle, 0f, 1f);
        float limiterTorqueMultiplier = MathHelper.Clamp(request.LimiterTorqueMultiplier, 0f, 1.25f);
        float driveTorque = _parameters.TorqueAtRpm(rpm) * throttle * limiterTorqueMultiplier;
        float brakeTorque = _parameters.EngineBrakeTorqueAtRpm(rpm);
        float load = MathHelper.Clamp(
            0.14f + MathF.Pow(throttle, 1.35f) * 0.82f - MathHelper.Clamp(request.Overrun, 0f, 1f) * 0.10f,
            0f,
            1f);

        State = new EnginePowerUnitState(
            true,
            false,
            false,
            driveTorque,
            brakeTorque,
            driveTorque,
            driveTorque - brakeTorque,
            driveTorque,
            -brakeTorque,
            0f,
            0f,
            load,
            rpm,
            CalculateTransmissionRpm(request),
            0f,
            brakeTorque,
            MathHelper.Clamp(request.Limiter, 0f, 1f),
            ReferenceDriveTorqueNm: driveTorque,
            CalibratedDriveTorqueNm: driveTorque);
        return State;
    }

    private static float CalculateTransmissionRpm(EnginePowerUnitRequest request)
    {
        if (request.TransmissionRpm > 0f)
        {
            return request.TransmissionRpm;
        }

        if (request.Gear == 0 ||
            request.GearRatio <= 0.0001f ||
            request.FinalDriveRatio <= 0.0001f ||
            request.WheelRadiusMeters <= 0.0001f)
        {
            return 0f;
        }

        float wheelOmega = request.ForwardSpeedMetersPerSecond / request.WheelRadiusMeters;
        float signedGearRatio = request.Gear < 0 ? -request.GearRatio : request.GearRatio;
        return wheelOmega * signedGearRatio * request.FinalDriveRatio * (60f / MathF.Tau);
    }
}
