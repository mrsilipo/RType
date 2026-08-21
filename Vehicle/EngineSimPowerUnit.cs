using Microsoft.Xna.Framework;
using RetroRacer.Audio;

namespace RetroRacer.Vehicle;

internal sealed class EngineSimPowerUnit : IEnginePowerUnit
{
    private readonly VehicleSimulationParameters _parameters;
    private readonly EngineSimGasFlowModel? _gasModel;
    private readonly float[] _audioScratch;
    private readonly int _sampleRate;
    private float _smoothedIndicatedTorqueNm;
    private float _smoothedPositiveTorqueNm;
    private float _smoothedNegativeTorqueNm;
    private float _previousVtecBlend;
    private float _vtecKickIntensity;
    private bool _torqueInitialized;
    private float _crankOmegaRadiansPerSecond;
    private float _lastTransmissionOmegaRadiansPerSecond;
    private bool _drivelineInitialized;

    public EngineSimPowerUnit(VehicleSimulationParameters parameters)
    {
        _parameters = parameters;
        Enabled = parameters.EngineSimulatorDrivesPhysics &&
                  parameters.Audio.EngineSimulatorEnabled &&
                  parameters.Audio.EngineSimulatorCylinderCount > 0;
        if (!Enabled)
        {
            _audioScratch = [];
            _sampleRate = 0;
            State = EnginePowerUnitState.Disabled;
            return;
        }

        _sampleRate = Math.Clamp((int)MathF.Round(parameters.EngineSimulatorPhysicsSimulationFrequencyHz), 120, 4000);
        _gasModel = new EngineSimGasFlowModel(parameters.Audio, _sampleRate, parameters.EngineSimulatorPhysicsFluidSimulationSteps);
        _audioScratch = new float[_gasModel.ExhaustChannelCount];
        State = EnginePowerUnitState.Disabled;
    }

    public bool Enabled { get; }

    public bool UsesEngineSimulator => Enabled;

    public bool OwnsDriveline => Enabled && _parameters.EngineSimulatorFullDriveline;

    public EnginePowerUnitState State { get; private set; }

