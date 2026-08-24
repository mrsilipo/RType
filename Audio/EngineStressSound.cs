using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace RType.Audio;

internal sealed class EngineStressSound : IDisposable
{
    private const int SampleRate = 44100;
    private const int FramesPerBuffer = 1024;
    private const int TargetPendingBuffers = 3;
    private const int BufferRingSize = 8;

    private readonly DynamicSoundEffectInstance _instance = new(SampleRate, AudioChannels.Mono);
    private readonly byte[][] _buffers;
    private uint _noiseState = 0x9e3779b9u;
    private float _currentLimiterIntensity;
    private float _targetLimiterIntensity;
    private float _currentOverRevIntensity;
    private float _targetOverRevIntensity;
    private float _rpm = 900f;
    private float _chatterPhase;
    private float _ringPhase;
    private float _raspFilter;
    private float _metalResonance;
    private int _nextBufferIndex;

    public EngineStressSound()
    {
        _buffers = new byte[BufferRingSize][];
        int bufferBytes = FramesPerBuffer * sizeof(short);
        for (int i = 0; i < _buffers.Length; i++)
        {
            _buffers[i] = new byte[bufferBytes];
        }
    }

    public void Update(float limiterIntensity, float overRevIntensity, float rpm, float viewVolume, float pauseScale)
    {
        _targetLimiterIntensity = MathHelper.Clamp(limiterIntensity, 0f, 1f);
        _targetOverRevIntensity = MathHelper.Clamp(overRevIntensity, 0f, 1f);
        _rpm = MathHelper.Clamp(rpm, 300f, 12500f);

        float targetIntensity = MathF.Max(_targetLimiterIntensity, _targetOverRevIntensity);
        float currentIntensity = MathF.Max(_currentLimiterIntensity, _currentOverRevIntensity);
        if (targetIntensity <= 0.004f && currentIntensity <= 0.004f)
        {
            Stop();
            return;
        }

        float loudness = _targetLimiterIntensity * 0.46f +
                         _targetOverRevIntensity * 0.66f +
                         MathF.Max(targetIntensity, currentIntensity) * 0.22f;
        _instance.Volume = MathHelper.Clamp(loudness * viewVolume * pauseScale, 0f, 0.95f);

        while (_instance.PendingBufferCount < TargetPendingBuffers)
        {
            _instance.SubmitBuffer(CreateBuffer());
        }

        if (_instance.State != SoundState.Playing)
        {
            _instance.Play();
        }
    }

    public void Stop()
    {
        if (_instance.State != SoundState.Stopped)
        {
            _instance.Stop();
        }

        _currentLimiterIntensity = 0f;
        _targetLimiterIntensity = 0f;
        _currentOverRevIntensity = 0f;
        _targetOverRevIntensity = 0f;
        _raspFilter = 0f;
        _metalResonance = 0f;
    }

    public void Dispose()
    {
        Stop();
        _instance.Dispose();
    }

    private byte[] CreateBuffer()
    {
        byte[] buffer = _buffers[_nextBufferIndex];
        _nextBufferIndex = (_nextBufferIndex + 1) % _buffers.Length;
        float rpmT = SmoothStep(4500f, 10500f, _rpm);

        for (int frame = 0; frame < FramesPerBuffer; frame++)
        {
            _currentLimiterIntensity = MathHelper.Lerp(_currentLimiterIntensity, _targetLimiterIntensity, 0.0065f);
            _currentOverRevIntensity = MathHelper.Lerp(_currentOverRevIntensity, _targetOverRevIntensity, 0.0090f);

            float limiter = _currentLimiterIntensity;
            float overRev = _currentOverRevIntensity;
            float intensity = MathHelper.Clamp(MathF.Max(limiter * 0.75f, overRev), 0f, 1f);
            float overRevMix = overRev / MathF.Max(0.001f, limiter + overRev);

            float chatterHz = MathHelper.Lerp(
                MathHelper.Lerp(24f, 42f, rpmT),
                MathHelper.Lerp(36f, 68f, rpmT),
                overRevMix);
            _chatterPhase = Wrap01(_chatterPhase + chatterHz / SampleRate);

            float cycle = _chatterPhase;
            float firstTap = TapEnvelope(cycle, 0.060f);
            float secondTap = TapEnvelope(Wrap01(cycle - 0.47f), 0.052f) * MathHelper.Lerp(0.52f, 0.78f, overRevMix);
            float valveTap = firstTap + secondTap;

            float ringHz = MathHelper.Lerp(680f, 1250f, rpmT) + overRevMix * 280f;
            _ringPhase += MathF.Tau * ringHz / SampleRate;
            if (_ringPhase > MathF.Tau)
            {
                _ringPhase -= MathF.Tau * MathF.Floor(_ringPhase / MathF.Tau);
            }

            float white = NextNoise();
            float raspRate = MathHelper.Lerp(0.16f, 0.44f, overRev);
            _raspFilter += (white - _raspFilter) * raspRate;
            _metalResonance = _metalResonance * MathHelper.Lerp(0.70f, 0.82f, limiter) +
                              valveTap * (0.22f + overRev * 0.16f);

            float ring = MathF.Sin(_ringPhase);
            float limiterSample = limiter * (valveTap * ring * 0.82f + _metalResonance * ring * 0.46f + _raspFilter * 0.075f);
            float overRevSample = overRev * (valveTap * ring * 1.22f + _metalResonance * ring * 0.66f + _raspFilter * 0.24f + white * 0.065f);
            float sampleFloat = MathHelper.Clamp((limiterSample + overRevSample) * intensity, -1f, 1f);

            short sample = (short)(sampleFloat * short.MaxValue);
            int write = frame * sizeof(short);
            buffer[write] = (byte)(sample & 0xff);
            buffer[write + 1] = (byte)((sample >> 8) & 0xff);
        }

        return buffer;
    }

    private static float TapEnvelope(float phase, float width)
    {
        if (phase >= width)
        {
            return 0f;
        }

        float t = phase / MathF.Max(0.001f, width);
        return (1f - t) * (1f - t);
    }

    private static float Wrap01(float value)
    {
        return value - MathF.Floor(value);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private float NextNoise()
    {
        _noiseState = _noiseState * 1664525u + 1013904223u;
        return ((_noiseState >> 8) / 8388607.5f) - 1f;
    }
}
