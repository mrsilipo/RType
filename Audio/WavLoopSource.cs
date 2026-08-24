using Microsoft.Xna.Framework.Audio;

namespace RType.Audio;

public sealed class WavLoopSource
{
    private WavLoopSource(int sampleRate, int channelCount, float[] samples, short[] pcmSamples)
    {
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        Channels = channelCount == 2 ? AudioChannels.Stereo : AudioChannels.Mono;
        Samples = samples;
        PcmSamples = pcmSamples;
        FrameCount = samples.Length / channelCount;
    }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public AudioChannels Channels { get; }

    public int FrameCount { get; }

    public float[] Samples { get; }

    public short[] PcmSamples { get; }

    public WavLoopSource Slice(float startRatio, float endRatio)
    {
        startRatio = Math.Clamp(startRatio, 0f, 0.98f);
        endRatio = Math.Clamp(endRatio, startRatio + 0.01f, 1f);

        int startFrame = Math.Clamp((int)(FrameCount * startRatio), 0, FrameCount - 1);
        int endFrame = Math.Clamp((int)(FrameCount * endRatio), startFrame + 1, FrameCount);
        int sliceFrameCount = endFrame - startFrame;
        float[] slicedSamples = new float[sliceFrameCount * ChannelCount];
        short[] slicedPcmSamples = new short[slicedSamples.Length];
        Array.Copy(
            Samples,
            startFrame * ChannelCount,
            slicedSamples,
            0,
            slicedSamples.Length);
        Array.Copy(
            PcmSamples,
            startFrame * ChannelCount,
            slicedPcmSamples,
            0,
            slicedPcmSamples.Length);

        return new WavLoopSource(SampleRate, ChannelCount, slicedSamples, slicedPcmSamples);
    }

    public static WavLoopSource Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);

        string riff = ReadFourCc(reader);
        _ = reader.ReadInt32();
        string wave = ReadFourCc(reader);
        if (riff != "RIFF" || wave != "WAVE")
        {
            throw new InvalidDataException($"'{path}' is not a RIFF/WAVE file.");
        }

        short formatTag = 0;
        short channelCount = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? pcmData = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = ReadFourCc(reader);
            int chunkSize = reader.ReadInt32();
            long chunkEnd = stream.Position + chunkSize;

            if (chunkId == "fmt ")
            {
                formatTag = reader.ReadInt16();
                channelCount = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
            }
            else if (chunkId == "data")
            {
                pcmData = reader.ReadBytes(chunkSize);
            }

            stream.Position = chunkEnd + (chunkSize & 1);
        }

        if (formatTag != 1 || channelCount is not (1 or 2) || sampleRate <= 0 || bitsPerSample != 16 || pcmData is null)
        {
            throw new InvalidDataException($"'{path}' must be 16-bit PCM mono or stereo WAV.");
        }

        float[] samples = new float[pcmData.Length / 2];
        short[] pcmSamples = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = BitConverter.ToInt16(pcmData, i * 2);
            pcmSamples[i] = value;
            samples[i] = value / 32768f;
        }

        return new WavLoopSource(sampleRate, channelCount, samples, pcmSamples);
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return new string(reader.ReadChars(4));
    }
}