    public EnginePowerUnitState Advance(EnginePowerUnitRequest request)
    {
        if (!Enabled || _gasModel is null)
        {
            State = EnginePowerUnitState.Disabled;
            return State;
        }

        float dt = request.Dt;
        float inputRpm = MathHelper.Clamp(request.Rpm, 450f, MathF.Max(450f, _parameters.RedlineRpm + _parameters.RevLimiterBounceRpm));
        float clampedRpm = PrepareCrankRpm(inputRpm, request.Phase, dt);
        float clampedThrottle = MathHelper.Clamp(request.Throttle, 0f, 1f);
        float clampedLimiter = MathHelper.Clamp(request.Limiter, 0f, 1f);
        float clampedOverrun = MathHelper.Clamp(request.Overrun, 0f, 1f);
        float clampedShock = MathHelper.Clamp(request.Shock, 0f, 1f);
        float vtecBlend = CalculateVtecBlend(clampedRpm, clampedThrottle, request.ForwardSpeedMetersPerSecond);
        float vtecKick = UpdateVtecKick(vtecBlend, clampedThrottle, dt);
        float vtecIntensity = MathHelper.Clamp(_parameters.Audio.EngineSimulatorVtecIntensity, 0f, 1.4f);
        float vtecTorqueGain = CalculateVtecTorqueGain(vtecBlend, vtecKick, vtecIntensity);
        float effectiveVtecBlend = MathHelper.Clamp(vtecBlend + vtecKick * 0.18f, 0f, 1f);
        float effectiveShock = MathHelper.Clamp(clampedShock + vtecKick * 0.70f, 0f, 1f);
        float load = MathHelper.Clamp(
            CalculateLoad(clampedThrottle, clampedOverrun, effectiveShock) + vtecKick * 0.085f,
            0f,
            1f);

        int steps = Math.Max(1, (int)MathF.Round(MathHelper.Clamp(dt, 0f, 0.05f) * _sampleRate));
        float indicatedTorqueSum = 0f;
        float positiveTorqueSum = 0f;
        float negativeTorqueSum = 0f;
        float fuelCutSum = 0f;
        float crankPhaseDegrees = 0f;
        for (int i = 0; i < steps; i++)
        {
            _gasModel.Step(
                clampedRpm,
                clampedThrottle,
                load,
                effectiveVtecBlend,
                clampedLimiter,
                clampedOverrun,
                effectiveShock,
                _audioScratch);
            EngineSimGasFlowPowerState gas = _gasModel.LastPowerState;
            indicatedTorqueSum += gas.IndicatedTorqueNm;
            positiveTorqueSum += gas.PositiveTorqueNm;
            negativeTorqueSum += gas.NegativeTorqueNm;
            fuelCutSum += gas.FuelCutBlend;
            crankPhaseDegrees = gas.CrankPhaseDegrees;
        }

        float inverseSteps = 1f / steps;
        float rawIndicatedTorque = FiniteOrZero(indicatedTorqueSum * inverseSteps);
        float rawPositiveTorque = FiniteOrZero(positiveTorqueSum * inverseSteps);
        float rawNegativeTorque = FiniteOrZero(negativeTorqueSum * inverseSteps);
        float fuelCutBlend = FiniteOrZero(fuelCutSum * inverseSteps);
        SmoothGasTorque(rawIndicatedTorque, rawPositiveTorque, rawNegativeTorque, clampedRpm, dt);
        rawIndicatedTorque = _smoothedIndicatedTorqueNm;
        rawPositiveTorque = _smoothedPositiveTorqueNm;
        rawNegativeTorque = _smoothedNegativeTorqueNm;

        float curveDriveTorque = _parameters.TorqueAtRpm(clampedRpm) * clampedThrottle;
        float simDriveTorque = MathHelper.Clamp(
            MathF.Max(0f, rawPositiveTorque) *
            SmoothStep(0.02f, 0.82f, clampedThrottle) *
            MathF.Max(0f, _parameters.EngineSimulatorPhysicsTorqueScale),
            0f,
            MathF.Max(1f, _parameters.EngineSimulatorPhysicsMaxTorqueNm));
        simDriveTorque = CalibrateDriveTorque(simDriveTorque, curveDriveTorque, clampedThrottle, vtecBlend, vtecKick, vtecIntensity, vtecTorqueGain, clampedShock);
        float driveBlend = MathHelper.Clamp(_parameters.EngineSimulatorPhysicsTorqueBlend, 0f, 1f);
        float driveTorque = MathHelper.Lerp(curveDriveTorque, simDriveTorque, driveBlend);
        driveTorque *= vtecTorqueGain;
        driveTorque *= MathHelper.Clamp(request.LimiterTorqueMultiplier, 0f, 1.25f);
        float engineDriveTorque = driveTorque;

        float curveBrakeTorque = MathF.Max(
            _parameters.ClosedThrottleEngineBrakeTorqueNm,
            _parameters.EngineBrakeTorqueAtRpm(clampedRpm));
        float simBrakeTorque = MathHelper.Clamp(
            MathF.Max(0f, -rawNegativeTorque) * MathF.Max(0f, _parameters.EngineSimulatorPhysicsEngineBrakeScale),
            0f,
            MathF.Max(1f, _parameters.EngineSimulatorPhysicsMaxEngineBrakeTorqueNm));
        float brakeBlend = MathHelper.Clamp(_parameters.EngineSimulatorPhysicsEngineBrakeBlend, 0f, 1f);
        float engineBrakeTorque = MathHelper.Lerp(curveBrakeTorque, simBrakeTorque, brakeBlend);
        float transmissionRpm = OmegaToRpm(_lastTransmissionOmegaRadiansPerSecond);
        float clutchTorque = 0f;
        float crankFrictionTorque = 0f;
        if (OwnsDriveline)
        {
            EngineSimDrivelineSample driveline = AdvanceFullDriveline(
                driveTorque,
                engineBrakeTorque,
                clampedThrottle,
                request.ForwardSpeedMetersPerSecond,
                request.Gear,
                request.GearRatio,
                request.TransmissionRpm,
                request.FinalDriveRatio,
                request.WheelRadiusMeters,
                request.ClutchEngagement,
                request.Phase,
                request.PhaseProgress,
                request.DrivenSlipRatio,
                request.ClutchSlipRpm,
                request.LimiterTorqueMultiplier,
                dt);
            clampedRpm = driveline.CrankRpm;
            transmissionRpm = driveline.TransmissionRpm;
            clutchTorque = driveline.ClutchTorqueNm;
            crankFrictionTorque = driveline.FrictionTorqueNm;
            driveTorque = driveline.DriveTorqueNm;
            engineBrakeTorque = MathF.Max(engineBrakeTorque, driveline.EngineBrakeTorqueNm);
        }

        State = new EnginePowerUnitState(
            true,
            true,
            OwnsDriveline,
            driveTorque,
            engineBrakeTorque,
            engineDriveTorque,
            rawIndicatedTorque,
            rawPositiveTorque,
            rawNegativeTorque,
            vtecBlend,
            vtecKick,
            load,
            clampedRpm,
            transmissionRpm,
            clutchTorque,
            crankFrictionTorque,
            fuelCutBlend,
            crankPhaseDegrees);
        return State;
    }

