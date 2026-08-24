using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace RType.Audio;

internal sealed class TireSkidSound : IDisposable
{
    private const int SampleRate = 22050;
    private const int FramesPerBuffer = 1024;
    private const int TargetPendingBuffers = 4;
    private const int BufferRingSize = 8;

    private readonly DynamicSoundEffectInstance _instance = new(SampleRate, AudioChannels.Mono);
    private readonly byte[][] _buffers;
    private uint _noiseState = 0x6d2b79f5u;
    private float _currentIntensity;
    private float _targetIntensity;
    private float _roughness;
    private float _filteredNoise;
    private int _nextBufferIndex;

    public TireSkidSound()
    {
        _buffers = new byte[BufferRingSize][];
        int bufferBytes = FramesPerBuffer * sizeof(short);
        for (int i = 0; i < _buffers.Length; i++)
        {
            _buffers[i] = new byte[bufferBytes];
        }
    }

    public void Update(float intensity, float roughness)
    {
        _targetIntensity = MathHelper.Clamp(intensity, 0f, 1f);
        _roughness = MathHelper.Clamp(roughness, 0f, 1f);

        if (_targetIntensity <= 0.005f && _currentIntensity <= 0.005f)
        {
            Stop();
            return;
        }

        _instance.Volume = MathHelper.Clamp(0.72f * MathF.Max(_currentIntensity, _targetIntensity), 0f, 0.85f);
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

        _currentIntensity = 0f;
        _targetIntensity = 0f;
        _filteredNoise = 0f;
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
        float filterRate = MathHelper.Lerp(0.04f, 0.32f, _roughness);

        for (int frame = 0; frame < FramesPerBuffer; frame++)
        {
            _currentIntensity = MathHelper.Lerp(_currentIntensity, _targetIntensity, 0.0045f);
            float white = NextNoise();
            _filteredNoise += (white - _filteredNoise) * filterRate;
            float coarseScrape = white * 0.58f + _filteredNoise * 0.42f;
            float shimmer = NextNoise() * MathHelper.Lerp(0.10f, 0.26f, _roughness);
            float sampleFloat = MathHelper.Clamp((coarseScrape + shimmer) * _currentIntensity, -1f, 1f);
            short sample = (short)(sampleFloat * short.MaxValue);
            int write = frame * sizeof(short);
            buffer[write] = (byte)(sample & 0xff);
            buffer[write + 1] = (byte)((sample >> 8) & 0xff);
        }

        return buffer;
    }

    private float NextNoise()
    {
        _noiseState = _noiseState * 1664525u + 1013904223u;
        return ((_noiseState >> 8) / 8388607.5f) - 1f;
    }
}
