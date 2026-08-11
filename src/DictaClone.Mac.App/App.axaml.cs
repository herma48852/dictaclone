using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace DictaClone.Mac;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Avalonia owns the Application lifetime; the controller is disposed during desktop shutdown.")]
public sealed partial class App : Application
{
    private MacAppController? _controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.UIThread.UnhandledException += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                _ = ShutdownAsync(desktop, exitCode: 1);
            };
            desktop.Exit += (_, _) =>
            {
                _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
            _ = StartAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _controller = new MacAppController(desktop);
            await _controller.StartAsync(
                smokeTest: false,
                openSettingsOnStart: Program.OpenSettingsOnStart,
                CancellationToken.None);
        }
        catch (Exception)
        {
            await ShutdownAsync(desktop, exitCode: 1);
        }
    }

    private async Task ShutdownAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        int exitCode)
    {
        try
        {
            if (_controller is not null)
            {
                await _controller.DisposeAsync();
                _controller = null;
            }
        }
        finally
        {
            desktop.Shutdown(exitCode);
        }
    }
}
