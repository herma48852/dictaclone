namespace DictaClone.Audio;

public sealed record CaptureProbeResult(
    string DeviceId,
    string FriendlyName,
    string WaveFormat,
    TimeSpan RequestedDuration,
    TimeSpan ElapsedDuration,
    long BytesCaptured,
    int BufferCount)
{
    public bool ReceivedAudioBuffers => BytesCaptured > 0 && BufferCount > 0;
}
