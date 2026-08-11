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

        public MacPasteboardSnapshot Capture() => _original;

        public void SetText(string text)
        {
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
