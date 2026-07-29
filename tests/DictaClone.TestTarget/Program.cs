using System.Windows;
using System.Windows.Controls;

namespace DictaClone.TestTarget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Margin = new Thickness(16),
        };
        var window = new Window
        {
            Title = "DictaClone Test Target",
            Width = 640,
            Height = 360,
            Content = textBox,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };

        window.Loaded += (_, _) => textBox.Focus();
        application.Run(window);
    }
}
