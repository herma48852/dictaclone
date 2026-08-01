using System.Buffers.Binary;
using DictaClone.Core.Dictation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DictaClone.Audio;

public static class PcmAudioConverter
{
    public const int WhisperSampleRate = 16_000;

    public static readonly TimeSpan DefaultMinimumSpeechDuration =
        TimeSpan.FromMilliseconds(150);

    public static CapturedAudio ConvertToWhisperPcm16(
        ReadOnlyMemory<byte> sourceAudio,
        WaveFormat sourceFormat,
        double silenceThreshold,
        TimeSpan? minimumSpeechDuration = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFormat);
        if (!double.IsFinite(silenceThreshold) ||
            silenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(silenceThreshold));
        }

        TimeSpan minimumDuration =
            minimumSpeechDuration ?? DefaultMinimumSpeechDuration;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            minimumDuration,
            TimeSpan.Zero);

        int alignedLength = sourceAudio.Length -
            (sourceAudio.Length % sourceFormat.BlockAlign);
        if (alignedLength == 0)
        {
            return new(
                ReadOnlyMemory<byte>.Empty,
                WhisperSampleRate,
                ChannelCount: 1,
                TimeSpan.Zero,
                IsSilent: true);
        }

        using var sourceStream = new MemoryStream(
            sourceAudio[..alignedLength].ToArray(),
            writable: false);
        using var rawStream = new RawSourceWaveStream(
            sourceStream,
            sourceFormat);
        ISampleProvider samples = rawStream.ToSampleProvider();

        if (samples.WaveFormat.Channels > 1)
        {
            samples = new MonoMixingSampleProvider(samples);
        }

        if (samples.WaveFormat.SampleRate != WhisperSampleRate)
        {
            samples = new WdlResamplingSampleProvider(
                samples,
                WhisperSampleRate);
        }

        using var pcm = new MemoryStream();
        var sampleBuffer = new float[4096];
        Span<byte> encoded = stackalloc byte[sizeof(short)];
        double sumSquares = 0;
        double peak = 0;
        long sampleCount = 0;
        int samplesRead;

        while ((samplesRead = samples.Read(
                   sampleBuffer,
                   offset: 0,
                   sampleBuffer.Length)) > 0)
        {
            for (int index = 0; index < samplesRead; index++)
            {
                float sample = Math.Clamp(sampleBuffer[index], -1f, 1f);
                double magnitude = Math.Abs((double)sample);
                peak = Math.Max(peak, magnitude);
                sumSquares += sample * sample;
                sampleCount++;

                short pcmSample = sample >= 1f
                    ? short.MaxValue
                    : sample <= -1f
                        ? short.MinValue
                        : (short)Math.Clamp(
                            (int)Math.Round(sample * 32768f),
                            short.MinValue,
                            short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(encoded, pcmSample);
                pcm.Write(encoded);
            }
        }

        TimeSpan duration = TimeSpan.FromSeconds(
            sampleCount / (double)WhisperSampleRate);
        double rootMeanSquare = sampleCount == 0
            ? 0
            : Math.Sqrt(sumSquares / sampleCount);
        bool isSilent =
            duration < minimumDuration ||
            rootMeanSquare < silenceThreshold ||
            peak == 0;

        return new(
            pcm.ToArray(),
            WhisperSampleRate,
            ChannelCount: 1,
            duration,
            isSilent);
    }

    public static AudioSignalMetrics MeasureWhisperPcm16(
        ReadOnlySpan<byte> pcm16)
    {
        int sampleCount = pcm16.Length / sizeof(short);
        if (sampleCount == 0)
        {
            return new(0, 0);
        }

        double sumSquares = 0;
        double peak = 0;

        for (int index = 0; index < sampleCount; index++)
        {
            short encoded = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16.Slice(index * sizeof(short), sizeof(short)));
            double sample = encoded / 32768d;
            double magnitude = Math.Abs(sample);
            peak = Math.Max(peak, magnitude);
            sumSquares += sample * sample;
        }

        return new(Math.Sqrt(sumSquares / sampleCount), peak);
    }

    private sealed class MonoMixingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float[] _sourceBuffer = [];

        public MonoMixingSampleProvider(ISampleProvider source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                source.WaveFormat.SampleRate,
                channels: 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            int channels = _source.WaveFormat.Channels;
            int sourceCount = checked(count * channels);
            if (_sourceBuffer.Length < sourceCount)
            {
                _sourceBuffer = new float[sourceCount];
            }

            int sourceRead = _source.Read(_sourceBuffer, 0, sourceCount);
            int framesRead = sourceRead / channels;

            for (int frame = 0; frame < framesRead; frame++)
            {
                double sum = 0;
                int sourceOffset = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += _sourceBuffer[sourceOffset + channel];
                }

                buffer[offset + frame] = (float)(sum / channels);
            }

            return framesRead;
        }
    }
}

public readonly record struct AudioSignalMetrics(
    double RootMeanSquare,
    double Peak);
