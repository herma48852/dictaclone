using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class SelectedTextServiceTests
{
    [Fact]
    public async Task Capture_CopiesSelectionAndRestoresPriorClipboard()
    {
        var clipboard = new FakeSelectionClipboard("clipboard before");
        var copy = new FakeSelectionCopy(clipboard)
        {
            SelectedText = "selected text",
        };
        var service = CreateService(clipboard, copy);

        SelectedTextSnapshot? snapshot = await service.CaptureAsync(
            Target("notepad"),
            CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("selected text", snapshot.Text);
        Assert.Equal(64, snapshot.Fingerprint.Length);
        Assert.Equal("clipboard before", clipboard.Text);
        Assert.False(copy.UsedEmacsShortcut);
    }

    [Fact]
    public async Task Capture_UsesEmacsCopyAndTreatsNoSelectionAsAbsent()
    {
        var clipboard = new FakeSelectionClipboard("keep");
        var copy = new FakeSelectionCopy(clipboard)
        {
            SelectedText = null,
        };
        var service = CreateService(clipboard, copy);

        SelectedTextSnapshot? snapshot = await service.CaptureAsync(
            Target("emacs"),
            CancellationToken.None);

        Assert.Null(snapshot);
        Assert.True(copy.UsedEmacsShortcut);
        Assert.Equal("keep", clipboard.Text);
    }

    [Fact]
    public async Task Revalidate_RequiresTheExactOriginalSelection()
    {
        var clipboard = new FakeSelectionClipboard("keep");
        var copy = new FakeSelectionCopy(clipboard)
        {
            SelectedText = "original",
        };
        var service = CreateService(clipboard, copy);
        SelectedTextSnapshot original = Assert.IsType<SelectedTextSnapshot>(
            await service.CaptureAsync(Target("notepad"),
                CancellationToken.None));

        Assert.True(await service.RevalidateAsync(
            original,
            Target("notepad"),
            CancellationToken.None));
        copy.SelectedText = "changed";
        Assert.False(await service.RevalidateAsync(
            original,
            Target("notepad"),
            CancellationToken.None));
    }

    private static SelectedTextService CreateService(
        FakeSelectionClipboard clipboard,
        FakeSelectionCopy copy) => new(
            new ImmediateStaThreadRunner(),
            clipboard,
            copy,
            new NoDelay());

    private static ForegroundTarget Target(string processName) => new(
        "target",
        processName,
        "window");

    private sealed class ImmediateStaThreadRunner : IStaThreadRunner
    {
        public Task RunAsync(Action action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class NoDelay : IInsertionDelay
    {
        public void Wait(TimeSpan delay, CancellationToken cancellationToken) =>
            cancellationToken.ThrowIfCancellationRequested();

        public Task WaitAsync(TimeSpan delay,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSelectionCopy(FakeSelectionClipboard clipboard)
        : ISelectionCopyInjector
    {
        public string? SelectedText { get; set; }

        public bool UsedEmacsShortcut { get; private set; }

        public void SendCopy(bool emacs)
        {
            UsedEmacsShortcut = emacs;
            if (SelectedText is not null)
            {
                clipboard.SetCopiedText(SelectedText);
            }
        }
    }

    private sealed class FakeSelectionClipboard(string? text)
        : ISelectionClipboard
    {
        private uint _sequence = 1;

        public string? Text { get; private set; } = text;

        public uint GetSequenceNumber() => _sequence;

        public ClipboardSnapshot Capture() => new(Text);

        public string? GetUnicodeText() => Text;

        public void Clear()
        {
            Text = null;
            _sequence++;
        }

        public void Restore(ClipboardSnapshot snapshot)
        {
            Text = snapshot.Data as string;
            _sequence++;
        }

        public void SetCopiedText(string selectedText)
        {
            Text = selectedText;
            _sequence++;
        }
    }
}
