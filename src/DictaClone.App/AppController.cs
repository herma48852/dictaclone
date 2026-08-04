using System.Collections.Immutable;
using System.IO;
using System.Windows;
using DictaClone.App.Presentation;
using DictaClone.Audio;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Infrastructure;
using DictaClone.Speech;
using DictaClone.Text;
using DictaClone.Windows;
using DictaClone.Windows.Input;
using WpfApplication = System.Windows.Application;

namespace DictaClone.App;

public sealed class AppController : IAsyncDisposable
{
    private readonly WpfApplication _application;
    private readonly LowLevelHotkeySource _hotkeys;
    private readonly StatusOverlayWindow _overlay;
    private readonly WhisperTranscriptionEngine _transcription;
    private readonly LiveDictationController _dictationUi;
    private readonly TrayIconService _trayIcon;
    private readonly JsonSettingsStore _settingsStore;
    private readonly SettingsTransferService _settingsTransfer;
    private readonly JsonTranscriptHistoryStore _historyStore;
    private readonly TranscriptHistoryRecorder _historyRecorder;
    private readonly PrivacySafeDiagnosticLog _diagnostics;
    private readonly PrivacySafeSupportBundleService _supportBundles;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly SemaphoreSlim _settingsApplyGate = new(1, 1);
    private ImmutableArray<HotkeyBinding> _bindings;
    private DictaCloneSettings _settings;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private readonly bool _enableModelWarmup;
    private readonly bool _enablePersistentState;
    private readonly bool _enableFirstRunUi;
    private readonly bool _enableSystemIntegration;
    private bool _disposed;

