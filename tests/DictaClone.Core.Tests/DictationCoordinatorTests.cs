using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using DictaClone.Core.Workflow;

namespace DictaClone.Core.Tests;

public sealed class DictationCoordinatorTests
{
    [Fact]
    public async Task SuccessfulDictation_TraversesPipelineAndInsertsText()
    {
        var rig = new TestRig();
        await using DictationCoordinator coordinator = rig.CreateCoordinator();
        var states = new List<DictationState>();
        coordinator.StateChanged += (_, change) => states.Add(change.Current);

        DictationStartResult start = await coordinator.StartAsync(DictationMode.Dictation);
        DictationResult result = await coordinator.StopAsync();

        Assert.Equal(DictationStartOutcome.Started, start.Outcome);
        Assert.Equal(DictationOutcome.Completed, result.Outcome);
        Assert.Equal("Clean text.", result.Text);
        Assert.Equal(["Clean text."], rig.Insertion.InsertedTexts);
        Assert.Same(DictaCloneSettings.Default.Audio, rig.Audio.LastSettings);
        Assert.Same(
            DictaCloneSettings.Default.Transcription,
            rig.Transcriber.LastSettings);
        Assert.Equal(
            [
                DictationState.Recording,
                DictationState.Transcribing,
                DictationState.Cleaning,
                DictationState.Inserting,
                DictationState.Idle,
            ],
            states);
        Assert.True(rig.Session.Disposed);
    }

    [Fact]
    public async Task RepeatedAndConcurrentStarts_CreateOnlyOneCapture()
    {
        var rig = new TestRig();
        await using DictationCoordinator coordinator = rig.CreateCoordinator();

        Task<DictationStartResult>[] starts = Enumerable.Range(0, 20)
            .Select(_ => coordinator.StartAsync(DictationMode.Dictation))
            .ToArray();
        DictationStartResult[] results = await Task.WhenAll(starts);

        Assert.Single(
            results,
            result => result.Outcome == DictationStartOutcome.Started);
        Assert.Equal(
            19,
            results.Count(
                result => result.Outcome ==
                    DictationStartOutcome.IgnoredAlreadyActive));
        Assert.Equal(1, rig.Audio.StartCount);

        Assert.True(await coordinator.CancelAsync());
    }

    [Fact]
    public async Task CancelDuringRecording_CancelsAndDisposesCapture()
    {
        var rig = new TestRig();
        await using DictationCoordinator coordinator = rig.CreateCoordinator();
        var states = new List<DictationState>();
        coordinator.StateChanged += (_, change) => states.Add(change.Current);

        await coordinator.StartAsync(DictationMode.Dictation);
        bool cancelled = await coordinator.CancelAsync();

        Assert.True(cancelled);
        Assert.True(rig.Session.Cancelled);
        Assert.True(rig.Session.Disposed);
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Equal(
            [
                DictationState.Recording,
                DictationState.Cancelled,
                DictationState.Idle,
            ],
            states);
        Assert.Empty(rig.Insertion.InsertedTexts);
    }

    [Fact]
    public async Task CancelDuringTranscription_PreventsInsertion()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = new TestRig
        {
            Transcribe = async (_, _, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "unreachable";
            },
        };
        await using DictationCoordinator coordinator = rig.CreateCoordinator();

        await coordinator.StartAsync(DictationMode.Dictation);
        Task<DictationResult> stopping = coordinator.StopAsync();
        await entered.Task;
        bool cancelled = await coordinator.CancelAsync();
        DictationResult result = await stopping;

