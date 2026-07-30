using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;

namespace DictaClone.Core.Workflow;

public sealed class DictationCoordinator : IAsyncDisposable
{
    private readonly IAudioCaptureService _audioCapture;
    private readonly ITranscriptionEngine _transcription;
    private readonly ITextProcessor _textProcessor;
    private readonly IForegroundTargetService _foregroundTarget;
    private readonly ITextInsertionService _textInsertion;
    private readonly DictaCloneSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveOperation? _activeOperation;
    private int _state = (int)DictationState.Idle;
    private bool _disposed;

    public DictationCoordinator(
        IAudioCaptureService audioCapture,
        ITranscriptionEngine transcription,
        ITextProcessor textProcessor,
        IForegroundTargetService foregroundTarget,
        ITextInsertionService textInsertion,
        DictaCloneSettings settings)
    {
        _audioCapture = audioCapture ??
            throw new ArgumentNullException(nameof(audioCapture));
        _transcription = transcription ??
            throw new ArgumentNullException(nameof(transcription));
        _textProcessor = textProcessor ??
            throw new ArgumentNullException(nameof(textProcessor));
        _foregroundTarget = foregroundTarget ??
            throw new ArgumentNullException(nameof(foregroundTarget));
        _textInsertion = textInsertion ??
            throw new ArgumentNullException(nameof(textInsertion));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var validationErrors = SettingsValidator.Validate(settings);
        if (validationErrors.Length > 0)
        {
            throw new ArgumentException(
                $"Settings are invalid: {validationErrors[0].Message}",
                nameof(settings));
        }
    }

    public event EventHandler<DictationStateChangedEvent>? StateChanged;

    public DictationState State => (DictationState)Volatile.Read(ref _state);

    public async Task<DictationStartResult> StartAsync(
        DictationMode mode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_activeOperation is not null)
            {
                return new(DictationStartOutcome.IgnoredAlreadyActive);
            }

            SetState(DictationState.Recording);
            ForegroundTarget target;

            try
            {
                target = await _foregroundTarget
                    .CaptureAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                SetState(DictationState.Cancelled);
                SetState(DictationState.Idle);
                return new(DictationStartOutcome.Cancelled);
            }
            catch (Exception exception)
            {
                return FailStart(
                    DictationFailureStage.ForegroundTarget,
                    exception);
            }

