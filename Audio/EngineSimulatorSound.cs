using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using RetroRacer.Camera;
using RetroRacer.Vehicle;

namespace RetroRacer.Audio;

internal sealed class EngineSimulatorSound : IDisposable
{
    private const int SampleRate = 44100;
    private const int FramesPerBuffer = 512;
    private const int TargetPendingBuffers = 2;
    private const int MinimumStartupBuffers = 2;
    private const int ReadyBufferCapacity = 3;
    private const int BufferPoolSize = TargetPendingBuffers + ReadyBufferCapacity + 6;
    private const int BufferBytes = FramesPerBuffer * sizeof(short);
    private const int DeClickFrames = 96;
    private const float DeClickThreshold = 0.18f;
    private const int RealtimeFluidSimulationStepLimit = 1;
    private const int EmergencyGenerationPendingThreshold = 1;
    private const int EmergencyRecoveryPendingBuffers = 2;
    private const int MaximumEmergencyBuffersPerUpdate = 2;
    private const double StreamHealthLogIntervalSeconds = 3.0;
    private const double StreamRecoveryLogIntervalSeconds = 1.0;

    private readonly DynamicSoundEffectInstance _instance = new(SampleRate, AudioChannels.Mono);
    private readonly VehicleAudioParameters _parameters;
    private readonly EngineSimulatorSampleSynth _synth;
    private readonly ConcurrentQueue<GeneratedBuffer> _readyBuffers = new();
    private readonly ConcurrentQueue<byte[]> _freeBuffers = new();
    private readonly Queue<SubmittedBuffer> _submittedBuffers = new();
    private readonly AutoResetEvent _workerSignal = new(false);
    private readonly Thread _workerThread;
    private readonly object _synthLock = new();
    private readonly object _targetLock = new();
    private readonly object _submitLock = new();
    private EngineSimulatorSynthesisTarget _target = new(900f, 0f, 0f, 0f, 0f, 0f, 0f);
    private long _targetUpdatedTicks = Stopwatch.GetTimestamp();
    private long _lastTargetUpdateTicks;
    private int _readyBufferCount;
    private int _generation;
    private int _resetRequested = 1;
    private volatile bool _active;
    private volatile bool _workerRunning = true;
    private bool _hasPlayed;
    private float _lastOutputSample;
    private int _fadeInFramesRemaining = DeClickFrames;
    private float _lastTargetRpm = 900f;
    private double _lastLowBufferLogSeconds = -999.0;
    private double _lastRecoveryLogSeconds = -999.0;
    private double _lastStreamHealthLogSeconds;
    private long _lastStreamHealthGeneratedBuffers;
    private long _lastStreamHealthEmergencyGeneratedBuffers;
    private long _generatedBufferCount;
    private long _emergencyGeneratedBufferCount;
    private long _maximumBufferFillTicks;
    private long _maximumEmergencyBufferFillTicks;
    private long _maximumTargetAgeAtFillTicks;
    private long _maximumTargetAgeAtSubmitTicks;
    private long _maximumReadyAgeAtSubmitTicks;
    private long _maximumEstimatedAudibleAgeTicks;
    private long _maximumTargetUpdateGapTicks;
    private int _minimumPendingBufferCount = int.MaxValue;
    private int _minimumReadyBufferCount = int.MaxValue;
    private EngineSimulatorSynthesisTarget _lastLoggedTarget = new(900f, 0f, 0f, 0f, 0f, 0f, 0f);

