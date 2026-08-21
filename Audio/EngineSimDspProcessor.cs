using Microsoft.Xna.Framework;
using RetroRacer.Vehicle;
using SimdVector = System.Numerics.Vector<float>;

namespace RetroRacer.Audio;

internal sealed class EngineSimDspProcessor
{
    private const float Int16Scale = 32768f;
    // The MR texture values are useful reference settings, but are too hot
    // for a real-time managed output path and make the result sound like
    // digital static. Keep the simulated pressure signal intact and soften
    // only the deliberately noisy presentation layer.
    // Randomly reordering combustion samples creates a granular, insect-like
    // texture in the managed realtime path. Keep the deterministic solver
    // waveform intact and reserve jitter for offline reference experiments.
    private const float RealtimeJitterScale = 0.0f;
    private const float RealtimeAirNoiseScale = 0.12f;
    private readonly ChannelFilters[] _filters;
    private readonly ButterworthLowPassFilter _antialiasing = new();
    private readonly LevelingFilter _levelingFilter = new();
    private readonly OnePoleLowPassFilter _intakeBodyFilter = new();
    private readonly OnePoleLowPassFilter _transientBodyFilter = new();
    private readonly OnePoleLowPassFilter _drivelineBodyFilter = new();
    private readonly BiquadBandPassFilter _exhaustResonanceLow = new();
    private readonly BiquadBandPassFilter _exhaustResonanceHigh = new();
    private readonly AudioParameters _audioParameters;
    private readonly float _sampleRate;
    private readonly int _exhaustChannelCount;
    private readonly double[] _channelEnergy;
    private readonly float[] _channelPeak;
    private long _channelSampleCount;
    private float _runtimeDerivativeMix;
    private float _runtimeInputSampleNoise;
    private float _runtimeAirNoise;
    private float _runtimeIntakeLayer;
    private float _runtimeVtecLayer;
    private float _runtimeThrottleTransientLayer;
    private float _runtimeDrivelineLayer;
    private float _runtimeRpm;
    private float _runtimeThrottle;
    private float _runtimeLoad;
    private float _runtimeVtec;
    private float _runtimeSpeed;
    private float _runtimeGear;
    private float _runtimeTransmissionRpm;
    private float _runtimeBackfire;
    private uint _noiseState = 0x8f93a2bdu;

    public EngineSimDspProcessor(VehicleAudioParameters parameters, int sampleRate, int inputChannelCount)
    {
        int channelCount = Math.Max(1, inputChannelCount);
        _filters = new ChannelFilters[channelCount];
        _channelEnergy = new double[channelCount];
        _channelPeak = new float[channelCount];
        _exhaustChannelCount = Math.Clamp(parameters.EngineSimulatorExhaustVolumes.Length, 1, Math.Max(1, channelCount - 4));
        _audioParameters = new AudioParameters
        {
            Volume = MathF.Max(0f, parameters.EngineSimulatorDspOutputGain),
            // Match the known-good clean realtime path from commit 5425575.
            // Optional presentation DSP is kept out of the base waveform.
            Convolution = 0f,
            DerivativeMix = 0f,
            InputSampleNoise = 0f,
            InputSampleNoiseFrequencyCutoff = 10000f,
            AirNoise = 0f,
            AirNoiseFrequencyCutoff = 2000f,
            LevelerTarget = 24000f,
            LevelerMaxGain = 1.25f,
            LevelerMinGain = 0.00001f
        };
        _runtimeDerivativeMix = _audioParameters.DerivativeMix;
        _runtimeInputSampleNoise = _audioParameters.InputSampleNoise;
        _runtimeAirNoise = _audioParameters.AirNoise;
        _runtimeVtecLayer = 0f;

        float safeSampleRate = MathF.Max(1f, sampleRate);
        _sampleRate = safeSampleRate;
        float[] impulseResponse = LoadImpulseResponse(parameters);
        for (int i = 0; i < _filters.Length; i++)
        {
            _filters[i] = new ChannelFilters(safeSampleRate, impulseResponse, ChannelSourceCutoff(i, safeSampleRate));
        }

        _antialiasing.SetCutoffFrequency(safeSampleRate * 0.45f, safeSampleRate);
        _intakeBodyFilter.Dt = 1f / safeSampleRate;
        _intakeBodyFilter.SetCutoffFrequency(620f);
        _transientBodyFilter.Dt = 1f / safeSampleRate;
        _transientBodyFilter.SetCutoffFrequency(1450f);
        _drivelineBodyFilter.Dt = 1f / safeSampleRate;
        _drivelineBodyFilter.SetCutoffFrequency(240f);
        _exhaustResonanceLow.Set(420f, 2.2f, safeSampleRate);
        _exhaustResonanceHigh.Set(980f, 2.8f, safeSampleRate);
        _levelingFilter.Target = _audioParameters.LevelerTarget;
        _levelingFilter.MaxLevel = _audioParameters.LevelerMaxGain;
        _levelingFilter.MinLevel = _audioParameters.LevelerMinGain;

        AudioDiagnostics.Log(
            "engine-sim-dsp",
            $"ported synthesizer, channels {channelCount}, hf {_audioParameters.DerivativeMix:0.0000}, noise {_audioParameters.AirNoise:0.000}, jitter {_audioParameters.InputSampleNoise:0.000}, idle texture scaled, convolution wet {_audioParameters.Convolution:0.00}, conv taps {impulseResponse.Length}, {ConvolutionFilter.Describe(impulseResponse.Length)}, pressure scale {parameters.EngineSimulatorDspPressureScale:0.0}, output gain {_audioParameters.Volume:0.00}");
    }

