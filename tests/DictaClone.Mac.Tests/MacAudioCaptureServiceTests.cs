using System.Buffers.Binary;
using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;
using DictaClone.Mac.Audio;

namespace DictaClone.Mac.Tests;

public sealed class MacAudioCaptureServiceTests
{
    [Fact]
    public async Task Capture_ReturnsWhisperPcmAndPublishesLevel()
    {
        var queue = new FakeAudioQueue();
        var service = new MacAudioCaptureService(new FakeFactory(queue));
        IAudioCaptureSession session = await service.StartAsync(
            DictaCloneSettings.Default.Audio,
            CancellationToken.None);
        double peak = 0;
        ((IAudioLevelSource)session).LevelChanged +=
            (_, eventArgs) => peak = eventArgs.Peak;

        queue.Publish(CreateTone(sampleCount: 4_800, amplitude: 10_000));
        var audio = await session.StopAsync(CancellationToken.None);
        await session.DisposeAsync();

        Assert.Equal(16_000, audio.SampleRate);
        Assert.Equal(1, audio.ChannelCount);
        Assert.False(audio.IsSilent);
        Assert.True(peak > 0.3);
        Assert.True(queue.Stopped);
        Assert.True(queue.Disposed);
    }

    [Fact]
    public async Task Capture_RejectsShortOrSilentAudio()
    {
        var queue = new FakeAudioQueue();
        var service = new MacAudioCaptureService(new FakeFactory(queue));
        IAudioCaptureSession session = await service.StartAsync(
            DictaCloneSettings.Default.Audio,
            CancellationToken.None);

        queue.Publish(CreateTone(sampleCount: 1_000, amplitude: 0));
        var audio = await session.StopAsync(CancellationToken.None);
        await session.DisposeAsync();

        Assert.True(audio.IsSilent);
    }

    private static byte[] CreateTone(int sampleCount, short amplitude)
    {
        var pcm = new byte[sampleCount * sizeof(short)];
        for (int index = 0; index < sampleCount; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                pcm.AsSpan(index * sizeof(short), sizeof(short)),
                index % 2 == 0 ? amplitude : (short)-amplitude);
        }

        return pcm;
    }

    private sealed class FakeFactory(FakeAudioQueue queue) : IMacAudioQueueFactory
    {
        public IMacAudioQueue Create(string? deviceId) => queue;
    }

    private sealed class FakeAudioQueue : IMacAudioQueue
    {
        public event EventHandler<MacAudioDataEventArgs>? DataAvailable;

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public void Stop() => Stopped = true;

        public void Dispose() => Disposed = true;

        public void Publish(byte[] pcm) => DataAvailable?.Invoke(this, new(pcm));
    }
}
