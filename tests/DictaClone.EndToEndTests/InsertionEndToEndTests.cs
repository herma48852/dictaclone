using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using DictaClone.Windows;

namespace DictaClone.EndToEndTests;

public sealed partial class InsertionEndToEndTests
{
    [Fact]
    [Trait("Category", "DesktopE2E")]
    public async Task TestTarget_AcceptsPasteAndTypingUnicodeCorpus()
    {
        await using TestTargetClient target = await TestTargetClient.StartAsync();
        IDataObject? originalClipboard = await RunStaAsync(CaptureClipboard);

        try
        {
            const string sentinel = "DictaClone clipboard sentinel";
            await RunStaAsync(() => Clipboard.SetText(sentinel));
            var foreground = new ForegroundTargetService();
            var insertion = new TextInsertionService();
            string[] pasteCases =
            [
                "single line",
                "line one\r\nline two",
                "Punctuation: \"quotes\", commas—done!",
                "Grüße, Καλημέρα, 你好",
                "Emoji 😀🚀",
                new string('x', 4_096),
            ];

            foreach (string expected in pasteCases)
            {
                await target.ClearAndFocusAsync();
                ForegroundTarget captured = await foreground.CaptureAsync(
                    CancellationToken.None);
                await insertion.InsertAsync(
                    expected,
                    captured,
                    new(TextInsertionMode.Paste, TimeSpan.Zero),
                    CancellationToken.None);

                Assert.Equal(expected, await target.WaitForTextAsync(expected));
                Assert.Equal(sentinel, await RunStaAsync(Clipboard.GetText));
            }

            await target.ClearAndFocusAsync();
            ForegroundTarget typingTarget = await foreground.CaptureAsync(
                CancellationToken.None);
            uint sequenceBeforeTyping = GetClipboardSequenceNumber();
            const string typed = "Typed 😀\r\nSecond\tcolumn";
            await insertion.InsertAsync(
                typed,
                typingTarget,
                new(TextInsertionMode.DelayedTyping, TimeSpan.Zero),
                CancellationToken.None);

            Assert.Equal(typed, await target.WaitForTextAsync(typed));
            Assert.Equal(sequenceBeforeTyping, GetClipboardSequenceNumber());
            Assert.Equal(sentinel, await RunStaAsync(Clipboard.GetText));
        }
        finally
        {
            await RunStaAsync(() => RestoreClipboard(originalClipboard));
        }
    }

    [Fact]
    [Trait("Category", "DesktopE2E")]
    [Trait("Category", "ManualDesktopStress")]
    public async Task FiftyConsecutiveInsertionCycles_PreserveClipboardAndTarget()
    {
        await using TestTargetClient target = await TestTargetClient.StartAsync();
        using var clipboardSession = new ClipboardStaSession();
        IDataObject? originalClipboard = await clipboardSession.InvokeAsync(
            CaptureClipboard);

        try
        {
            const string sentinel = "DictaClone 50-cycle clipboard sentinel";
            var foreground = new ForegroundTargetService();
            var insertion = new TextInsertionService();

            for (int cycle = 0; cycle < 50; cycle++)
            {
                string expected = $"release cycle {cycle + 1:D2} 😀";
                await target.ClearAndFocusAsync();
                await SetClipboardTextWithRetryAsync(
                    clipboardSession,
                    sentinel);
                Assert.Equal(
                    sentinel,
                    await clipboardSession.InvokeAsync(Clipboard.GetText));
                ForegroundTarget captured = await foreground.CaptureAsync(
                    CancellationToken.None);
                TextInsertionMode mode = cycle % 10 == 9
                    ? TextInsertionMode.DelayedTyping
                    : TextInsertionMode.Paste;

                await insertion.InsertAsync(
                    expected,
                    captured,
                    new(mode, TimeSpan.Zero),
                    CancellationToken.None);

                Assert.Equal(expected, await target.WaitForTextAsync(expected));
                string clipboard = await WaitForClipboardTextAsync(
                    clipboardSession,
                    sentinel);
                Assert.True(
                    string.Equals(
                        sentinel,
                        clipboard,
                        StringComparison.Ordinal),
                    $"Clipboard mismatch after cycle {cycle + 1} in {mode}; " +
                    $"actual length was {clipboard.Length}.");
            }
        }
        finally
        {
            await clipboardSession.InvokeAsync(
                () => RestoreClipboard(originalClipboard));
        }
    }

    private static IDataObject? CaptureClipboard()
    {
        IDataObject? source = Clipboard.GetDataObject();
        if (source is null)
        {
            return null;
        }

        var snapshot = new DataObject();
        foreach (string format in source.GetFormats(autoConvert: false))
        {
            object? data = source.GetData(format, autoConvert: false);
            if (data is not null)
            {
                snapshot.SetData(format, autoConvert: false, data);
            }
        }

        return snapshot;
    }

    private static void RestoreClipboard(IDataObject? snapshot)
    {
        if (snapshot is null)
        {
            Clipboard.Clear();
        }
        else
        {
            Clipboard.SetDataObject(snapshot, copy: true);
        }
    }

    private static Task<bool> RunStaAsync(Action action) =>
        RunStaAsync(() =>
        {
            action();
            return true;
        });

