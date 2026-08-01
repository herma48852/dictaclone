using System.Windows;
using System.Windows.Threading;
using DictaClone.App.Presentation;
using DictaClone.Core.Hotkeys;

namespace DictaClone.App.Tests;

public sealed class WindowConfigurationTests
{
    [Fact]
    public async Task Overlay_IsNonActivatingTopmostAndNotFocusable()
    {
        await RunOnStaAsync(() =>
        {
            var overlay = new StatusOverlayWindow();

            Assert.False(overlay.ShowActivated);
            Assert.False(overlay.ShowInTaskbar);
            Assert.False(overlay.Focusable);
            Assert.False(overlay.IsHitTestVisible);
            Assert.True(overlay.Topmost);
            Assert.Equal(WindowStyle.None, overlay.WindowStyle);

            overlay.ShowStatus(OverlayStatus.Recording);
            Assert.True(overlay.IsVisible);
            Assert.True(overlay.HasNoActivateExtendedStyle);
            Assert.False(overlay.IsKeyboardFocusWithin);

            overlay.HideStatus();
            Assert.False(overlay.IsVisible);
            overlay.Close();
        });
    }

    [Fact]
    public async Task SettingsWindow_LoadsDefaultBindingsOnStaThread()
    {
        await RunOnStaAsync(() =>
        {
            var window = new SettingsWindow(HotkeyDefaults.Bindings);

            Assert.Equal("DictaClone settings", window.Title);
            Assert.Equal(
                HotkeyDefaults.Bindings.Length,
                window.Bindings.Length);
            Assert.Equal(
                HotkeyDefaults.Bindings.ToArray(),
                window.Bindings.ToArray());

            window.Close();
        });
    }

    private static Task RunOnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
