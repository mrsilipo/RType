using Microsoft.Xna.Framework;
using RType.Camera;
using RType.Vehicle;

namespace RType.Audio;

internal sealed class RaceEngineSampleSound : IDisposable
{
    private const float SilentLoopVolume = 0.0001f;

    private readonly VehicleAudioParameters _parameters;
    private readonly SampleLoop[] _loops;
    private readonly SampleLoop? _limiterLoop;
    private float _smoothedRpm;
    private float _previousVtecBlend;
    private float _vtecSurge;
    private float _vtecCrack;
    private float _limiterBouncePhase;
    private float _limiterAudioHoldSeconds;
    private float _limiterAudioBlend;
    private bool _active;

    public RaceEngineSampleSound(VehicleAudioParameters parameters)
    {
        _parameters = parameters;
        List<SampleLoop> loops = [];

        foreach (EngineAudioSampleParameters sample in parameters.EngineSamples.OrderBy(sample => sample.Rpm))
        {
            if (string.IsNullOrWhiteSpace(sample.Path) || !File.Exists(sample.Path))
            {
                AudioDiagnostics.Log("race-engine-sample-missing", $"{sample.Path} at {sample.Rpm:0} rpm");
                continue;
            }

            bool preserveFullLoop = sample.LoopStartRatio <= 0f && sample.LoopEndRatio >= 1f;
            WavLoopSource source = WavLoopSource.Load(sample.Path);
            if (!preserveFullLoop &&
                sample.LoopEndRatio > sample.LoopStartRatio &&
                (sample.LoopStartRatio > 0f || sample.LoopEndRatio < 1f))
            {
                source = source.Slice(sample.LoopStartRatio, sample.LoopEndRatio);
            }

            SampleLoop loop = new(
                sample,
                new LoopingPitchedSound(source, $"race engine {Path.GetFileName(sample.Path)}", preserveFullLoop));
            if (sample.Limiter)
            {
                _limiterLoop = loop;
            }
            else
            {
                loops.Add(loop);
            }
        }

        if (loops.Count == 0)
        {
            TryAddFallbackLoop(loops, parameters.EngineLoopPath, parameters.BaseSampleRpm, highRpm: false);
            TryAddFallbackLoop(loops, parameters.HighRpmLoopPath, parameters.HighRpmBlendInRpm, highRpm: true);
        }

        _loops = [.. loops.OrderBy(loop => loop.Sample.Rpm)];
        if (_loops.Length == 0 && _limiterLoop is null)
        {
            throw new InvalidOperationException("Race engine audio has no valid samples.");
        }

        AudioDiagnostics.Log(
            "race-engine-audio",
            $"profile {_parameters.EngineAudioProfileId}, sample loops {_loops.Length}, limiter {(_limiterLoop is null ? "none" : Path.GetFileName(_limiterLoop.Sample.Path))}, volume {_parameters.EngineSampleVolume:0.00}");
    }

    public RaceEngineAudioState State { get; private set; }

