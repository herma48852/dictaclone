using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace DictaClone.TestTarget;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
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

        var loaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.Loaded += (_, _) =>
        {
            FocusTarget(window, textBox);
            loaded.TrySetResult();
        };

        string? pipeName = GetPipeName(args);
        if (pipeName is not null)
        {
            _ = RunPipeServerAsync(
                pipeName,
                application,
                window,
                textBox,
                loaded.Task);
        }

        application.Run(window);
    }

    private static string? GetPipeName(string[] args)
    {
        int index = Array.FindIndex(
            args,
            argument => string.Equals(
                argument,
                "--pipe",
                StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : null;
    }

    private static async Task RunPipeServerAsync(
        string pipeName,
        Application application,
        Window window,
        TextBox textBox,
        Task loaded)
    {
        try
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            await loaded.ConfigureAwait(false);
            using var reader = new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            await writer.WriteLineAsync("READY").ConfigureAwait(false);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is
                { } command)
            {
                if (string.Equals(command, "FOCUS", StringComparison.Ordinal))
                {
                    await application.Dispatcher.InvokeAsync(
                        () => FocusTarget(window, textBox));
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                }
                else if (string.Equals(command, "CLEAR", StringComparison.Ordinal))
                {
                    await application.Dispatcher.InvokeAsync(() =>
                    {
                        textBox.Clear();
                        FocusTarget(window, textBox);
                    });
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                }
                else if (string.Equals(command, "GET", StringComparison.Ordinal))
                {
                    string text = await application.Dispatcher.InvokeAsync(
                        () => textBox.Text);
                    string encoded = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(text));
                    await writer.WriteLineAsync(encoded).ConfigureAwait(false);
                }
                else if (string.Equals(command, "EXIT", StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                    _ = application.Dispatcher.BeginInvoke(window.Close);
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("ERROR").ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            _ = application.Dispatcher.BeginInvoke(window.Close);
        }
    }

    private static void FocusTarget(Window window, TextBox textBox)
    {
        window.Activate();
        _ = textBox.Focus();
        _ = Keyboard.Focus(textBox);
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle != nint.Zero)
        {
            _ = SetForegroundWindow(handle);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
