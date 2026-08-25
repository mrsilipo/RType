using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace RType.Audio;

internal sealed class LoopingPitchedSound : IDisposable
{
    private const int FramesPerBuffer = 1024;
    private const int TargetPendingBuffers = 6;
    private const int BufferRingSize = 10;

    private readonly WavLoopSource _source;
    private readonly LoopWindow _loopWindow;
    private readonly DynamicSoundEffectInstance _instance;
    private readonly byte[][] _buffers;
    private readonly string _label;
    private readonly float[] _toneLowPass;
    private readonly float[] _idleLowPass;
    private readonly float[][] _echoDelayLines;
    private readonly int[] _echoWriteIndices;
    private uint _noiseState = 0x12345678;
    private double _sourceFrame;
    private long _outputFrame;
    private float _subBassPhase;
    private float _playbackRatio;
    private float _resonance;
    private float _saturation;
    private float _noiseBurst;
    private float _idleAmount;
    private int _nextBufferIndex;
    private bool _hasPlaybackRatio;
    private bool _hasPlayed;
    private double _lastLowBufferLogSeconds = -999.0;
    private double _lastRestartLogSeconds = -999.0;

    public LoopingPitchedSound(WavLoopSource source, string label = "", bool preserveFullLoop = false)
    {
        _source = source;
        _loopWindow = preserveFullLoop
            ? new LoopWindow(source.FrameCount, 0, 0f)
            : LoopWindowPlanner.Plan(source);
        _instance = new DynamicSoundEffectInstance(source.SampleRate, source.Channels);
        _buffers = new byte[BufferRingSize][];
        _toneLowPass = new float[source.ChannelCount];
        _idleLowPass = new float[source.ChannelCount];
        _echoDelayLines = new float[source.ChannelCount][];
        _echoWriteIndices = new int[source.ChannelCount];
        int echoDelayFrames = Math.Max(1, (int)MathF.Round(source.SampleRate * 0.037f));
        for (int channel = 0; channel < source.ChannelCount; channel++)
        {
            _echoDelayLines[channel] = new float[echoDelayFrames];
        }

        _label = string.IsNullOrWhiteSpace(label) ? "unlabelled loop" : label;
        int bufferBytes = FramesPerBuffer * source.ChannelCount * sizeof(short);
        for (int i = 0; i < _buffers.Length; i++)
        {
            _buffers[i] = new byte[bufferBytes];
        }

        AudioDiagnostics.Log(
            "loop-load",
            $"{_label}: {source.SampleRate} Hz, {source.ChannelCount} ch, {source.FrameCount} frames, loopEnd {_loopWindow.EndFrame}, crossfade {_loopWindow.CrossfadeFrames}, match {_loopWindow.MatchError:0.00000}");
    }

    public void Update(
        float playbackRatio,
        float volume,
        float resonance = 0f,
        float saturation = 0f,
        float noiseBurst = 0f,
        float hardCut = 0f,
        float idleAmount = 0f,
        float idlePulseHz = 30f,
        float hardCutFrequencyHz = 15f,
        float hardCutOffDuty = 0.48f)
    {
        playbackRatio = MathHelper.Clamp(playbackRatio, 0.05f, 4.0f);
        resonance = MathHelper.Clamp(resonance, 0f, 0.85f);
        saturation = MathHelper.Clamp(saturation, 0f, 1f);
        noiseBurst = MathHelper.Clamp(noiseBurst, 0f, 1f);
        hardCut = MathHelper.Clamp(hardCut, 0f, 1f);
        idleAmount = MathHelper.Clamp(idleAmount, 0f, 1f);
        idlePulseHz = MathHelper.Clamp(idlePulseHz, 12f, 60f);
        float clampedVolume = MathHelper.Clamp(volume, 0f, 1f);
        int pendingBefore = _instance.PendingBufferCount;
        SoundState stateBefore = _instance.State;
        LogLowBufferIfNeeded(pendingBefore, stateBefore, playbackRatio, clampedVolume);

        _instance.Volume = clampedVolume;
        if (!_hasPlaybackRatio)
        {
            _playbackRatio = playbackRatio;
            _resonance = resonance;
            _saturation = saturation;
            _noiseBurst = noiseBurst;
            _idleAmount = idleAmount;
            _hasPlaybackRatio = true;
        }

        while (_instance.PendingBufferCount < TargetPendingBuffers)
        {
            _instance.SubmitBuffer(CreateBuffer(playbackRatio, resonance, saturation, noiseBurst, hardCut, idleAmount, idlePulseHz, hardCutFrequencyHz, hardCutOffDuty));
        }

        if (_instance.State != SoundState.Playing)
        {
            LogRestartIfNeeded(stateBefore, pendingBefore, _instance.PendingBufferCount, playbackRatio, clampedVolume);
            _instance.Play();
            _hasPlayed = true;
        }
    }

