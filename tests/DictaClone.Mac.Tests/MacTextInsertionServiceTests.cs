using System.Runtime.InteropServices;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using DictaClone.Mac.Insertion;

namespace DictaClone.Mac.Tests;

public sealed class MacTextInsertionServiceTests
{
    private static readonly ForegroundTarget Target = new(
        "target",
        "TextEdit",
        "com.apple.TextEdit");

    [Fact]
    public async Task PasteMode_PreservesEveryCapturedPasteboardFormat()
    {
        var pasteboard = new FakePasteboard();
        var keyboard = new FakeKeyboard();
        var service = new MacTextInsertionService(
            pasteboard,
            keyboard,
            NoDelay);

        await service.InsertAsync(
            "replacement",
            Target,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(1, keyboard.PasteCount);
        Assert.True(pasteboard.Restored);
        Assert.Equal("original", pasteboard.Items[0][0].Type);
        Assert.Equal([1, 2, 3], pasteboard.Items[0][0].Data);
    }

    [Fact]
    public async Task PasteMode_DoesNotOverwriteConcurrentUserChange()
    {
        var pasteboard = new FakePasteboard();
        var keyboard = new FakeKeyboard(() => pasteboard.ChangeExternally());
        var service = new MacTextInsertionService(
            pasteboard,
            keyboard,
            NoDelay);

        await service.InsertAsync(
            "replacement",
            Target,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.False(pasteboard.Restored);
    }

    [Fact]
    public async Task PasteMode_AbortsIfPasteboardChangesDuringReadyWindow()
    {
        var pasteboard = new FakePasteboard();
        bool readyWindow = true;
        var service = new MacTextInsertionService(
            pasteboard,
            new FakeKeyboard(),
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (readyWindow)
                {
                    readyWindow = false;
                    pasteboard.ChangeExternally();
                }

                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<ClipboardContentionException>(() =>
            service.InsertAsync(
                "replacement",
                Target,
                new(TextInsertionMode.Paste, TimeSpan.Zero),
                CancellationToken.None));

        Assert.False(pasteboard.Restored);
    }

    [Fact]
    public async Task PasteMode_DefaultRetryWindow_OutlastsBriefPasteboardOwner()
    {
        var pasteboard = new FakePasteboard
        {
            CaptureFailuresRemaining = 6,
        };
        var delays = new List<TimeSpan>();
        var service = new MacTextInsertionService(
            pasteboard,
            new FakeKeyboard(),
            (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await service.InsertAsync(
            "replacement",
            Target,
            new(TextInsertionMode.Paste, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(7, pasteboard.CaptureCalls);
        Assert.Equal(
            [25, 50, 75, 100, 125, 150],
            delays.Take(6).Select(delay => (int)delay.TotalMilliseconds));
        Assert.True(pasteboard.Restored);
    }

    [Fact]
    public async Task CopyText_RetriesTransientPasteboardContention()
    {
        var pasteboard = new FakePasteboard
        {
            SetTextFailuresRemaining = 6,
        };
        var delays = new List<TimeSpan>();
        var service = new MacTextInsertionService(
            pasteboard,
            new FakeKeyboard(),
            (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                delays.Add(delay);
                return Task.CompletedTask;
            });

        bool copied = await service.TryCopyTextAsync(
            "recovered",
            CancellationToken.None);

        Assert.True(copied);
        Assert.Equal(7, pasteboard.SetTextCalls);
        Assert.Equal(
            [25, 50, 75, 100, 125, 150],
            delays.Select(delay => (int)delay.TotalMilliseconds));
    }

    [Fact]
    public async Task CopyText_ReportsPersistentPasteboardContention()
    {
        var pasteboard = new FakePasteboard
        {
            SetTextFailuresRemaining = 10,
        };
        var service = new MacTextInsertionService(
            pasteboard,
            new FakeKeyboard(),
            NoDelay);

        bool copied = await service.TryCopyTextAsync(
            "blocked",
            CancellationToken.None);

        Assert.False(copied);
        Assert.Equal(10, pasteboard.SetTextCalls);
    }

    [Fact]
    public async Task TypingMode_PreservesGraphemesAndLeavesPasteboardUntouched()
    {
        var pasteboard = new FakePasteboard();
        var keyboard = new FakeKeyboard();
        var service = new MacTextInsertionService(
            pasteboard,
            keyboard,
            NoDelay);
        long originalSequence = pasteboard.ChangeCount;

        await service.InsertAsync(
            "A👩🏽‍💻\nB\tC",
            Target,
            new(TextInsertionMode.DelayedTyping, TimeSpan.FromMilliseconds(10)),
            CancellationToken.None);

        Assert.Equal(originalSequence, pasteboard.ChangeCount);
        Assert.Equal(["A", "👩🏽‍💻", "B", "C"], keyboard.Text);
        Assert.Equal(1, keyboard.ReturnCount);
        Assert.Equal(1, keyboard.TabCount);
    }

    private static Task NoDelay(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private sealed class FakePasteboard : IMacPasteboard
    {
        private readonly MacPasteboardSnapshot _original = new(
            [[new("original", [1, 2, 3])]]);

        public long ChangeCount { get; private set; } = 10;

        public IReadOnlyList<IReadOnlyList<MacPasteboardValue>> Items { get; private set; }
            = [[new("original", [1, 2, 3])]];

        public bool Restored { get; private set; }

        public int CaptureCalls { get; private set; }

        public int CaptureFailuresRemaining { get; set; }

        public int SetTextCalls { get; private set; }

        public int SetTextFailuresRemaining { get; set; }

        public MacPasteboardSnapshot Capture()
        {
            CaptureCalls++;
            if (CaptureFailuresRemaining-- > 0)
            {
                throw new FakePasteboardBusyException();
            }

            return _original;
        }

        public void SetText(string text)
        {
            SetTextCalls++;
            if (SetTextFailuresRemaining-- > 0)
            {
                throw new FakePasteboardBusyException();
            }

            Items = [[new("public.utf8-plain-text", System.Text.Encoding.UTF8.GetBytes(text))]];
            ChangeCount++;
        }

        public void Restore(MacPasteboardSnapshot snapshot)
        {
            Items = snapshot.Items;
            Restored = true;
            ChangeCount++;
        }

        public void ChangeExternally() => ChangeCount++;
    }

    private sealed class FakePasteboardBusyException()
        : ExternalException("busy");

    private sealed class FakeKeyboard(Action? onPaste = null) : IMacKeyboardInjector
    {
        public int PasteCount { get; private set; }

        public int ReturnCount { get; private set; }

        public int TabCount { get; private set; }

        public List<string> Text { get; } = [];

        public void Paste()
        {
            PasteCount++;
            onPaste?.Invoke();
        }

        public void TypeText(string text) => Text.Add(text);

        public void PressReturn() => ReturnCount++;

        public void PressTab() => TabCount++;
    }
}