            try
            {
                IAudioCaptureSession session = await _audioCapture
                    .StartAsync(_settings.Audio, cancellationToken)
                    .ConfigureAwait(false);
                _activeOperation = new(
                    mode,
                    target,
                    session,
                    new CancellationTokenSource());
                return new(DictationStartOutcome.Started);
            }
            catch (OperationCanceledException)
            {
                SetState(DictationState.Cancelled);
                SetState(DictationState.Idle);
                return new(DictationStartOutcome.Cancelled);
            }
            catch (Exception exception)
            {
                return FailStart(DictationFailureStage.AudioCapture, exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DictationResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task<DictationResult> completion;
        CancellationTokenSource operationCancellation;

        try
        {
            if (_activeOperation is not { } operation ||
                State != DictationState.Recording)
            {
                return new(DictationOutcome.IgnoredNotRecording);
            }

            SetState(DictationState.Transcribing);
            completion = ProcessAsync(operation);
            operation.Completion = completion;
            operationCancellation = operation.Cancellation;
        }
        finally
        {
            _gate.Release();
        }

        using CancellationTokenRegistration registration =
            cancellationToken.Register(
                static state => ((CancellationTokenSource)state!).Cancel(),
                operationCancellation);

        return await completion.ConfigureAwait(false);
    }

    public async Task<bool> CancelAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync().ConfigureAwait(false);
        ActiveOperation? operation;
        Task<DictationResult>? completion;
        bool cancelCapture;

        try
        {
            operation = _activeOperation;
            if (operation is null)
            {
                return false;
            }

            cancelCapture = State == DictationState.Recording;
            completion = operation.Completion;
            operation.Cancellation.Cancel();
            SetState(DictationState.Cancelled);

            if (cancelCapture)
            {
                _activeOperation = null;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (cancelCapture)
        {
            await CancelCaptureAsync(operation!).ConfigureAwait(false);
        }
        else if (completion is not null)
        {
            await completion.ConfigureAwait(false);
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CancelAsync().ConfigureAwait(false);
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<DictationResult> ProcessAsync(ActiveOperation operation)
    {
        DictationFailureStage stage = DictationFailureStage.AudioCapture;

        try
        {
            CancellationToken cancellationToken = operation.Cancellation.Token;
            CapturedAudio audio = await operation.Session
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (audio.IsSilent || audio.Pcm16.IsEmpty)
            {
                return new(DictationOutcome.NoSpeech);
            }

            stage = DictationFailureStage.Transcription;
            string transcript = await _transcription
                .TranscribeAsync(audio, _settings.Transcription, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(transcript))
            {
                return new(DictationOutcome.NoSpeech);
            }

            SetState(DictationState.Cleaning);
            stage = DictationFailureStage.TextProcessing;
            string finalText = await _textProcessor
                .ProcessAsync(
                    transcript,
                    operation.Mode,
                    _settings.Text,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(finalText))
            {
                return new(DictationOutcome.NoSpeech);
            }

            stage = DictationFailureStage.TextInsertion;
            bool targetIsCurrent = await _foregroundTarget
                .IsCurrentAsync(operation.Target, cancellationToken)
                .ConfigureAwait(false);
            if (!targetIsCurrent)
            {
                throw new ForegroundTargetChangedException();
            }

            SetState(DictationState.Inserting);
            await _textInsertion
                .InsertAsync(
                    finalText,
                    operation.Target,
                    _settings.Insertion,
                    cancellationToken)
                .ConfigureAwait(false);

            return new(DictationOutcome.Completed, finalText);
        }
        catch (OperationCanceledException)
            when (operation.Cancellation.IsCancellationRequested)
        {
            SetState(DictationState.Cancelled);
            return new(DictationOutcome.Cancelled);
        }
        catch (Exception exception)
        {
            SetState(DictationState.Faulted);
            return new(
                DictationOutcome.Failed,
                Failure: CreateFailure(stage, exception));
        }
        finally
        {
            await DisposeSessionSafelyAsync(operation.Session).ConfigureAwait(false);
            operation.Cancellation.Dispose();
            await _gate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (ReferenceEquals(_activeOperation, operation))
                {
                    _activeOperation = null;
                }

                SetState(DictationState.Idle);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private async Task CancelCaptureAsync(ActiveOperation operation)
    {
        try
        {
            await operation.Session
                .CancelAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            SetState(DictationState.Faulted);
        }
        finally
        {
            await DisposeSessionSafelyAsync(operation.Session).ConfigureAwait(false);
            operation.Cancellation.Dispose();
            SetState(DictationState.Idle);
        }
    }

    private async Task DisposeSessionSafelyAsync(IAudioCaptureSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            SetState(DictationState.Faulted);
        }
    }

    private DictationStartResult FailStart(
        DictationFailureStage stage,
        Exception exception)
    {
        SetState(DictationState.Faulted);
        SetState(DictationState.Idle);
        return new(
            DictationStartOutcome.Failed,
            CreateFailure(stage, exception));
    }

    private static DictationFailure CreateFailure(
        DictationFailureStage stage,
        Exception exception) =>
        new(stage, exception.GetType().Name);

    private void SetState(DictationState state)
    {
        var previous = (DictationState)Interlocked.Exchange(
            ref _state,
            (int)state);
        if (previous != state)
        {
            NotifyStateChanged(new(previous, state));
        }
    }

    private void NotifyStateChanged(DictationStateChangedEvent change)
    {
        Delegate[] handlers = StateChanged?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler<DictationStateChangedEvent>)handler)(this, change);
            }
            catch (Exception)
            {
                // UI observers cannot be allowed to corrupt the workflow state.
            }
        }
    }

    private sealed class ActiveOperation(
        DictationMode mode,
        ForegroundTarget target,
        IAudioCaptureSession session,
        CancellationTokenSource cancellation)
    {
        public DictationMode Mode { get; } = mode;

        public ForegroundTarget Target { get; } = target;

        public IAudioCaptureSession Session { get; } = session;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task<DictationResult>? Completion { get; set; }
    }

    private sealed class ForegroundTargetChangedException : InvalidOperationException;
}
