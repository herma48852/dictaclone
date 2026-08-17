using System.Net.Http;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Speech;

namespace DictaClone.App.Presentation;

public sealed class LiveDictationController : IAsyncDisposable
{
    private const int MaximumDisplayedTranscriptLength = 240;

    private readonly IAudioCaptureService _audioCapture;
    private readonly ITranscriptionEngine _transcription;
    private readonly ITextProcessor _textProcessor;
    private readonly IForegroundTargetService _foregroundTarget;
    private readonly ITextInsertionService _textInsertion;
    private readonly ISmartEditProvider _smartEdit;
    private readonly ISelectedTextService _selectedText;
    private readonly IStatusOverlay _overlay;
    private readonly Action<Action> _postToUi;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DictaCloneSettings _settings;
    private IAudioCaptureSession? _captureSession;
    private ForegroundTarget? _capturedTarget;
    private SelectedTextSnapshot? _capturedSelection;
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource? _operationCompletion;
    private HotkeyAction? _activeAction;
    private bool _processing;
    private bool _disposed;

    public LiveDictationController(
        IAudioCaptureService audioCapture,
        ITranscriptionEngine transcription,
        ITextProcessor textProcessor,
        IForegroundTargetService foregroundTarget,
        ITextInsertionService textInsertion,
        IStatusOverlay overlay,
        DictaCloneSettings settings,
        Action<Action>? postToUi = null,
        ISmartEditProvider? smartEdit = null,
        ISelectedTextService? selectedText = null)
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
        _smartEdit = smartEdit ?? new DisabledSmartEditProvider();
        _selectedText = selectedText ?? new NoSelectedTextService();
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _postToUi = postToUi ?? (action => action());