    public AppController(
        WpfApplication application,
        bool enableModelWarmup = true,
        bool enablePersistentState = true,
        bool enableFirstRunUi = true,
        bool enableSystemIntegration = true)
    {
        _application = application ??
            throw new ArgumentNullException(nameof(application));
        _enableModelWarmup = enableModelWarmup;
        _enablePersistentState = enablePersistentState;
        _enableFirstRunUi = enableFirstRunUi;
        _enableSystemIntegration = enableSystemIntegration;
        _settings = DictaCloneSettings.Default;
        _bindings = _settings.Hotkeys;
        _hotkeys = new();
        _overlay = new();
        var modelManager = new WhisperModelManager(
            WhisperModelStorage.ResolveDefaultDirectory());
        _transcription = new(modelManager, ownsModelManager: true);
        _dictationUi = new(
            new WasapiAudioCaptureService(),
            _transcription,
            new DeterministicTextProcessor(),
            new ForegroundTargetService(),
            new TextInsertionService(),
            _overlay,
            _settings,
            PostToUi);
        _trayIcon = new();
        DictaCloneDataPaths paths = DictaCloneDataPaths.Default;
        _settingsStore = new(paths);
        _settingsTransfer = new();
        _historyStore = new(paths);
        _historyRecorder = new(_historyStore);
        _diagnostics = new(paths);
        _supportBundles = new(paths);
        _startupRegistration = new();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        ObjectDisposedException.ThrowIf(_disposed, this);

        SettingsLoadResult? settingsLoad = null;
        await WriteDiagnosticSafeAsync(
            DiagnosticEventKind.ApplicationStartup,
            DiagnosticOutcome.Started,
            cancellationToken: cancellationToken).ConfigureAwait(true);
        if (_enablePersistentState)
        {
            settingsLoad = await _settingsStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(true);
            _settings = settingsLoad.Settings;
            _bindings = _settings.Hotkeys;
            await _dictationUi
                .UpdateSettingsAsync(_settings)
                .ConfigureAwait(true);
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsLoad,
                settingsLoad.QuarantinedFilePath is null
                    ? DiagnosticOutcome.Succeeded
                    : DiagnosticOutcome.Recovered,
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        if (_enableSystemIntegration)
        {
            try
            {
                _startupRegistration.SetEnabled(
                    _settings.Preferences.StartWithWindows);
            }
            catch (Exception exception)
            {
                await WriteDiagnosticSafeAsync(
                    DiagnosticEventKind.SettingsLoad,
                    DiagnosticOutcome.Failed,
                    exception: exception,
                    cancellationToken: cancellationToken).ConfigureAwait(true);
            }
        }

        _hotkeys.Triggered += HotkeysTriggered;
        _trayIcon.SettingsRequested += SettingsRequested;
        _trayIcon.ExitRequested += ExitRequested;
        _trayIcon.CopyLastRequested += CopyLastRequested;
        _trayIcon.HistoryRequested += HistoryRequested;
        _dictationUi.TranscriptAvailable += TranscriptAvailable;
        await _hotkeys
            .StartAsync(_bindings, cancellationToken)
            .ConfigureAwait(true);

        bool warmed = !_enableModelWarmup;
        if (_enableModelWarmup)
        {
            try
            {
                _overlay.ShowStatus(
                    OverlayStatus.Processing,
                    "Preparing local speech model…");
                warmed = await _transcription
                    .WarmUpIfAvailableAsync(
                        _settings.Transcription,
                        cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception)
            {
                _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Speech model warm-up failed");
            }
        }

        _overlay.ShowStatus(
            OverlayStatus.Success,
            warmed
                ? "✓  DictaClone is ready"
                : "✓  Ready; model prepares on first use");
        _trayIcon.ShowNotification(
            "DictaClone is running",
            warmed
                ? "Hold Ctrl+Win+Space to start local dictation."
                : "Hold Ctrl+Win+Space; the local model will be prepared on first use.");

        if (settingsLoad?.QuarantinedFilePath is not null)
        {
            _trayIcon.ShowNotification(
                "DictaClone recovered safe settings",
                "A corrupt settings file was quarantined. Review your settings.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }

        if (_enableFirstRunUi && !_settings.Preferences.FirstRunCompleted)
        {
            ShowSettings();
        }

        await WriteDiagnosticSafeAsync(
            DiagnosticEventKind.ApplicationStartup,
            DiagnosticOutcome.Succeeded,
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeys.Triggered -= HotkeysTriggered;
        _trayIcon.SettingsRequested -= SettingsRequested;
        _trayIcon.ExitRequested -= ExitRequested;
        _trayIcon.CopyLastRequested -= CopyLastRequested;
        _trayIcon.HistoryRequested -= HistoryRequested;
        _dictationUi.TranscriptAvailable -= TranscriptAvailable;

        await WriteDiagnosticSafeAsync(
            DiagnosticEventKind.ApplicationShutdown,
            DiagnosticOutcome.Started).ConfigureAwait(true);

        try
        {
            await _hotkeys.DisposeAsync().ConfigureAwait(true);
        }
        finally
        {
            try
            {
                await _dictationUi.DisposeAsync().ConfigureAwait(true);
            }
            finally
            {
                await _transcription.DisposeAsync().ConfigureAwait(true);
                _settingsWindow?.Close();
                _settingsWindow = null;
                _historyWindow?.Close();
                _historyWindow = null;
                _overlay.Close();
                _trayIcon.Dispose();
                await WriteDiagnosticSafeAsync(
                    DiagnosticEventKind.ApplicationShutdown,
                    DiagnosticOutcome.Succeeded).ConfigureAwait(true);
                _historyStore.Dispose();
                _settingsStore.Dispose();
                _diagnostics.Dispose();
                _settingsApplyGate.Dispose();
            }
        }
    }

    private async void HotkeysTriggered(object? sender, HotkeyEvent inputEvent)
    {
        try
        {
            await _dictationUi.HandleAsync(inputEvent);
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "!  Shortcut UI failed");
        }
    }

    private void SettingsRequested(object? sender, EventArgs eventArgs)
    {
        _ = _application.Dispatcher.BeginInvoke(ShowSettings);
    }

    private void ExitRequested(object? sender, EventArgs eventArgs)
    {
        if (_application.Dispatcher.CheckAccess())
        {
            _ = ShutdownAsync();
            return;
        }

        _ = _application.Dispatcher.BeginInvoke(
            () => _ = ShutdownAsync());
    }

    private void CopyLastRequested(object? sender, EventArgs eventArgs)
    {
        _ = _application.Dispatcher.BeginInvoke(CopyLastTranscript);
    }

    private void HistoryRequested(object? sender, EventArgs eventArgs)
    {
        _ = _application.Dispatcher.BeginInvoke(
            () => _ = ShowHistoryAsync());
    }

    private async void TranscriptAvailable(
        object? sender,
        TranscriptAvailableEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        _trayIcon.SetCopyLastAvailable(available: true);
        try
        {
            bool recorded = await _historyRecorder.RecordIfEnabledAsync(
                eventArgs.Transcript,
                _settings.Preferences,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            if (!recorded)
            {
                return;
            }

            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.HistoryWrite,
                DiagnosticOutcome.Succeeded);
            if (_historyWindow is not null)
            {
                await RefreshHistoryWindowAsync();
            }
        }
        catch (Exception exception)
        {
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.HistoryWrite,
                DiagnosticOutcome.Failed,
                exception: exception);
        }
    }

    private void CopyLastTranscript()
    {
        string? transcript = _dictationUi.LastTranscript;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "No transcript is available to copy");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(transcript);
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Last result copied");
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Clipboard is busy; copy the last result again");
        }
    }

    private async Task ShowHistoryAsync()
    {
        if (!_settings.Preferences.HistoryEnabled)
        {
            _trayIcon.ShowNotification(
                "Transcript history is off",
                "Enable local transcript history in Privacy & recovery settings.");
            ShowSettings();
            return;
        }

        if (_historyWindow is null)
        {
            HistoryLoadResult loaded = await _historyStore.LoadAsync(
                CancellationToken.None);
            _historyWindow = new(loaded.Entries);
            _historyWindow.CopyRequested += HistoryCopyRequested;
            _historyWindow.ClearRequested += HistoryClearRequested;
            _historyWindow.Closed += (_, _) => _historyWindow = null;
            _historyWindow.Show();
            if (loaded.QuarantinedFilePath is not null)
            {
                _trayIcon.ShowNotification(
                    "Transcript history recovered",
                    "A corrupt history file was quarantined.",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }

            return;
        }

        if (_historyWindow.WindowState == WindowState.Minimized)
        {
            _historyWindow.WindowState = WindowState.Normal;
        }

        _historyWindow.Activate();
    }

    private void HistoryCopyRequested(
        object? sender,
        HistoryCopyRequestedEventArgs eventArgs)
    {
        try
        {
            System.Windows.Clipboard.SetText(eventArgs.Entry.Text);
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  History entry copied");
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Clipboard is busy; try copying again");
        }
    }

    private async void HistoryClearRequested(
        object? sender,
        EventArgs eventArgs)
    {
        MessageBoxResult result = System.Windows.MessageBox.Show(
            _historyWindow,
            "Permanently delete all locally saved transcript history?",
            "Clear DictaClone history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _historyStore.ClearAsync(CancellationToken.None);
            _historyWindow?.ReplaceEntries([]);
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Transcript history cleared");
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Transcript history could not be cleared");
        }
    }

    private async Task RefreshHistoryWindowAsync()
    {
        HistoryLoadResult loaded = await _historyStore.LoadAsync(
            CancellationToken.None);
        _historyWindow?.ReplaceEntries(loaded.Entries);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            IReadOnlyList<MicrophoneDeviceInfo> devices;
            try
            {
                devices = WasapiAudioDeviceService.GetActiveCaptureDevices();
            }
            catch (Exception)
            {
                devices = [];
            }

            _settingsWindow = new(
                _bindings,
                _settings.Audio,
                _settings.Transcription,
                devices,
                _settings.Insertion,
                _settings.Text,
                _settings.Preferences,
                firstRun: !_settings.Preferences.FirstRunCompleted);
            _settingsWindow.BindingsChanged += BindingsChanged;
            _settingsWindow.AudioSpeechSettingsChanged +=
                AudioSpeechSettingsChanged;
            _settingsWindow.TextSettingsChanged += TextSettingsChanged;
            _settingsWindow.PreferencesChanged += PreferencesChanged;
            _settingsWindow.SettingsImportRequested += SettingsImportRequested;
            _settingsWindow.SettingsExportRequested += SettingsExportRequested;
            _settingsWindow.SupportBundleRequested += SupportBundleRequested;
            _settingsWindow.MicrophonePermissionHelpRequested +=
                MicrophonePermissionHelpRequested;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            return;
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private async void BindingsChanged(
        object? sender,
        HotkeyBindingsChanged eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        _ = await ApplySettingsAsync(
            _settings with { Hotkeys = eventArgs.Bindings },
            "✓  Shortcuts saved");
    }

    private async Task ShutdownAsync()
    {
        try
        {
            await DisposeAsync();
        }
        catch (Exception)
        {
            // Process shutdown releases any hook the OS could not remove.
        }
        finally
        {
            _application.Shutdown();
        }
    }

    private async Task TryRestoreBindingsAsync(
        ImmutableArray<HotkeyBinding> bindings)
    {
        try
        {
            await _hotkeys.StopAsync(CancellationToken.None);
            await _hotkeys.StartAsync(bindings, CancellationToken.None);
        }
        catch (Exception)
        {
            _trayIcon.ShowNotification(
                "DictaClone shortcuts are unavailable",
                "Exit and restart DictaClone to restore the global hook.",
                System.Windows.Forms.ToolTipIcon.Error);
        }
    }

    private async void AudioSpeechSettingsChanged(
        object? sender,
        AudioSpeechSettingsChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        DictaCloneSettings updated = _settings with
        {
            Audio = eventArgs.Audio,
            Transcription = eventArgs.Transcription,
            Insertion = eventArgs.Insertion,
        };

        _ = await ApplySettingsAsync(updated, "✓  Settings saved");
    }

    private async void TextSettingsChanged(
        object? sender,
        TextSettingsChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        _ = await ApplySettingsAsync(
            _settings with { Text = eventArgs.Settings },
            "✓  Knowledge saved");
    }

    private async void PreferencesChanged(
        object? sender,
        PreferencesChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        bool applied = await ApplySettingsAsync(
            _settings with { Preferences = eventArgs.Preferences },
            "✓  Privacy settings saved");
        if (applied && _settings.Preferences.FirstRunCompleted)
        {
            _settingsWindow!.Title = "DictaClone settings";
        }
    }

    private async void SettingsImportRequested(
        object? sender,
        SettingsTransferRequestedEventArgs eventArgs)
    {
        try
        {
            DictaCloneSettings imported = await _settingsTransfer.ImportAsync(
                eventArgs.Path,
                CancellationToken.None);
            bool applied = await ApplySettingsAsync(
                imported,
                "✓  Settings imported");
            if (applied)
            {
                await WriteDiagnosticSafeAsync(
                    DiagnosticEventKind.SettingsImport,
                    DiagnosticOutcome.Succeeded);
                _settingsWindow?.Close();
                ShowSettings();
            }
        }
        catch (Exception exception)
        {
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsImport,
                DiagnosticOutcome.Failed,
                exception: exception);
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Settings import failed; the current settings were kept");
        }
    }

    private async void SettingsExportRequested(
        object? sender,
        SettingsTransferRequestedEventArgs eventArgs)
    {
        try
        {
            await _settingsTransfer.ExportAsync(
                eventArgs.Path,
                _settings,
                CancellationToken.None);
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsExport,
                DiagnosticOutcome.Succeeded);
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Settings exported");
        }
        catch (Exception exception)
        {
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsExport,
                DiagnosticOutcome.Failed,
                exception: exception);
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Settings export failed");
        }
    }