    private float PrepareCrankRpm(float inputRpm, EnginePowerUnitPhase phase, float dt)
    {
        if (!OwnsDriveline)
        {
            return inputRpm;
        }

        float inputOmega = RpmToOmega(inputRpm);
        if (!_drivelineInitialized)
        {
            _crankOmegaRadiansPerSecond = inputOmega;
            _drivelineInitialized = true;
            return inputRpm;
        }

        float currentRpm = OmegaToRpm(_crankOmegaRadiansPerSecond);
        if (phase == EnginePowerUnitPhase.Shifting)
        {
            float sync = MathHelper.Clamp(1f - MathF.Exp(-34f * MathHelper.Clamp(dt, 0f, 0.05f)), 0f, 1f);
            _crankOmegaRadiansPerSecond = MathHelper.Lerp(_crankOmegaRadiansPerSecond, inputOmega, sync);
        }
        else if (MathF.Abs(inputRpm - currentRpm) > 2400f)
        {
            float sync = MathHelper.Clamp(1f - MathF.Exp(-18f * MathHelper.Clamp(dt, 0f, 0.05f)), 0f, 1f);
            _crankOmegaRadiansPerSecond = MathHelper.Lerp(_crankOmegaRadiansPerSecond, inputOmega, sync);
        }

        float maxRpm = MathF.Max(_parameters.RedlineRpm, _parameters.IdleRpm + 1000f);
        _crankOmegaRadiansPerSecond = MathHelper.Clamp(
            _crankOmegaRadiansPerSecond,
            RpmToOmega(450f),
            RpmToOmega(maxRpm));
        return OmegaToRpm(_crankOmegaRadiansPerSecond);
    }