    public void Update(EngineAudioFrame frame)
    {
        if (!frame.Audible || _parameters.EngineSampleVolume <= 0.001f)
        {
            Stop();
            return;
        }

        float dtRpmBlend = _active ? 0.42f : 1f;
        _smoothedRpm = MathHelper.Lerp(_smoothedRpm <= 0f ? frame.Rpm : _smoothedRpm, frame.Rpm, dtRpmBlend);
        float rpm = MathHelper.Clamp(_smoothedRpm, 450f, MathF.Max(900f, frame.RedlineRpm * 1.12f));
        bool wasLimiterAudioActive = IsLimiterAudioActive;
        UpdateLimiterBouncePhase(frame);
        bool limiterJustReleased = wasLimiterAudioActive && !IsLimiterAudioActive;
        float limiterBlend = _limiterLoop is not null ? _limiterAudioBlend : 0f;
        float baseVolume = CalculateBaseVolume(frame, rpm);
        float limiterVolume = baseVolume * limiterBlend * 0.80f;

        float normalScale = _limiterLoop is not null
            ? 1f
            : 1f - MathHelper.Clamp(limiterVolume / MathF.Max(0.001f, baseVolume), 0f, 0.72f);
        UpdateVtecSurge(frame);
        normalScale *= 1f + _vtecSurge * 0.14f;
        if (limiterJustReleased)
        {
            StopLimiterLoop();
        }

        UpdateSampleLoops(frame, rpm, baseVolume * normalScale, limiterJustReleased, limiterBlend);
        UpdateLimiterLoop(frame, rpm, limiterVolume);

        State = default(RaceEngineAudioState) with
        {
            Active = true,
            ProfileId = "race-sample-engine",
            Rpm = rpm,
            CrankPhaseDegrees = frame.CrankPhaseDegrees,
            VtecBlend = MathHelper.Clamp(frame.VtecBlend, 0f, 1f),
            LimiterCut = frame.Limiter > 0.5f,
            RevLimitTimerSeconds = frame.Limiter,
            LastIgnitedCylinder = -1,
            LastThrottle = frame.Throttle,
            LastOutputPeak = baseVolume,
            LastOutputRms = baseVolume * 0.45f
        };
        _active = true;
    }

    public void Stop()
    {
        foreach (SampleLoop loop in _loops)
        {
            loop.Sound.Stop();
        }

        _limiterLoop?.Sound.Stop();
        _smoothedRpm = 0f;
        _limiterBouncePhase = 0f;
        _limiterAudioHoldSeconds = 0f;
        _limiterAudioBlend = 0f;
        _active = false;
        State = default;
    }

    public void Dispose()
    {
        Stop();
        foreach (SampleLoop loop in _loops)
        {
            loop.Sound.Dispose();
        }

        _limiterLoop?.Sound.Dispose();
    }