        Assert.True(cancelled);
        Assert.Equal(DictationOutcome.Cancelled, result.Outcome);
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Empty(rig.Insertion.InsertedTexts);
    }

    [Fact]
    public async Task SilenceAndEmptyAudio_DoNotInvokeTranscription()
    {
        foreach (CapturedAudio audio in new[]
                 {
                     TestRig.AudioData with { IsSilent = true },
                     TestRig.AudioData with { Pcm16 = ReadOnlyMemory<byte>.Empty },
                 })
        {
            var rig = new TestRig();
            rig.Session.Audio = audio;
            await using DictationCoordinator coordinator = rig.CreateCoordinator();

            await coordinator.StartAsync(DictationMode.Dictation);
            DictationResult result = await coordinator.StopAsync();

            Assert.Equal(DictationOutcome.NoSpeech, result.Outcome);
            Assert.Equal(0, rig.Transcriber.CallCount);
            Assert.Empty(rig.Insertion.InsertedTexts);
        }
    }

    [Fact]
    public async Task BlankTranscriptAndBlankFinalText_DoNotInsert()
    {
        var blankTranscriptRig = new TestRig
        {
            Transcribe = (_, _, _) => Task.FromResult(" "),
        };
        await using (DictationCoordinator coordinator =
                     blankTranscriptRig.CreateCoordinator())
        {
            await coordinator.StartAsync(DictationMode.Dictation);
            Assert.Equal(
                DictationOutcome.NoSpeech,
                (await coordinator.StopAsync()).Outcome);
            Assert.Equal(0, blankTranscriptRig.Processor.CallCount);
        }

        var blankFinalRig = new TestRig
        {
            Process = (_, _, _, _) => Task.FromResult(string.Empty),
        };
        await using (DictationCoordinator coordinator =
                     blankFinalRig.CreateCoordinator())
        {
            await coordinator.StartAsync(DictationMode.Dictation);
            Assert.Equal(
                DictationOutcome.NoSpeech,
                (await coordinator.StopAsync()).Outcome);
            Assert.Empty(blankFinalRig.Insertion.InsertedTexts);
        }
    }

    [Theory]
    [InlineData(DictationFailureStage.AudioCapture)]
    [InlineData(DictationFailureStage.Transcription)]
    [InlineData(DictationFailureStage.TextProcessing)]
    [InlineData(DictationFailureStage.TextInsertion)]
    public async Task ProcessingFailure_FaultsRecoversAndDoesNotLeakText(
        DictationFailureStage failureStage)
    {
        var rig = new TestRig();
        switch (failureStage)
        {
            case DictationFailureStage.AudioCapture:
                rig.Session.StopException = new IOException();
                break;
            case DictationFailureStage.Transcription:
                rig.Transcribe = (_, _, _) => throw new NotSupportedException();
                break;
            case DictationFailureStage.TextProcessing:
                rig.Process = (_, _, _, _) => throw new FormatException();
                break;
            case DictationFailureStage.TextInsertion:
                rig.Target.IsCurrent = false;
                break;
            default:
                throw new InvalidOperationException();
        }

        await using DictationCoordinator coordinator = rig.CreateCoordinator();
        var states = new List<DictationState>();
        coordinator.StateChanged += (_, change) => states.Add(change.Current);

        await coordinator.StartAsync(DictationMode.Dictation);
        DictationResult result = await coordinator.StopAsync();

        Assert.Equal(DictationOutcome.Failed, result.Outcome);
        Assert.Equal(failureStage, result.Failure?.Stage);
        Assert.NotEmpty(result.Failure?.ErrorCode ?? string.Empty);
        Assert.Contains(DictationState.Faulted, states);
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Empty(rig.Insertion.InsertedTexts);
    }

    [Fact]
    public async Task InsertionException_IsReportedWithoutTranscriptContent()
    {
        var rig = new TestRig
        {
            Insert = (_, _, _, _) => throw new InvalidOperationException(
                "must not escape"),
        };
        await using DictationCoordinator coordinator = rig.CreateCoordinator();

        await coordinator.StartAsync(DictationMode.Dictation);
        DictationResult result = await coordinator.StopAsync();

        Assert.Equal(DictationOutcome.Failed, result.Outcome);
        Assert.Equal(DictationFailureStage.TextInsertion, result.Failure?.Stage);
        Assert.Equal("InvalidOperationException", result.Failure?.ErrorCode);
        Assert.DoesNotContain("must not escape", result.Failure?.ErrorCode);
    }

    [Theory]
    [InlineData(DictationFailureStage.ForegroundTarget)]
    [InlineData(DictationFailureStage.AudioCapture)]
    public async Task StartFailure_FaultsAndReturnsToIdle(
        DictationFailureStage stage)
    {
        var rig = new TestRig();
        if (stage == DictationFailureStage.ForegroundTarget)
        {
            rig.Target.CaptureException = new InvalidOperationException();
        }
        else
        {
            rig.Audio.StartException = new IOException();
        }

        await using DictationCoordinator coordinator = rig.CreateCoordinator();
        var states = new List<DictationState>();
        coordinator.StateChanged += (_, change) => states.Add(change.Current);

        DictationStartResult result =
            await coordinator.StartAsync(DictationMode.Dictation);

        Assert.Equal(DictationStartOutcome.Failed, result.Outcome);
        Assert.Equal(stage, result.Failure?.Stage);
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Contains(DictationState.Faulted, states);
    }

    [Fact]
    public async Task CancelledStart_RecoversToIdle()
    {
        var rig = new TestRig
        {
            CaptureTarget = _ =>
                Task.FromCanceled<ForegroundTarget>(new(canceled: true)),
        };
        await using DictationCoordinator coordinator = rig.CreateCoordinator();

        DictationStartResult result =
            await coordinator.StartAsync(DictationMode.Dictation);

        Assert.Equal(DictationStartOutcome.Cancelled, result.Outcome);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task StopAndCancelWhileIdle_AreIgnored()
    {
        var rig = new TestRig();
        await using DictationCoordinator coordinator = rig.CreateCoordinator();

        Assert.Equal(
            DictationOutcome.IgnoredNotRecording,
            (await coordinator.StopAsync()).Outcome);
        Assert.False(await coordinator.CancelAsync());
    }

    [Fact]
    public async Task ThrowingStateObserver_CannotCorruptWorkflow()
    {
        var rig = new TestRig();
        await using DictationCoordinator coordinator = rig.CreateCoordinator();
        coordinator.StateChanged += (_, _) => throw new InvalidOperationException();

        Assert.Equal(
            DictationStartOutcome.Started,
            (await coordinator.StartAsync(DictationMode.Dictation)).Outcome);
        Assert.Equal(
            DictationOutcome.Completed,
            (await coordinator.StopAsync()).Outcome);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task CancelCaptureFailure_StillRecoversToIdle()
    {
        var rig = new TestRig();
        rig.Session.CancelException = new IOException();
        await using DictationCoordinator coordinator = rig.CreateCoordinator();
        var states = new List<DictationState>();
        coordinator.StateChanged += (_, change) => states.Add(change.Current);

        await coordinator.StartAsync(DictationMode.Dictation);
        Assert.True(await coordinator.CancelAsync());

        Assert.Contains(DictationState.Faulted, states);
        Assert.Equal(DictationState.Idle, coordinator.State);
    }

    [Fact]
    public async Task ConstructorRejectsInvalidDependenciesAndSettings()
    {
        var rig = new TestRig();

        Assert.Throws<ArgumentNullException>(
            () => new DictationCoordinator(
                null!,
                rig.Transcriber,
                rig.Processor,
                rig.Target,
                rig.Insertion,
                DictaCloneSettings.Default));
        Assert.Throws<ArgumentNullException>(
            () => new DictationCoordinator(
                rig.Audio,
                null!,
                rig.Processor,
                rig.Target,
                rig.Insertion,
                DictaCloneSettings.Default));
        Assert.Throws<ArgumentNullException>(
            () => new DictationCoordinator(
                rig.Audio,
                rig.Transcriber,
                null!,
                rig.Target,
                rig.Insertion,
                DictaCloneSettings.Default));
        Assert.Throws<ArgumentNullException>(
            () => new DictationCoordinator(
                rig.Audio,
                rig.Transcriber,
                rig.Processor,
                null!,
                rig.Insertion,
                DictaCloneSettings.Default));
        Assert.Throws<ArgumentNullException>(
            () => new DictationCoordinator(
                rig.Audio,
                rig.Transcriber,
                rig.Processor,
                rig.Target,
                null!,
                DictaCloneSettings.Default));

        DictaCloneSettings invalid = DictaCloneSettings.Default with
        {
            Audio = DictaCloneSettings.Default.Audio with
            {
                SilenceThreshold = -1,
            },
        };
        Assert.Throws<ArgumentException>(
            () => rig.CreateCoordinator(settings: invalid));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task DisposeIsIdempotentAndRejectsFurtherUse()
    {
        var rig = new TestRig();
        DictationCoordinator coordinator = rig.CreateCoordinator();

        await coordinator.StartAsync(DictationMode.Dictation);
        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => coordinator.StartAsync(DictationMode.Dictation));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => coordinator.StopAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => coordinator.CancelAsync());
    }

    private sealed class TestRig
    {
        public static CapturedAudio AudioData { get; } = new(
            new byte[] { 1, 2 },
            16_000,
            1,
            TimeSpan.FromSeconds(1),
            IsSilent: false);

        public TestRig()
        {
            Session = new FakeCaptureSession { Audio = AudioData };
            Audio = new FakeAudioCaptureService(Session);
            Transcriber = new FakeTranscriber(this);
            Processor = new FakeTextProcessor(this);
            Target = new FakeTargetService(this);
            Insertion = new FakeInsertionService(this);
        }

        public FakeCaptureSession Session { get; }

        public FakeAudioCaptureService Audio { get; }

        public FakeTranscriber Transcriber { get; }

        public FakeTextProcessor Processor { get; }

        public FakeTargetService Target { get; }

        public FakeInsertionService Insertion { get; }

        public Func<CapturedAudio, TranscriptionSettings, CancellationToken, Task<string>>
            Transcribe
        { get; set; } =
                (_, _, _) => Task.FromResult("raw transcript");

        public Func<string, DictationMode, TextProcessingSettings, CancellationToken, Task<string>>
            Process
        { get; set; } =
                (_, _, _, _) => Task.FromResult("Clean text.");

        public Func<CancellationToken, Task<ForegroundTarget>> CaptureTarget
        { get; init; } =
                _ => Task.FromResult(new ForegroundTarget("1", "notepad", "Edit"));

        public Func<string, ForegroundTarget, InsertionSettings, CancellationToken, Task>
            Insert
        { get; init; } =
                (_, _, _, _) => Task.CompletedTask;

        public DictationCoordinator CreateCoordinator(
            IAudioCaptureService? audio = default,
            ITranscriptionEngine? transcriber = default,
            ITextProcessor? processor = default,
            IForegroundTargetService? target = default,
            ITextInsertionService? insertion = default,
            DictaCloneSettings? settings = default)
        {
            return new(
                audio ?? Audio,
                transcriber ?? Transcriber,
                processor ?? Processor,
                target ?? Target,
                insertion ?? Insertion,
                settings ?? DictaCloneSettings.Default);
        }
    }

    private sealed class FakeAudioCaptureService(
        FakeCaptureSession session) : IAudioCaptureService
    {
        public int StartCount { get; private set; }

        public AudioSettings? LastSettings { get; private set; }

        public Exception? StartException { get; set; }

        public Task<IAudioCaptureSession> StartAsync(
            AudioSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            LastSettings = settings;

            return StartException is not null
                ? Task.FromException<IAudioCaptureSession>(StartException)
                : Task.FromResult<IAudioCaptureSession>(session);
        }
    }

    private sealed class FakeCaptureSession : IAudioCaptureSession
    {
        public required CapturedAudio Audio { get; set; }

        public Exception? StopException { get; set; }

        public Exception? CancelException { get; set; }

        public bool Cancelled { get; private set; }

        public bool Disposed { get; private set; }

        public Task<CapturedAudio> StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StopException is not null
                ? Task.FromException<CapturedAudio>(StopException)
                : Task.FromResult(Audio);
        }

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Cancelled = true;
            return CancelException is not null
                ? Task.FromException(CancelException)
                : Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTranscriber(TestRig rig) : ITranscriptionEngine
    {
        public int CallCount { get; private set; }

        public TranscriptionSettings? LastSettings { get; private set; }

        public Task<string> TranscribeAsync(
            CapturedAudio audio,
            TranscriptionSettings settings,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSettings = settings;
            return rig.Transcribe(audio, settings, cancellationToken);
        }
    }

    private sealed class FakeTextProcessor(TestRig rig) : ITextProcessor
    {
        public int CallCount { get; private set; }

        public Task<string> ProcessAsync(
            string transcript,
            DictationMode mode,
            TextProcessingSettings settings,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return rig.Process(transcript, mode, settings, cancellationToken);
        }
    }

    private sealed class FakeTargetService(TestRig rig) : IForegroundTargetService
    {
        public bool IsCurrent { get; set; } = true;

        public Exception? CaptureException { get; set; }

        public Task<ForegroundTarget> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CaptureException is not null
                ? Task.FromException<ForegroundTarget>(CaptureException)
                : rig.CaptureTarget(cancellationToken);
        }

        public Task<bool> IsCurrentAsync(
            ForegroundTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IsCurrent);
        }
    }

    private sealed class FakeInsertionService(TestRig rig) : ITextInsertionService
    {
        public List<string> InsertedTexts { get; } = [];

        public async Task InsertAsync(
            string text,
            ForegroundTarget target,
            InsertionSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await rig.Insert(text, target, settings, cancellationToken);
            InsertedTexts.Add(text);
        }
    }
}
