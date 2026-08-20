using Microsoft.Xna.Framework;
using RetroRacer.Camera;
using RetroRacer.Vehicle;

namespace RetroRacer.Audio;

internal readonly record struct EngineAudioFrame(
    float Rpm,
    float RedlineRpm,
    float Throttle,
    float ShapedThrottle,
    float Load,
    float VtecBlend,
    float VtecKickIntensity,
    float Limiter,
    float Overrun,
    float Shock,
    float IntakeDrive,
    float ThrottleTransient,
    float DrivelineDrive,
    float DriveVolume,
    float PauseScale,
    CameraMode CameraMode,
    int Gear,
    bool IsShifting,
    float SpeedMetersPerSecond,
    float EngineDriveTorqueNm,
    float DriveForceN,
    float EngineBrakeTorqueNm,
    float ClutchSlipRpm,
    float ClutchTorqueNm,
    float CrankFrictionTorqueNm,
    float TransmissionRpm)
{
    public bool Audible => DriveVolume > 0.001f && PauseScale > 0f;

    public EngineSimulatorSynthesisTarget ToSynthesisTarget() => new(
        MathHelper.Clamp(Rpm, 450f, MathF.Max(450f, RedlineRpm * 1.12f)),
        Throttle,
        MathHelper.Clamp(Load + Overrun * 0.22f, 0f, 1f),
        VtecBlend,
        Limiter,
        Overrun,
        Shock,
        IntakeDrive,
        ThrottleTransient,
        DrivelineDrive);

    public static EngineAudioFrame FromVehicleState(
        VehicleAudioParameters parameters,
        VehicleState vehicle,
        float rpm,
        float highRpmBlend,
        float driveVolume,
        CameraMode cameraMode,
        bool paused,
        float throttleTransient)
    {
        float pauseScale = paused ? 0f : 1f;
        float redlineRpm = MathF.Max(450f, vehicle.RedlineRpm);
        float throttle = MathHelper.Clamp(MathF.Max(vehicle.Throttle, vehicle.EffectiveThrottle), 0f, 1f);
        float shapedThrottle = MathF.Pow(throttle, MathF.Max(0.1f, parameters.EngineSimulatorThrottleGamma));
        float overrun = CalculateOverrun(throttle, rpm, redlineRpm, vehicle.SpeedMetersPerSecond);
        float vtecKick = MathHelper.Clamp(vehicle.EngineSimulatorVtecKickIntensity, 0f, 1f);
        float vtecBlend = MathHelper.Clamp(
            MathF.Max(highRpmBlend, vehicle.EngineSimulatorVtecBlend) + vtecKick * 0.32f,
            0f,
            1f);
        float load = CalculateLoad(vehicle, shapedThrottle, overrun, vtecKick);
        float limiter = MathHelper.Clamp(
            MathF.Max(
                MathF.Max(vehicle.RevLimiterBounceIntensity, vehicle.EngineSimulatorFuelCutBlend),
                vehicle.RevLimiterActive ? 0.55f : 0f),
            0f,
            1f);
        float shock = MathHelper.Clamp(
            MathF.Max(
                MathF.Max(vehicle.ShiftKickIntensity, vehicle.PowertrainShockIntensity),
                vtecKick * 0.42f),
            0f,
            1f);
        float clampedTransient = MathHelper.Clamp(throttleTransient, 0f, 1f);
        float intakeDrive = CalculateIntakeDrive(shapedThrottle, load, vtecBlend, overrun, clampedTransient);
        float drivelineDrive = CalculateDrivelineDrive(vehicle, throttle);

        return new EngineAudioFrame(
            MathHelper.Clamp(rpm, 450f, redlineRpm * 1.12f),
            redlineRpm,
            throttle,
            shapedThrottle,
            load,
            vtecBlend,
            vtecKick,
            limiter,
            overrun,
            shock,
            intakeDrive,
            clampedTransient,
            drivelineDrive,
            MathHelper.Clamp(driveVolume, 0f, 1f),
            pauseScale,
            cameraMode,
            vehicle.Gear,
            vehicle.IsShifting,
            vehicle.SpeedMetersPerSecond,
            vehicle.EngineSimulatorEngineDriveTorqueNm,
            vehicle.DriveForce,
            vehicle.EngineBrakeTorqueNm,
            vehicle.ClutchSlipRpm,
            vehicle.EngineSimulatorClutchTorqueNm,
            vehicle.EngineSimulatorCrankFrictionTorqueNm,
            vehicle.EngineSimulatorTransmissionRpm);
    }

    private static float CalculateLoad(VehicleState vehicle, float shapedThrottle, float overrun, float vtecKick)
    {
        float load = MathHelper.Clamp(
            0.14f + shapedThrottle * 0.82f + vehicle.ShiftKickIntensity * 0.16f + vtecKick * 0.12f - overrun * 0.10f,
            0f,
            1f);
        if (vehicle.EngineSimulatorPowerActive && vehicle.EngineSimulatorLoad > 0f)
        {
            load = MathHelper.Lerp(load, MathHelper.Clamp(vehicle.EngineSimulatorLoad, 0f, 1f), 0.72f);
        }

        return load;
    }

    private static float CalculateOverrun(float throttle, float rpm, float redlineRpm, float speedMetersPerSecond)
    {
        return (1f - SmoothStep(0.05f, 0.25f, throttle)) *
               SmoothStep(2600f, MathF.Max(3200f, redlineRpm), rpm) *
               SmoothStep(2f, 11f, speedMetersPerSecond);
    }

    private static float CalculateIntakeDrive(
        float shapedThrottle,
        float load,
        float vtecBlend,
        float overrun,
        float throttleTransient)
    {
        float loadedThrottle = MathF.Max(shapedThrottle, load * 0.62f);
        float highCamHarden = SmoothStep(0.18f, 0.92f, vtecBlend) * 0.18f;
        float snapHarden = throttleTransient * 0.24f;
        float liftOffMute = MathHelper.Lerp(1f, 0.42f, SmoothStep(0.08f, 0.88f, overrun));
        return MathHelper.Clamp((loadedThrottle + highCamHarden + snapHarden) * liftOffMute, 0f, 1f);
    }

    private static float CalculateDrivelineDrive(VehicleState vehicle, float throttle)
    {
        float clutchSlip = SmoothStep(180f, 2800f, MathF.Abs(vehicle.ClutchSlipRpm));
        float clutchLoad = SmoothStep(20f, 220f, MathF.Abs(vehicle.EngineSimulatorClutchTorqueNm));
        float shiftShock = MathF.Max(vehicle.ShiftKickIntensity, vehicle.PowertrainShockIntensity);
        float gearSpeed = vehicle.Gear == 0 ? 0f : SmoothStep(8f, 48f, MathF.Abs(vehicle.SpeedMetersPerSecond));
        float throttleGate = SmoothStep(0.08f, 0.56f, throttle);

        return MathHelper.Clamp(
            MathF.Max(shiftShock * 0.70f, clutchSlip * clutchLoad * throttleGate) + gearSpeed * 0.12f,
            0f,
            1f);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