    private void TryAddFallbackLoop(List<SampleLoop> loops, string path, float rpm, bool highRpm)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        EngineAudioSampleParameters sample = new(path, rpm, highRpm);
        loops.Add(new SampleLoop(sample, new LoopingPitchedSound(WavLoopSource.Load(path), $"race engine {Path.GetFileName(path)}", preserveFullLoop: true)));
    }

    private void UpdateSampleLoops(EngineAudioFrame frame, float rpm, float baseVolume, bool snapVolume, float limiterBlend)
    {
        if (_loops.Length == 0)
        {
            return;
        }

        if (IsLimiterAudioDominant && _limiterLoop is not null)
        {
            foreach (SampleLoop loop in _loops)
            {
                if (IsRole(loop.Sample.Role, "vtec"))
                {
                    continue;
                }

                loop.SmoothedVolume = 0f;
                loop.Sound.Stop();
            }
        }

        Span<float> weights = stackalloc float[_loops.Length];
        bool vtecSampleSet = IsVtecSampleSet();
        if (vtecSampleSet)
        {
            CalculateVtecSampleWeights(frame, weights);
        }
        else
        {
            CalculateAdjacentSampleWeights(rpm, weights);
        }

        float totalWeight = 0f;
        for (int i = 0; i < _loops.Length; i++)
        {
            SampleLoop loop = _loops[i];
            float weight = weights[i];
            if (!vtecSampleSet && loop.Sample.HighRpm)
            {
                weight *= MathHelper.Lerp(0.18f, 1f, frame.VtecBlend);
            }
            else if (!vtecSampleSet && _loops.Any(sampleLoop => sampleLoop.Sample.HighRpm))
            {
                weight *= MathHelper.Lerp(1f, 0.55f, frame.VtecBlend);
            }

            if (!vtecSampleSet)
            {
                weight *= CalculateRoleGate(loop.Sample.Role, frame);
            }

            weights[i] = weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0.0001f)
        {
            totalWeight = 1f;
        }
        else if (vtecSampleSet)
        {
            totalWeight = 1f;
        }

        for (int i = 0; i < _loops.Length; i++)
        {
            SampleLoop loop = _loops[i];
            float normalized = weights[i] / totalWeight;
            if (limiterBlend > 0f)
            {
                normalized = IsRole(loop.Sample.Role, "vtec")
                    ? MathHelper.Lerp(normalized, 1f, limiterBlend)
                    : normalized * (1f - limiterBlend);
            }

            float idleAmount = CalculateIdleAmount(frame, rpm);
            bool rawLimiterRedline = IsLimiterAudioActive && (IsRole(loop.Sample.Role, "redline") || IsRole(loop.Sample.Role, "limiter"));
            float ratio = MathHelper.Clamp(
                rpm / MathF.Max(1f, loop.Sample.Rpm),
                _parameters.MinimumPlaybackRatio,
                _parameters.MaximumPlaybackRatio);
            float vtecBlend = MathHelper.Clamp(frame.VtecBlend, 0f, 1f);
            float sampleVtecEffect = loop.Sample.HighRpm ? vtecBlend : 0f;
            float acousticLift = rawLimiterRedline
                ? 1f
                : 1f + SmoothStep(0.18f, 1f, sampleVtecEffect) * 0.010f + (loop.Sample.HighRpm ? _vtecSurge * 0.006f : 0f);
            float limiterWobble = 1f;
            ratio = MathHelper.Clamp(ratio * acousticLift * limiterWobble, _parameters.MinimumPlaybackRatio, _parameters.MaximumPlaybackRatio);
            float resonance = rawLimiterRedline ? 0f : loop.Sample.HighRpm ? CalculateVtecResonance(frame, _vtecSurge) : 0f;
            float saturation = rawLimiterRedline ? 0f : loop.Sample.HighRpm ? CalculateVtecSaturation(frame, _vtecSurge) : 0f;
            float hardCut = rawLimiterRedline ? 0f : CalculateHardCut(frame);
            float sampleGain = MathHelper.Clamp(loop.Sample.Volume, 0f, 2f);
            float limiterVolume = 1f;
            float targetVolume = baseVolume * normalized * sampleGain * limiterVolume;
            loop.SmoothedVolume = snapVolume || IsSingleContinuousSample() || (vtecSampleSet && IsLimiterAudioActive)
                ? targetVolume
                : SmoothVolume(loop.SmoothedVolume, targetVolume, targetVolume > loop.SmoothedVolume ? 22f : 16f);
            loop.Sound.Update(
                ratio,
                IsLimiterAudioActive && normalized <= 0.0001f
                    ? 0f
                    : MathF.Max(SilentLoopVolume, loop.SmoothedVolume),
                resonance,
                saturation,
                0f,
                hardCut,
                0f,
                CalculateFiringPulseHz(rpm));
        }
    }

    private static float CalculateIdleAmount(EngineAudioFrame frame, float rpm)
    {
        float rpmGate = 1f - SmoothStep(1050f, 1850f, rpm);
        float throttleGate = 1f - SmoothStep(0.04f, 0.22f, MathF.Max(frame.Throttle, frame.ShapedThrottle));
        return MathHelper.Clamp(rpmGate * MathHelper.Lerp(1f, 0.45f, 1f - throttleGate), 0f, 1f);
    }

    private static float CalculateFiringPulseHz(float rpm)
    {
        return MathHelper.Clamp(rpm / 60f * 2f, 18f, 42f);
    }

    private void UpdateVtecSurge(EngineAudioFrame frame)
    {
        float vtecBlend = MathHelper.Clamp(frame.VtecBlend, 0f, 1f);
        float risingEdge = MathF.Max(0f, vtecBlend - _previousVtecBlend);
        if (vtecBlend > 0.18f && risingEdge > 0.015f && frame.Throttle > 0.35f)
        {
            _vtecSurge = MathF.Max(_vtecSurge, MathHelper.Clamp(risingEdge * 8f, 0f, 1f));
            _vtecCrack = 0f;
        }

        _vtecSurge *= 0.90f;
        _vtecCrack *= 0.52f;
        if (_vtecSurge < 0.001f)
        {
            _vtecSurge = 0f;
        }

        if (_vtecCrack < 0.001f)
        {
            _vtecCrack = 0f;
        }

        _previousVtecBlend = vtecBlend;
    }

    private void UpdateLimiterLoop(EngineAudioFrame frame, float rpm, float volume)
    {
        if (_limiterLoop is null)
        {
            return;
        }

        if (!IsLimiterAudioActive || volume <= 0.0001f)
        {
            StopLimiterLoop();
            return;
        }

        float ratio = IsLimiterAudioActive
            ? CalculateLimiterPlaybackRatio()
            : MathHelper.Clamp(
                rpm / MathF.Max(1f, _limiterLoop.Sample.Rpm),
                _parameters.MinimumPlaybackRatio,
                _parameters.MaximumPlaybackRatio);
        float targetVolume = volume * MathHelper.Clamp(_limiterLoop.Sample.Volume, 0f, 2f);
        _limiterLoop.SmoothedVolume = targetVolume;
        _limiterLoop.Sound.Update(ratio, MathF.Max(SilentLoopVolume, _limiterLoop.SmoothedVolume));
    }

    private float CalculateLimiterPlaybackRatio()
    {
        if (_limiterLoop is null)
        {
            return 1f;
        }

        float targetRpm = 8200f;
        return MathHelper.Clamp(
            targetRpm / MathF.Max(1f, _limiterLoop.Sample.Rpm),
            _parameters.MinimumPlaybackRatio,
            _parameters.MaximumPlaybackRatio);
    }

    private void StopLimiterLoop()
    {
        if (_limiterLoop is null)
        {
            return;
        }

        _limiterLoop.SmoothedVolume = 0f;
        _limiterLoop.Sound.Stop();
    }

    private float GetLimiterAudioRpm(float fallbackRedlineRpm)
    {
        if (_limiterLoop is not null)
        {
            return _limiterLoop.Sample.Rpm;
        }

        for (int i = _loops.Length - 1; i >= 0; i--)
        {
            SampleLoop loop = _loops[i];
            if (loop.Sample.HighRpm && IsRole(loop.Sample.Role, "redline"))
            {
                return loop.Sample.Rpm;
            }
        }

        for (int i = _loops.Length - 1; i >= 0; i--)
        {
            SampleLoop loop = _loops[i];
            if (loop.Sample.HighRpm)
            {
                return loop.Sample.Rpm;
            }
        }

        return fallbackRedlineRpm;
    }

    private void UpdateLimiterBouncePhase(EngineAudioFrame frame)
    {
        const float bounceSeconds = 0.25f;
        const float limiterCrossfadeRpm = 50f;
        float dt = MathHelper.Clamp(frame.DeltaSeconds, 0f, 0.1f);
        float limiterRpm = frame.RedlineRpm;
        _limiterAudioBlend = SmoothStep(limiterRpm - limiterCrossfadeRpm, limiterRpm, frame.Rpm);
        bool limiterSignal = _limiterAudioBlend > 0.001f;

        if (!limiterSignal)
        {
            _limiterAudioHoldSeconds = 0f;
            _limiterBouncePhase = 0f;
            return;
        }

        _limiterAudioHoldSeconds = bounceSeconds;
        _limiterBouncePhase += dt / bounceSeconds;
        _limiterBouncePhase -= MathF.Floor(_limiterBouncePhase);
    }

    private void CalculateAdjacentSampleWeights(float rpm, Span<float> weights)
    {
        weights.Clear();
        if (_loops.Length == 1)
        {
            weights[0] = 1f;
            return;
        }

        if (rpm <= _loops[0].Sample.Rpm)
        {
            weights[0] = 1f;
            return;
        }

        int last = _loops.Length - 1;
        if (rpm >= _loops[last].Sample.Rpm)
        {
            weights[last] = 1f;
            return;
        }

        for (int i = 0; i < last; i++)
        {
            float leftRpm = _loops[i].Sample.Rpm;
            float rightRpm = _loops[i + 1].Sample.Rpm;
            if (rpm < leftRpm || rpm > rightRpm)
            {
                continue;
            }

            float t = MathHelper.Clamp((rpm - leftRpm) / MathF.Max(1f, rightRpm - leftRpm), 0f, 1f);
            float crossfadeWidth = MathHelper.Clamp(
                _parameters.EngineSampleCrossfadeWidthRpm / MathF.Max(1f, rightRpm - leftRpm),
                0.02f,
                0.48f);
            if (t <= 0.5f - crossfadeWidth)
            {
                weights[i] = 1f;
            }
            else if (t >= 0.5f + crossfadeWidth)
            {
                weights[i + 1] = 1f;
            }
            else
            {
                float blend = SmoothStep(0.5f - crossfadeWidth, 0.5f + crossfadeWidth, t);
                weights[i] = 1f - blend;
                weights[i + 1] = blend;
            }

            return;
        }
    }

    private void CalculateVtecSampleWeights(EngineAudioFrame frame, Span<float> weights)
    {
        weights.Clear();
        float vtec = SmoothStep(0.02f, 0.98f, MathHelper.Clamp(frame.VtecBlend, 0f, 1f));
        int idleIndex = -1;
        int normalIndex = -1;
        Span<int> highIndices = stackalloc int[_loops.Length];
        int highCount = 0;
        for (int i = 0; i < _loops.Length; i++)
        {
            if (_loops[i].Sample.HighRpm)
            {
                highIndices[highCount++] = i;
            }
            else if (IsRole(_loops[i].Sample.Role, "idle"))
            {
                idleIndex = i;
            }
            else if (IsRole(_loops[i].Sample.Role, "normal") || normalIndex < 0)
            {
                normalIndex = i;
            }
        }

        float lowSide = 1f - vtec;
        float idleToNormal = SmoothStep(900f, 1000f, frame.Rpm);
        if (idleIndex >= 0)
        {
            weights[idleIndex] = 1f;
        }

        if (normalIndex >= 0)
        {
            weights[normalIndex] = lowSide * (idleIndex >= 0 ? idleToNormal : 1f);
        }

        if (IsLimiterAudioActive && highCount > 0)
        {
            weights.Clear();
            for (int i = 0; i < highCount; i++)
            {
                int index = highIndices[i];
                if (IsRole(_loops[index].Sample.Role, "vtec"))
                {
                    weights[index] = 1f;
                    return;
                }
            }

            weights[highIndices[0]] = 1f;
            return;
        }

        if (highCount == 0)
        {
            return;
        }

        if (highCount == 1)
        {
            weights[highIndices[0]] = vtec;
            return;
        }

        int redlineIndex = highIndices[highCount - 1];
        int vtecIndex = highIndices[0];
        float redlineSampleRpm = _loops[redlineIndex].Sample.Rpm;
        float redlineBlend = SmoothStep(redlineSampleRpm - 350f, redlineSampleRpm + 120f, frame.Rpm);
        weights[vtecIndex] = vtec * (1f - redlineBlend);
        weights[redlineIndex] = vtec * redlineBlend;

        for (int i = 1; i < highCount - 1; i++)
        {
            weights[highIndices[i]] = 0f;
        }
    }

    private static bool IsRole(string role, string expected)
    {
        return role.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static float CalculateRoleGate(string role, EngineAudioFrame frame)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "idle" => MathHelper.Lerp(1f, 0.35f, SmoothStep(1100f, 2200f, frame.Rpm)),
            "load" or "onload" or "throttle" => MathHelper.Lerp(0.25f, 1f, MathF.Max(frame.Load, frame.ShapedThrottle)),
            "decel" or "offload" or "overrun" => MathHelper.Lerp(0.04f, 1f, frame.Overrun),
            "vtec" or "highcam" => MathHelper.Lerp(0.12f, 1f, frame.VtecBlend),
            _ => 1f
        };
    }

    private static float CalculateVtecResonance(EngineAudioFrame frame, float surge)
    {
        float vtecBlend = MathHelper.Clamp(frame.VtecBlend, 0f, 1f);
        float throttleGate = SmoothStep(0.28f, 0.82f, MathF.Max(frame.Throttle, frame.ShapedThrottle));
        float resonance = SmoothStep(0.16f, 1f, vtecBlend) * throttleGate * 0.54f;
        resonance += surge * 0.16f;
        return MathHelper.Clamp(resonance, 0f, 0.68f);
    }

    private static float CalculateVtecSaturation(EngineAudioFrame frame, float surge)
    {
        float vtecBlend = MathHelper.Clamp(frame.VtecBlend, 0f, 1f);
        float throttleGate = SmoothStep(0.35f, 0.90f, MathF.Max(frame.Throttle, frame.ShapedThrottle));
        float rpmGate = SmoothStep(frame.RedlineRpm * 0.62f, frame.RedlineRpm, frame.Rpm);
        float camOpen = SmoothStep(0.12f, 1f, vtecBlend);
        return MathHelper.Clamp(camOpen * throttleGate * rpmGate * 0.64f + surge * 0.18f, 0f, 0.78f);
    }

    private static float CalculateHardCut(EngineAudioFrame frame)
    {
        return 0f;
    }

    private static float SmoothVolume(float current, float target, float responseRate)
    {
        return MathHelper.Lerp(current, target, MathHelper.Clamp(1f - MathF.Exp(-responseRate / 60f), 0f, 1f));
    }

    private float CalculateBaseVolume(EngineAudioFrame frame, float rpm)
    {
        if (IsSingleContinuousSample() || IsVtecSampleSet())
        {
            float idleBody = 1f + CalculateIdleAmount(frame, rpm) * 0.70f;
            return MathHelper.Clamp(
                _parameters.EngineSampleVolume *
                frame.DriveVolume *
                frame.PauseScale *
                idleBody,
                0f,
                1f);
        }

        float rpmT = SmoothStep(850f, MathF.Max(1200f, frame.RedlineRpm), rpm);
        float load = MathHelper.Clamp(MathF.Max(frame.Load, frame.ShapedThrottle), 0f, 1f);
        float throttleBody = MathHelper.Lerp(0.42f, 1f, load);
        float overrunBody = MathHelper.Lerp(1f, 1.16f, frame.Overrun);
        float fallbackVtecBody = MathHelper.Lerp(1f, 1.08f, frame.VtecBlend);
        float shiftBody = MathHelper.Lerp(1f, 1.10f, MathF.Max(frame.Shock, frame.ThrottleTransient));
        float fallbackIdleBody = 1f + CalculateIdleAmount(frame, rpm) * 0.90f;
        float viewGain = frame.CameraMode == CameraMode.InCar ? 0.78f : 0.64f;
        return MathHelper.Clamp(
            _parameters.EngineSampleVolume *
            frame.DriveVolume *
            frame.PauseScale *
            viewGain *
            MathHelper.Lerp(0.58f, 1f, rpmT) *
            throttleBody *
            fallbackIdleBody *
            overrunBody *
            fallbackVtecBody *
            shiftBody,
            0f,
            0.85f);
    }

    private bool IsSingleContinuousSample()
    {
        return _loops.Length == 1 && _limiterLoop is null;
    }

    private bool IsLimiterAudioActive => _limiterAudioHoldSeconds > 0f;

    private bool IsLimiterAudioDominant => _limiterAudioBlend >= 0.999f;

    private bool IsVtecSampleSet()
    {
        return _loops.Length >= 2 &&
               _loops.Any(loop => loop.Sample.HighRpm) &&
               _loops.Any(loop => !loop.Sample.HighRpm);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = MathHelper.Clamp((value - edge0) / MathF.Max(0.001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private sealed record SampleLoop(EngineAudioSampleParameters Sample, LoopingPitchedSound Sound)
    {
        public float SmoothedVolume { get; set; }
    }
}
