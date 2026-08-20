namespace RetroRacer.Vehicle;

internal interface IEnginePowerUnit
{
    bool Enabled { get; }

    bool UsesEngineSimulator { get; }

    bool OwnsDriveline { get; }

    EnginePowerUnitState State { get; }

    EnginePowerUnitState Advance(EnginePowerUnitRequest request);
}

internal enum EnginePowerUnitPhase
{
    Driving,
    Launch,
    Shifting,
    EngineBraking,
    NeutralHold
}

internal readonly record struct EnginePowerUnitRequest(
    float Rpm,
    float Throttle,
    float ForwardSpeedMetersPerSecond,
    float Limiter,
    float LimiterTorqueMultiplier,
    float Overrun,
    float Shock,
    int Gear,
    float GearRatio,
    float TransmissionRpm,
    float FinalDriveRatio,
    float WheelRadiusMeters,
    float ClutchEngagement,
    EnginePowerUnitPhase Phase,
    float PhaseProgress,
    float DrivenSlipRatio,
    float ClutchSlipRpm,
    float Dt);

internal readonly record struct EnginePowerUnitState(
    bool Enabled,
    bool UsesEngineSimulator,
    bool OwnsDriveline,
    float DriveTorqueNm,
    float EngineBrakeTorqueNm,
    float EngineDriveTorqueNm,
    float RawIndicatedTorqueNm,
    float RawPositiveTorqueNm,
    float RawNegativeTorqueNm,
    float VtecBlend,
    float VtecKickIntensity,
    float Load,
    float CrankRpm,
    float TransmissionRpm,
    float ClutchTorqueNm,
    float CrankFrictionTorqueNm,
    float FuelCutBlend)
{
    public static EnginePowerUnitState Disabled => new(
        false,
        false,
        false,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f);
}

internal static class EnginePowerUnitFactory
{
    public static IEnginePowerUnit Create(VehicleSimulationParameters parameters)
    {
        if (parameters.EngineSimulatorDrivesPhysics &&
            parameters.Audio.EngineSimulatorEnabled &&
            parameters.Audio.EngineSimulatorCylinderCount > 0)
        {
            return new EngineSimPowerUnit(parameters);
        }

        return new TorqueCurveEnginePowerUnit(parameters);
    }
}