    public int ChannelCount => _filters.Length;

    public void SetOperatingPoint(
        float rpm,
        float throttle,
        float load,
        float vtecBlend,
        float limiter,
        float overrun,
        float shock,
        float intake,
        float throttleTransient,
        float driveline,
        float speedMetersPerSecond,
        float gear,
        float transmissionRpm,
        float backfire)
    {
        float rpmTexture = SmoothStep(1500f, 5200f, rpm);
        float loadTexture = SmoothStep(0.22f, 0.82f, load);
        float transientTexture = MathHelper.Clamp(
            vtecBlend * 0.60f +
            limiter * 0.90f +
            overrun * 0.35f +
            shock * 0.45f,
            0f,
            1f);
        float texture = MathHelper.Clamp(
            MathF.Max(rpmTexture, loadTexture * 0.85f) +
            MathHelper.Clamp(throttle, 0f, 1f) * 0.16f +
            transientTexture * 0.32f,
            0f,
            1f);

        _runtimeInputSampleNoise = _audioParameters.InputSampleNoise * MathHelper.Lerp(0.22f, 1f, texture);
        _runtimeAirNoise = _audioParameters.AirNoise * MathHelper.Lerp(0.30f, 1f, texture);
        _runtimeDerivativeMix = MathHelper.Clamp(
            _audioParameters.DerivativeMix * MathHelper.Lerp(0.55f, 1f, texture) +
            MathHelper.Clamp(vtecBlend, 0f, 1f) * 0.012f +
            MathHelper.Clamp(limiter, 0f, 1f) * 0.0010f,
            0f,
            0.25f);
        _runtimeIntakeLayer = MathHelper.Clamp(
            intake * MathHelper.Lerp(0.45f, 1f, SmoothStep(1600f, 7200f, rpm)) *
            (0.55f + MathHelper.Clamp(throttle, 0f, 1f) * 0.25f + MathHelper.Clamp(load, 0f, 1f) * 0.20f) +
            vtecBlend * MathHelper.Clamp(throttle, 0f, 1f) * 0.10f,
            0f,
            1f);
        _runtimeVtecLayer = MathHelper.Clamp(vtecBlend, 0f, 1f);
        _runtimeThrottleTransientLayer = MathHelper.Clamp(
            throttleTransient * MathHelper.Lerp(0.62f, 1f, loadTexture) +
            shock * 0.12f,
            0f,
            1f);
        _runtimeDrivelineLayer = MathHelper.Clamp(driveline, 0f, 1f);
        _runtimeRpm = MathF.Max(450f, rpm);
        _runtimeThrottle = MathHelper.Clamp(throttle, 0f, 1f);
        _runtimeLoad = MathHelper.Clamp(load, 0f, 1f);
        _runtimeVtec = MathHelper.Clamp(vtecBlend, 0f, 1f);
        _runtimeSpeed = MathF.Max(0f, speedMetersPerSecond);
        _runtimeGear = MathHelper.Clamp(gear, -1f, 8f);
        _runtimeTransmissionRpm = MathF.Max(0f, transmissionRpm);
        _runtimeBackfire = MathHelper.Clamp(backfire, 0f, 1f);

        float rpmNorm = SmoothStep(900f, 9000f, _runtimeRpm);
        _exhaustResonanceLow.Set(MathHelper.Lerp(360f, 620f, rpmNorm), 2.2f, _sampleRate);
        _exhaustResonanceHigh.Set(MathHelper.Lerp(820f, 1850f, rpmNorm), 2.8f, _sampleRate);
    }