    public void Stop()
    {
        if (_instance.State != SoundState.Stopped)
        {
            _instance.Stop(true);
        }

        _sourceFrame = 0.0;
        _outputFrame = 0;
        _subBassPhase = 0f;
        Array.Clear(_toneLowPass);
        Array.Clear(_idleLowPass);
        for (int channel = 0; channel < _echoDelayLines.Length; channel++)
        {
            Array.Clear(_echoDelayLines[channel]);
            _echoWriteIndices[channel] = 0;
        }

        _hasPlaybackRatio = false;
        _hasPlayed = false;
    }

    public void Dispose()
    {
        Stop();
        _instance.Dispose();
    }

    private byte[] CreateBuffer(
        float playbackRatio,
        float resonance,
        float saturation,
        float noiseBurst,
        float hardCut,
        float idleAmount,
        float idlePulseHz,
        float hardCutFrequencyHz,
        float hardCutOffDuty)
    {
        int channelCount = _source.ChannelCount;
        byte[] buffer = _buffers[_nextBufferIndex];
        _nextBufferIndex = (_nextBufferIndex + 1) % _buffers.Length;
        int write = 0;
        float startPlaybackRatio = _playbackRatio;
        float startResonance = _resonance;
        float startSaturation = _saturation;
        float startNoiseBurst = _noiseBurst;
        float startIdleAmount = _idleAmount;

        for (int frame = 0; frame < FramesPerBuffer; frame++)
        {
            float rampT = (frame + 1f) / FramesPerBuffer;
            float framePlaybackRatio = MathHelper.Lerp(startPlaybackRatio, playbackRatio, rampT);
            float frameResonance = MathHelper.Lerp(startResonance, resonance, rampT);
            float frameSaturation = MathHelper.Lerp(startSaturation, saturation, rampT);
            float frameNoiseBurst = MathHelper.Lerp(startNoiseBurst, noiseBurst, rampT);
            float frameIdleAmount = MathHelper.Lerp(startIdleAmount, idleAmount, rampT);
            float limiterGate = hardCut > 0f ? CalculateHardCutGate(hardCut, hardCutFrequencyHz, hardCutOffDuty) : 1f;
            float outputSeconds = _outputFrame / (float)_source.SampleRate;
            float pulse = 1f - frameIdleAmount * 0.08f * (0.5f + 0.5f * MathF.Sin(outputSeconds * idlePulseHz * MathHelper.TwoPi));
            float subBass = MathF.Sin(_subBassPhase) * frameIdleAmount * 0.004f;
            bool directPcm =
                MathF.Abs(framePlaybackRatio - 1f) <= 0.000001f &&
                frameResonance <= 0.0001f &&
                frameNoiseBurst <= 0.0001f &&
                frameSaturation <= 0.0001f &&
                frameIdleAmount <= 0.0001f &&
                hardCut <= 0.0001f;
            int directFrame = directPcm
                ? Math.Clamp((int)Math.Round(_sourceFrame), 0, _source.FrameCount - 1)
                : 0;
            for (int channel = 0; channel < channelCount; channel++)
            {
                if (directPcm)
                {
                    short directSample = _source.PcmSamples[directFrame * channelCount + channel];
                    buffer[write++] = (byte)(directSample & 0xff);
                    buffer[write++] = (byte)((directSample >> 8) & 0xff);
                    continue;
                }

                float rawSample = ReadLoopedSample(_sourceFrame, channel);
                float processedSample = rawSample;
                if (frameResonance > 0.0001f || frameNoiseBurst > 0.0001f || frameSaturation > 0.0001f)
                {
                    _toneLowPass[channel] = MathHelper.Lerp(_toneLowPass[channel], rawSample, 0.16f);
                    float highBand = rawSample - _toneLowPass[channel];
                    float echo = ReadWriteEcho(channel, rawSample, frameResonance);
                    processedSample = rawSample + highBand * frameResonance * 1.25f + echo * frameResonance * 0.16f;
                    processedSample += NextNoiseSample() * frameNoiseBurst * 0.34f;
                    processedSample = SaturateAsymmetric(processedSample, frameSaturation);
                }

                if (frameIdleAmount > 0.0001f)
                {
                    _idleLowPass[channel] = MathHelper.Lerp(_idleLowPass[channel], processedSample, 0.115f);
                    float idleSample = _idleLowPass[channel] * pulse * 1.08f + subBass;
                    processedSample = MathHelper.Lerp(processedSample, idleSample, frameIdleAmount * 0.34f);
                }

                if (frameNoiseBurst > 0.0001f || frameSaturation > 0.0001f || frameIdleAmount > 0.0001f)
                {
                    processedSample = SoftClip(processedSample);
                }

                processedSample *= limiterGate;
                short sample = (short)(MathHelper.Clamp(processedSample, -1f, 1f) * short.MaxValue);
                buffer[write++] = (byte)(sample & 0xff);
                buffer[write++] = (byte)((sample >> 8) & 0xff);
            }

            _sourceFrame += framePlaybackRatio;
            _outputFrame++;
            _subBassPhase += MathHelper.TwoPi * idlePulseHz / _source.SampleRate;
            if (_subBassPhase >= MathHelper.TwoPi)
            {
                _subBassPhase -= MathHelper.TwoPi;
            }

            int wrapFrameCount = Math.Max(1, _loopWindow.EndFrame - _loopWindow.CrossfadeFrames);
            while (_sourceFrame >= _loopWindow.EndFrame)
            {
                _sourceFrame -= wrapFrameCount;
            }
        }

        _playbackRatio = playbackRatio;
        _resonance = resonance;
        _saturation = saturation;
        _noiseBurst = noiseBurst;
        _idleAmount = idleAmount;
        return buffer;
    }