    private EngineSimDrivelineSample AdvanceFullDriveline(
        float gasDriveTorqueNm,
        float engineBrakeTorqueNm,
        float throttle,
        float forwardSpeedMetersPerSecond,
        int gear,
        float gearRatio,
        float transmissionRpm,
        float finalDriveRatio,
        float wheelRadiusMeters,
        float clutchEngagement,
        EnginePowerUnitPhase phase,
        float phaseProgress,
        float drivenSlipRatio,
        float requestClutchSlipRpm,
        float limiterTorqueMultiplier,
        float dt)
    {
        VehicleAudioParameters audio = _parameters.Audio;
        float clampedDt = MathHelper.Clamp(dt, 0f, 0.05f);
        float transmissionOmega = CalculateTransmissionOmega(
            gear,
            gearRatio,
            transmissionRpm,
            finalDriveRatio,
            wheelRadiusMeters,
            forwardSpeedMetersPerSecond);
        _lastTransmissionOmegaRadiansPerSecond = transmissionOmega;

        float effectiveClutchEngagement = gear == 0 || gearRatio <= 0.0001f || finalDriveRatio <= 0.0001f
            ? 0f
            : MathHelper.Clamp(clutchEngagement, 0f, 1f);
        float maxClutchTorque = audio.EngineSimulatorTransmissionMaxClutchTorqueNm > 1f
            ? MathF.Max(_parameters.ClutchTorqueCapacityNm, audio.EngineSimulatorTransmissionMaxClutchTorqueNm)
            : _parameters.ClutchTorqueCapacityNm;
        float slipOmega = _crankOmegaRadiansPerSecond - transmissionOmega;
        float slipRpm = MathF.Abs(OmegaToRpm(slipOmega));
        float contextualSlipRpm = MathF.Max(slipRpm, MathF.Abs(requestClutchSlipRpm));
        float phaseCapacityScale = CalculatePhaseClutchCapacityScale(
            phase,
            phaseProgress,
            drivenSlipRatio,
            contextualSlipRpm);
        float clutchCapacity = MathF.Max(0f, maxClutchTorque) *
                               effectiveClutchEngagement *
                               phaseCapacityScale;
        float crankRpm = OmegaToRpm(_crankOmegaRadiansPerSecond);
        float throttleRelief = SmoothStep(0.02f, 0.18f, throttle);
        float frictionTorque = MathF.Max(0f, audio.EngineSimulatorCrankshaftFrictionTorqueNm);
        frictionTorque += MathF.Max(0f, crankRpm - _parameters.IdleRpm) * 0.00022f;
        float pumpingBrakeTorque = engineBrakeTorqueNm * (1f - throttleRelief);
        float idleAssistTorque = CalculateIdleAssistTorque(throttle);
        float limiterDragTorque = MathF.Max(0f, 1f - MathHelper.Clamp(limiterTorqueMultiplier, 0f, 1f)) *
                                  MathHelper.Lerp(0f, _parameters.EngineSimulatorPhysicsMaxTorqueNm * 0.34f, SmoothStep(_parameters.RevLimiterResumeRpm, _parameters.RedlineRpm, crankRpm));
        float clutchTorque = 0f;
        if (clutchCapacity > 0.001f)
        {
            float stiffness = MathHelper.Clamp(clutchCapacity * 0.10f, 14f, 72f);
            float viscousTorque = slipOmega * stiffness;
            float coulombTorque = MathF.CopySign(
                clutchCapacity * SmoothStep(20f, 420f, slipRpm),
                slipOmega);
            float coulombBlend = SmoothStep(180f, 1200f, slipRpm);
            clutchTorque = MathHelper.Clamp(
                MathHelper.Lerp(viscousTorque, coulombTorque, coulombBlend),
                -clutchCapacity,
                clutchCapacity);
            float lockBlend = effectiveClutchEngagement * (1f - SmoothStep(140f, 820f, slipRpm));
            float steadyStateTorque = MathHelper.Clamp(
                gasDriveTorqueNm + idleAssistTorque - frictionTorque - pumpingBrakeTorque - limiterDragTorque,
                -clutchCapacity,
                clutchCapacity);
            clutchTorque = MathHelper.Lerp(clutchTorque, steadyStateTorque, lockBlend * 0.86f);
        }

        float netCrankTorque = gasDriveTorqueNm +
                               idleAssistTorque -
                               clutchTorque -
                               frictionTorque -
                               pumpingBrakeTorque -
                               limiterDragTorque;

        float inertia = MathF.Max(
            0.04f,
            audio.EngineSimulatorCrankshaftMomentOfInertiaKgM2 > 0f
                ? audio.EngineSimulatorCrankshaftMomentOfInertiaKgM2
                : _parameters.EngineRotationalInertiaKgM2);
        float angularAcceleration = MathHelper.Clamp(netCrankTorque / inertia, -9000f, 9000f);
        _crankOmegaRadiansPerSecond += angularAcceleration * clampedDt;
        float maxRpm = MathF.Max(_parameters.RedlineRpm, _parameters.IdleRpm + 1000f);
        _crankOmegaRadiansPerSecond = MathHelper.Clamp(
            _crankOmegaRadiansPerSecond,
            RpmToOmega(450f),
            RpmToOmega(maxRpm));

        float deliveredCrankTorque = effectiveClutchEngagement > 0f ? clutchTorque : 0f;
        float driveTorque = MathHelper.Clamp(
            deliveredCrankTorque,
            -MathF.Max(1f, _parameters.EngineSimulatorPhysicsMaxEngineBrakeTorqueNm),
            MathF.Max(1f, _parameters.EngineSimulatorPhysicsMaxTorqueNm));
        float engineBrakeTorque = MathF.Max(
            engineBrakeTorqueNm,
            MathF.Max(0f, -deliveredCrankTorque) + pumpingBrakeTorque * 0.35f);
        return new EngineSimDrivelineSample(
            driveTorque,
            engineBrakeTorque,
            OmegaToRpm(_crankOmegaRadiansPerSecond),
            OmegaToRpm(transmissionOmega),
            deliveredCrankTorque,
            frictionTorque + pumpingBrakeTorque + limiterDragTorque);
    }

