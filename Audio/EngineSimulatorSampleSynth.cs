using Microsoft.Xna.Framework;
using RetroRacer.Vehicle;

namespace RetroRacer.Audio;

internal sealed class EngineSimulatorSampleSynth
{
    private const float RpmTrackingStep = 0.0032f;
    private const float FastRpmTrackingStep = 0.0068f;
    private const float ThrottleTrackingStep = 0.0062f;
    private const float LoadTrackingStep = 0.0058f;
    private const float VtecTrackingStep = 0.0064f;
    private const float LimiterTrackingStep = 0.0092f;
    private const float OverrunTrackingStep = 0.0052f;
    private const float ShockTrackingStep = 0.0094f;
    private const float IntakeTrackingStep = 0.0062f;
    private const float ThrottleTransientTrackingStep = 0.0115f;
    private const float DrivelineTrackingStep = 0.0048f;
    private const float SpeedTrackingStep = 0.008f;
    private const float TransmissionRpmTrackingStep = 0.006f;

    private readonly VehicleAudioParameters _parameters;
    private readonly EngineSimGasFlowModel _engineModel;
    private readonly EngineSimDspProcessor _dsp;
    private readonly int _sampleRate;
    private readonly float _simulationAdvancePerAudioSample;
    private readonly float[] _previousSimulationInput;
    private readonly float[] _nextSimulationInput;
    private readonly float[] _interpolatedSimulationInput;
    private readonly ButterworthLowPassFilter[] _inputAntialiasingFilters;
    private float _targetRpm = 900f;
    private float _currentRpm = 900f;
    private float _targetThrottle;
    private float _currentThrottle;
    private float _targetLoad;
    private float _currentLoad;
    private float _targetVtec;
    private float _currentVtec;
    private float _targetLimiter;
    private float _currentLimiter;
    private float _targetOverrun;
    private float _currentOverrun;
    private float _targetShock;
    private float _currentShock;
    private float _targetIntake;
    private float _currentIntake;
    private float _targetThrottleTransient;
    private float _currentThrottleTransient;
    private float _targetDriveline;
    private float _currentDriveline;
    private float _targetSpeed;
    private float _currentSpeed;
    private float _targetTransmissionRpm;
    private float _currentTransmissionRpm;
    private float _targetGear;
    private float _currentGear;
    private float _targetBackfire;
    private float _currentBackfire;
    private float _targetCrankPhase;
    private bool _phaseSyncPending;
    private float _simulationPhase;
    private bool _hasSimulationInput;

    public EngineSimulatorSampleSynth(
        VehicleAudioParameters parameters,
        int sampleRate,
        int? simulationFrequencyHzOverride = null,
        int? fluidSimulationStepsOverride = null)
    {
        _parameters = parameters;
        _sampleRate = Math.Max(1, sampleRate);
        int simulationRate = Math.Clamp(
            (int)MathF.Round(simulationFrequencyHzOverride ?? parameters.EngineSimulatorSimulationFrequencyHz),
            1000,
            96000);
        _engineModel = new EngineSimGasFlowModel(parameters, simulationRate, fluidSimulationStepsOverride);
        _dsp = new EngineSimDspProcessor(
            parameters,
            _sampleRate,
            _engineModel.AudioChannelCount);
        _simulationAdvancePerAudioSample = simulationRate / (float)_sampleRate;
        _previousSimulationInput = new float[_dsp.ChannelCount];
        _nextSimulationInput = new float[_dsp.ChannelCount];
        _interpolatedSimulationInput = new float[_dsp.ChannelCount];
        _inputAntialiasingFilters = new ButterworthLowPassFilter[_dsp.ChannelCount];
        for (int i = 0; i < _inputAntialiasingFilters.Length; i++)
        {
            _inputAntialiasingFilters[i] = new ButterworthLowPassFilter();
            // Preserve the firing harmonics while rejecting the upper pressure
            // wave/upsampling band that reads as a high-pitched insect texture.
            _inputAntialiasingFilters[i].SetCutoffFrequency(5500f, _sampleRate);
        }

        AudioDiagnostics.Log(
            "engine-sim-synth",
            $"MR {FormatScriptPath(parameters.EngineSimulatorMrScriptPath)}, gas-flow chamber model, cylinders {_engineModel.CylinderCount}, audio channels {_engineModel.AudioChannelCount} (exhaust {_engineModel.ExhaustChannelCount} + intake), route {_engineModel.EventRouteSummary}, attenuation {_engineModel.EventAttenuationSummary}, exhaust {_engineModel.ExhaustGainSummary}, cam {_engineModel.CamSummary}, {_engineModel.FlowSummary}, timing {string.Join("/", parameters.EngineSimulatorIgnitionTimingDegrees.Select(value => value.ToString("0")))}, sim {simulationRate} Hz, fluid steps {_engineModel.FluidSimulationSteps}, input antialias 5500 Hz");
    }

