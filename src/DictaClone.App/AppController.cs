using System.Collections.Immutable;
using System.Windows;
using DictaClone.App.Presentation;
using DictaClone.Audio;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Speech;
using DictaClone.Text;
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
    private ImmutableArray<HotkeyBinding> _bindings;
    private DictaCloneSettings _settings;
    private SettingsWindow? _settingsWindow;
    private readonly bool _enableModelWarmup;
    private bool _disposed;

    public AppController(
        WpfApplication application,
        bool enableModelWarmup = true)
    {
        _application = application ??
            throw new ArgumentNullException(nameof(application));
        _enableModelWarmup = enableModelWarmup;
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
            _overlay,
            _settings,
            PostToUi);
        _trayIcon = new();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        ObjectDisposedException.ThrowIf(_disposed, this);

        _hotkeys.Triggered += HotkeysTriggered;
        _trayIcon.SettingsRequested += SettingsRequested;
        _trayIcon.ExitRequested += ExitRequested;
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
                _overlay.Close();
                _trayIcon.Dispose();
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
                devices);
            _settingsWindow.BindingsChanged += BindingsChanged;
            _settingsWindow.AudioSpeechSettingsChanged +=
                AudioSpeechSettingsChanged;
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

        ImmutableArray<HotkeyBinding> previous = _bindings;

        try
        {
            await _hotkeys.StopAsync(CancellationToken.None);
            await _hotkeys.StartAsync(
                eventArgs.Bindings,
                CancellationToken.None);
            DictaCloneSettings updated = _settings with
            {
                Hotkeys = eventArgs.Bindings,
            };
            await _dictationUi.UpdateSettingsAsync(updated);
            _bindings = eventArgs.Bindings;
            _settings = updated;
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Shortcuts updated");
        }
        catch (Exception)
        {
            await TryRestoreBindingsAsync(previous);
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "!  Shortcuts could not be updated");
        }
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
        };

        try
        {
            await _dictationUi.UpdateSettingsAsync(updated);
            _settings = updated;
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Audio settings updated");
        }
        catch (Exception)
        {
            _overlay.ShowStatus(
                OverlayStatus.Failure,
                "Finish the active dictation before changing audio settings");
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