    private static float CalculatePhaseClutchCapacityScale(
        EnginePowerUnitPhase phase,
        float phaseProgress,
        float drivenSlipRatio,
        float clutchSlipRpm)
    {
        float progress = MathHelper.Clamp(phaseProgress, 0f, 1f);
        float slip = MathF.Max(0f, clutchSlipRpm);
        float drivenSlip = MathF.Max(0f, drivenSlipRatio);

        return phase switch
        {
            EnginePowerUnitPhase.Launch => CalculateLaunchClutchCapacityScale(progress, drivenSlip, slip),
            EnginePowerUnitPhase.EngineBraking => CalculateEngineBrakingClutchCapacityScale(drivenSlip, slip),
            EnginePowerUnitPhase.Shifting => MathHelper.Lerp(0.30f, 0.92f, SmoothStep(0.10f, 0.86f, progress)),
            EnginePowerUnitPhase.NeutralHold => 0f,
            _ => 1f
        };
    }

    private static float CalculateLaunchClutchCapacityScale(float progress, float drivenSlipRatio, float clutchSlipRpm)
    {
        float earlyLaunchT = 1f - SmoothStep(0.28f, 0.88f, progress);
        float wheelSpinRelief = SmoothStep(0.12f, 0.55f, drivenSlipRatio) * earlyLaunchT;
        float loadedWheelSpinRelief = wheelSpinRelief * SmoothStep(2600f, 7200f, clutchSlipRpm);
        return MathHelper.Clamp(1f - wheelSpinRelief * 0.16f - loadedWheelSpinRelief * 0.04f, 0.80f, 1f);
    }

    private static float CalculateEngineBrakingClutchCapacityScale(float drivenSlipRatio, float clutchSlipRpm)
    {
        float slipLockT = 1f - SmoothStep(1800f, 5600f, clutchSlipRpm);
        float correctionReliefT = SmoothStep(0.35f, 0.95f, drivenSlipRatio);
        float extraBite = MathHelper.Lerp(0.08f, 0.02f, correctionReliefT) * slipLockT;
        return MathHelper.Clamp(1f + extraBite, 0.92f, 1.10f);
    }

