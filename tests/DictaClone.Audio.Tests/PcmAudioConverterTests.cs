using System.Buffers.Binary;
using DictaClone.Audio;
using NAudio.Wave;

namespace DictaClone.Audio.Tests;

public sealed class PcmAudioConverterTests
{
    [Fact]
    public void FloatStereo48Khz_IsMixedAndResampledForWhisper()
    {
        const int sourceRate = 48_000;
        const int frames = sourceRate / 4;
        var samples = new float[frames * 2];

        for (int frame = 0; frame < frames; frame++)
        {
            float sample = (float)(0.5 * Math.Sin(
                2 * Math.PI * 440 * frame / sourceRate));
            samples[frame * 2] = sample;
            samples[(frame * 2) + 1] = sample;
        }

        byte[] source = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, source, 0, source.Length);

        var result = PcmAudioConverter.ConvertToWhisperPcm16(
            source,
            WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, channels: 2),
            silenceThreshold: 0.01);
        AudioSignalMetrics metrics =
            PcmAudioConverter.MeasureWhisperPcm16(result.Pcm16.Span);

        Assert.Equal(16_000, result.SampleRate);
        Assert.Equal(1, result.ChannelCount);
        Assert.InRange(result.Duration.TotalMilliseconds, 245, 255);
        Assert.InRange(result.Pcm16.Length, 7_800, 8_200);
        Assert.False(result.IsSilent);
        Assert.InRange(metrics.RootMeanSquare, 0.34, 0.37);
        Assert.InRange(metrics.Peak, 0.48, 0.52);
    }

    [Fact]
    public void OpposingStereoChannels_MixToSilence()
    {
        const int frames = 4_800;
        var samples = new float[frames * 2];
        for (int frame = 0; frame < frames; frame++)
        {
            samples[frame * 2] = 0.5f;
            samples[(frame * 2) + 1] = -0.5f;
        }

        byte[] source = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, source, 0, source.Length);

        var result = PcmAudioConverter.ConvertToWhisperPcm16(
            source,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, channels: 2),
            silenceThreshold: 0.001,
            minimumSpeechDuration: TimeSpan.Zero);

        Assert.True(result.IsSilent);
        Assert.All(result.Pcm16.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void FloatSamples_AreClampedToPcm16Range()
    {
        float[] samples = [2f, -2f, 0.5f, -0.5f];
        byte[] source = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, source, 0, source.Length);

        var result = PcmAudioConverter.ConvertToWhisperPcm16(
            source,
            WaveFormat.CreateIeeeFloatWaveFormat(16_000, channels: 1),
            silenceThreshold: 0,
            minimumSpeechDuration: TimeSpan.Zero);

        Assert.Equal(
            short.MaxValue,
            BinaryPrimitives.ReadInt16LittleEndian(result.Pcm16.Span));
        Assert.Equal(
            short.MinValue,
            BinaryPrimitives.ReadInt16LittleEndian(result.Pcm16.Span[2..]));
        Assert.Equal(8, result.Pcm16.Length);
    }

    [Fact]
    public void EmptyOrSubBlockInput_ReturnsSilentEmptyAudio()
    {
        WaveFormat format = new(16_000, bits: 16, channels: 1);

        var empty = PcmAudioConverter.ConvertToWhisperPcm16(
            ReadOnlyMemory<byte>.Empty,
            format,
            silenceThreshold: 0.01);
        var partial = PcmAudioConverter.ConvertToWhisperPcm16(
            new byte[1],
            format,
            silenceThreshold: 0.01);

        Assert.True(empty.IsSilent);
        Assert.True(empty.Pcm16.IsEmpty);
        Assert.True(partial.IsSilent);
        Assert.True(partial.Pcm16.IsEmpty);
        Assert.Equal(new AudioSignalMetrics(0, 0),
            PcmAudioConverter.MeasureWhisperPcm16([]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidSilenceThreshold_IsRejected(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PcmAudioConverter.ConvertToWhisperPcm16(
                new byte[2],
                new WaveFormat(16_000, bits: 16, channels: 1),
                threshold));
    }
}