        if (_transcription is IModelProgressSource progressSource)
        {
            progressSource.ModelProgressChanged += ModelProgressChanged;
        }
    }

    public event EventHandler<TranscriptionCompletedEventArgs>?
        TranscriptionCompleted;

    public event EventHandler<TranscriptAvailableEventArgs>?
        TranscriptAvailable;

    public string? LastTranscript { get; private set; }

    public async Task HandleAsync(HotkeyEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (inputEvent.Action == HotkeyAction.Cancel &&
            inputEvent.Kind == HotkeyEventKind.Pressed)
        {
            await CancelAsync().ConfigureAwait(false);
            return;
        }

        if (inputEvent.Kind == HotkeyEventKind.Pressed)
        {
            await StartAsync(inputEvent.Action).ConfigureAwait(false);
        }
        else
        {
            await StopAndTranscribeAsync(inputEvent.Action)
                .ConfigureAwait(false);
        }
    }

    public async Task UpdateSettingsAsync(DictaCloneSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_captureSession is not null || _processing)
            {
                throw new InvalidOperationException(
                    "Settings cannot change during an active dictation.");
            }

            _settings = settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAsync()
    {
        IAudioCaptureSession? session;
        CancellationTokenSource? cancellation;
        Task? completion;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed && _captureSession is null && !_processing)
            {
                return;
            }

            session = _captureSession;
            cancellation = _operationCancellation;
            completion = _operationCompletion?.Task;
            _captureSession = null;
            _capturedTarget = null;
            _capturedSelection = null;
            _activeAction = null;
            cancellation?.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        if (session is not null)
        {
            UnsubscribeLevel(session);
            try
            {
                await session.CancelAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
                cancellation?.Dispose();
                await ClearOperationAsync(cancellation).ConfigureAwait(false);
            }
        }
        else if (completion is not null)
        {
            await completion.ConfigureAwait(false);
        }

        Post(() => _overlay.ShowStatus(
            OverlayStatus.Failure,
            "Dictation cancelled"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_transcription is IModelProgressSource progressSource)
        {
            progressSource.ModelProgressChanged -= ModelProgressChanged;
        }

        await CancelAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task StartAsync(HotkeyAction action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_captureSession is not null || _processing)
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "DictaClone is busy"));
                return;
            }

            if (action == HotkeyAction.SmartEdit && !_settings.SmartEdit.Enabled)
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Smart Edit is off; configure it in Smart Edit settings"));
                return;
            }

            var cancellation = new CancellationTokenSource();
            try
            {
                ForegroundTarget target = await _foregroundTarget
                    .CaptureAsync(cancellation.Token)
                    .ConfigureAwait(false);
                SelectedTextSnapshot? selection = action == HotkeyAction.SmartEdit
                    ? await _selectedText
                        .CaptureAsync(target, cancellation.Token)
                        .ConfigureAwait(false)
                    : null;
                IAudioCaptureSession session = await _audioCapture
                    .StartAsync(_settings.Audio, cancellation.Token)
                    .ConfigureAwait(false);
                _captureSession = session;
                _capturedTarget = target;
                _capturedSelection = selection;
                _operationCancellation = cancellation;
                _operationCompletion = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _activeAction = action;
                SubscribeLevel(session);
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Recording,
                    GetRecordingLabel(action)));
            }
            catch (Exception exception)
            {
                cancellation.Dispose();
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    GetFailureLabel(exception)));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopAndTranscribeAsync(HotkeyAction action)
    {
        IAudioCaptureSession? session;
        ForegroundTarget? target;
        SelectedTextSnapshot? selection;
        CancellationTokenSource? cancellation;
        DictaCloneSettings settings;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_captureSession is null || _activeAction != action)
            {
                return;
            }

            session = _captureSession;
            target = _capturedTarget;
            selection = _capturedSelection;
            cancellation = _operationCancellation;
            settings = _settings;
            _captureSession = null;
            _capturedTarget = null;
            _capturedSelection = null;
            _activeAction = null;
            _processing = true;
            UnsubscribeLevel(session);
        }
        finally
        {
            _gate.Release();
        }

        Post(() => _overlay.ShowStatus(
            OverlayStatus.Processing,
            "Finishing microphone…"));

        try
        {
            CancellationToken token = cancellation!.Token;
            CapturedAudio audio = await session!
                .StopAsync(token)
                .ConfigureAwait(false);
            if (audio.Pcm16.IsEmpty)
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "No microphone audio was captured; check the selected microphone"));
                return;
            }

            if (audio.IsSilent)
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Speech was too quiet; speak closer or lower the silence threshold"));
                return;
            }

            Post(() => _overlay.ShowStatus(
                OverlayStatus.Processing,
                "Transcribing locally…"));

            TranscriptionSettings transcriptionSettings =
                settings.Transcription with
                {
                    InitialPrompt =
                        settings.Transcription.InitialPrompt ??
                        WhisperPromptBuilder.FromVocabulary(
                            settings.Text.Vocabulary,
                            settings.Text.WorkDomain),
                };
            string transcript = await _transcription
                .TranscribeAsync(audio, transcriptionSettings, token)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Speech was captured but not recognized; try speaking clearly"));
                return;
            }

            string instruction = await _textProcessor
                .ProcessAsync(
                    transcript,
                    MapMode(action),
                    settings.Text,
                    token)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(instruction))
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Transcription was removed by text processing; review text settings"));
                return;
            }

            string finalText = instruction;
            if (action == HotkeyAction.SmartEdit)
            {
                Post(() => _overlay.ShowStatus(
                    OverlayStatus.Processing,
                    "Applying Smart Edit securely…"));
                finalText = await _smartEdit.EditAsync(
                    new SmartEditRequest(
                        instruction,
                        selection?.Text,
                        target!.ProcessName,
                        target.WindowClass,
                        settings.Text,
                        settings.SmartEdit),
                    token).ConfigureAwait(false);
            }

            LastTranscript = finalText;
            Post(() => PublishAvailable(finalText));

            bool targetIsCurrent = await _foregroundTarget
                .IsCurrentAsync(target!, token)
                .ConfigureAwait(false);
            if (!targetIsCurrent)
            {
                throw new ForegroundTargetChangedException();
            }

            if (selection is not null &&
                !await _selectedText
                    .RevalidateAsync(selection, target!, token)
                    .ConfigureAwait(false))
            {
                throw new SelectionChangedException();
            }

            Post(() => _overlay.ShowStatus(
                OverlayStatus.Processing,
                "Inserting text…"));
            InsertionSettings insertionSettings = action ==
                HotkeyAction.TypingMode
                    ? settings.Insertion with
                    {
                        Mode = TextInsertionMode.DelayedTyping,
                    }
                    : settings.Insertion;
            await _textInsertion
                .InsertAsync(finalText, target!, insertionSettings, token)
                .ConfigureAwait(false);

            Post(() =>
            {
                _overlay.ShowStatus(
                    OverlayStatus.Success,
                    TruncateForDisplay(finalText));
                PublishCompleted(finalText);
            });
        }
        catch (OperationCanceledException)
            when (cancellation?.IsCancellationRequested == true)
        {
            Post(() => _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Dictation cancelled"));
        }
        catch (Exception exception)
        {
            Post(() => _overlay.ShowStatus(
                OverlayStatus.Failure,
                GetFailureLabel(exception)));
        }
        finally
        {
            await session!.DisposeAsync().ConfigureAwait(false);
            cancellation!.Dispose();
            await ClearOperationAsync(cancellation).ConfigureAwait(false);
        }
    }

    private async Task ClearOperationAsync(
        CancellationTokenSource? cancellation)
    {
        TaskCompletionSource? completion = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
                completion = _operationCompletion;
                _operationCompletion = null;
                _processing = false;
            }
        }
        finally
        {
            _gate.Release();
        }

        completion?.TrySetResult();
    }

    private void SubscribeLevel(IAudioCaptureSession session)
    {
        if (session is IAudioLevelSource levelSource)
        {
            levelSource.LevelChanged += AudioLevelChanged;
        }
    }

    private void UnsubscribeLevel(IAudioCaptureSession session)
    {
        if (session is IAudioLevelSource levelSource)
        {
            levelSource.LevelChanged -= AudioLevelChanged;
        }
    }

    private void AudioLevelChanged(
        object? sender,
        AudioLevelChangedEvent eventArgs)
    {
        double level = Math.Clamp(
            Math.Max(eventArgs.Peak, eventArgs.RootMeanSquare),
            0,
            1);
        Post(() => _overlay.UpdateLevel(level));
    }

    private void ModelProgressChanged(
        object? sender,
        ModelDownloadProgressEventArgs eventArgs)
    {
        string message = eventArgs.Stage switch
        {
            ModelDownloadStage.Checking => "Checking local speech model…",
            ModelDownloadStage.Downloading =>
                $"Downloading {eventArgs.ModelName}: {eventArgs.Fraction:P0}",
            ModelDownloadStage.Verifying => "Verifying local speech model…",
            ModelDownloadStage.Ready => "Transcribing locally…",
            _ => "Preparing local speech model…",
        };
        Post(() => _overlay.ShowStatus(OverlayStatus.Processing, message));
    }

    private void PublishCompleted(string transcript)
    {
        Delegate[] handlers =
            TranscriptionCompleted?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler<TranscriptionCompletedEventArgs>)handler)(
                    this,
                    new(transcript));
            }
            catch (Exception)
            {
                // Result observers cannot invalidate a completed dictation.
            }
        }
    }

    private void PublishAvailable(string transcript)
    {
        Delegate[] handlers = TranscriptAvailable?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler<TranscriptAvailableEventArgs>)handler)(
                    this,
                    new(transcript));
            }
            catch (Exception)
            {
                // Recovery observers cannot invalidate a completed transcript.
            }
        }
    }

    private void Post(Action action)
    {
        try
        {
            _postToUi(action);
        }
        catch (Exception)
        {
            // UI shutdown can race a final capture callback.
        }
    }

    private static DictationMode MapMode(HotkeyAction action) => action switch
    {
        HotkeyAction.Dictation => DictationMode.Dictation,
        HotkeyAction.SmartEdit => DictationMode.SmartEdit,
        HotkeyAction.TypingMode => DictationMode.Typing,
        _ => DictationMode.Dictation,
    };

    private static string GetRecordingLabel(HotkeyAction action) =>
        action switch
        {
            HotkeyAction.SmartEdit => "●  Smart Edit listening…",
            HotkeyAction.TypingMode => "●  Typing Mode listening…",
            _ => "●  Listening…",
        };

    private static string GetFailureLabel(Exception exception) =>
        exception switch
        {
            AudioCaptureException =>
                "Microphone unavailable; check the selected device and microphone privacy settings",
            PlatformPermissionException permission =>
                $"{permission.Permission} permission is required; open privacy settings to enable it",
            ModelIntegrityException => "Speech model verification failed",
            HttpRequestException => "Speech model is unavailable offline",
            ForegroundTargetUnavailableException =>
                "No focused app is available for insertion",
            ForegroundTargetChangedException =>
                "Focus changed; text was not inserted",
            SelectionChangedException =>
                "Selection changed; Smart Edit result was not inserted (use Copy last result)",
            ElevatedTargetException =>
                "The operating system blocks input to this app",
            ClipboardContentionException =>
                "Clipboard is busy; text was not inserted",
            InputInjectionException =>
                "The operating system blocked text insertion",
            SmartEditNotConfiguredException =>
                "Smart Edit needs an API key in Smart Edit settings",
            SmartEditAuthenticationException =>
                "Smart Edit API key was rejected; update it in settings",
            SmartEditRateLimitException =>
                "Smart Edit rate limit reached; wait and try again",
            SmartEditTimeoutException =>
                "Smart Edit timed out; the selected text was not changed",
            SmartEditUnavailableException =>
                "Smart Edit provider is unavailable; check the network and endpoint",
            SmartEditResponseException =>
                "Smart Edit returned an invalid response; the selected text was not changed",
            SmartEditRequestTooLargeException =>
                "Smart Edit selection is too large; shorten it and try again",
            _ => $"Dictation failed ({exception.GetType().Name})",
        };

    private static string TruncateForDisplay(string transcript) =>
        transcript.Length <= MaximumDisplayedTranscriptLength
            ? transcript
            : transcript[..(MaximumDisplayedTranscriptLength - 1)] + "…";
}

internal sealed class DisabledSmartEditProvider : ISmartEditProvider
{
    public Task<string> EditAsync(
        SmartEditRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<string>(new SmartEditNotConfiguredException());
}

internal sealed class NoSelectedTextService : ISelectedTextService
{
    public Task<SelectedTextSnapshot?> CaptureAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken) =>
        Task.FromResult<SelectedTextSnapshot?>(null);

    public Task<bool> RevalidateAsync(
        SelectedTextSnapshot snapshot,
        ForegroundTarget target,
        CancellationToken cancellationToken) => Task.FromResult(false);
}

public sealed class TranscriptionCompletedEventArgs(string transcript)
    : EventArgs
{
    public string Transcript { get; } = transcript;
}

public sealed class TranscriptAvailableEventArgs(string transcript)
    : EventArgs
{
    public string Transcript { get; } = transcript;
}
