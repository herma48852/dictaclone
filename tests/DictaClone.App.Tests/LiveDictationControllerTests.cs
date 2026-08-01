using DictaClone.App.Presentation;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;

namespace DictaClone.App.Tests;

public sealed class LiveDictationControllerTests
{
    private static readonly HotkeyEvent Press = new(
        HotkeyAction.Dictation,
        HotkeyEventKind.Pressed,
        IsInjected: false);
    private static readonly HotkeyEvent Release = new(
        HotkeyAction.Dictation,
        HotkeyEventKind.Released,
        IsInjected: false);

    [Fact]
    public async Task HoldDictation_CapturesTranscribesCleansAndDisplaysText()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var overlay = new FakeOverlay();
        var foreground = new FakeForegroundTargetService();
        var insertion = new FakeTextInsertionService();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine(" hello world "),
            new FakeTextProcessor(text => text.Trim() + "!"),
            foreground,
            insertion,
            overlay,
            DictaCloneSettings.Default);
        string? completed = null;
        controller.TranscriptionCompleted +=
            (_, eventArgs) => completed = eventArgs.Transcript;

        await controller.HandleAsync(Press);
        session.PublishLevel(0.2, 0.4);
        await controller.HandleAsync(Release);

        Assert.Equal("hello world!", controller.LastTranscript);
        Assert.Equal("hello world!", completed);
        Assert.Equal(
            [
                OverlayStatus.Recording,
                OverlayStatus.Processing,
                OverlayStatus.Processing,
                OverlayStatus.Processing,
                OverlayStatus.Success,
            ],
            overlay.Statuses);
        Assert.Equal("Finishing microphone…", overlay.Messages[1]);
        Assert.Equal("Transcribing locally…", overlay.Messages[2]);
        Assert.Equal("Inserting text…", overlay.Messages[3]);
        Assert.Equal([0.4], overlay.Levels);
        Assert.Equal("hello world!", insertion.Text);
        Assert.Equal(foreground.Target, insertion.Target);
        Assert.Equal(1, foreground.ValidationCount);
        Assert.True(session.StopCalled);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task SilentCapture_DoesNotInvokeTranscription()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio() with
        {
            IsSilent = true,
        });
        var transcription = new FakeTranscriptionEngine("unexpected");
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            transcription,
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default);

        await controller.HandleAsync(Press);
        await controller.HandleAsync(Release);

        Assert.Equal(0, transcription.CallCount);
        Assert.Null(controller.LastTranscript);
        Assert.Contains("No speech", overlay.Messages[^1]);
    }

    [Fact]
    public async Task CancelWhileRecording_CancelsAndDisposesCapture()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var overlay = new FakeOverlay();
        await using var controller = CreateController(session, overlay);

        await controller.HandleAsync(Press);
        await controller.HandleAsync(new(
            HotkeyAction.Cancel,
            HotkeyEventKind.Pressed,
            IsInjected: false));

        Assert.True(session.CancelCalled);
        Assert.True(session.Disposed);
        Assert.Contains("cancelled", overlay.Messages[^1]);
    }

    [Fact]
    public async Task CancelDuringTranscription_WaitsForOperationCleanup()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var transcription = new BlockingTranscriptionEngine();
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            transcription,
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default);

        await controller.HandleAsync(Press);
        Task release = controller.HandleAsync(Release);
        await transcription.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await controller.HandleAsync(new(
            HotkeyAction.Cancel,
            HotkeyEventKind.Pressed,
            IsInjected: false));
        await release;

        Assert.True(session.Disposed);
        Assert.Contains("cancelled", overlay.Messages[^1]);
    }

    [Fact]
    public async Task OverlappingPress_ShowsBusyAndKeepsFirstCapture()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var service = new FakeCaptureService(session);
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            service,
            new FakeTranscriptionEngine("done"),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default);

        await controller.HandleAsync(Press);
        await controller.HandleAsync(Press);
        await controller.HandleAsync(Release);

        Assert.Equal(1, service.CallCount);
        Assert.Contains(
            overlay.Messages,
            message => message.Contains(
                "busy",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("done", controller.LastTranscript);
    }

    [Fact]
    public async Task CaptureOrTranscriptionFailure_IsShownWithoutThrowing()
    {
        var overlay = new FakeOverlay();
        await using (var startFailure = new LiveDictationController(
            new FakeCaptureService(new InvalidOperationException("open")),
            new FakeTranscriptionEngine("unused"),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default))
        {
            await startFailure.HandleAsync(Press);
        }

        var session = new FakeCaptureSession(CreateSpeechAudio());
        await using (var transcriptionFailure = new LiveDictationController(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine(
                new InvalidOperationException("inference")),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default))
        {
            await transcriptionFailure.HandleAsync(Press);
            await transcriptionFailure.HandleAsync(Release);
        }

        Assert.Contains(
            overlay.Messages,
            message => message.Contains(
                nameof(InvalidOperationException),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangedForeground_PreventsInsertionAndExplainsFailure()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var foreground = new FakeForegroundTargetService
        {
            IsCurrent = false,
        };
        var insertion = new FakeTextInsertionService();
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine("do not insert"),
            new FakeTextProcessor(text => text),
            foreground,
            insertion,
            overlay,
            DictaCloneSettings.Default);

        await controller.HandleAsync(Press);
        await controller.HandleAsync(Release);

        Assert.Null(insertion.Text);
        Assert.Null(controller.LastTranscript);
        Assert.Contains("Focus changed", overlay.Messages[^1]);
    }

    [Fact]
    public async Task TypingShortcut_ForcesDelayedTypingWithoutChangingSettings()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var insertion = new FakeTextInsertionService();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine("typed"),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            insertion,
            new FakeOverlay(),
            DictaCloneSettings.Default);
        var typingPress = new HotkeyEvent(
            HotkeyAction.TypingMode,
            HotkeyEventKind.Pressed,
            IsInjected: false);
        var typingRelease = typingPress with
        {
            Kind = HotkeyEventKind.Released,
        };

        await controller.HandleAsync(typingPress);
        await controller.HandleAsync(typingRelease);

        Assert.Equal(TextInsertionMode.DelayedTyping, insertion.Settings!.Mode);
        Assert.Equal(
            TextInsertionMode.Paste,
            DictaCloneSettings.Default.Insertion.Mode);
    }

    [Theory]
    [InlineData(typeof(ElevatedTargetException), "elevated")]
    [InlineData(typeof(ClipboardContentionException), "Clipboard is busy")]
    [InlineData(typeof(InputInjectionException), "blocked text insertion")]
    public async Task InsertionFailure_IsActionableAndDoesNotPublishCompletion(
        Type exceptionType,
        string expectedMessage)
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var insertion = new FakeTextInsertionService
        {
            Exception = exceptionType == typeof(ElevatedTargetException)
                ? new ElevatedTargetException()
                : exceptionType == typeof(ClipboardContentionException)
                    ? new ClipboardContentionException()
                    : new InputInjectionException(),
        };
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine("text"),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            insertion,
            overlay,
            DictaCloneSettings.Default);
        int completions = 0;
        controller.TranscriptionCompleted += (_, _) => completions++;

        await controller.HandleAsync(Press);
        await controller.HandleAsync(Release);

        Assert.Equal(0, completions);
        Assert.Null(controller.LastTranscript);
        Assert.Contains(expectedMessage, overlay.Messages[^1]);
    }

    [Fact]
    public async Task ForegroundCaptureFailure_DoesNotOpenMicrophone()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var audio = new FakeCaptureService(session);
        var foreground = new FakeForegroundTargetService
        {
            CaptureException = new ForegroundTargetUnavailableException(),
        };
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            audio,
            new FakeTranscriptionEngine("unused"),
            new FakeTextProcessor(text => text),
            foreground,
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default);

        await controller.HandleAsync(Press);

        Assert.Equal(0, audio.CallCount);
        Assert.Contains("No focused app", overlay.Messages[^1]);
    }

    [Fact]
    public async Task CancelDuringInsertion_CancelsAndCleansOperation()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        var insertion = new BlockingTextInsertionService();
        var overlay = new FakeOverlay();
        await using var controller = new LiveDictationController(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine("text"),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            insertion,
            overlay,
            DictaCloneSettings.Default);

        await controller.HandleAsync(Press);
        Task release = controller.HandleAsync(Release);
        await insertion.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.CancelAsync();
        await release;

        Assert.True(session.Disposed);
        Assert.Contains("cancelled", overlay.Messages[^1]);
    }

    [Fact]
    public async Task SettingsCannotChangeDuringCapture_ButCanChangeWhenIdle()
    {
        var session = new FakeCaptureSession(CreateSpeechAudio());
        await using var controller = CreateController(
            session,
            new FakeOverlay());
        DictaCloneSettings updated = DictaCloneSettings.Default with
        {
            Audio = DictaCloneSettings.Default.Audio with
            {
                DeviceId = "different-device",
            },
        };

        await controller.UpdateSettingsAsync(updated);
        await controller.HandleAsync(Press);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.UpdateSettingsAsync(DictaCloneSettings.Default));
        await controller.CancelAsync();
    }

    private static LiveDictationController CreateController(
        FakeCaptureSession session,
        FakeOverlay overlay) =>
        new(
            new FakeCaptureService(session),
            new FakeTranscriptionEngine("text"),
            new FakeTextProcessor(text => text),
            new FakeForegroundTargetService(),
            new FakeTextInsertionService(),
            overlay,
            DictaCloneSettings.Default);

    private static CapturedAudio CreateSpeechAudio() =>
        new(
            new byte[3_200],
            SampleRate: 16_000,
            ChannelCount: 1,
            Duration: TimeSpan.FromMilliseconds(100),
            IsSilent: false);

    private sealed class FakeOverlay : IStatusOverlay
    {
        public List<OverlayStatus> Statuses { get; } = [];

        public List<string> Messages { get; } = [];

        public List<double> Levels { get; } = [];

        public void ShowStatus(OverlayStatus status, string? message = null)
        {
            Statuses.Add(status);
            Messages.Add(message ?? string.Empty);
        }

        public void HideStatus()
        {
        }

        public void UpdateLevel(double level) => Levels.Add(level);
    }

    private sealed class FakeCaptureService : IAudioCaptureService
    {
        private readonly FakeCaptureSession? _session;
        private readonly Exception? _exception;

        public FakeCaptureService(FakeCaptureSession session)
        {
            _session = session;
        }

        public FakeCaptureService(Exception exception)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public Task<IAudioCaptureSession> StartAsync(
            AudioSettings settings,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _exception is null
                ? Task.FromResult<IAudioCaptureSession>(_session!)
                : Task.FromException<IAudioCaptureSession>(_exception);
        }
    }

    private sealed class FakeCaptureSession(CapturedAudio audio) :
        IAudioCaptureSession,
        IAudioLevelSource
    {
        public bool StopCalled { get; private set; }

        public bool CancelCalled { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler<AudioLevelChangedEvent>? LevelChanged;

        public Task<CapturedAudio> StopAsync(CancellationToken cancellationToken)
        {
            StopCalled = true;
            return Task.FromResult(audio);
        }

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            CancelCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void PublishLevel(double rootMeanSquare, double peak) =>
            LevelChanged?.Invoke(this, new(rootMeanSquare, peak));
    }

    private sealed class FakeTranscriptionEngine : ITranscriptionEngine
    {
        private readonly string? _transcript;
        private readonly Exception? _exception;

        public FakeTranscriptionEngine(string transcript)
        {
            _transcript = transcript;
        }

        public FakeTranscriptionEngine(Exception exception)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public Task<string> TranscribeAsync(
            CapturedAudio audio,
            TranscriptionSettings settings,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return _exception is null
                ? Task.FromResult(_transcript!)
                : Task.FromException<string>(_exception);
        }
    }

    private sealed class BlockingTranscriptionEngine : ITranscriptionEngine
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> TranscribeAsync(
            CapturedAudio audio,
            TranscriptionSettings settings,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return string.Empty;
        }
    }

    private sealed class FakeTextProcessor(Func<string, string> process)
        : ITextProcessor
    {
        public Task<string> ProcessAsync(
            string transcript,
            DictationMode mode,
            TextProcessingSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(process(transcript));
    }

    private sealed class FakeForegroundTargetService : IForegroundTargetService
    {
        public ForegroundTarget Target { get; set; } = new(
            "window",
            "notepad",
            "Notepad");

        public bool IsCurrent { get; set; } = true;

        public int CaptureCount { get; private set; }

        public int ValidationCount { get; private set; }

        public Exception? CaptureException { get; set; }

        public Task<ForegroundTarget> CaptureAsync(
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return CaptureException is null
                ? Task.FromResult(Target)
                : Task.FromException<ForegroundTarget>(CaptureException);
        }

        public Task<bool> IsCurrentAsync(
            ForegroundTarget target,
            CancellationToken cancellationToken)
        {
            ValidationCount++;
            return Task.FromResult(IsCurrent);
        }
    }

    private sealed class FakeTextInsertionService : ITextInsertionService
    {
        public string? Text { get; private set; }

        public ForegroundTarget? Target { get; private set; }

        public InsertionSettings? Settings { get; private set; }

        public Exception? Exception { get; set; }

        public Task InsertAsync(
            string text,
            ForegroundTarget target,
            InsertionSettings settings,
            CancellationToken cancellationToken)
        {
            Text = text;
            Target = target;
            Settings = settings;
            return Exception is null
                ? Task.CompletedTask
                : Task.FromException(Exception);
        }
    }

    private sealed class BlockingTextInsertionService : ITextInsertionService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InsertAsync(
            string text,
            ForegroundTarget target,
            InsertionSettings settings,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