    private float CalculateTransmissionOmega(
        int gear,
        float gearRatio,
        float transmissionRpm,
        float finalDriveRatio,
        float wheelRadiusMeters,
        float forwardSpeedMetersPerSecond)
    {
        if (gear == 0 || gearRatio <= 0.0001f || finalDriveRatio <= 0.0001f)
        {
            return 0f;
        }

        if (transmissionRpm > 0f)
        {
            return RpmToOmega(transmissionRpm);
        }

        float wheelOmega = forwardSpeedMetersPerSecond / MathF.Max(0.05f, wheelRadiusMeters);
        float signedGearRatio = gear < 0 ? -gearRatio : gearRatio;
        return wheelOmega * signedGearRatio * finalDriveRatio;
    }

    private float CalculateIdleAssistTorque(float throttle)
    {
        if (throttle > 0.04f)
        {
            return 0f;
        }

        float idleOmega = RpmToOmega(_parameters.IdleRpm);
        float idleError = idleOmega - _crankOmegaRadiansPerSecond;
        if (idleError <= 0f)
        {
            return 0f;
        }

        float inertia = MathF.Max(0.04f, _parameters.Audio.EngineSimulatorCrankshaftMomentOfInertiaKgM2);
        return MathHelper.Clamp(idleError * inertia * 18f, 0f, 38f);
    }

    private float UpdateVtecKick(float vtecBlend, float throttle, float dt)
    {
        float vtecRise = MathF.Max(0f, vtecBlend - _previousVtecBlend);
        float throttleGate = SmoothStep(
            MathHelper.Clamp(_parameters.Audio.HighRpmMinimumThrottle, 0f, 0.95f),
            0.86f,
            throttle);
        float camGate = SmoothStep(0.08f, 0.62f, vtecBlend);
        float configuredIntensity = MathHelper.Clamp(_parameters.Audio.EngineSimulatorVtecIntensity, 0f, 1.4f);
        if (vtecRise > 0.006f && camGate > 0f && throttleGate > 0f)
        {
            float trigger = SmoothStep(0.006f, 0.055f, vtecRise) * camGate * throttleGate * configuredIntensity;
            _vtecKickIntensity = MathF.Max(_vtecKickIntensity, trigger);
        }

        float kick = MathHelper.Clamp(_vtecKickIntensity, 0f, 1f);
        float decayRate = MathHelper.Lerp(4.2f, 6.8f, MathHelper.Clamp(vtecBlend, 0f, 1f));
        _vtecKickIntensity *= MathF.Exp(-MathF.Max(0f, dt) * decayRate);
        if (vtecBlend <= 0.02f && _vtecKickIntensity <= 0.015f)
        {
            _vtecKickIntensity = 0f;
        }

        _previousVtecBlend = vtecBlend;
        return kick;
    }

    private float CalculateVtecBlend(float rpm, float throttle, float forwardSpeedMetersPerSecond)
    {
        if (!_parameters.VtecEnabled ||
            throttle < _parameters.Audio.HighRpmMinimumThrottle ||
            MathF.Abs(forwardSpeedMetersPerSecond) < _parameters.Audio.HighRpmMinimumSpeedMetersPerSecond)
        {
            return 0f;
        }

        return SmoothStep(
            _parameters.VtecActivationRpm,
            _parameters.VtecActivationRpm + MathF.Max(1f, _parameters.VtecTransitionWidthRpm),
            rpm);
    }

