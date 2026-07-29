using System.Windows;
using DictaClone.Core;

namespace DictaClone.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };
        var window = new Window
        {
            Title = ProductInfo.Name,
            Width = 640,
            Height = 360,
            Content = "Milestone 0 scaffold — product UI begins in a later milestone.",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        application.Run(window);
    }
}