    public int CylinderCount => _engineModel.CylinderCount;

    public float TargetRpm => _targetRpm;

    public void SetTarget(EngineSimulatorSynthesisTarget target)
    {
        _targetRpm = MathHelper.Clamp(target.Rpm, 450f, 12000f);
        _targetThrottle = MathHelper.Clamp(target.Throttle, 0f, 1f);
        _targetLoad = MathHelper.Clamp(target.Load, 0f, 1f);
        _targetVtec = MathHelper.Clamp(target.VtecBlend, 0f, 1f);
        _targetLimiter = MathHelper.Clamp(target.Limiter, 0f, 1f);
        _targetOverrun = MathHelper.Clamp(target.Overrun, 0f, 1f);
        _targetShock = MathHelper.Clamp(target.Shock, 0f, 1f);
        _targetIntake = MathHelper.Clamp(target.Intake, 0f, 1f);
        _targetThrottleTransient = MathHelper.Clamp(target.ThrottleTransient, 0f, 1f);
        _targetDriveline = MathHelper.Clamp(target.Driveline, 0f, 1f);
        _targetSpeed = MathF.Max(0f, target.SpeedMetersPerSecond);
        _targetTransmissionRpm = MathF.Max(0f, target.TransmissionRpm);
        _targetGear = MathHelper.Clamp(target.Gear, -1f, 8f);
        _targetBackfire = MathHelper.Clamp(target.Backfire, 0f, 1f);
        _targetCrankPhase = target.CrankPhaseDegrees;
        float phaseError = SignedPhaseDifference(_targetCrankPhase, _engineModel.CrankPhaseDegrees);
        if (MathF.Abs(phaseError) > 75f)
        {
            _phaseSyncPending = true;
        }
    }

    public float NextSample()
    {
        float rpmDelta = MathF.Abs(_targetRpm - _currentRpm);
        float rpmTrackingStep = rpmDelta > 850f || _targetLimiter > 0.12f || _targetShock > 0.18f
            ? FastRpmTrackingStep
            : RpmTrackingStep;
        _currentRpm = MathHelper.Lerp(_currentRpm, _targetRpm, rpmTrackingStep);
        _currentThrottle = MathHelper.Lerp(_currentThrottle, _targetThrottle, ThrottleTrackingStep);
        _currentLoad = MathHelper.Lerp(_currentLoad, _targetLoad, LoadTrackingStep);
        _currentVtec = MathHelper.Lerp(_currentVtec, _targetVtec, VtecTrackingStep);
        _currentLimiter = MathHelper.Lerp(_currentLimiter, _targetLimiter, LimiterTrackingStep);
        _currentOverrun = MathHelper.Lerp(_currentOverrun, _targetOverrun, OverrunTrackingStep);
        _currentShock = MathHelper.Lerp(_currentShock, _targetShock, ShockTrackingStep);
        _currentIntake = MathHelper.Lerp(_currentIntake, _targetIntake, IntakeTrackingStep);
        _currentThrottleTransient = MathHelper.Lerp(_currentThrottleTransient, _targetThrottleTransient, ThrottleTransientTrackingStep);
        _currentDriveline = MathHelper.Lerp(_currentDriveline, _targetDriveline, DrivelineTrackingStep);
        _currentSpeed = MathHelper.Lerp(_currentSpeed, _targetSpeed, SpeedTrackingStep);
        _currentTransmissionRpm = MathHelper.Lerp(_currentTransmissionRpm, _targetTransmissionRpm, TransmissionRpmTrackingStep);
        _currentGear = MathHelper.Lerp(_currentGear, _targetGear, SpeedTrackingStep);
        _currentBackfire = MathHelper.Lerp(_currentBackfire, _targetBackfire, ThrottleTransientTrackingStep);
        if (_phaseSyncPending)
        {
            _engineModel.SynchronizeCrankPhase(_targetCrankPhase);
            _phaseSyncPending = false;
        }

        _dsp.SetOperatingPoint(
            _currentRpm,
            _currentThrottle,
            _currentLoad,
            _currentVtec,
            _currentLimiter,
            _currentOverrun,
            _currentShock,
            _currentIntake,
            _currentThrottleTransient,
            _currentDriveline,
            _currentSpeed,
            _currentGear,
            _currentTransmissionRpm,
            _currentBackfire);
        ReadInterpolatedSimulationInput();
        return SoftLimit(_dsp.Process(_interpolatedSimulationInput));
    }