    private float CalculateVtecTorqueGain(float vtecBlend, float vtecKick, float configuredIntensity)
    {
        float lowCamFlow = MathF.Max(0.65f, _parameters.VtecLowCamFlowMultiplier);
        float highCamFlow = MathF.Max(lowCamFlow, _parameters.VtecHighCamFlowMultiplier);
        float flowDelta = MathHelper.Clamp(highCamFlow - lowCamFlow, 0f, 0.35f);
        float highCamTorqueGain = 1f + flowDelta * 0.24f * MathHelper.Clamp(vtecBlend, 0f, 1f);
        float kickGain = vtecKick * (0.018f + configuredIntensity * 0.024f);
        return MathHelper.Clamp(highCamTorqueGain + kickGain, 0.82f, 1.08f);
    }

    private float CalculateLoad(float throttle, float overrun, float shock)
    {
        float shapedThrottle = MathF.Pow(MathHelper.Clamp(throttle, 0f, 1f), MathF.Max(0.1f, _parameters.Audio.EngineSimulatorThrottleGamma));
        return MathHelper.Clamp(0.14f + shapedThrottle * 0.82f + shock * 0.16f - overrun * 0.10f, 0f, 1f);
    }

    private void SmoothGasTorque(float indicatedTorque, float positiveTorque, float negativeTorque, float rpm, float dt)
    {
        if (!_torqueInitialized)
        {
            _smoothedIndicatedTorqueNm = indicatedTorque;
            _smoothedPositiveTorqueNm = positiveTorque;
            _smoothedNegativeTorqueNm = negativeTorque;
            _torqueInitialized = true;
            return;
        }

        float cyclesPerSecond = MathF.Max(1f, rpm / 120f);
        float responseHz = MathHelper.Clamp(cyclesPerSecond * 1.8f, 12f, 42f);
        float alpha = MathHelper.Clamp(1f - MathF.Exp(-responseHz * MathHelper.Clamp(dt, 0f, 0.05f)), 0f, 1f);
        _smoothedIndicatedTorqueNm = MathHelper.Lerp(_smoothedIndicatedTorqueNm, indicatedTorque, alpha);
        _smoothedPositiveTorqueNm = MathHelper.Lerp(_smoothedPositiveTorqueNm, positiveTorque, alpha);
        _smoothedNegativeTorqueNm = MathHelper.Lerp(_smoothedNegativeTorqueNm, negativeTorque, alpha);
    }

    private static float CalibrateDriveTorque(
        float simDriveTorque,
        float referenceDriveTorque,
        float throttle,
        float vtecBlend,
        float vtecKick,
        float vtecIntensity,
        float vtecTorqueGain,
        float shock)
    {
        if (referenceDriveTorque <= 1f || simDriveTorque <= 0f)
        {
            return simDriveTorque;
        }

        float throttleT = SmoothStep(0.15f, 0.72f, throttle);
        float highCamFloor = vtecBlend * (0.025f + vtecIntensity * 0.035f) +
                             vtecKick * vtecIntensity * 0.030f;
        float lower = referenceDriveTorque * MathHelper.Lerp(0.62f, 0.94f + highCamFloor, throttleT);
        float upper = referenceDriveTorque * (
            MathHelper.Lerp(1.045f, 1.075f, MathHelper.Clamp(vtecTorqueGain - 1f, 0f, 0.08f) / 0.08f) +
            vtecBlend * (0.010f + vtecIntensity * 0.022f) +
            vtecKick * (0.018f + vtecIntensity * 0.040f) +
            shock * 0.03f);
        return MathHelper.Clamp(simDriveTorque, lower, MathF.Max(lower + 1f, upper));
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float RpmToOmega(float rpm)
    {
        return rpm * (MathF.Tau / 60f);
    }

    private static float OmegaToRpm(float omega)
    {
        return omega * (60f / MathF.Tau);
    }

    private static float FiniteOrZero(float value)
    {
        return float.IsFinite(value) ? value : 0f;
    }
}

internal readonly record struct EngineSimDrivelineSample(
    float DriveTorqueNm,
    float EngineBrakeTorqueNm,
    float CrankRpm,
    float TransmissionRpm,
    float ClutchTorqueNm,
    float FrictionTorqueNm);
