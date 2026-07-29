using DictaClone.Audio;

namespace DictaClone.Audio.Tests;

public sealed class CaptureProbeResultTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    public void ReceivedAudioBuffers_RequiresBytesAndBuffers(
        long bytesCaptured,
        int bufferCount,
        bool expected)
    {
        var result = new CaptureProbeResult(
            "device",
            "Microphone",
            "16 bit PCM: 16kHz 1 channels",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            bytesCaptured,
            bufferCount);

        Assert.Equal(expected, result.ReceivedAudioBuffers);
    }
}