    public float Process(ReadOnlySpan<float> input)
    {
        float signal = 0f;
        for (int i = 0; i < _filters.Length; i++)
        {
            ChannelFilters filters = _filters[i];
            float source = i < input.Length ? input[i] : 0f;
            float jitteredSample = filters.Jitter.Process(source, _runtimeInputSampleNoise);
            float fIn = jitteredSample;
            float fDc = filters.InputDcFilter.Process(fIn);
            float f = fIn - fDc;
            float fP = filters.Derivative.Process(fIn);
            float noise = NextNoise();
            float r = filters.AirNoiseLowPass.Process(noise);
            float rMixed = 1f + _runtimeAirNoise * r;

            float derivativeMix = _runtimeDerivativeMix;
            float vIn = fP * derivativeMix +
                        f * rMixed * (1f - derivativeMix);
            vIn = filters.SourceFilter.Process(vIn);
            if (MathF.Abs(vIn) < 1.0e-30f)
            {
                vIn = 0f;
            }

            float v = vIn;
            _channelEnergy[i] += v * v;
            _channelPeak[i] = MathF.Max(_channelPeak[i], MathF.Abs(v));
            signal += v * ChannelGain(i);
        }
        _channelSampleCount++;
        LogChannelDiagnosticsIfNeeded();

        signal = _antialiasing.Process(signal);
        _levelingFilter.Target = _audioParameters.LevelerTarget;
        float output = _levelingFilter.Process(signal) * _audioParameters.Volume / Int16Scale;
        // The base engine waveform must remain the Engine Sim waveform. The
        // former synthetic resonance, sine, and texture overlays were the
        // source of the insect-like character and are handled by their own
        // physical channels in the simulator instead.
        return MathHelper.Clamp(output, -1f, 1f);
    }

    private float ChannelGain(int channelIndex)
    {
        if (channelIndex < _exhaustChannelCount)
        {
            return 0.55f;
        }

        int relative = channelIndex - _exhaustChannelCount;
        return relative switch
        {
            0 => 0.34f, // intake
            1 => 0.90f, // combustion pressure
            2 => 0.26f, // piston/crank motion
            3 => 0.20f, // valvetrain flow
            _ => 0.25f
        };
    }

    private float ChannelSourceCutoff(int channelIndex, float sampleRate)
    {
        if (channelIndex < _exhaustChannelCount)
        {
            return MathF.Min(9000f, sampleRate * 0.40f);
        }

        return (channelIndex - _exhaustChannelCount) switch
        {
            0 => 3200f, // intake
            1 => 1800f, // combustion pressure
            2 => 650f,  // crank/piston mechanical motion
            3 => 4200f, // valvetrain flow
            _ => 3200f
        };
    }

    public void Reset()
    {
        _noiseState = 0x8f93a2bdu;
        _antialiasing.Reset();
        _intakeBodyFilter.Reset();
        _transientBodyFilter.Reset();
        _drivelineBodyFilter.Reset();
        _exhaustResonanceLow.Reset();
        _exhaustResonanceHigh.Reset();
        _levelingFilter.Reset();
        Array.Clear(_channelEnergy);
        Array.Clear(_channelPeak);
        _channelSampleCount = 0;
        foreach (ChannelFilters filters in _filters)
        {
            filters.Reset();
        }
    }

    private float AdvancePhase(float phase, float frequency)
    {
        phase += frequency / _sampleRate;
        return phase >= 1f ? phase - MathF.Floor(phase) : phase;
    }

