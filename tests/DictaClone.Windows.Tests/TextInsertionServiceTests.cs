using System.Runtime.InteropServices;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class TextInsertionServiceTests
{
    private static readonly ForegroundTarget LocalTarget = new(
        "window",
        "notepad",
        "Notepad");

    [Fact]
    public async Task Paste_RestoresStableClipboardAfterInput()
    {
        var context = new TestContext();
        context.Clipboard.Value = "original";

        await context.Service.InsertAsync(
            "hello",
            LocalTarget,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal("original", context.Clipboard.Value);
        Assert.Equal(1, context.Clipboard.CaptureCalls);
        Assert.Equal(1, context.Clipboard.RestoreCalls);
        Assert.Equal(["clipboard:StandardPaste"], context.Keyboard.Events);
        Assert.Equal(1, context.StaThreads.CallCount);
    }

    [Fact]
    public async Task Paste_DoesNotOverwriteConcurrentClipboardChange()
    {
        var context = new TestContext();
        context.Clipboard.Value = "original";
        context.Keyboard.OnPaste = () =>
            context.Clipboard.ChangeExternally("target update");

        await context.Service.InsertAsync(
            "dictated",
            LocalTarget,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal("target update", context.Clipboard.Value);
        Assert.Equal(0, context.Clipboard.RestoreCalls);
    }

    [Fact]
    public async Task Paste_WaitsForTargetBeforeRestoringClipboard()
    {
        var context = new TestContext(
            clipboardRestoreDelay: TimeSpan.FromMilliseconds(250));
        context.Clipboard.Value = "original";
        bool observedSettleWindow = false;
        context.Delay.OnBlockingWait = () =>
        {
            observedSettleWindow = true;
            Assert.Equal("dictated", context.Clipboard.Value);
            Assert.Equal(0, context.Clipboard.RestoreCalls);
        };

        await context.Service.InsertAsync(
            "dictated",
            LocalTarget,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.True(observedSettleWindow);
        Assert.Equal("original", context.Clipboard.Value);
        Assert.Contains(
            TimeSpan.FromMilliseconds(250),
            context.Delay.BlockingWaits);
    }

    [Fact]
    public async Task Paste_RetriesTransientClipboardContention()
    {
        var context = new TestContext(clipboardAttempts: 4);
        context.Clipboard.CaptureFailuresRemaining = 2;

        await context.Service.InsertAsync(
            "retry",
            LocalTarget,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(3, context.Clipboard.CaptureCalls);
        Assert.Equal(3, context.Delay.BlockingWaits.Count);
        Assert.Equal(1, context.Clipboard.RestoreCalls);
    }

    [Fact]
    public async Task Paste_ReportsPersistentClipboardContention()
    {
        var context = new TestContext(clipboardAttempts: 3);
        context.Clipboard.CaptureFailuresRemaining = 3;

        await Assert.ThrowsAsync<ClipboardContentionException>(() =>
            context.Service.InsertAsync(
                "blocked",
                LocalTarget,
                new(TextInsertionMode.Paste, TimeSpan.Zero),
                CancellationToken.None));

        Assert.Empty(context.Keyboard.Events);
        Assert.Equal(0, context.Clipboard.SetTextCalls);
    }

    [Fact]
    public async Task Paste_RestoresClipboardWhenInputFails()
    {
        var context = new TestContext();
        context.Clipboard.Value = "original";
        context.Keyboard.PasteException = new InputInjectionException();

        await Assert.ThrowsAsync<InputInjectionException>(() =>
            context.Service.InsertAsync(
                "text",
                LocalTarget,
                new(TextInsertionMode.Paste, TimeSpan.Zero),
                CancellationToken.None));

        Assert.Equal("original", context.Clipboard.Value);
        Assert.Equal(1, context.Clipboard.RestoreCalls);
    }

    [Theory]
    [InlineData(TextInsertionMode.Paste)]
    [InlineData(TextInsertionMode.DelayedTyping)]
    public async Task NativeEmacs_UsesYankAndRestoresClipboard(
        TextInsertionMode mode)
    {
        var context = new TestContext();
        context.Clipboard.Value = "original";
        ForegroundTarget emacsTarget = LocalTarget with
        {
            ProcessName = "EmAcS",
            WindowClass = "Emacs",
        };

        await context.Service.InsertAsync(
            "open the JSON file",
            emacsTarget,
            new(mode, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(
            ["clipboard:EmacsYank"],
            context.Keyboard.Events);
        Assert.Equal("original", context.Clipboard.Value);
        Assert.Equal(1, context.Clipboard.RestoreCalls);
        Assert.Empty(context.Keyboard.MappingAttempts);
        Assert.Equal(1, context.StaThreads.CallCount);
    }

    [Fact]
    public async Task Typing_TokenizesUnicodeLineEndingsAndLeavesClipboardUntouched()
    {
        var context = new TestContext();

        await context.Service.InsertAsync(
            "A😀\r\né\tB",
            LocalTarget,
            new(TextInsertionMode.DelayedTyping, TimeSpan.FromMilliseconds(3)),
            CancellationToken.None);

        Assert.Equal(
            ["unicode:A", "unicode:😀", "key:Enter", "unicode:é", "key:Tab", "unicode:B"],
            context.Keyboard.Events);
        Assert.Equal(5, context.Delay.AsyncWaits.Count);
        Assert.Equal(0, context.Clipboard.TotalCalls);
        Assert.Equal(0, context.StaThreads.CallCount);
    }

    [Fact]
    public async Task Typing_PrefersPhysicalKeysForLocalTargetsWithUnicodeFallback()
    {
        var context = new TestContext();
        context.Keyboard.MappableCharacters.Add('A');

        await context.Service.InsertAsync(
            "Aé",
            LocalTarget,
            new(TextInsertionMode.DelayedTyping, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(["mapped:A", "unicode:é"], context.Keyboard.Events);
        Assert.Equal(['A', 'é'], context.Keyboard.MappingAttempts);
        Assert.Equal(0, context.Clipboard.TotalCalls);
    }

    [Fact]
    public async Task Typing_UsesMappedKeysForRemoteTargetsWithUnicodeFallback()
    {
        var context = new TestContext();
        context.Keyboard.MappableCharacters.Add('A');
        var remote = LocalTarget with { ProcessName = "mstsc" };

        await context.Service.InsertAsync(
            "Aé",
            remote,
            new(TextInsertionMode.DelayedTyping, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(["mapped:A", "unicode:é"], context.Keyboard.Events);
        Assert.Equal(['A', 'é'], context.Keyboard.MappingAttempts);
        Assert.Equal(0, context.Clipboard.TotalCalls);
    }

    [Fact]
    public async Task ElevatedTargetAndInvalidDelay_AreRejectedBeforeInput()
    {
        var context = new TestContext();

        await Assert.ThrowsAsync<ElevatedTargetException>(() =>
            context.Service.InsertAsync(
                "text",
                LocalTarget with { IsElevated = true },
                new(TextInsertionMode.Paste, TimeSpan.Zero),
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            context.Service.InsertAsync(
                "text",
                LocalTarget,
                new(
                    TextInsertionMode.DelayedTyping,
                    TimeSpan.FromMilliseconds(101)),
                CancellationToken.None));

        Assert.Empty(context.Keyboard.Events);
        Assert.Equal(0, context.Clipboard.TotalCalls);
    }

    [Fact]
    public async Task CancellationDuringPaste_RestoresOwnedClipboard()
    {
        var context = new TestContext();
        context.Clipboard.Value = "original";
        context.Delay.CancelBlockingWait = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Service.InsertAsync(
                "temporary",
                LocalTarget,
                new(TextInsertionMode.Paste, TimeSpan.Zero),
                CancellationToken.None));

        Assert.Equal("original", context.Clipboard.Value);
        Assert.Equal(1, context.Clipboard.RestoreCalls);
    }

    [Fact]
    public void Planner_CollapsesLineEndingsAndKeepsSurrogatePairsTogether()
    {
        IReadOnlyList<TextInputToken> tokens = TextInputPlanner.Tokenize(
            "x\r\ny\rz\n🚀");

        Assert.Equal(7, tokens.Count);
        Assert.Equal(TextInputTokenKind.Enter, tokens[1].Kind);
        Assert.Equal(TextInputTokenKind.Enter, tokens[3].Kind);
        Assert.Equal(TextInputTokenKind.Enter, tokens[5].Kind);
        Assert.Equal("🚀", tokens[6].Value);
    }

    private sealed class TestContext
    {
        public TestContext(
            int clipboardAttempts = 5,
            TimeSpan? clipboardRestoreDelay = null)
        {
            Service = new(
                Clipboard,
                Keyboard,
                StaThreads,
                Delay,
                clipboardAttempts,
                clipboardRetryDelay: TimeSpan.FromMilliseconds(1),
                clipboardRestoreDelay:
                    clipboardRestoreDelay ?? TimeSpan.FromMilliseconds(1));
        }

        public FakeClipboardBackend Clipboard { get; } = new();

        public FakeKeyboardInjector Keyboard { get; } = new();

        public InlineStaThreadRunner StaThreads { get; } = new();

        public FakeDelay Delay { get; } = new();

        public TextInsertionService Service { get; }
    }

    private sealed class FakeClipboardBackend : IClipboardBackend
    {
        public uint Sequence { get; private set; } = 1;

        public object? Value { get; set; }

        public int CaptureFailuresRemaining { get; set; }

        public int CaptureCalls { get; private set; }

        public int SetTextCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public int TotalCalls { get; private set; }

        public uint GetSequenceNumber()
        {
            TotalCalls++;
            return Sequence;
        }

        public ClipboardSnapshot Capture()
        {
            TotalCalls++;
            CaptureCalls++;
            if (CaptureFailuresRemaining-- > 0)
            {
                throw new FakeClipboardBusyException();
            }

            return new(Value);
        }

        public void SetUnicodeText(string text)
        {
            TotalCalls++;
            SetTextCalls++;
            Value = text;
            Sequence++;
        }

        public void Restore(ClipboardSnapshot snapshot)
        {
            TotalCalls++;
            RestoreCalls++;
            Value = snapshot.Data;
            Sequence++;
        }

        public void ChangeExternally(object? value)
        {
            Value = value;
            Sequence++;
        }
    }

    private sealed class FakeKeyboardInjector : IKeyboardInjector
    {
        public List<string> Events { get; } = [];

        public List<char> MappingAttempts { get; } = [];

        public HashSet<char> MappableCharacters { get; } = [];

        public Action? OnPaste { get; set; }

        public Exception? PasteException { get; set; }

        public void SendClipboardInsert(ClipboardInsertionShortcut shortcut)
        {
            Events.Add($"clipboard:{shortcut}");
            OnPaste?.Invoke();
            if (PasteException is not null)
            {
                throw PasteException;
            }
        }

        public void SendUnicode(string text) => Events.Add($"unicode:{text}");

        public void SendVirtualKey(VirtualKey key) => Events.Add($"key:{key}");

        public bool TrySendMappedCharacter(char character)
        {
            MappingAttempts.Add(character);
            bool mapped = MappableCharacters.Contains(character);
            if (mapped)
            {
                Events.Add($"mapped:{character}");
            }

            return mapped;
        }
    }

    private sealed class FakeClipboardBusyException()
        : ExternalException("busy");

    private sealed class InlineStaThreadRunner : IStaThreadRunner
    {
        public int CallCount { get; private set; }

        public Task RunAsync(
            Action action,
            CancellationToken cancellationToken)
        {
            CallCount++;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }
    }

    private sealed class FakeDelay : IInsertionDelay
    {
        public List<TimeSpan> BlockingWaits { get; } = [];

        public List<TimeSpan> AsyncWaits { get; } = [];

        public bool CancelBlockingWait { get; set; }

        public Action? OnBlockingWait { get; set; }

        public void Wait(TimeSpan delay, CancellationToken cancellationToken)
        {
            BlockingWaits.Add(delay);
            cancellationToken.ThrowIfCancellationRequested();
            OnBlockingWait?.Invoke();
            if (CancelBlockingWait)
            {
                throw new OperationCanceledException(
                    new CancellationToken(canceled: true));
            }
        }

        public Task WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            AsyncWaits.Add(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
