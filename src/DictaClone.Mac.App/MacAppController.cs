using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DictaClone.App;
using DictaClone.App.Presentation;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Infrastructure;
using DictaClone.Mac.Audio;
using DictaClone.Mac.Foreground;
using DictaClone.Mac.Input;
using DictaClone.Mac.Insertion;
using DictaClone.Mac.Lifecycle;
using DictaClone.Mac.Permissions;
using DictaClone.Mac.Presentation;
using DictaClone.Mac.Security;
using DictaClone.Mac.Selection;
using DictaClone.Speech;
using DictaClone.Text;

namespace DictaClone.Mac;

public sealed class MacAppController : IAsyncDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly MacHotkeyEventSource _hotkeys = new();
    private readonly MacStatusOverlayWindow _overlay = new();
    private readonly MacPermissionService _permissions = new();
    private readonly MacTextInsertionService _insertion = new();
    private readonly MacStartupRegistrationService _startup = new();
    private readonly MacKeychainSecretStore _secrets = new();
    private readonly JsonSettingsStore _settingsStore;
    private readonly SettingsTransferService _settingsTransfer = new();
    private readonly JsonTranscriptHistoryStore _historyStore;
    private readonly TranscriptHistoryRecorder _historyRecorder;
    private readonly PrivacySafeDiagnosticLog _diagnostics;
    private readonly PrivacySafeSupportBundleService _supportBundles;
    private readonly WhisperTranscriptionEngine _transcription;
    private readonly HttpClient _smartEditHttpClient;
    private readonly LiveDictationController _dictation;
    private readonly MacTrayIconService _tray;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private DictaCloneSettings _settings = DictaCloneSettings.Default;
    private MacSettingsWindow? _settingsWindow;
    private MacHistoryWindow? _historyWindow;
    private bool _apiKeyStored;
    private bool _disposed;

    public MacAppController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        DictaCloneDataPaths paths = DictaCloneDataPaths.Default;
        _settingsStore = new(paths);
        _historyStore = new(paths);
        _historyRecorder = new(_historyStore);
        _diagnostics = new(paths);
        _supportBundles = new(paths);
        var modelManager = new WhisperModelManager(
            WhisperModelStorage.ResolveDefaultDirectory());
        _transcription = new(modelManager, ownsModelManager: true);
        _smartEditHttpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        });
        _dictation = new LiveDictationController(
            new MacAudioCaptureService(),
            _transcription,
            new DeterministicTextProcessor(),
            new MacForegroundTargetService(),
            _insertion,
            _overlay,
            _settings,
            action => Dispatcher.UIThread.Post(action),
            new OpenAiResponsesSmartEditProvider(
                _smartEditHttpClient,
                _secrets),
            new MacSelectedTextService());
        _tray = new MacTrayIconService(Application.Current ??
            throw new InvalidOperationException(
                "The Avalonia application is unavailable."));
    }

    public async Task StartAsync(
        bool smokeTest,
        bool openSettingsOnStart,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await WriteDiagnosticSafeAsync(
            DiagnosticEventKind.ApplicationStartup,
            DiagnosticOutcome.Started,
            cancellationToken: cancellationToken).ConfigureAwait(true);

        if (!smokeTest)
        {
            SettingsLoadResult loaded = await _settingsStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(true);
            _settings = loaded.Settings;
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsLoad,
                loaded.QuarantinedFilePath is null
                    ? DiagnosticOutcome.Succeeded
                    : DiagnosticOutcome.Recovered,
                cancellationToken: cancellationToken).ConfigureAwait(true);
            _apiKeyStored = !string.IsNullOrWhiteSpace(
                await _secrets.ReadAsync(
                    OpenAiResponsesSmartEditProvider.ApiKeySecretName,
                    cancellationToken).ConfigureAwait(true));
            if (_settings.SmartEdit.Enabled && !_apiKeyStored)
            {
                _settings = DisableSmartEdit(_settings);
                await _settingsStore.SaveAsync(_settings, cancellationToken)
                    .ConfigureAwait(true);
            }

            try
            {
                _startup.SetEnabled(_settings.Preferences.StartWithWindows);
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

        await _dictation.UpdateSettingsAsync(_settings).ConfigureAwait(true);
        WireEvents();

        if (!smokeTest)
        {
            bool hotkeysReady = true;
            try
            {
                await _hotkeys.StartAsync(
                    _settings.Hotkeys,
                    cancellationToken).ConfigureAwait(true);
            }
            catch (PlatformPermissionException)
            {
                hotkeysReady = false;
            }

            bool warmed = false;
            bool warmupFailed = false;
            try
            {
                _overlay.ShowStatus(
                    OverlayStatus.Processing,
                    "Preparing local speech model…");
                warmed = await _transcription.WarmUpIfAvailableAsync(
                    _settings.Transcription,
                    cancellationToken).ConfigureAwait(true);
            }
            catch (Exception)
            {
                warmupFailed = true;
            }

            if (!hotkeysReady)
            {
                _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Enable Accessibility, then restart DictaClone.");
            }
            else if (warmupFailed)
            {
                _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Speech model warm-up failed; review model settings.");
            }
            else
            {
                _overlay.ShowStatus(
                    OverlayStatus.Success,
                    warmed
                        ? "✓  DictaClone is ready"
                        : "✓  Ready; the local model downloads on first use");
            }

            if (openSettingsOnStart ||
                !_settings.Preferences.FirstRunCompleted)
            {
                ShowSettings();
            }
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
        UnwireEvents();
        await WriteDiagnosticSafeAsync(
            DiagnosticEventKind.ApplicationShutdown,
            DiagnosticOutcome.Started).ConfigureAwait(true);
        try
        {
            await _hotkeys.DisposeAsync().ConfigureAwait(true);
            await _dictation.DisposeAsync().ConfigureAwait(true);
            await _transcription.DisposeAsync().ConfigureAwait(true);
        }
        finally
        {
            _settingsWindow?.CloseForShutdown();
            _historyWindow?.CloseForShutdown();
            _overlay.Close();
            _tray.Dispose();
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.ApplicationShutdown,
                DiagnosticOutcome.Succeeded).ConfigureAwait(true);
            _historyStore.Dispose();
            _settingsStore.Dispose();
            _diagnostics.Dispose();
            _smartEditHttpClient.Dispose();
            _settingsGate.Dispose();
        }
    }

    private void WireEvents()
    {
        _hotkeys.Triggered += HotkeysTriggered;
        _dictation.TranscriptAvailable += TranscriptAvailable;
        _tray.SettingsRequested += SettingsRequested;
        _tray.CopyLastRequested += CopyLastRequested;
        _tray.HistoryRequested += HistoryRequested;
        _tray.PermissionsRequested += PermissionsRequested;
        _tray.ExitRequested += ExitRequested;
    }

    private void UnwireEvents()
    {
        _hotkeys.Triggered -= HotkeysTriggered;
        _dictation.TranscriptAvailable -= TranscriptAvailable;
        _tray.SettingsRequested -= SettingsRequested;
        _tray.CopyLastRequested -= CopyLastRequested;
        _tray.HistoryRequested -= HistoryRequested;
        _tray.PermissionsRequested -= PermissionsRequested;
        _tray.ExitRequested -= ExitRequested;
    }

    private async void HotkeysTriggered(object? sender, HotkeyEvent hotkeyEvent)
    {
        try
        {
            await _dictation.HandleAsync(hotkeyEvent).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Dispatcher.UIThread.Post(() => _overlay.ShowStatus(
                OverlayStatus.Failure,
                "The global shortcut operation failed."));
        }
    }

    private async void TranscriptAvailable(
        object? sender,
        TranscriptAvailableEventArgs eventArgs)
    {
        try
        {
            bool recorded = await _historyRecorder.RecordIfEnabledAsync(
                eventArgs.Transcript,
                _settings.Preferences,
                DateTimeOffset.UtcNow,
                CancellationToken.None).ConfigureAwait(false);
            if (recorded)
            {
                await WriteDiagnosticSafeAsync(
                    DiagnosticEventKind.HistoryWrite,
                    DiagnosticOutcome.Succeeded).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.HistoryWrite,
                DiagnosticOutcome.Failed,
                exception: exception).ConfigureAwait(false);
        }
    }

    private void SettingsRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(ShowSettings);

    private void CopyLastRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(CopyLastResult);

    private void HistoryRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() => _ = ShowHistoryAsync());

    private void PermissionsRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            ShowSettings();
            _permissions.OpenAccessibilitySettings();
        });

    private void ExitRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() => _ = ExitAsync());

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new(
                _settings,
                _apiKeyStored,
                _permissions.Inspect(),
                GetMicrophones());
            _settingsWindow.ApplyRequested += SettingsApplyRequested;
            _settingsWindow.PermissionSettingsRequested +=
                PermissionSettingsRequested;
            _settingsWindow.Activated += SettingsWindowActivated;
            _settingsWindow.OpenDataFolderRequested += OpenDataFolderRequested;
            _settingsWindow.ExportSettingsRequested += ExportSettingsRequested;
            _settingsWindow.ImportSettingsRequested += ImportSettingsRequested;
            _settingsWindow.SupportBundleRequested += SupportBundleRequested;
            _lifetime.MainWindow = _settingsWindow;
        }
        else
        {
            _settingsWindow.Update(
                _settings,
                _apiKeyStored,
                _permissions.Inspect());
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async void SettingsApplyRequested(
        object? sender,
        MacSettingsApplyEventArgs eventArgs)
    {
        await ApplySettingsAsync(
            eventArgs.Settings,
            eventArgs.ApiKey,
            CancellationToken.None).ConfigureAwait(true);
    }

    private async Task ApplySettingsAsync(
        DictaCloneSettings requested,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        await _settingsGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                await _secrets.WriteAsync(
                    OpenAiResponsesSmartEditProvider.ApiKeySecretName,
                    apiKey,
                    cancellationToken).ConfigureAwait(true);
                _apiKeyStored = true;
            }

            if (requested.SmartEdit.Enabled && !_apiKeyStored)
            {
                _settingsWindow?.ShowValidation(
                    "Smart Edit requires an API key stored in macOS Keychain.");
                return;
            }

            await _dictation.UpdateSettingsAsync(requested).ConfigureAwait(true);
            await _hotkeys.StopAsync(cancellationToken).ConfigureAwait(true);
            _settings = requested;
            await _settingsStore.SaveAsync(_settings, cancellationToken)
                .ConfigureAwait(true);
            _startup.SetEnabled(_settings.Preferences.StartWithWindows);
            bool hotkeysReady = true;
            try
            {
                await _hotkeys.StartAsync(
                    _settings.Hotkeys,
                    cancellationToken).ConfigureAwait(true);
            }
            catch (PlatformPermissionException)
            {
                hotkeysReady = false;
            }

            _settingsWindow?.Update(
                _settings,
                _apiKeyStored,
                _permissions.Inspect());
            _overlay.ShowStatus(
                hotkeysReady ? OverlayStatus.Success : OverlayStatus.Failure,
                hotkeysReady
                    ? "✓  Settings applied"
                    : "Settings saved. Enable Accessibility, then restart DictaClone.");
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsSave,
                DiagnosticOutcome.Succeeded,
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            _settingsWindow?.ShowValidation(exception.Message);
            await WriteDiagnosticSafeAsync(
                DiagnosticEventKind.SettingsSave,
                DiagnosticOutcome.Failed,
                exception: exception,
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private async void PermissionSettingsRequested(
        object? sender,
        MacPermissionRequestEventArgs eventArgs)
    {
        switch (eventArgs.Permission)
        {
            case "microphone":
                await RequestMicrophonePermissionAsync().ConfigureAwait(true);
                break;
            case "accessibility":
                _ = _permissions.RequestAccessibility();
                _permissions.OpenAccessibilitySettings();
                break;
            case "input":
                _ = _permissions.RequestInputMonitoring();
                _permissions.OpenInputMonitoringSettings();
                break;
        }
    }

    private async Task RequestMicrophonePermissionAsync()
    {
        try
        {
            MacPermissionState state = await _permissions
                .RequestMicrophoneAsync()
                .ConfigureAwait(true);
            _settingsWindow?.UpdatePermissions(_permissions.Inspect());
            if (state == MacPermissionState.Authorized)
            {
                _overlay.ShowStatus(
                    OverlayStatus.Success,
                    "✓  Microphone access enabled");
            }
            else
            {
                _permissions.OpenMicrophoneSettings();
                _overlay.ShowStatus(
                    OverlayStatus.Failure,
                    "Enable DictaClone under Privacy & Security > Microphone.");
            }
        }
        catch (Exception exception)
        {
            _settingsWindow?.ShowValidation(
                $"Could not request microphone access: {exception.Message}");
            _permissions.OpenMicrophoneSettings();
        }
    }

    private void SettingsWindowActivated(object? sender, EventArgs eventArgs) =>
        _settingsWindow?.UpdatePermissions(_permissions.Inspect());

    private async void CopyLastResult()
    {
        if (string.IsNullOrWhiteSpace(_dictation.LastTranscript))
        {
            _overlay.ShowStatus(OverlayStatus.Failure, "No result is available yet");
            return;
        }

        await CopyTextAsync(
            _dictation.LastTranscript,
            "✓  Last result copied").ConfigureAwait(true);
    }

    private async Task ShowHistoryAsync()
    {
        HistoryLoadResult history = await _historyStore.LoadAsync(
            CancellationToken.None).ConfigureAwait(true);
        if (_historyWindow is null)
        {
            _historyWindow = new();
            _historyWindow.CopyRequested += async (_, eventArgs) =>
            {
                await CopyTextAsync(
                    eventArgs.Text,
                    "✓  Transcript copied").ConfigureAwait(true);
            };
            _historyWindow.ClearRequested += async (_, _) =>
            {
                await _historyStore.ClearAsync(CancellationToken.None);
                _historyWindow.SetEntries([]);
            };
        }

        _historyWindow.SetEntries(history.Entries);
        if (_settingsWindow is null)
        {
            _historyWindow.Show();
        }
        else
        {
            _historyWindow.Show(_settingsWindow);
        }
        _historyWindow.Activate();
    }

    private void OpenDataFolderRequested(object? sender, EventArgs eventArgs)
    {
        Directory.CreateDirectory(DictaCloneDataPaths.Default.RootDirectory);
        StartProcess("/usr/bin/open", DictaCloneDataPaths.Default.RootDirectory);
    }

    private void ExportSettingsRequested(object? sender, EventArgs eventArgs) =>
        _ = ExportSettingsAsync();

    private async Task ExportSettingsAsync()
    {
        IStorageFile? file = await _settingsWindow!.StorageProvider
            .SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export DictaClone settings",
                SuggestedFileName = "dictaclone-settings.json",
            });
        if (file is null)
        {
            return;
        }

        await _settingsTransfer.ExportAsync(
            file.Path.LocalPath,
            _settings,
            CancellationToken.None).ConfigureAwait(true);
        _overlay.ShowStatus(OverlayStatus.Success, "✓  Settings exported");
    }

    private void ImportSettingsRequested(object? sender, EventArgs eventArgs) =>
        _ = ImportSettingsAsync();

    private async Task ImportSettingsAsync()
    {
        IReadOnlyList<IStorageFile> files = await _settingsWindow!.StorageProvider
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import DictaClone settings",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON settings")
                    {
                        Patterns = ["*.json"],
                    },
                ],
            });
        if (files.Count != 1)
        {
            return;
        }

        DictaCloneSettings imported = await _settingsTransfer.ImportAsync(
            files[0].Path.LocalPath,
            CancellationToken.None).ConfigureAwait(true);
        await ApplySettingsAsync(imported, apiKey: null, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private void SupportBundleRequested(object? sender, EventArgs eventArgs) =>
        _ = CreateSupportBundleAsync();

    private async Task CreateSupportBundleAsync()
    {
        IStorageFile? file = await _settingsWindow!.StorageProvider
            .SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Create privacy-safe support bundle",
                SuggestedFileName = "DictaClone-support.zip",
            });
        if (file is null)
        {
            return;
        }

        await _supportBundles.CreateAsync(
            file.Path.LocalPath,
            _settings,
            CancellationToken.None).ConfigureAwait(true);
        _overlay.ShowStatus(OverlayStatus.Success, "✓  Support bundle created");
    }

    private async Task ExitAsync()
    {
        await DisposeAsync();
        _lifetime.Shutdown();
    }

    private async ValueTask WriteDiagnosticSafeAsync(
        DiagnosticEventKind kind,
        DiagnosticOutcome outcome,
        TimeSpan? duration = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _diagnostics.WriteAsync(
                kind,
                outcome,
                duration,
                exception,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Diagnostics must never break dictation or shutdown.
        }
    }

    private async Task CopyTextAsync(string text, string successMessage)
    {
        try
        {
            bool copied = await _insertion.TryCopyTextAsync(
                text,
                CancellationToken.None).ConfigureAwait(true);
            _overlay.ShowStatus(
                copied ? OverlayStatus.Success : OverlayStatus.Failure,
                copied
                    ? successMessage
                    : "Clipboard is busy; try copying again");
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Could not copy text to the clipboard");
        }
    }

    private static DictaCloneSettings DisableSmartEdit(
        DictaCloneSettings settings) => settings with
        {
            SmartEdit = settings.SmartEdit with { Enabled = false },
            Hotkeys = settings.Hotkeys
            .Select(binding => binding.Action == HotkeyAction.SmartEdit
                ? binding with { Enabled = false }
                : binding)
            .ToImmutableArray(),
        };

    private static void StartProcess(string fileName, string argument)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(argument);
        _ = Process.Start(startInfo);
    }

    private static IReadOnlyList<MacMicrophoneDevice> GetMicrophones()
    {
        try
        {
            return new MacMicrophoneDeviceService().GetActiveCaptureDevices();
        }
        catch (Exception)
        {
            return
            [
                new MacMicrophoneDevice(
                    Id: null,
                    FriendlyName: "Follow system default microphone",
                    IsDefault: true),
            ];
        }
    }
}
