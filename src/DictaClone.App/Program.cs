using System.Windows;
using System.Windows.Threading;
using DictaClone.Core;
using DictaClone.Windows;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace DictaClone.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool smokeTest = args.Contains(
            "--smoke-test",
            StringComparer.OrdinalIgnoreCase);

        if (!SingleInstanceGuard.TryAcquire(
                "DictaClone.Desktop",
                out SingleInstanceGuard? instanceGuard))
        {
            if (smokeTest)
            {
                return 2;
            }

            WpfMessageBox.Show(
                "DictaClone is already running. Use its notification-area icon.",
                ProductInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return 0;
        }

        using (instanceGuard)
        {
            var application = new WpfApplication
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            AppController? controller = null;
            int exitCode = 0;

            application.Startup += (_, _) =>
            {
                try
                {
                    controller = new(application);
                    controller.StartAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    if (smokeTest)
                    {
                        _ = application.Dispatcher.BeginInvoke(
                            DispatcherPriority.ApplicationIdle,
                            () =>
                            {
                                try
                                {
                                    controller.DisposeAsync()
                                        .AsTask()
                                        .GetAwaiter()
                                        .GetResult();
                                }
                                finally
                                {
                                    application.Shutdown();
                                }
                            });
                    }
                }
                catch (Exception exception)
                {
                    exitCode = 1;
                    if (!smokeTest)
                    {
                        ShowStartupError(exception);
                    }

                    QueueShutdown(application, exitCode);
                }
            };
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                exitCode = 1;
                eventArgs.Handled = true;
                if (!smokeTest)
                {
                    ShowUnhandledError(eventArgs);
                }

                application.Shutdown(exitCode);
            };

            try
            {
                _ = application.Run();
            }
            finally
            {
                controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return exitCode;
        }
    }

    private static void QueueShutdown(
        WpfApplication application,
        int exitCode)
    {
        _ = application.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => application.Shutdown(exitCode));
    }

    private static void ShowStartupError(Exception exception)
    {
        WpfMessageBox.Show(
            $"DictaClone could not start ({exception.GetType().Name}).",
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void ShowUnhandledError(
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        WpfMessageBox.Show(
            $"DictaClone encountered an unexpected error " +
            $"({eventArgs.Exception.GetType().Name}) and will close.",
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
