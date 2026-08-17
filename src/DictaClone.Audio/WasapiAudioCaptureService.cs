using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using NAudio.Wave;

namespace DictaClone.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private readonly INativeAudioCaptureFactory _captureFactory;

    public WasapiAudioCaptureService()
        : this(new WasapiNativeAudioCaptureFactory())
    {
    }

    internal WasapiAudioCaptureService(
        INativeAudioCaptureFactory captureFactory)
    {
        _captureFactory = captureFactory ??
            throw new ArgumentNullException(nameof(captureFactory));
    }

    public async Task<IAudioCaptureSession> StartAsync(
        AudioSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(
                () => StartCaptureSession(settings, cancellationToken),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private WasapiAudioCaptureSession StartCaptureSession(
        AudioSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        INativeAudioCapture capture;
        try
        {
            capture = _captureFactory.Create(settings.DeviceId);
        }
        catch (Exception exception)
        {
            throw new AudioCaptureDeviceException(
                "The selected microphone could not be opened.",
                exception);
        }

        var session = new WasapiAudioCaptureSession(capture, settings);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Start();
            return session;
        }
        catch
        {
            session.DisposeAfterFailedStart();
            throw;
        }
    }

    private sealed class WasapiAudioCaptureSession :
        IAudioCaptureSession,
        IAudioLevelSource
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        private readonly object _sync = new();
        private readonly INativeAudioCapture _capture;
        private readonly AudioSettings _settings;
        private readonly MemoryStream _nativeAudio = new();
        private readonly TaskCompletionSource<Exception?> _recordingStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _durationCancellation = new();
        private readonly long _maximumBytes;
        private readonly NativeAudioLevelMeter? _levelMeter;
        private Task? _durationTask;
        private Task<CapturedAudio>? _stopTask;
        private bool _discardAudio;
        private bool _disposed;
        private int _stopRequested;

        public WasapiAudioCaptureSession(
            INativeAudioCapture capture,
            AudioSettings settings)
        {
            _capture = capture;
            _settings = settings;
            _maximumBytes = GetMaximumBytes(
                capture.WaveFormat,
                settings.MaximumDuration);
            if (NativeAudioLevelMeter.TryCreate(
                    capture.WaveFormat,
                    out NativeAudioLevelMeter levelMeter))
            {
                _levelMeter = levelMeter;
            }

            _capture.DataAvailable += CaptureDataAvailable;
            _capture.RecordingStopped += CaptureRecordingStopped;
        }

        public event EventHandler<AudioLevelChangedEvent>? LevelChanged;

        public void Start()
        {
            try
            {
                _capture.StartRecording();
                _durationTask = EnforceMaximumDurationAsync();
            }
            catch (Exception exception)
            {
                throw new AudioCaptureDeviceException(
                    "Microphone capture could not start.",
                    exception);
            }
        }

        public async Task<CapturedAudio> StopAsync(
            CancellationToken cancellationToken)
        {
            Task<CapturedAudio> stopTask;
            TaskCompletionSource<CapturedAudio>? stopCompletion = null;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_stopTask is null)
                {
                    stopCompletion = new(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopTask = stopCompletion.Task;
                }

                stopTask = _stopTask;
            }

            if (stopCompletion is not null)
            {
                _ = CompleteStopAndPublishAsync(stopCompletion);
            }

            return await stopTask
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task CancelAsync(CancellationToken cancellationToken)
        {
            Task<CapturedAudio> stopTask;
            TaskCompletionSource<CapturedAudio>? stopCompletion = null;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _discardAudio = true;
                if (_stopTask is null)
                {
                    stopCompletion = new(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopTask = stopCompletion.Task;
                }

                stopTask = _stopTask;
            }

            if (stopCompletion is not null)
            {
                _ = CompleteStopAndPublishAsync(stopCompletion);
            }

            try
            {
                _ = await stopTask
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AudioCaptureDeviceException)
            {
                // Cancellation still completed its cleanup after device loss.
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
            }

            await CancelAsync(CancellationToken.None).ConfigureAwait(false);

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            await _durationCancellation.CancelAsync().ConfigureAwait(false);
            if (_durationTask is not null)
            {
                await _durationTask.ConfigureAwait(false);
            }

            _durationCancellation.Dispose();
            _capture.DataAvailable -= CaptureDataAvailable;
            _capture.RecordingStopped -= CaptureRecordingStopped;
            _capture.Dispose();
            _nativeAudio.Dispose();
        }

        public void DisposeAfterFailedStart()
        {
            _capture.DataAvailable -= CaptureDataAvailable;
            _capture.RecordingStopped -= CaptureRecordingStopped;
            _durationCancellation.Dispose();
            _capture.Dispose();
            _nativeAudio.Dispose();
            _disposed = true;
        }

        private async Task CompleteStopAndPublishAsync(
            TaskCompletionSource<CapturedAudio> stopCompletion)
        {
            try
            {
                CapturedAudio audio = await CompleteStopAsync()
                    .ConfigureAwait(false);
                stopCompletion.TrySetResult(audio);
            }
            catch (OperationCanceledException exception)
            {
                stopCompletion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                stopCompletion.TrySetException(exception);
            }
        }

        private async Task<CapturedAudio> CompleteStopAsync()
        {
            Exception? captureException;
            using var stopTimeout = new CancellationTokenSource(StopTimeout);

            try
            {
                await Task.Run(RequestStop)
                    .WaitAsync(stopTimeout.Token)
                    .ConfigureAwait(false);
                captureException = await _recordingStopped.Task
                    .WaitAsync(stopTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (stopTimeout.IsCancellationRequested)
            {
                throw new AudioCaptureDeviceException(
                    "The microphone did not stop within five seconds.",
                    new TimeoutException(
                        "Native microphone shutdown timed out.",
                        exception));
            }

            await _durationCancellation.CancelAsync().ConfigureAwait(false);

            if (captureException is not null)
            {
                throw new AudioCaptureDeviceException(
                    "The microphone was disconnected or stopped unexpectedly.",
                    captureException);
            }

            byte[] sourceBytes;
            bool discard;
            lock (_sync)
            {
                sourceBytes = _nativeAudio.ToArray();
                discard = _discardAudio;
            }

            if (discard)
            {
                return new(
                    ReadOnlyMemory<byte>.Empty,
                    PcmAudioConverter.WhisperSampleRate,
                    ChannelCount: 1,
                    TimeSpan.Zero,
                    IsSilent: true);
            }

            try
            {
                return PcmAudioConverter.ConvertToWhisperPcm16(
                    sourceBytes,
                    _capture.WaveFormat,
                    _settings.SilenceThreshold);
            }
            catch (Exception exception)
                when (exception is not AudioCaptureDeviceException)
            {
                throw new AudioCaptureDeviceException(
                    "Captured microphone audio could not be converted.",
                    exception);
            }
        }

        private async Task EnforceMaximumDurationAsync()
        {
            try
            {
                await Task.Delay(
                        _settings.MaximumDuration,
                        _durationCancellation.Token)
                    .ConfigureAwait(false);
                RequestStop();
            }
            catch (OperationCanceledException)
                when (_durationCancellation.IsCancellationRequested)
            {
            }
        }

        private void CaptureDataAvailable(
            object? sender,
            NativeAudioDataEventArgs eventArgs)
        {
            bool reachedLimit = false;
            ReadOnlyMemory<byte> accepted = ReadOnlyMemory<byte>.Empty;

            lock (_sync)
            {
                if (_disposed || Volatile.Read(ref _stopRequested) != 0)
                {
                    return;
                }

                long remaining = _maximumBytes - _nativeAudio.Length;
                int acceptedLength = (int)Math.Min(
                    eventArgs.Data.Length,
                    Math.Max(remaining, 0));
                acceptedLength -= acceptedLength % _capture.WaveFormat.BlockAlign;

                if (acceptedLength > 0)
                {
                    accepted = eventArgs.Data[..acceptedLength];
                    _nativeAudio.Write(accepted.Span);
                }

                reachedLimit = _nativeAudio.Length >= _maximumBytes;
            }

            if (!accepted.IsEmpty)
            {
                PublishLevel(accepted);
            }

            if (reachedLimit)
            {
                RequestStop();
            }
        }

        private void CaptureRecordingStopped(
            object? sender,
            NativeAudioStoppedEventArgs eventArgs)
        {
            _recordingStopped.TrySetResult(eventArgs.Exception);
        }

        private void PublishLevel(ReadOnlyMemory<byte> nativeAudio)
        {
            if (_levelMeter is not NativeAudioLevelMeter meter)
            {
                return;
            }

            try
            {
                AudioSignalMetrics metrics = meter.Measure(nativeAudio.Span);
                Delegate[] handlers = LevelChanged?.GetInvocationList() ?? [];

                foreach (Delegate handler in handlers)
                {
                    try
                    {
                        ((EventHandler<AudioLevelChangedEvent>)handler)(
                            this,
                            new(metrics.RootMeanSquare, metrics.Peak));
                    }
                    catch (Exception)
                    {
                        // Observers cannot interrupt the capture callback.
                    }
                }
            }
            catch (Exception)
            {
                // Metering is best-effort; conversion on stop remains authoritative.
            }
        }

        private void RequestStop()
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            {
                return;
            }

            try
            {
                _capture.StopRecording();
            }
            catch (Exception exception)
            {
                _recordingStopped.TrySetResult(exception);
            }
        }

        private static long GetMaximumBytes(
            WaveFormat format,
            TimeSpan maximumDuration)
        {
            double requested =
                format.AverageBytesPerSecond * maximumDuration.TotalSeconds;
            long maximum = checked((long)Math.Ceiling(requested));
            maximum -= maximum % format.BlockAlign;
            return Math.Max(maximum, format.BlockAlign);
        }
    }
}