    private float CalculateHardCutGate(float hardCut, float frequencyHz, float offDuty)
    {
        float cutFrequencyHz = MathHelper.Clamp(frequencyHz, 1f, 30f);
        float cycle = (float)(_outputFrame / (double)_source.SampleRate * cutFrequencyHz);
        cycle -= MathF.Floor(cycle);
        float cutDuty = MathHelper.Clamp(offDuty, 0.02f, 0.92f) * hardCut;
        if (cutDuty <= 0f)
        {
            return 1f;
        }

        float fade = MathF.Min(0.08f, MathF.Min(cutDuty * 0.45f, (1f - cutDuty) * 0.45f));
        float envelope;
        if (cycle < fade)
        {
            envelope = SmoothStep(0f, fade, cycle) * 0.08f;
        }
        else if (cycle < cutDuty - fade)
        {
            envelope = 0.08f;
        }
        else if (cycle < cutDuty + fade)
        {
            envelope = MathHelper.Lerp(0.08f, 1f, SmoothStep(cutDuty - fade, cutDuty + fade, cycle));
        }
        else
        {
            envelope = 1f;
        }

        return MathHelper.Lerp(1f, envelope, hardCut);
    }

    private float NextNoiseSample()
    {
        _noiseState = _noiseState * 1664525u + 1013904223u;
        return ((_noiseState >> 8) / 8388607.5f) - 1f;
    }

