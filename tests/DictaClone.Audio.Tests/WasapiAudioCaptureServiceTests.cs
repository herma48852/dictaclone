using DictaClone.Audio;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using NAudio.Wave;

namespace DictaClone.Audio.Tests;

public sealed class WasapiAudioCaptureServiceTests
{
    private static readonly AudioSettings DefaultSettings = new(
        DeviceId: null,
        SilenceThreshold: 0.01,
        MaximumDuration: TimeSpan.FromSeconds(2));

    [Fact]
    public async Task DefaultDevice_IsResolvedForEveryNewSession()
    {
        var first = new FakeNativeCapture();
        var second = new FakeNativeCapture();
        var factory = new FakeCaptureFactory(first, second);
        var service = new WasapiAudioCaptureService(factory);

        await using IAudioCaptureSession firstSession =
            await service.StartAsync(DefaultSettings, CancellationToken.None);
        await firstSession.CancelAsync(CancellationToken.None);
        await using IAudioCaptureSession secondSession =
            await service.StartAsync(DefaultSettings, CancellationToken.None);
        await secondSession.CancelAsync(CancellationToken.None);

        Assert.Equal([null, null], factory.RequestedDeviceIds);
        Assert.Equal(1, first.StartCount);
        Assert.Equal(1, second.StartCount);
    }

    [Fact]
    public async Task Capture_IsCreatedWithoutTheCallingSynchronizationContext()
    {
        var native = new FakeNativeCapture();
        var factory = new FakeCaptureFactory(native);
        var service = new WasapiAudioCaptureService(factory);
        var startReturned = new TaskCompletionSource<
            Task<IAudioCaptureSession>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var callingThread = new Thread(() =>
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new());
            try
            {
                startReturned.TrySetResult(service.StartAsync(
                    DefaultSettings,
                    CancellationToken.None));
            }
            catch (Exception exception)
            {
                startReturned.TrySetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        })
        {
            IsBackground = true,
        };
        callingThread.Start();

        Task<IAudioCaptureSession> starting = await startReturned.Task
            .WaitAsync(TimeSpan.FromSeconds(2));
        IAudioCaptureSession session = await starting
            .WaitAsync(TimeSpan.FromSeconds(2));

        await using (session)
        {
            await session.CancelAsync(CancellationToken.None);
        }