    public EngineSimulatorSound(VehicleAudioParameters parameters)
    {
        _parameters = parameters;
        int realtimeFluidSteps = Math.Clamp(
            Math.Min(parameters.EngineSimulatorFluidSimulationSteps, RealtimeFluidSimulationStepLimit),
            1,
            16);
        if (parameters.EngineSimulatorFluidSimulationSteps != realtimeFluidSteps)
        {
            AudioDiagnostics.Log(
                "engine-sim-realtime-cap",
                $"fluid steps {parameters.EngineSimulatorFluidSimulationSteps} -> {realtimeFluidSteps} for live audio stream");
        }

        _synth = new EngineSimulatorSampleSynth(parameters, SampleRate, fluidSimulationStepsOverride: realtimeFluidSteps);
        WarmSynth();
        _instance.BufferNeeded += HandleBufferNeeded;
        for (int i = 0; i < BufferPoolSize; i++)
        {
            _freeBuffers.Enqueue(new byte[BufferBytes]);
        }

        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Engine Simulator Audio",
            Priority = ThreadPriority.Highest
        };
        _workerThread.Start();
        _lastStreamHealthLogSeconds = AudioDiagnostics.NowSeconds;
        AudioDiagnostics.Log(
            "engine-sim-stream",
            $"buffer {FramesPerBuffer} frames ({BufferDurationMilliseconds():0.0} ms), target pending {TargetPendingBuffers} ({TargetLatencyMilliseconds():0.0} ms), startup {MinimumStartupBuffers}, ready cap {ReadyBufferCapacity}");
    }

    public void Update(EngineAudioFrame frame)
    {
        float configuredVolume = MathHelper.Clamp(_parameters.EngineSimulatorVolume, 0f, 1f);
        if (configuredVolume <= 0.001f || !frame.Audible)
        {
            Stop();
            return;
        }

        EngineSimulatorSynthesisTarget target = frame.ToSynthesisTarget();
        long targetUpdatedTicks = Stopwatch.GetTimestamp();
        lock (_targetLock)
        {
            _target = target;
            _targetUpdatedTicks = targetUpdatedTicks;
        }

        long previousTargetUpdatedTicks = Interlocked.Exchange(ref _lastTargetUpdateTicks, targetUpdatedTicks);
        if (previousTargetUpdatedTicks > 0)
        {
            UpdateMaximum(ref _maximumTargetUpdateGapTicks, targetUpdatedTicks - previousTargetUpdatedTicks);
        }

        _lastTargetRpm = target.Rpm;
        _lastLoggedTarget = target;
        _active = true;
        _workerSignal.Set();

        float rpmT = SmoothStep(900f, MathF.Max(1200f, frame.RedlineRpm), frame.Rpm);
        float viewGain = frame.CameraMode == CameraMode.InCar ? 0.74f : 0.56f;
        float limiterGain = MathHelper.Lerp(1f, 1.14f, frame.Limiter);
        float rpmGain = MathHelper.Lerp(0.76f, 1.0f, rpmT);
        float overrunLoudness = MathHelper.Lerp(1f, 1.16f, SmoothStep(0.15f, 0.95f, frame.Overrun));
        _instance.Volume = MathHelper.Clamp(configuredVolume * frame.DriveVolume * frame.PauseScale * viewGain * limiterGain * rpmGain * overrunLoudness, 0f, 0.55f);

        SubmitReadyBuffers();
        SubmitEmergencyBuffersIfNeeded();
        TrackBufferDepth();
        LogLowBufferIfNeeded();
        LogStreamHealthIfNeeded();

        if (_instance.State != SoundState.Playing && _instance.PendingBufferCount >= MinimumStartupBuffers)
        {
            _instance.Play();
            _hasPlayed = true;
            TrackBufferDepth();
            LogStreamReady();
        }
    }

    public void Stop()
    {
        _active = false;
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _resetRequested, 1);
        Interlocked.Exchange(ref _lastTargetUpdateTicks, 0);
        DrainReadyBuffers();
        _workerSignal.Set();

        lock (_submitLock)
        {
            if (_instance.State != SoundState.Stopped)
            {
                _instance.Stop();
            }

            RecycleSubmittedBuffers(force: true);
        }

        _hasPlayed = false;
    }

    public void Dispose()
    {
        Stop();
        _workerRunning = false;
        _workerSignal.Set();
        if (!_workerThread.Join(1000))
        {
            AudioDiagnostics.Log("engine-sim-worker", "worker did not stop within 1000 ms");
        }

        _workerSignal.Dispose();
        _instance.BufferNeeded -= HandleBufferNeeded;
        _instance.Dispose();
    }

    private void SubmitReadyBuffers()
    {
        lock (_submitLock)
        {
            int currentGeneration = Volatile.Read(ref _generation);
            long newestTargetUpdatedTicks = Volatile.Read(ref _targetUpdatedTicks);
            while (_instance.PendingBufferCount < TargetPendingBuffers &&
                   _readyBuffers.TryDequeue(out GeneratedBuffer generated))
            {
                Interlocked.Decrement(ref _readyBufferCount);
                if (generated.Generation == currentGeneration)
                {
                    if (generated.TargetUpdatedTicks < newestTargetUpdatedTicks &&
                        Volatile.Read(ref _readyBufferCount) > 0)
                    {
                        _freeBuffers.Enqueue(generated.Buffer);
                        continue;
                    }

                    long submitTicks = Stopwatch.GetTimestamp();
                    int pendingBeforeSubmit = _instance.PendingBufferCount;
                    UpdateMaximum(ref _maximumReadyAgeAtSubmitTicks, submitTicks - generated.GeneratedTicks);
                    UpdateMaximum(ref _maximumTargetAgeAtSubmitTicks, submitTicks - generated.TargetUpdatedTicks);
                    UpdateMaximum(
                        ref _maximumEstimatedAudibleAgeTicks,
                        submitTicks - generated.TargetUpdatedTicks + pendingBeforeSubmit * BufferDurationTicks());

                    _instance.SubmitBuffer(generated.Buffer);
                    _submittedBuffers.Enqueue(new SubmittedBuffer(
                        generated.Buffer,
                        submitTicks,
                        generated.GeneratedTicks,
                        generated.TargetUpdatedTicks));
                    RecycleSubmittedBuffers(force: false);
                    continue;
                }

                _freeBuffers.Enqueue(generated.Buffer);
            }
        }

        _workerSignal.Set();
    }

    private void HandleBufferNeeded(object? sender, EventArgs e)
    {
        if (!_active)
        {
            return;
        }

        SubmitReadyBuffers();
        TrackBufferDepth();
        LogLowBufferIfNeeded();
    }

    private void DrainReadyBuffers()
    {
        while (_readyBuffers.TryDequeue(out GeneratedBuffer generated))
        {
            Interlocked.Decrement(ref _readyBufferCount);
            _freeBuffers.Enqueue(generated.Buffer);
        }
    }

    private void RecycleSubmittedBuffers(bool force)
    {
        while (_submittedBuffers.Count > 0 &&
               (force || _submittedBuffers.Count > BufferPoolSize))
        {
            _freeBuffers.Enqueue(_submittedBuffers.Dequeue().Buffer);
        }
    }

    private void WorkerLoop()
    {
        while (_workerRunning)
        {
            if (Interlocked.Exchange(ref _resetRequested, 0) != 0)
            {
                lock (_synthLock)
                {
                    _synth.Reset();
                    _lastOutputSample = 0f;
                    _fadeInFramesRemaining = DeClickFrames;
                }
            }

            if (!_active)
            {
                _workerSignal.WaitOne(25);
                continue;
            }

            if (Volatile.Read(ref _readyBufferCount) >= ReadyBufferCapacity)
            {
                _workerSignal.WaitOne(2);
                continue;
            }

            if (!_freeBuffers.TryDequeue(out byte[]? buffer))
            {
                buffer = new byte[BufferBytes];
            }

            int generation = Volatile.Read(ref _generation);
            TargetSnapshot target = ReadTargetSnapshot();

            long fillStart = Stopwatch.GetTimestamp();
            UpdateMaximum(ref _maximumTargetAgeAtFillTicks, fillStart - target.UpdatedTicks);
            long newestTargetUpdatedTicks;
            lock (_synthLock)
            {
                newestTargetUpdatedTicks = FillBuffer(buffer, target);
            }
            long generatedTicks = Stopwatch.GetTimestamp();
            long fillTicks = generatedTicks - fillStart;
            Interlocked.Increment(ref _generatedBufferCount);
            UpdateMaximum(ref _maximumBufferFillTicks, fillTicks);

            if (_active && generation == Volatile.Read(ref _generation))
            {
                _readyBuffers.Enqueue(new GeneratedBuffer(buffer, generation, generatedTicks, newestTargetUpdatedTicks));
                Interlocked.Increment(ref _readyBufferCount);
            }
            else
            {
                _freeBuffers.Enqueue(buffer);
            }
        }
    }

    private void WarmSynth()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        _synth.SetTarget(new EngineSimulatorSynthesisTarget(1200f, 0.08f, 0.18f, 0f, 0f, 0f, 0f));
        for (int i = 0; i < FramesPerBuffer; i++)
        {
            _ = _synth.NextSample();
        }

        _synth.Reset();
        _lastOutputSample = 0f;
        _fadeInFramesRemaining = DeClickFrames;
        stopwatch.Stop();
        AudioDiagnostics.Log("engine-sim-synth-warmup", $"{FramesPerBuffer} frames in {stopwatch.Elapsed.TotalMilliseconds:0.00} ms");
    }

    private void SubmitEmergencyBuffersIfNeeded()
    {
        lock (_submitLock)
        {
            if (!_active ||
                !_hasPlayed ||
                _instance.PendingBufferCount > EmergencyGenerationPendingThreshold ||
                Volatile.Read(ref _readyBufferCount) > 0)
            {
                return;
            }

            int currentGeneration = Volatile.Read(ref _generation);
            int generatedCount = 0;
            long maxFillTicks = 0;
            while (_active &&
                   _instance.PendingBufferCount < EmergencyRecoveryPendingBuffers &&
                   generatedCount < MaximumEmergencyBuffersPerUpdate)
            {
                if (!_freeBuffers.TryDequeue(out byte[]? buffer))
                {
                    buffer = new byte[BufferBytes];
                }

                TargetSnapshot target = ReadTargetSnapshot();

                long fillStart = Stopwatch.GetTimestamp();
                UpdateMaximum(ref _maximumTargetAgeAtFillTicks, fillStart - target.UpdatedTicks);
                long newestTargetUpdatedTicks;
                lock (_synthLock)
                {
                    newestTargetUpdatedTicks = FillBuffer(buffer, target);
                }

                long generatedTicks = Stopwatch.GetTimestamp();
                long fillTicks = generatedTicks - fillStart;
                maxFillTicks = Math.Max(maxFillTicks, fillTicks);
                Interlocked.Increment(ref _generatedBufferCount);
                Interlocked.Increment(ref _emergencyGeneratedBufferCount);
                UpdateMaximum(ref _maximumBufferFillTicks, fillTicks);
                UpdateMaximum(ref _maximumEmergencyBufferFillTicks, fillTicks);

                if (!_active || currentGeneration != Volatile.Read(ref _generation))
                {
                    _freeBuffers.Enqueue(buffer);
                    break;
                }

                long submitTicks = Stopwatch.GetTimestamp();
                int pendingBeforeSubmit = _instance.PendingBufferCount;
                UpdateMaximum(ref _maximumReadyAgeAtSubmitTicks, submitTicks - generatedTicks);
                UpdateMaximum(ref _maximumTargetAgeAtSubmitTicks, submitTicks - newestTargetUpdatedTicks);
                UpdateMaximum(
                    ref _maximumEstimatedAudibleAgeTicks,
                    submitTicks - newestTargetUpdatedTicks + pendingBeforeSubmit * BufferDurationTicks());

                _instance.SubmitBuffer(buffer);
                _submittedBuffers.Enqueue(new SubmittedBuffer(
                    buffer,
                    submitTicks,
                    generatedTicks,
                    newestTargetUpdatedTicks));
                generatedCount++;
            }

            if (generatedCount > 0)
            {
                RecycleSubmittedBuffers(force: false);
                LogStreamRecoveryIfNeeded(generatedCount, maxFillTicks);
            }
        }

        _workerSignal.Set();
    }

    private TargetSnapshot ReadTargetSnapshot()
    {
        lock (_targetLock)
        {
            return new TargetSnapshot(_target, _targetUpdatedTicks);
        }
    }

    private long FillBuffer(byte[] buffer, TargetSnapshot initialTarget)
    {
        EngineSimulatorSynthesisTarget target = initialTarget.Target;
        long newestTargetUpdatedTicks = initialTarget.UpdatedTicks;
        _synth.SetTarget(target);
        float startCorrection = 0f;

        for (int frame = 0; frame < FramesPerBuffer; frame++)
        {
            if ((frame & 63) == 0)
            {
                TargetSnapshot targetSnapshot = ReadTargetSnapshot();
                target = targetSnapshot.Target;
                newestTargetUpdatedTicks = Math.Max(newestTargetUpdatedTicks, targetSnapshot.UpdatedTicks);

                _synth.SetTarget(target);
            }

            float sample = _synth.NextSample();
            if (frame == 0)
            {
                float discontinuity = _lastOutputSample - sample;
                startCorrection = MathF.Abs(discontinuity) >= DeClickThreshold ? discontinuity : 0f;
            }

            if (startCorrection != 0f && frame < DeClickFrames)
            {
                float fade = 1f - (frame / (float)DeClickFrames);
                sample += startCorrection * fade * fade;
            }

            if (_fadeInFramesRemaining > 0)
            {
                float fade = 1f - (_fadeInFramesRemaining / (float)DeClickFrames);
                sample *= fade * fade * (3f - 2f * fade);
                _fadeInFramesRemaining--;
            }

            _lastOutputSample = sample;
            short output = (short)(MathHelper.Clamp(sample, -1f, 1f) * short.MaxValue);
            int write = frame * sizeof(short);
            buffer[write] = (byte)(output & 0xff);
            buffer[write + 1] = (byte)((output >> 8) & 0xff);
        }

        return newestTargetUpdatedTicks;
    }

    private void LogLowBufferIfNeeded()
    {
        if (!_hasPlayed ||
            _instance.PendingBufferCount > 1 ||
            _instance.Volume <= 0.004f)
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
            "engine-sim-buffer-low",
            $"pending {_instance.PendingBufferCount}, ready {Volatile.Read(ref _readyBufferCount)}, state {_instance.State}, rpm {_lastTargetRpm:0}, volume {_instance.Volume:0.000}");
    }

    private void LogStreamReady()
    {
        _lastStreamHealthLogSeconds = AudioDiagnostics.NowSeconds;
        _lastStreamHealthGeneratedBuffers = Interlocked.Read(ref _generatedBufferCount);
        _lastStreamHealthEmergencyGeneratedBuffers = Interlocked.Read(ref _emergencyGeneratedBufferCount);
        AudioDiagnostics.Log(
            "engine-sim-stream-ready",
            $"pending {_instance.PendingBufferCount}, ready {Volatile.Read(ref _readyBufferCount)}, generated {_lastStreamHealthGeneratedBuffers}, max fill {ReadMaximumBufferFillMilliseconds():0.00} ms, target age fill {ReadMaximumTargetAgeAtFillMilliseconds():0.00} ms");
    }

    private void TrackBufferDepth()
    {
        if (!_hasPlayed)
        {
            return;
        }

        UpdateMinimum(ref _minimumPendingBufferCount, _instance.PendingBufferCount);
        UpdateMinimum(ref _minimumReadyBufferCount, Volatile.Read(ref _readyBufferCount));
    }

    private void LogStreamHealthIfNeeded()
    {
        if (!_hasPlayed || _instance.Volume <= 0.004f)
        {
            return;
        }

        double now = AudioDiagnostics.NowSeconds;
        double elapsedSeconds = now - _lastStreamHealthLogSeconds;
        if (elapsedSeconds < StreamHealthLogIntervalSeconds)
        {
            return;
        }

        long generatedBuffers = Interlocked.Read(ref _generatedBufferCount);
        long generatedDelta = generatedBuffers - _lastStreamHealthGeneratedBuffers;
        long emergencyGeneratedBuffers = Interlocked.Read(ref _emergencyGeneratedBufferCount);
        long emergencyGeneratedDelta = emergencyGeneratedBuffers - _lastStreamHealthEmergencyGeneratedBuffers;
        long maxFillTicks = Interlocked.Exchange(ref _maximumBufferFillTicks, 0);
        long maxEmergencyFillTicks = Interlocked.Exchange(ref _maximumEmergencyBufferFillTicks, 0);
        long maxTargetAgeAtFillTicks = Interlocked.Exchange(ref _maximumTargetAgeAtFillTicks, 0);
        long maxTargetAgeAtSubmitTicks = Interlocked.Exchange(ref _maximumTargetAgeAtSubmitTicks, 0);
        long maxReadyAgeAtSubmitTicks = Interlocked.Exchange(ref _maximumReadyAgeAtSubmitTicks, 0);
        long maxEstimatedAudibleAgeTicks = Interlocked.Exchange(ref _maximumEstimatedAudibleAgeTicks, 0);
        long maxTargetUpdateGapTicks = Interlocked.Exchange(ref _maximumTargetUpdateGapTicks, 0);
        int minimumPending = Interlocked.Exchange(ref _minimumPendingBufferCount, int.MaxValue);
        int minimumReady = Interlocked.Exchange(ref _minimumReadyBufferCount, int.MaxValue);
        int currentPending = _instance.PendingBufferCount;
        int currentReady = Volatile.Read(ref _readyBufferCount);

        _lastStreamHealthLogSeconds = now;
        _lastStreamHealthGeneratedBuffers = generatedBuffers;
        _lastStreamHealthEmergencyGeneratedBuffers = emergencyGeneratedBuffers;
        if (minimumPending == int.MaxValue)
        {
            minimumPending = currentPending;
        }

        if (minimumReady == int.MaxValue)
        {
            minimumReady = currentReady;
        }

        double maxFillMilliseconds = maxFillTicks * 1000.0 / Stopwatch.Frequency;
        double maxEmergencyFillMilliseconds = maxEmergencyFillTicks * 1000.0 / Stopwatch.Frequency;
        double maxTargetAgeAtFillMilliseconds = TicksToMilliseconds(maxTargetAgeAtFillTicks);
        double maxTargetAgeAtSubmitMilliseconds = TicksToMilliseconds(maxTargetAgeAtSubmitTicks);
        double maxReadyAgeAtSubmitMilliseconds = TicksToMilliseconds(maxReadyAgeAtSubmitTicks);
        double maxEstimatedAudibleAgeMilliseconds = TicksToMilliseconds(maxEstimatedAudibleAgeTicks);
        double maxTargetUpdateGapMilliseconds = TicksToMilliseconds(maxTargetUpdateGapTicks);
        double generatedPerSecond = generatedDelta / Math.Max(0.001, elapsedSeconds);
        AudioDiagnostics.Log(
            "engine-sim-stream-health",
            $"pending {currentPending}, ready {currentReady}, min pending {minimumPending}, min ready {minimumReady}, generated {generatedPerSecond:0.0}/s, emergency {emergencyGeneratedDelta}, max fill {maxFillMilliseconds:0.00} ms, max emergency {maxEmergencyFillMilliseconds:0.00} ms, target age fill {maxTargetAgeAtFillMilliseconds:0.00} ms, target age submit {maxTargetAgeAtSubmitMilliseconds:0.00} ms, ready age submit {maxReadyAgeAtSubmitMilliseconds:0.00} ms, estimated audible age {maxEstimatedAudibleAgeMilliseconds:0.00} ms, target update gap {maxTargetUpdateGapMilliseconds:0.00} ms, rpm {_lastTargetRpm:0}, load {_lastLoggedTarget.Load:0.00}, vtec {_lastLoggedTarget.VtecBlend:0.00}, limiter {_lastLoggedTarget.Limiter:0.00}, overrun {_lastLoggedTarget.Overrun:0.00}, intake {_lastLoggedTarget.Intake:0.00}, transient {_lastLoggedTarget.ThrottleTransient:0.00}, driveline {_lastLoggedTarget.Driveline:0.00}");
    }

    private void LogStreamRecoveryIfNeeded(int generatedCount, long maxFillTicks)
    {
        double now = AudioDiagnostics.NowSeconds;
        if (now - _lastRecoveryLogSeconds < StreamRecoveryLogIntervalSeconds)
        {
            return;
        }

        _lastRecoveryLogSeconds = now;
        double maxFillMilliseconds = maxFillTicks * 1000.0 / Stopwatch.Frequency;
        AudioDiagnostics.Log(
            "engine-sim-stream-recovery",
            $"generated {generatedCount}, pending {_instance.PendingBufferCount}, ready {Volatile.Read(ref _readyBufferCount)}, max fill {maxFillMilliseconds:0.00} ms");
    }

    private double ReadMaximumBufferFillMilliseconds()
    {
        return Volatile.Read(ref _maximumBufferFillTicks) * 1000.0 / Stopwatch.Frequency;
    }

    private double ReadMaximumTargetAgeAtFillMilliseconds()
    {
        return TicksToMilliseconds(Volatile.Read(ref _maximumTargetAgeAtFillTicks));
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        long current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static void UpdateMinimum(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value >= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static double BufferDurationMilliseconds()
    {
        return FramesPerBuffer * 1000.0 / SampleRate;
    }

    private static double TargetLatencyMilliseconds()
    {
        return TargetPendingBuffers * BufferDurationMilliseconds();
    }

    private static long BufferDurationTicks()
    {
        return FramesPerBuffer * Stopwatch.Frequency / SampleRate;
    }

    private readonly record struct TargetSnapshot(EngineSimulatorSynthesisTarget Target, long UpdatedTicks);

    private readonly record struct GeneratedBuffer(
        byte[] Buffer,
        int Generation,
        long GeneratedTicks,
        long TargetUpdatedTicks);

    private readonly record struct SubmittedBuffer(
        byte[] Buffer,
        long SubmittedTicks,
        long GeneratedTicks,
        long TargetUpdatedTicks);
}
