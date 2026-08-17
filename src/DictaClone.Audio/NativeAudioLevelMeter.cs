using System.Buffers.Binary;
using NAudio.Wave;

namespace DictaClone.Audio;

internal readonly struct NativeAudioLevelMeter
{
    private static readonly Guid PcmSubFormat =
        new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat =
        new("00000003-0000-0010-8000-00aa00389b71");

    private readonly SampleEncoding _encoding;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly int _blockAlign;

    private NativeAudioLevelMeter(
        SampleEncoding encoding,
        int channels,
        int bytesPerSample,
        int blockAlign)
    {
        _encoding = encoding;
        _channels = channels;
        _bytesPerSample = bytesPerSample;
        _blockAlign = blockAlign;
    }

    internal static bool TryCreate(
        WaveFormat format,
        out NativeAudioLevelMeter meter)
    {
        ArgumentNullException.ThrowIfNull(format);
        SampleEncoding encoding = ResolveEncoding(format);
        int bytesPerSample = format.BitsPerSample / 8;
        bool supportedWidth = encoding switch
        {
            SampleEncoding.Pcm => format.BitsPerSample is 8 or 16 or 24 or 32,
            SampleEncoding.Float => format.BitsPerSample is 32 or 64,
            _ => false,
        };
        if (!supportedWidth ||
            format.Channels <= 0 ||
            format.BlockAlign < checked(format.Channels * bytesPerSample))
        {
            meter = default;
            return false;
        }

        meter = new(
            encoding,
            format.Channels,
            bytesPerSample,
            format.BlockAlign);
        return true;
    }

    internal AudioSignalMetrics Measure(ReadOnlySpan<byte> audio)
    {
        int frameCount = audio.Length / _blockAlign;
        if (frameCount == 0)
        {
            return new(0, 0);
        }

        double sumSquares = 0;
        double peak = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int frameOffset = frame * _blockAlign;
            double mixed = 0;
            for (int channel = 0; channel < _channels; channel++)
            {
                int sampleOffset = frameOffset + channel * _bytesPerSample;
                mixed += ReadSample(audio.Slice(sampleOffset, _bytesPerSample));
            }

            double sample = Math.Clamp(mixed / _channels, -1d, 1d);
            double magnitude = Math.Abs(sample);
            peak = Math.Max(peak, magnitude);
            sumSquares += sample * sample;
        }

        return new(Math.Sqrt(sumSquares / frameCount), peak);
    }

    private double ReadSample(ReadOnlySpan<byte> sample)
    {
        double value = _encoding switch
        {
            SampleEncoding.Float when _bytesPerSample == 4 =>
                BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(sample)),
            SampleEncoding.Float => BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64LittleEndian(sample)),
            SampleEncoding.Pcm when _bytesPerSample == 1 =>
                (sample[0] - 128) / 128d,
            SampleEncoding.Pcm when _bytesPerSample == 2 =>
                BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768d,
            SampleEncoding.Pcm when _bytesPerSample == 3 =>
                ReadPcm24(sample) / 8388608d,
            _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648d,
        };
        return double.IsFinite(value) ? value : 0;
    }

    private static int ReadPcm24(ReadOnlySpan<byte> sample)
    {
        int value = sample[0] | sample[1] << 8 | sample[2] << 16;
        return (value & 0x00800000) == 0
            ? value
            : value | unchecked((int)0xFF000000);
    }

    private static SampleEncoding ResolveEncoding(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.Pcm)
        {
            return SampleEncoding.Pcm;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return SampleEncoding.Float;
        }

        if (format is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == PcmSubFormat)
            {
                return SampleEncoding.Pcm;
            }

            if (extensible.SubFormat == FloatSubFormat)
            {
                return SampleEncoding.Float;
            }
        }

        return SampleEncoding.Unsupported;
    }

    private enum SampleEncoding
    {
        Unsupported,
        Pcm,
        Float,
    }
}