        Assert.Null(factory.CreationSynchronizationContext);
    }

    [Fact]
    public async Task Capture_ReportsLevelsAndReturnsInMemoryWhisperAudio()
    {
        var native = new FakeNativeCapture();
        var factory = new FakeCaptureFactory(native);
        var service = new WasapiAudioCaptureService(factory);
        await using IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings with { DeviceId = "microphone-1" },
            CancellationToken.None);
        var levels = new List<AudioLevelChangedEvent>();
        ((IAudioLevelSource)session).LevelChanged +=
            (_, level) => levels.Add(level);

        native.Emit(CreatePcm16(
            sampleCount: 4_000,
            sample: short.MaxValue / 2));
        var audio = await session.StopAsync(CancellationToken.None);

        Assert.Equal(["microphone-1"], factory.RequestedDeviceIds);
        Assert.False(audio.IsSilent);
        Assert.Equal(8_000, audio.Pcm16.Length);
        Assert.InRange(audio.Duration.TotalMilliseconds, 249, 251);
        AudioLevelChangedEvent level = Assert.Single(levels);
        Assert.InRange(level.RootMeanSquare, 0.49, 0.51);
        Assert.InRange(level.Peak, 0.49, 0.51);
    }

    [Fact]
    public async Task Capture_ReportsNativeStereoFloatLevels()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(
            sampleRate: 48_000,
            channels: 2);
        var native = new FakeNativeCapture(format);
        var service = new WasapiAudioCaptureService(
            new FakeCaptureFactory(native));
        await using IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings,
            CancellationToken.None);
        var levels = new List<AudioLevelChangedEvent>();
        ((IAudioLevelSource)session).LevelChanged +=
            (_, level) => levels.Add(level);

        native.Emit(CreateFloat32Stereo(
            frameCount: 12_000,
            left: 0.5f,
            right: 0.25f));
        CapturedAudio audio = await session.StopAsync(CancellationToken.None);

        AudioLevelChangedEvent level = Assert.Single(levels);
        Assert.InRange(level.RootMeanSquare, 0.374, 0.376);
        Assert.InRange(level.Peak, 0.374, 0.376);
        Assert.False(audio.IsSilent);
        Assert.InRange(audio.Pcm16.Length, 7_800, 8_200);
    }

    [Fact]
    public async Task Stop_DoesNotBlockFinalCaptureCallbackOnSessionLock()
    {
        var native = new FakeNativeCapture
        {
            SynchronizeDataCallbackDuringStop = true,
        };
        var service = new WasapiAudioCaptureService(
            new FakeCaptureFactory(native));
        await using IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings,
            CancellationToken.None);
        native.Emit(CreatePcm16(
            sampleCount: 4_000,
            sample: short.MaxValue / 2));

        CapturedAudio audio = await session
            .StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(audio.IsSilent);
        Assert.Equal(1, native.StopCount);
    }

    [Fact]
    public async Task Stop_YieldsWhileNativeShutdownIsBlocked()
    {
        var native = new FakeNativeCapture
        {
            BlockStopUntilReleased = true,
        };
        var service = new WasapiAudioCaptureService(
            new FakeCaptureFactory(native));
        await using IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings,
            CancellationToken.None);
        native.Emit(CreatePcm16(
            sampleCount: 4_000,
            sample: short.MaxValue / 2));

        var stopReturned = new TaskCompletionSource<Task<CapturedAudio>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callingThread = new Thread(() =>
        {
            try
            {
                stopReturned.TrySetResult(
                    session.StopAsync(CancellationToken.None));
            }
            catch (Exception exception)
            {
                stopReturned.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
        };
        callingThread.Start();
        await native.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<CapturedAudio> stopping;
        try
        {
            stopping = await stopReturned.Task
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(stopping.IsCompleted);
        }
        finally
        {
            native.ReleaseStop();
        }

        CapturedAudio audio = await stopping.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(audio.IsSilent);
    }

    [Fact]
    public async Task MaximumDuration_CapsBufferAndStopsExactlyOnce()
    {
        var native = new FakeNativeCapture();
        var service = new WasapiAudioCaptureService(
            new FakeCaptureFactory(native));
        await using IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings with { MaximumDuration = TimeSpan.FromSeconds(1) },
            CancellationToken.None);

        native.Emit(CreatePcm16(
            sampleCount: 32_000,
            sample: short.MaxValue / 2));
        var audio = await session.StopAsync(CancellationToken.None);

        Assert.Equal(1, native.StopCount);
        Assert.Equal(32_000, audio.Pcm16.Length);
        Assert.InRange(audio.Duration.TotalMilliseconds, 999, 1001);
    }

    [Fact]
    public async Task DeviceRemoval_IsReportedByStop()
    {
        var native = new FakeNativeCapture();
        var service = new WasapiAudioCaptureService(
            new FakeCaptureFactory(native));
        await using IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings,
            CancellationToken.None);

        native.Fail(new InvalidOperationException("device removed"));

        AudioCaptureDeviceException exception =
            await Assert.ThrowsAsync<AudioCaptureDeviceException>(
                () => session.StopAsync(CancellationToken.None));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Cancel_DiscardsCapturedAudioAndIsIdempotent()
    {
        var native = new FakeNativeCapture();
        var service = new WasapiAudioCaptureService(
            new FakeCaptureFactory(native));
        IAudioCaptureSession session = await service.StartAsync(
            DefaultSettings,
            CancellationToken.None);
        native.Emit(CreatePcm16(
            sampleCount: 4_000,
            sample: short.MaxValue / 2));

        await session.CancelAsync(CancellationToken.None);
        await session.CancelAsync(CancellationToken.None);
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, native.StopCount);
        Assert.True(native.IsDisposed);
    }

    [Fact]
    public async Task Start_ValidatesCancellationAndWrapsDeviceFailure()
    {
        var cancelledService = new WasapiAudioCaptureService(
            new FakeCaptureFactory(new FakeNativeCapture()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledService.StartAsync(
                DefaultSettings,
                new(canceled: true)));

        var failingService = new WasapiAudioCaptureService(
            new ThrowingCaptureFactory());
        await Assert.ThrowsAsync<AudioCaptureDeviceException>(() =>
            failingService.StartAsync(
                DefaultSettings,
                CancellationToken.None));
    }

    private static byte[] CreatePcm16(int sampleCount, short sample)
    {
        var bytes = new byte[sampleCount * sizeof(short)];
        for (int index = 0; index < sampleCount; index++)
        {
            BitConverter.TryWriteBytes(
                bytes.AsSpan(index * sizeof(short), sizeof(short)),
                sample);
        }

        return bytes;
    }

    private static byte[] CreateFloat32Stereo(
        int frameCount,
        float left,
        float right)
    {
        var bytes = new byte[frameCount * 2 * sizeof(float)];
        for (int frame = 0; frame < frameCount; frame++)
        {
            BitConverter.TryWriteBytes(
                bytes.AsSpan(frame * 2 * sizeof(float), sizeof(float)),
                left);
            BitConverter.TryWriteBytes(
                bytes.AsSpan(
                    (frame * 2 + 1) * sizeof(float),
                    sizeof(float)),
                right);
        }

        return bytes;
    }

    private sealed class FakeCaptureFactory(
        params FakeNativeCapture[] captures) : INativeAudioCaptureFactory
    {
        private readonly Queue<FakeNativeCapture> _captures = new(captures);

        public List<string?> RequestedDeviceIds { get; } = [];

        public SynchronizationContext? CreationSynchronizationContext
        {
            get;
            private set;
        }

        public INativeAudioCapture Create(string? deviceId)
        {
            RequestedDeviceIds.Add(deviceId);
            CreationSynchronizationContext = SynchronizationContext.Current;
            return _captures.Dequeue();
        }
    }

    private sealed class ThrowingCaptureFactory : INativeAudioCaptureFactory
    {
        public INativeAudioCapture Create(string? deviceId) =>
            throw new InvalidOperationException("open failed");
    }

    private sealed class FakeNativeCapture : INativeAudioCapture
    {
        private bool _stopped;
        private readonly TaskCompletionSource _releaseStop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeNativeCapture()
            : this(new WaveFormat(16_000, bits: 16, channels: 1))
        {
        }

        public FakeNativeCapture(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool SynchronizeDataCallbackDuringStop { get; init; }

        public bool BlockStopUntilReleased { get; init; }

        public TaskCompletionSource StopEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<NativeAudioDataEventArgs>? DataAvailable;

        public event EventHandler<NativeAudioStoppedEventArgs>? RecordingStopped;

        public void StartRecording() => StartCount++;

        public void StopRecording()
        {
            StopCount++;
            StopEntered.TrySetResult();
            if (BlockStopUntilReleased)
            {
                _releaseStop.Task.GetAwaiter().GetResult();
            }

            if (!_stopped)
            {
                if (SynchronizeDataCallbackDuringStop)
                {
                    Task finalCallback = Task.Run(() =>
                        DataAvailable?.Invoke(
                            this,
                            new(CreatePcm16(sampleCount: 1, sample: 0))));
                    if (!finalCallback.Wait(TimeSpan.FromSeconds(1)))
                    {
                        throw new TimeoutException(
                            "The final capture callback was blocked.");
                    }
                }

                _stopped = true;
                RecordingStopped?.Invoke(this, new(exception: null));
            }
        }

        public void Emit(byte[] data) =>
            DataAvailable?.Invoke(this, new(data));

        public void Fail(Exception exception)
        {
            _stopped = true;
            RecordingStopped?.Invoke(this, new(exception));
        }

        public void ReleaseStop() => _releaseStop.TrySetResult();

        public void Dispose() => IsDisposed = true;
    }
}