    private async void SupportBundleRequested(
        object? sender,
        SettingsTransferRequestedEventArgs eventArgs)
    {
        try
        {
            await _supportBundles.CreateAsync(
                eventArgs.Path,
                _settings,
                CancellationToken.None);
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SupportBundle,
                DiagnosticOutcome.Succeeded);
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Privacy-safe support bundle created");
        }
        catch (Exception exception)
        {
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SupportBundle,
                DiagnosticOutcome.Failed,
                exception: exception);
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Support bundle creation failed");
        }
    }

    private void MicrophonePermissionHelpRequested(
        object? sender,
        EventArgs eventArgs)
    {
        try
        {
            PermissionHelpService.OpenMicrophonePrivacySettings();
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Open Windows Settings > Privacy & security > Microphone");
        }
    }

    private async Task<bool> ApplySettingsAsync(
        DictaCloneSettings updated,
        string successMessage)
    {
        await _settingsApplyGate.WaitAsync().ConfigureAwait(true);
        DictaCloneSettings previous = _settings;
        bool runtimeApplied = false;
        bool startupApplied = false;
        bool settingsSaved = false;
        bool hotkeysStopped = false;
        try
        {
            var errors = SettingsValidator.Validate(updated);
            if (!errors.IsEmpty)
            {
                throw new InvalidDataException(errors[0].Message);
            }

            await _hotkeys.StopAsync(CancellationToken.None).ConfigureAwait(true);
            hotkeysStopped = true;
            await _dictationUi.UpdateSettingsAsync(updated).ConfigureAwait(true);
            runtimeApplied = true;

            if (_enableSystemIntegration &&
                previous.Preferences.StartWithWindows !=
                    updated.Preferences.StartWithWindows)
            {
                _startupRegistration.SetEnabled(
                    updated.Preferences.StartWithWindows);
                startupApplied = true;
            }

            if (_enablePersistentState)
            {
                await _settingsStore
                    .SaveAsync(updated, CancellationToken.None)
                    .ConfigureAwait(true);
                settingsSaved = true;
            }

            await _hotkeys
                .StartAsync(updated.Hotkeys, CancellationToken.None)
                .ConfigureAwait(true);
            hotkeysStopped = false;
            _settings = updated;
            _bindings = updated.Hotkeys;
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsSave,
                DiagnosticOutcome.Succeeded).ConfigureAwait(true);
            _overlay.ShowStatus(OverlayStatus.Success, successMessage);
            return true;
        }
        catch (Exception exception)
        {
            if (settingsSaved && _enablePersistentState)
            {
                try
                {
                    await _settingsStore
                        .SaveAsync(previous, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // The original failure remains primary; startup will still
                    // validate or quarantine any incomplete settings document.
                }
            }

            if (startupApplied)
            {
                try
                {
                    _startupRegistration.SetEnabled(
                        previous.Preferences.StartWithWindows);
                }
                catch (Exception)
                {
                    // The UI reports the failed transaction below.
                }
            }

            if (runtimeApplied)
            {
                try
                {
                    await _dictationUi
                        .UpdateSettingsAsync(previous)
                        .ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // Active work can delay rollback; no new settings are
                    // committed in memory.
                }
            }

            if (hotkeysStopped)
            {
                await TryRestoreBindingsAsync(previous.Hotkeys)
                    .ConfigureAwait(true);
            }

            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsSave,
                DiagnosticOutcome.Failed,
                exception: exception).ConfigureAwait(true);
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Settings were not saved; finish active dictation and retry");
            return false;
        }
        finally
        {
            _settingsApplyGate.Release();
        }
    }

    private async ValueTask WriteDiagnosticSafeAsync(
        DiagnosticEventKind eventKind,
        DiagnosticOutcome outcome,
        TimeSpan? duration = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        if (!_enablePersistentState)
        {
            return;
        }

        try
        {
            await _diagnostics.WriteAsync(
                eventKind,
                outcome,
                duration,
                exception,
                cancellationToken).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Diagnostics are best-effort and never interrupt dictation.
        }
    }

    private void PostToUi(Action action)
    {
        void ExecuteSafely()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                // A queued UI update can race window shutdown.
            }
        }

        if (_application.Dispatcher.CheckAccess())
        {
            ExecuteSafely();
            return;
        }

        _ = _application.Dispatcher.BeginInvoke(ExecuteSafely);
    }
}