    private static float SaturateAsymmetric(float value, float drive)
    {
        if (drive <= 0.0001f)
        {
            return value;
        }

        float gain = 1f + drive * 3.6f;
        float biased = value * gain + drive * 0.08f;
        float saturated = biased >= 0f
            ? MathF.Tanh(biased * 1.18f)
            : MathF.Tanh(biased * 0.82f);
        return MathHelper.Lerp(value, saturated, drive * 0.72f);
    }

    private float ReadWriteEcho(int channel, float sample, float resonance)
    {
        float[] delayLine = _echoDelayLines[channel];
        int index = _echoWriteIndices[channel];
        float delayed = delayLine[index];
        delayLine[index] = sample + delayed * resonance * 0.22f;
        _echoWriteIndices[channel] = (index + 1) % delayLine.Length;
        return delayed;
    }

    private static float SoftClip(float value)
    {
        return value / (1f + MathF.Abs(value) * 0.18f);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private float ReadLoopedSample(double sourceFrame, int channel)
    {
        int crossfadeFrames = _loopWindow.CrossfadeFrames;
        int crossfadeStart = _loopWindow.EndFrame - crossfadeFrames;
        float primary = ReadInterpolatedSample(sourceFrame, channel);
        if (crossfadeFrames <= 0 || sourceFrame < crossfadeStart)
        {
            return primary;
        }

        double wrappedFrame = sourceFrame - crossfadeStart;
        float fadeT = MathHelper.Clamp((float)wrappedFrame / Math.Max(1, crossfadeFrames), 0f, 1f);
        fadeT = fadeT * fadeT * (3f - 2f * fadeT);
        float wrapped = ReadInterpolatedSample(wrappedFrame, channel);
        return MathHelper.Lerp(primary, wrapped, fadeT);
    }

    private float ReadInterpolatedSample(double sourceFrame, int channel)
    {
        int channelCount = _source.ChannelCount;
        int frame0 = Math.Clamp((int)sourceFrame, 0, _source.FrameCount - 1);
        int frame1 = Math.Min(frame0 + 1, _source.FrameCount - 1);
        float t = (float)(sourceFrame - frame0);
        float a = _source.Samples[frame0 * channelCount + channel];
        float b = _source.Samples[frame1 * channelCount + channel];
        return MathHelper.Lerp(a, b, t);
    }

    private void LogLowBufferIfNeeded(int pendingBefore, SoundState stateBefore, float playbackRatio, float volume)
    {
        if (!_hasPlayed && stateBefore == SoundState.Stopped)
        {
            return;
        }

        if (pendingBefore > 1 || volume <= 0.004f)
        {
            return;
        }

        double now = AudioDiagnostics.NowSeconds;
        if (now - _lastLowBufferLogSeconds < 0.75)
        {
            return;
        }

        _lastLowBufferLogSeconds = now;
        AudioDiagnostics.Log(
            "loop-buffer-low",
            $"{_label}: pending {pendingBefore}, state {stateBefore}, ratio {playbackRatio:0.000}, volume {volume:0.000}, sourceFrame {_sourceFrame:0.0}");
    }

    private void LogRestartIfNeeded(SoundState stateBefore, int pendingBefore, int pendingAfter, float playbackRatio, float volume)
    {
        if (!_hasPlayed || volume <= 0.004f)
        {
            return;
        }

        double now = AudioDiagnostics.NowSeconds;
        if (now - _lastRestartLogSeconds < 0.75)
        {
            return;
        }

        _lastRestartLogSeconds = now;
        AudioDiagnostics.Log(
            "loop-restart",
            $"{_label}: state {stateBefore}->Playing, pending {pendingBefore}->{pendingAfter}, ratio {playbackRatio:0.000}, volume {volume:0.000}, sourceFrame {_sourceFrame:0.0}");
    }
}
