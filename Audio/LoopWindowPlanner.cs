namespace RetroRacer.Audio;

internal readonly record struct LoopWindow(int EndFrame, int CrossfadeFrames, float MatchError);

internal static class LoopWindowPlanner
{
    public static LoopWindow Plan(WavLoopSource source)
    {
        int crossfadeFrames = CalculateCrossfadeFrameCount(source);
        if (crossfadeFrames <= 0)
        {
            return new LoopWindow(source.FrameCount, 0, 0f);
        }

        int maximumSearchBackFrames = Math.Min(
            CalculateMaximumSearchBackFrames(source),
            Math.Max(0, source.FrameCount - crossfadeFrames * 2));
        int earliestEndFrame = Math.Max(crossfadeFrames * 2, source.FrameCount - maximumSearchBackFrames);
        int latestEndFrame = source.FrameCount;
        int bestEndFrame = latestEndFrame;
        float bestError = CalculateMatchError(source, bestEndFrame, crossfadeFrames);

        const int coarseStepFrames = 16;
        for (int candidateEndFrame = earliestEndFrame; candidateEndFrame <= latestEndFrame; candidateEndFrame += coarseStepFrames)
        {
            TryUseCandidate(source, candidateEndFrame, crossfadeFrames, maximumSearchBackFrames, ref bestEndFrame, ref bestError);
        }

        int fineStart = Math.Max(earliestEndFrame, bestEndFrame - coarseStepFrames);
        int fineEnd = Math.Min(latestEndFrame, bestEndFrame + coarseStepFrames);
        for (int candidateEndFrame = fineStart; candidateEndFrame <= fineEnd; candidateEndFrame++)
        {
            TryUseCandidate(source, candidateEndFrame, crossfadeFrames, maximumSearchBackFrames, ref bestEndFrame, ref bestError);
        }

        return new LoopWindow(bestEndFrame, crossfadeFrames, bestError);
    }

    public static float CalculateMatchError(WavLoopSource source, int endFrame, int crossfadeFrames)
    {
        int channelCount = source.ChannelCount;
        int startFrame = endFrame - crossfadeFrames;
        if (crossfadeFrames <= 0 || startFrame < 0 || endFrame > source.FrameCount)
        {
            return float.MaxValue;
        }

        double total = 0.0;
        int comparisons = 0;
        for (int frame = 0; frame < crossfadeFrames; frame++)
        {
            int tailFrame = startFrame + frame;
            int previousTailFrame = Math.Max(startFrame, tailFrame - 1);
            int previousHeadFrame = Math.Max(0, frame - 1);

            for (int channel = 0; channel < channelCount; channel++)
            {
                float tail = GetSample(source, tailFrame, channel);
                float head = GetSample(source, frame, channel);
                float tailDelta = tail - GetSample(source, previousTailFrame, channel);
                float headDelta = head - GetSample(source, previousHeadFrame, channel);
                float sampleDifference = tail - head;
                float slopeDifference = tailDelta - headDelta;
                total += sampleDifference * sampleDifference + slopeDifference * slopeDifference * 0.35f;
                comparisons++;
            }
        }

        return comparisons == 0 ? float.MaxValue : (float)Math.Sqrt(total / comparisons);
    }

    private static void TryUseCandidate(
        WavLoopSource source,
        int candidateEndFrame,
        int crossfadeFrames,
        int maximumSearchBackFrames,
        ref int bestEndFrame,
        ref float bestError)
    {
        float error = CalculateMatchError(source, candidateEndFrame, crossfadeFrames);
        if (maximumSearchBackFrames > 0)
        {
            float trimPenalty = (source.FrameCount - candidateEndFrame) / (float)maximumSearchBackFrames * 0.0015f;
            error += trimPenalty;
        }

        if (error < bestError)
        {
            bestError = error;
            bestEndFrame = candidateEndFrame;
        }
    }

    private static int CalculateCrossfadeFrameCount(WavLoopSource source)
    {
        int preferred = source.SampleRate / 20;
        int maximum = Math.Max(0, source.FrameCount / 4);
        if (maximum <= 0)
        {
            return 0;
        }

        int minimum = Math.Min(1024, maximum);
        return Math.Clamp(preferred, minimum, maximum);
    }

    private static int CalculateMaximumSearchBackFrames(WavLoopSource source)
    {
        int defaultSearchBackFrames = source.SampleRate / 8;
        if (source.FrameCount <= source.SampleRate / 2)
        {
            return Math.Max(1, source.FrameCount / 8);
        }

        return defaultSearchBackFrames;
    }

    private static float GetSample(WavLoopSource source, int frame, int channel)
    {
        int clampedFrame = Math.Clamp(frame, 0, source.FrameCount - 1);
        return source.Samples[clampedFrame * source.ChannelCount + channel];
    }
}