    public void Reset()
    {
        _targetRpm = 900f;
        _currentRpm = 900f;
        _targetThrottle = 0f;
        _currentThrottle = 0f;
        _targetLoad = 0f;
        _currentLoad = 0f;
        _targetVtec = 0f;
        _currentVtec = 0f;
        _targetLimiter = 0f;
        _currentLimiter = 0f;
        _targetOverrun = 0f;
        _currentOverrun = 0f;
        _targetShock = 0f;
        _currentShock = 0f;
        _targetIntake = 0f;
        _currentIntake = 0f;
        _targetThrottleTransient = 0f;
        _currentThrottleTransient = 0f;
        _targetDriveline = 0f;
        _currentDriveline = 0f;
        _targetSpeed = 0f;
        _currentSpeed = 0f;
        _targetTransmissionRpm = 0f;
        _currentTransmissionRpm = 0f;
        _targetGear = 0f;
        _currentGear = 0f;
        _targetBackfire = 0f;
        _currentBackfire = 0f;
        _targetCrankPhase = 0f;
        _phaseSyncPending = false;
        _simulationPhase = 0f;
        _hasSimulationInput = false;
        Array.Clear(_previousSimulationInput);
        Array.Clear(_nextSimulationInput);
        Array.Clear(_interpolatedSimulationInput);
        foreach (ButterworthLowPassFilter filter in _inputAntialiasingFilters)
        {
            filter.Reset();
        }

        _engineModel.Reset();
        _dsp.Reset();
    }

    private void ReadInterpolatedSimulationInput()
    {
        if (!_hasSimulationInput)
        {
            GenerateSimulationInput(_previousSimulationInput);
            GenerateSimulationInput(_nextSimulationInput);
            _hasSimulationInput = true;
        }

        _simulationPhase += _simulationAdvancePerAudioSample;
        while (_simulationPhase >= 1f)
        {
            Array.Copy(_nextSimulationInput, _previousSimulationInput, _nextSimulationInput.Length);
            GenerateSimulationInput(_nextSimulationInput);
            _simulationPhase -= 1f;
        }

        float alpha = MathHelper.Clamp(_simulationPhase, 0f, 1f);
        for (int i = 0; i < _interpolatedSimulationInput.Length; i++)
        {
            float sample = MathHelper.Lerp(_previousSimulationInput[i], _nextSimulationInput[i], alpha);
            _interpolatedSimulationInput[i] = _inputAntialiasingFilters[i].Process(sample);
        }
    }