    private static async Task<string> WaitForClipboardTextAsync(
        ClipboardStaSession clipboardSession,
        string expected)
    {
        var timeout = Stopwatch.StartNew();
        string actual;
        do
        {
            actual = await clipboardSession.InvokeAsync(Clipboard.GetText);
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return actual;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        while (timeout.Elapsed < TimeSpan.FromSeconds(2));

        return actual;
    }

    private static async Task SetClipboardTextWithRetryAsync(
        ClipboardStaSession clipboardSession,
        string text)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            bool published = await clipboardSession.InvokeAsync(() =>
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return Clipboard.ContainsText(TextDataFormat.UnicodeText) &&
                    string.Equals(
                        text,
                        Clipboard.GetText(TextDataFormat.UnicodeText),
                        StringComparison.Ordinal);
            });
            if (published)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(15 * attempt));
        }

        Assert.Fail("The test sentinel could not be published to the clipboard.");
    }

    private sealed class ClipboardStaSession : IDisposable
    {
        private readonly BlockingCollection<Action> _actions = new();
        private readonly Thread _thread;

        public ClipboardStaSession()
        {
            using var started = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                started.Set();
                foreach (Action action in _actions.GetConsumingEnumerable())
                {
                    action();
                }
            })
            {
                IsBackground = true,
                Name = "DictaClone E2E Clipboard Owner",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Assert.True(
                started.Wait(TimeSpan.FromSeconds(5)),
                "The clipboard STA thread did not start.");
        }

        public Task<bool> InvokeAsync(Action action) =>
            InvokeAsync(() =>
            {
                action();
                return true;
            });

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Add(() =>
            {
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            _actions.CompleteAdding();
            Assert.True(
                _thread.Join(millisecondsTimeout: 5_000),
                "The clipboard STA thread did not stop.");
            _actions.Dispose();
        }
    }

    private static Task<T> RunStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class TestTargetClient : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private TestTargetClient(
            Process process,
            NamedPipeClientStream pipe,
            StreamReader reader,
            StreamWriter writer)
        {
            _process = process;
            _pipe = pipe;
            _reader = reader;
            _writer = writer;
        }

        public static async Task<TestTargetClient> StartAsync()
        {
            string repositoryRoot = FindRepositoryRoot();
            string configuration = new DirectoryInfo(AppContext.BaseDirectory)
                .Parent?.Name ?? "Release";
            string targetAssembly = Path.Combine(
                repositoryRoot,
                "tests",
                "DictaClone.TestTarget",
                "bin",
                configuration,
                "net10.0-windows10.0.22000.0",
                "DictaClone.TestTarget.dll");
            string dotNet = Path.Combine(repositoryRoot, ".dotnet", "dotnet.exe");
            string pipeName = $"dictaclone-target-{Guid.NewGuid():N}";
            var process = new Process
            {
                StartInfo = new()
                {
                    FileName = dotNet,
                    Arguments = $"\"{targetAssembly}\" --pipe {pipeName}",
                    UseShellExecute = false,
                    WorkingDirectory = repositoryRoot,
                },
            };
            Assert.True(File.Exists(targetAssembly), targetAssembly);
            Assert.True(process.Start());

            var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(10));
            try
            {
                await pipe.ConnectAsync(timeout.Token);
                var reader = new StreamReader(
                    pipe,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);
                var writer = new StreamWriter(
                    pipe,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };
                Assert.Equal("READY", await reader.ReadLineAsync(timeout.Token));
                return new(process, pipe, reader, writer);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                process.Dispose();
                pipe.Dispose();
                throw;
            }
        }

        public async Task ClearAndFocusAsync()
        {
            Assert.Equal("OK", await SendAsync("CLEAR"));
            var timeout = Stopwatch.StartNew();
            do
            {
                Assert.Equal("OK", await SendAsync("FOCUS"));
                _process.Refresh();
                nint targetWindow = _process.MainWindowHandle;
                if (targetWindow != nint.Zero &&
                    GetForegroundWindow() == targetWindow)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
            while (timeout.Elapsed < TimeSpan.FromSeconds(5));

            Assert.Fail("The DictaClone test target did not become foreground.");
        }

        public async Task<string> GetTextAsync()
        {
            string encoded = await SendAsync("GET");
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }

        public async Task<string> WaitForTextAsync(string expected)
        {
            var timeout = Stopwatch.StartNew();
            string actual;
            do
            {
                actual = await GetTextAsync();
                if (string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    return actual;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }
            while (timeout.Elapsed < TimeSpan.FromSeconds(2));

            return actual;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _ = await SendAsync("EXIT");
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(5));
                    await _process.WaitForExitAsync(timeout.Token);
                }
            }
            catch (Exception)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(CancellationToken.None);
                }
            }
            finally
            {
                _writer.Dispose();
                _reader.Dispose();
                _pipe.Dispose();
                _process.Dispose();
            }
        }

        private async Task<string> SendAsync(string command)
        {
            await _writer.WriteLineAsync(command);
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            return await _reader.ReadLineAsync(timeout.Token) ??
                throw new EndOfStreamException();
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the DictaClone repository root.");
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