    private void LogChannelDiagnosticsIfNeeded()
    {
        if (_channelSampleCount < (long)(_sampleRate * 2f))
        {
            return;
        }

        string[] names = new string[_filters.Length];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = i < _exhaustChannelCount
                ? $"exhaust{i}"
                : (i - _exhaustChannelCount) switch
                {
                    0 => "intake",
                    1 => "combustion",
                    2 => "piston",
                    3 => "valvetrain",
                    _ => $"channel{i}"
                };
        }

        double inverseSamples = 1.0 / _channelSampleCount;
        string report = string.Join(
            ", ",
            Enumerable.Range(0, _filters.Length).Select(i =>
                $"{names[i]} rms {Math.Sqrt(_channelEnergy[i] * inverseSamples):0.000000} peak {_channelPeak[i]:0.000000}"));
        AudioDiagnostics.Log("engine-sim-channel-levels", report);
        Array.Clear(_channelEnergy);
        Array.Clear(_channelPeak);
        _channelSampleCount = 0;
    }

    private static float[] LoadImpulseResponse(VehicleAudioParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.EngineSimulatorImpulseResponsePath) ||
            parameters.EngineSimulatorImpulseResponseTaps <= 0)
        {
            return [];
        }

        try
        {
            string path = ResolveAssetPath(parameters.EngineSimulatorImpulseResponsePath);
            WavLoopSource source = WavLoopSource.Load(path);
            float threshold = 100f / Int16Scale;
            int clippedLength = 0;
            for (int frame = 0; frame < source.FrameCount; frame++)
            {
                float sample = ReadMono(source, frame);
                if (MathF.Abs(sample) > threshold)
                {
                    clippedLength = frame + 1;
                }
            }

            int requestedTaps = Math.Clamp(parameters.EngineSimulatorImpulseResponseTaps, 8, 10000);
            int sampleCount = Math.Min(requestedTaps, clippedLength);
            if (sampleCount <= 0)
            {
                return [];
            }

            float[] taps = new float[sampleCount];
            for (int i = 0; i < taps.Length; i++)
            {
                taps[i] = parameters.EngineSimulatorImpulseResponseVolume * ReadMono(source, i);
            }

            AudioDiagnostics.Log(
                "engine-sim-dsp-ir",
                $"{parameters.EngineSimulatorImpulseResponsePath}, {source.SampleRate} Hz, {source.ChannelCount} ch, source frames {source.FrameCount}, clipped {clippedLength}, taps {taps.Length}, volume {parameters.EngineSimulatorImpulseResponseVolume:0.000}");
            return taps;
        }
        catch (Exception ex)
        {
            AudioDiagnostics.Log("engine-sim-dsp-ir-error", $"{parameters.EngineSimulatorImpulseResponsePath}: {ex.Message}");
            return [];
        }
    }

    private static float ReadMono(WavLoopSource source, int frame)
    {
        int offset = Math.Clamp(frame, 0, source.FrameCount - 1) * source.ChannelCount;
        if (source.ChannelCount == 1)
        {
            return source.Samples[offset];
        }

        return (source.Samples[offset] + source.Samples[offset + 1]) * 0.5f;
    }

    private static string ResolveAssetPath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, path),
            Path.Combine(AppContext.BaseDirectory, path)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Engine Sim impulse response was not found: {path}", path);
    }

    private float NextNoise()
    {
        _noiseState = _noiseState * 1664525u + 1013904223u;
        return ((_noiseState >> 8) / 8388607.5f) - 1f;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private sealed class ChannelFilters
    {
        public ChannelFilters(float sampleRate, float[] impulseResponse, float sourceCutoff)
        {
            Derivative.Dt = 1f / sampleRate;
            InputDcFilter.Dt = 1f / sampleRate;
            InputDcFilter.SetCutoffFrequency(10f);
            SourceFilter.Dt = 1f / sampleRate;
            SourceFilter.SetCutoffFrequency(sourceCutoff);
            Jitter.Initialize(10, 10000f, sampleRate);
            AirNoiseLowPass.SetCutoffFrequency(2000f, sampleRate);
            Convolution.Initialize(impulseResponse);
        }

        public ConvolutionFilter Convolution { get; } = new();

        public DerivativeFilter Derivative { get; } = new();

        public JitterFilter Jitter { get; } = new();

        public ButterworthLowPassFilter AirNoiseLowPass { get; } = new();

        public OnePoleLowPassFilter InputDcFilter { get; } = new();

        public OnePoleLowPassFilter SourceFilter { get; } = new();

        public void Reset()
        {
            Convolution.Reset();
            Derivative.Reset();
            Jitter.Reset();
            AirNoiseLowPass.Reset();
            InputDcFilter.Reset();
            SourceFilter.Reset();
        }
    }

    private sealed class ConvolutionFilter
    {
        private const int DirectTapLimit = 512;
        private const int TailDecimation = 8;

        private float[] _directShiftRegister = [];
        private float[] _directImpulseResponse = [];
        private float[] _tailInputDelay = [];
        private float[] _tailHistory = [];
        private float[] _tailImpulseResponse = [];
        private int _directShiftOffset;
        private int _tailDelayOffset;
        private int _tailHistoryOffset;
        private int _tailAccumulatorCount;
        private int _tailInterpolationPosition = TailDecimation;
        private float _tailAccumulator;
        private float _tailPreviousOutput;
        private float _tailTargetOutput;
        private float _tailCurrentOutput;

        public static string Describe(int impulseResponseLength)
        {
            if (impulseResponseLength <= 0)
            {
                return "conv disabled";
            }

            if (impulseResponseLength <= DirectTapLimit)
            {
                return "direct convolution";
            }

            int tailSamples = impulseResponseLength - DirectTapLimit;
            int tailTaps = (tailSamples + TailDecimation - 1) / TailDecimation;
            return $"hybrid convolution direct {DirectTapLimit}, tail {tailSamples}->{tailTaps}";
        }

        public void Initialize(float[] impulseResponse)
        {
            int directLength = Math.Min(impulseResponse.Length, DirectTapLimit);
            _directImpulseResponse = new float[directLength];
            _directShiftRegister = new float[directLength];
            if (directLength > 0)
            {
                Array.Copy(impulseResponse, _directImpulseResponse, directLength);
            }

            int tailSamples = impulseResponse.Length - directLength;
            if (tailSamples > 0)
            {
                int tailLength = (tailSamples + TailDecimation - 1) / TailDecimation;
                _tailImpulseResponse = new float[tailLength];
                _tailHistory = new float[tailLength];
                _tailInputDelay = new float[Math.Max(1, directLength)];
                for (int tailIndex = 0; tailIndex < tailLength; tailIndex++)
                {
                    float sum = 0f;
                    int sourceStart = directLength + tailIndex * TailDecimation;
                    int sourceEnd = Math.Min(impulseResponse.Length, sourceStart + TailDecimation);
                    for (int source = sourceStart; source < sourceEnd; source++)
                    {
                        sum += impulseResponse[source];
                    }

                    _tailImpulseResponse[tailIndex] = sum;
                }
            }
            else
            {
                _tailImpulseResponse = [];
                _tailHistory = [];
                _tailInputDelay = [];
            }

            _directShiftOffset = 0;
            _tailDelayOffset = 0;
            _tailHistoryOffset = 0;
            _tailAccumulator = 0f;
            _tailAccumulatorCount = 0;
            _tailPreviousOutput = 0f;
            _tailTargetOutput = 0f;
            _tailCurrentOutput = 0f;
            _tailInterpolationPosition = TailDecimation;
        }

        public float Process(float sample)
        {
            if (_directImpulseResponse.Length == 0 && _tailImpulseResponse.Length == 0)
            {
                return sample;
            }

            return ProcessDirect(sample) + ProcessTail(sample);
        }

        private float ProcessDirect(float sample)
        {
            if (_directImpulseResponse.Length == 0)
            {
                return 0f;
            }

            _directShiftRegister[_directShiftOffset] = sample;

            int split = _directImpulseResponse.Length - _directShiftOffset;
            float result =
                Dot(_directImpulseResponse, 0, _directShiftRegister, _directShiftOffset, split) +
                Dot(_directImpulseResponse, split, _directShiftRegister, 0, _directImpulseResponse.Length - split);

            _directShiftOffset--;
            if (_directShiftOffset < 0)
            {
                _directShiftOffset = _directImpulseResponse.Length - 1;
            }

            return result;
        }

        private float ProcessTail(float sample)
        {
            if (_tailImpulseResponse.Length == 0)
            {
                return 0f;
            }

            float delayedSample = _tailInputDelay[_tailDelayOffset];
            _tailInputDelay[_tailDelayOffset] = sample;
            _tailDelayOffset--;
            if (_tailDelayOffset < 0)
            {
                _tailDelayOffset = _tailInputDelay.Length - 1;
            }

            _tailAccumulator += delayedSample;
            _tailAccumulatorCount++;
            if (_tailAccumulatorCount >= TailDecimation)
            {
                float averagedSample = _tailAccumulator / _tailAccumulatorCount;
                _tailAccumulator = 0f;
                _tailAccumulatorCount = 0;

                _tailHistoryOffset--;
                if (_tailHistoryOffset < 0)
                {
                    _tailHistoryOffset = _tailHistory.Length - 1;
                }

                _tailHistory[_tailHistoryOffset] = averagedSample;
                int split = _tailImpulseResponse.Length - _tailHistoryOffset;
                float nextOutput =
                    Dot(_tailImpulseResponse, 0, _tailHistory, _tailHistoryOffset, split) +
                    Dot(_tailImpulseResponse, split, _tailHistory, 0, _tailImpulseResponse.Length - split);
                _tailPreviousOutput = _tailCurrentOutput;
                _tailTargetOutput = nextOutput;
                _tailInterpolationPosition = 0;
            }

            if (_tailInterpolationPosition < TailDecimation)
            {
                float t = (_tailInterpolationPosition + 1f) / TailDecimation;
                _tailCurrentOutput = MathHelper.Lerp(_tailPreviousOutput, _tailTargetOutput, t);
                _tailInterpolationPosition++;
            }
            else
            {
                _tailCurrentOutput = _tailTargetOutput;
            }

            return _tailCurrentOutput;
        }

        private static float Dot(float[] left, int leftOffset, float[] right, int rightOffset, int count)
        {
            int i = 0;
            float result = 0f;
            if (System.Numerics.Vector.IsHardwareAccelerated)
            {
                SimdVector accumulator = SimdVector.Zero;
                int vectorWidth = SimdVector.Count;
                int vectorCount = count - count % vectorWidth;
                for (; i < vectorCount; i += vectorWidth)
                {
                    accumulator += new SimdVector(left, leftOffset + i) * new SimdVector(right, rightOffset + i);
                }

                for (int lane = 0; lane < vectorWidth; lane++)
                {
                    result += accumulator[lane];
                }
            }

            for (; i < count; i++)
            {
                result += left[leftOffset + i] * right[rightOffset + i];
            }

            return result;
        }

        public void Reset()
        {
            Array.Clear(_directShiftRegister);
            Array.Clear(_tailInputDelay);
            Array.Clear(_tailHistory);
            _directShiftOffset = 0;
            _tailDelayOffset = 0;
            _tailHistoryOffset = 0;
            _tailAccumulator = 0f;
            _tailAccumulatorCount = 0;
            _tailPreviousOutput = 0f;
            _tailTargetOutput = 0f;
            _tailCurrentOutput = 0f;
            _tailInterpolationPosition = TailDecimation;
        }
    }

    private sealed class DerivativeFilter
    {
        private float _previous;

        public float Dt { get; set; } = 1f / 44100f;

        public float Process(float sample)
        {
            float previous = _previous;
            _previous = sample;
            return (sample - previous) / MathF.Max(1.0e-9f, Dt);
        }

        public void Reset()
        {
            _previous = 0f;
        }
    }

    private sealed class OnePoleLowPassFilter
    {
        private float _y;
        private float _rc;

        public float Dt { get; set; } = 1f / 44100f;

        public void SetCutoffFrequency(float frequency)
        {
            _rc = 1f / (frequency * 2f * MathF.PI);
        }

        public float Process(float sample)
        {
            float alpha = Dt / (_rc + Dt);
            _y = alpha * sample + (1f - alpha) * _y;
            return _y;
        }

        public void Reset()
        {
            _y = 0f;
        }
    }

    private sealed class BiquadBandPassFilter
    {
        private float _b0;
        private float _b1;
        private float _b2;
        private float _a1;
        private float _a2;
        private float _x1;
        private float _x2;
        private float _y1;
        private float _y2;

        public void Set(float frequency, float q, float sampleRate)
        {
            float w0 = MathF.Tau * MathHelper.Clamp(frequency, 20f, sampleRate * 0.45f) / sampleRate;
            float alpha = MathF.Sin(w0) / (2f * MathF.Max(0.25f, q));
            float cos = MathF.Cos(w0);
            float a0 = 1f + alpha;
            _b0 = alpha / a0;
            _b1 = 0f;
            _b2 = -alpha / a0;
            _a1 = -2f * cos / a0;
            _a2 = (1f - alpha) / a0;
        }

        public float Process(float sample)
        {
            float y = _b0 * sample + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = sample;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        public void Reset()
        {
            _x1 = 0f;
            _x2 = 0f;
            _y1 = 0f;
            _y2 = 0f;
        }
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

    private sealed class JitterFilter
    {
        private readonly ButterworthLowPassFilter _noiseFilter = new();
        private float[] _history = [];
        private int _offset;
        private int _maxJitter;
        private uint _noiseState = 0x2f6e2b1du;

        public void Initialize(int maxJitter, float cutoffFrequency, float audioFrequency)
        {
            _maxJitter = Math.Max(1, maxJitter);
            _history = new float[_maxJitter];
            _offset = 0;
            _noiseFilter.SetCutoffFrequency(cutoffFrequency, audioFrequency);
        }

        public float Process(float sample, float jitterScale)
        {
            _history[_offset] = sample;
            _offset++;
            if (_offset >= _maxJitter)
            {
                _offset = 0;
            }

            float random = NextUnit() * (_maxJitter - 1);
            float s = _noiseFilter.Process(random * jitterScale);
            float s0 = MathHelper.Clamp(MathF.Floor(s), 0f, _maxJitter - 1);
            float s1 = MathHelper.Clamp(MathF.Ceiling(s), 0f, _maxJitter - 1);
            float fraction = s - s0;
            int i0 = (int)s0 + _offset;
            int i1 = (int)s1 + _offset;
            if (i0 >= _maxJitter) i0 -= _maxJitter;
            if (i1 >= _maxJitter) i1 -= _maxJitter;

            float v0 = _history[i0];
            float v1 = _history[i1];
            return v1 * fraction + v0 * (1f - fraction);
        }

        public void Reset()
        {
            Array.Clear(_history);
            _offset = 0;
            _noiseState = 0x2f6e2b1du;
            _noiseFilter.Reset();
        }

        private float NextUnit()
        {
            _noiseState = _noiseState * 1664525u + 1013904223u;
            return (_noiseState >> 8) / 16777215f;
        }
    }

    private sealed class LevelingFilter
    {
        private float _peak = 30000f;
        private float _attenuation = 1f;

        public float Target { get; set; } = 30000f;

        public float MaxLevel { get; set; } = 1.9f;

        public float MinLevel { get; set; } = 0.00001f;

        public float Process(float sample)
        {
            _peak *= 0.999f;
            if (MathF.Abs(sample) > _peak)
            {
                _peak = MathF.Abs(sample);
            }

            if (_peak <= 0f)
            {
                return 0f;
            }

            float rawAttenuation = Target / _peak;
            float attenuation = MathHelper.Clamp(rawAttenuation, MinLevel, MaxLevel);
            _attenuation = 0.9f * _attenuation + 0.1f * attenuation;
            return sample * _attenuation;
        }

        public void Reset()
        {
            _peak = 30000f;
            _attenuation = 1f;
        }
    }

    private sealed class AudioParameters
    {
        public float Volume { get; init; } = 1f;

        public float Convolution { get; init; } = 1f;

        public float DerivativeMix { get; init; } = 0.01f;

        public float InputSampleNoise { get; init; } = 0.5f;

        public float InputSampleNoiseFrequencyCutoff { get; init; } = 10000f;

        public float AirNoise { get; init; } = 1f;

        public float AirNoiseFrequencyCutoff { get; init; } = 2000f;

        public float LevelerTarget { get; init; } = 30000f;

        public float LevelerMaxGain { get; init; } = 1.9f;

        public float LevelerMinGain { get; init; } = 0.00001f;
    }
}
