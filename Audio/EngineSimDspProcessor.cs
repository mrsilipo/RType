using Microsoft.Xna.Framework;
using RetroRacer.Vehicle;
using SimdVector = System.Numerics.Vector<float>;

namespace RetroRacer.Audio;

internal sealed class EngineSimDspProcessor
{
    private const float Int16Scale = 32768f;
    private readonly ChannelFilters[] _filters;
    private readonly ButterworthLowPassFilter _antialiasing = new();
    private readonly LevelingFilter _levelingFilter = new();
    private readonly OnePoleLowPassFilter _intakeBodyFilter = new();
    private readonly OnePoleLowPassFilter _transientBodyFilter = new();
    private readonly OnePoleLowPassFilter _drivelineBodyFilter = new();
    private readonly AudioParameters _audioParameters;
    private float _runtimeDerivativeMix;
    private float _runtimeInputSampleNoise;
    private float _runtimeAirNoise;
    private float _runtimeIntakeLayer;
    private float _runtimeThrottleTransientLayer;
    private float _runtimeDrivelineLayer;
    private uint _noiseState = 0x8f93a2bdu;

    public EngineSimDspProcessor(VehicleAudioParameters parameters, int sampleRate, int inputChannelCount)
    {
        int channelCount = Math.Max(1, inputChannelCount);
        _filters = new ChannelFilters[channelCount];
        _audioParameters = new AudioParameters
        {
            Volume = MathF.Max(0f, parameters.EngineSimulatorDspOutputGain),
            Convolution = 1f,
            DerivativeMix = MathHelper.Clamp(parameters.EngineSimulatorHighFrequencyGain, 0f, 0.25f),
            InputSampleNoise = MathHelper.Clamp(parameters.EngineSimulatorJitter, 0f, 1f),
            InputSampleNoiseFrequencyCutoff = 10000f,
            AirNoise = MathHelper.Clamp(parameters.EngineSimulatorNoise, 0f, 1f),
            AirNoiseFrequencyCutoff = 2000f,
            LevelerTarget = 30000f,
            LevelerMaxGain = 1.9f,
            LevelerMinGain = 0.00001f
        };
        _runtimeDerivativeMix = _audioParameters.DerivativeMix;
        _runtimeInputSampleNoise = _audioParameters.InputSampleNoise;
        _runtimeAirNoise = _audioParameters.AirNoise;

        float safeSampleRate = MathF.Max(1f, sampleRate);
        float[] impulseResponse = LoadImpulseResponse(parameters);
        for (int i = 0; i < _filters.Length; i++)
        {
            _filters[i] = new ChannelFilters(safeSampleRate, impulseResponse);
        }

        _antialiasing.SetCutoffFrequency(safeSampleRate * 0.45f, safeSampleRate);
        _intakeBodyFilter.Dt = 1f / safeSampleRate;
        _intakeBodyFilter.SetCutoffFrequency(620f);
        _transientBodyFilter.Dt = 1f / safeSampleRate;
        _transientBodyFilter.SetCutoffFrequency(1450f);
        _drivelineBodyFilter.Dt = 1f / safeSampleRate;
        _drivelineBodyFilter.SetCutoffFrequency(240f);
        _levelingFilter.Target = _audioParameters.LevelerTarget;
        _levelingFilter.MaxLevel = _audioParameters.LevelerMaxGain;
        _levelingFilter.MinLevel = _audioParameters.LevelerMinGain;

        AudioDiagnostics.Log(
            "engine-sim-dsp",
            $"ported synthesizer, channels {channelCount}, hf {_audioParameters.DerivativeMix:0.0000}, noise {_audioParameters.AirNoise:0.000}, jitter {_audioParameters.InputSampleNoise:0.000}, idle texture scaled, conv taps {impulseResponse.Length}, pressure scale {parameters.EngineSimulatorDspPressureScale:0.0}, output gain {_audioParameters.Volume:0.00}");
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
        float driveline)
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
            MathHelper.Clamp(vtecBlend, 0f, 1f) * 0.0016f +
            MathHelper.Clamp(limiter, 0f, 1f) * 0.0010f,
            0f,
            0.25f);
        _runtimeIntakeLayer = MathHelper.Clamp(
            intake * MathHelper.Lerp(0.58f, 1f, SmoothStep(1600f, 7200f, rpm)) +
            vtecBlend * MathHelper.Clamp(throttle, 0f, 1f) * 0.10f,
            0f,
            1f);
        _runtimeThrottleTransientLayer = MathHelper.Clamp(
            throttleTransient * MathHelper.Lerp(0.62f, 1f, loadTexture) +
            shock * 0.12f,
            0f,
            1f);
        _runtimeDrivelineLayer = MathHelper.Clamp(driveline, 0f, 1f);
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
            float rMixed = _runtimeAirNoise * r + (1f - _runtimeAirNoise);

            float derivativeMix = _runtimeDerivativeMix;
            float vIn = fP * derivativeMix +
                        f * rMixed * (1f - derivativeMix);
            if (MathF.Abs(vIn) < 1.0e-30f)
            {
                vIn = 0f;
            }

            float convolved = filters.Convolution.Process(vIn);
            float v = _audioParameters.Convolution * convolved +
                      (1f - _audioParameters.Convolution) * vIn;
            signal += v;
        }

        signal = _antialiasing.Process(signal);
        _levelingFilter.Target = _audioParameters.LevelerTarget;
        float output = _levelingFilter.Process(signal) * _audioParameters.Volume / Int16Scale;
        float intakeSource = output - _intakeBodyFilter.Process(output);
        float transientSource = output - _transientBodyFilter.Process(output);
        float drivelineSource = _drivelineBodyFilter.Process(output);
        float layered = output +
                        intakeSource * _runtimeIntakeLayer * 0.115f +
                        transientSource * _runtimeThrottleTransientLayer * 0.075f +
                        drivelineSource * _runtimeDrivelineLayer * 0.040f;
        return MathHelper.Clamp(layered, -1f, 1f);
    }

    public void Reset()
    {
        _noiseState = 0x8f93a2bdu;
        _antialiasing.Reset();
        _intakeBodyFilter.Reset();
        _transientBodyFilter.Reset();
        _drivelineBodyFilter.Reset();
        _levelingFilter.Reset();
        foreach (ChannelFilters filters in _filters)
        {
            filters.Reset();
        }
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
        public ChannelFilters(float sampleRate, float[] impulseResponse)
        {
            Derivative.Dt = 1f / sampleRate;
            InputDcFilter.Dt = 1f / sampleRate;
            InputDcFilter.SetCutoffFrequency(10f);
            Jitter.Initialize(10, 10000f, sampleRate);
            AirNoiseLowPass.SetCutoffFrequency(2000f, sampleRate);
            Convolution.Initialize(impulseResponse);
        }

        public ConvolutionFilter Convolution { get; } = new();

        public DerivativeFilter Derivative { get; } = new();

        public JitterFilter Jitter { get; } = new();

        public ButterworthLowPassFilter AirNoiseLowPass { get; } = new();

        public OnePoleLowPassFilter InputDcFilter { get; } = new();

        public void Reset()
        {
            Convolution.Reset();
            Derivative.Reset();
            Jitter.Reset();
            AirNoiseLowPass.Reset();
            InputDcFilter.Reset();
        }
    }

    private sealed class ConvolutionFilter
    {
        private float[] _shiftRegister = [];
        private float[] _impulseResponse = [];
        private int _shiftOffset;

        public void Initialize(float[] impulseResponse)
        {
            _impulseResponse = impulseResponse;
            _shiftRegister = new float[impulseResponse.Length];
            _shiftOffset = 0;
        }

        public float Process(float sample)
        {
            if (_impulseResponse.Length == 0)
            {
                return sample;
            }

            _shiftRegister[_shiftOffset] = sample;

            int split = _impulseResponse.Length - _shiftOffset;
            float result =
                Dot(_impulseResponse, 0, _shiftRegister, _shiftOffset, split) +
                Dot(_impulseResponse, split, _shiftRegister, 0, _impulseResponse.Length - split);

            _shiftOffset--;
            if (_shiftOffset < 0)
            {
                _shiftOffset = _impulseResponse.Length - 1;
            }

            return result;
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
            Array.Clear(_shiftRegister);
            _shiftOffset = 0;
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