    private void GenerateSimulationInput(float[] target)
    {
        Array.Clear(target);
        float limiterDrive = MathHelper.Clamp(_currentLimiter * MathF.Max(0f, _parameters.EngineSimulatorLimiterGain), 0f, 2f);
        float overrunDrive = MathHelper.Clamp(_currentOverrun * MathF.Max(0f, _parameters.EngineSimulatorOverrunGain), 0f, 2.5f);
        float shockDrive = MathHelper.Clamp(_currentShock * MathF.Max(0f, _parameters.EngineSimulatorShockGain), 0f, 2.5f);
        _engineModel.Step(
            MathF.Max(450f, _currentRpm),
            _currentThrottle,
            _currentLoad,
            _currentVtec,
            limiterDrive,
            overrunDrive,
            shockDrive,
            target);
    }

    private static float SoftLimit(float value)
    {
        const float threshold = 0.92f;
        const float ceiling = 0.985f;
        float absolute = MathF.Abs(value);
        if (absolute <= threshold)
        {
            return value;
        }

        float range = ceiling - threshold;
        float compressed = threshold + range * (1f - MathF.Exp(-(absolute - threshold) / range));
        return MathF.CopySign(MathF.Min(ceiling, compressed), value);
    }

    private static float SignedPhaseDifference(float target, float current)
    {
        float difference = (target - current) % 720f;
        if (difference > 360f)
        {
            difference -= 720f;
        }
        else if (difference < -360f)
        {
            difference += 720f;
        }

        return difference;
    }

    private static string FormatScriptPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? "none"
            : path.Replace('\\', '/');
    }

    private sealed class ButterworthLowPassFilter
    {
        private readonly float[] _x = new float[4];
        private readonly float[] _y = new float[4];
        private readonly float[] _a = new float[5];
        private float _f4;

        public void SetCutoffFrequency(float cutoffFrequency, float sampleRate)
        {
            float f = MathF.Tan(MathF.PI * cutoffFrequency / MathF.Max(1f, sampleRate));
            float f2 = f * f;
            float f3 = f2 * f;
            float f4 = f2 * f2;
            float m = -2f * MathF.Cos(5f * MathF.PI / 8f);
            float n = -2f * MathF.Cos(7f * MathF.PI / 8f);

            _a[0] = 1f + (m + n) * f + (2f + n * m) * f2 + (m + n) * f3 + f4;
            _a[1] = (-4f - 2f * (n + m) * f + 2f * (m + n) * f3 + 4f * f4) / _a[0];
            _a[2] = (6f - 2f * (2f + m * n) * f2 + 6f * f4) / _a[0];
            _a[3] = (-4f + 2f * (m + n) * f - 2f * (m + n) * f3 + 4f * f4) / _a[0];
            _a[4] = (1f - (n + m) * f + (2f + m * n) * f2 - (m + n) * f3 + f4) / _a[0];
            _f4 = f4;
        }

        public float Process(float sample)
        {
            float n = _f4 / _a[0] * (sample + 4f * _x[3] + 6f * _x[2] + 4f * _x[1] + _x[0]);
            float d = -_a[1] * _y[3] - _a[2] * _y[2] - _a[3] * _y[1] - _a[4] * _y[0];
            float y = n + d;

            _x[0] = _x[1];
            _x[1] = _x[2];
            _x[2] = _x[3];
            _x[3] = sample;
            _y[0] = _y[1];
            _y[1] = _y[2];
            _y[2] = _y[3];
            _y[3] = y;
            return y;
        }

        public void Reset()
        {
            Array.Clear(_x);
            Array.Clear(_y);
        }
    }
}

internal readonly record struct EngineSimulatorSynthesisTarget(
    float Rpm,
    float Throttle,
    float Load,
    float VtecBlend,
    float Limiter,
    float Overrun,
    float Shock,
    float Intake,
    float ThrottleTransient,
    float Driveline,
    float SpeedMetersPerSecond = 0f,
    float Gear = 0f,
    float TransmissionRpm = 0f,
    float Backfire = 0f,
    float CrankPhaseDegrees = 0f)
{
    public EngineSimulatorSynthesisTarget(
        float rpm,
        float throttle,
        float load,
        float vtecBlend,
        float limiter,
        float overrun,
        float shock)
        : this(rpm, throttle, load, vtecBlend, limiter, overrun, shock, 0f, 0f, 0f, 0f, 0f, 0f)
    {
    }
}
