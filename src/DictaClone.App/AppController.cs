using System.Collections.Immutable;
using System.Windows;
using DictaClone.App.Presentation;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;
using DictaClone.Windows.Input;
using WpfApplication = System.Windows.Application;

namespace DictaClone.App;

public sealed class AppController : IAsyncDisposable
{
    private readonly WpfApplication _application;
    private readonly LowLevelHotkeySource _hotkeys;
    private readonly StatusOverlayWindow _overlay;
    private readonly TriggerUiController _triggerUi;
    private readonly TrayIconService _trayIcon;
    private ImmutableArray<HotkeyBinding> _bindings;
    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public AppController(WpfApplication application)
    {
        _application = application ??
            throw new ArgumentNullException(nameof(application));
        _bindings = HotkeyDefaults.Bindings;
        _hotkeys = new();
        _overlay = new();
        _triggerUi = new(_overlay);
        _trayIcon = new();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _hotkeys.Triggered += HotkeysTriggered;
        _trayIcon.SettingsRequested += SettingsRequested;
        _trayIcon.ExitRequested += ExitRequested;
        await _hotkeys
            .StartAsync(_bindings, cancellationToken)
            .ConfigureAwait(true);

        _overlay.ShowStatus(
            OverlayStatus.Success,
            "✓  DictaClone is ready");
        _trayIcon.ShowNotification(
            "DictaClone is running",
            "Hold Ctrl+Win to test the global shortcut overlay.");
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
            _settingsWindow?.Close();
            _settingsWindow = null;
            _triggerUi.Dispose();
            _overlay.Close();
            _trayIcon.Dispose();
        }
    }

    private async void HotkeysTriggered(object? sender, HotkeyEvent inputEvent)
    {
        try
        {
            await _triggerUi.HandleAsync(inputEvent);
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
            _settingsWindow = new(_bindings);
            _settingsWindow.BindingsChanged += BindingsChanged;
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
            _bindings = eventArgs.Bindings;
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
}
