using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace RetroRacer.Audio;

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
    private double _sourceFrame;
    private float _playbackRatio;
    private int _nextBufferIndex;
    private bool _hasPlaybackRatio;
    private bool _hasPlayed;
    private double _lastLowBufferLogSeconds = -999.0;
    private double _lastRestartLogSeconds = -999.0;

    public LoopingPitchedSound(WavLoopSource source, string label = "")
    {
        _source = source;
        _loopWindow = LoopWindowPlanner.Plan(source);
        _instance = new DynamicSoundEffectInstance(source.SampleRate, source.Channels);
        _buffers = new byte[BufferRingSize][];
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

    public void Update(float playbackRatio, float volume)
    {
        playbackRatio = MathHelper.Clamp(playbackRatio, 0.05f, 4.0f);
        float clampedVolume = MathHelper.Clamp(volume, 0f, 1f);
        int pendingBefore = _instance.PendingBufferCount;
        SoundState stateBefore = _instance.State;
        LogLowBufferIfNeeded(pendingBefore, stateBefore, playbackRatio, clampedVolume);

        _instance.Volume = clampedVolume;
        if (!_hasPlaybackRatio)
        {
            _playbackRatio = playbackRatio;
            _hasPlaybackRatio = true;
        }

        while (_instance.PendingBufferCount < TargetPendingBuffers)
        {
            _instance.SubmitBuffer(CreateBuffer(playbackRatio));
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
            _instance.Stop();
        }

        _sourceFrame = 0.0;
        _hasPlaybackRatio = false;
        _hasPlayed = false;
    }

    public void Dispose()
    {
        Stop();
        _instance.Dispose();
    }

    private byte[] CreateBuffer(float playbackRatio)
    {
        int channelCount = _source.ChannelCount;
        byte[] buffer = _buffers[_nextBufferIndex];
        _nextBufferIndex = (_nextBufferIndex + 1) % _buffers.Length;
        int write = 0;
        float startPlaybackRatio = _playbackRatio;

        for (int frame = 0; frame < FramesPerBuffer; frame++)
        {
            float rampT = (frame + 1f) / FramesPerBuffer;
            float framePlaybackRatio = MathHelper.Lerp(startPlaybackRatio, playbackRatio, rampT);
            for (int channel = 0; channel < channelCount; channel++)
            {
                short sample = (short)(MathHelper.Clamp(ReadLoopedSample(_sourceFrame, channel), -1f, 1f) * short.MaxValue);
                buffer[write++] = (byte)(sample & 0xff);
                buffer[write++] = (byte)((sample >> 8) & 0xff);
            }

            _sourceFrame += framePlaybackRatio;
            int wrapFrameCount = Math.Max(1, _loopWindow.EndFrame - _loopWindow.CrossfadeFrames);
            while (_sourceFrame >= _loopWindow.EndFrame)
            {
                _sourceFrame -= wrapFrameCount;
            }
        }

        _playbackRatio = playbackRatio;
        return buffer;
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
