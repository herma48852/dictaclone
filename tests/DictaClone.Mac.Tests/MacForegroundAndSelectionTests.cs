using DictaClone.Core.Dictation;
using DictaClone.Mac.Foreground;
using DictaClone.Mac.Selection;

namespace DictaClone.Mac.Tests;

public sealed class MacForegroundAndSelectionTests
{
    [Fact]
    public async Task ForegroundTarget_RejectsChangedControlWithinApplication()
    {
        var native = new FakeForegroundApi(new(
            42,
            100,
            90,
            "TextEdit",
            "com.apple.TextEdit"));
        var service = new MacForegroundTargetService(native);

        ForegroundTarget captured = await service.CaptureAsync(
            CancellationToken.None);
        Assert.Equal("0000002A:E:0000000000000064", captured.Id);
        Assert.True(await service.IsCurrentAsync(captured, CancellationToken.None));

        native.Snapshot = native.Snapshot with { FocusedElementHash = 101 };
        Assert.False(await service.IsCurrentAsync(captured, CancellationToken.None));
    }

    [Fact]
    public async Task ForegroundTarget_ReportsMissingAccessibilityPermission()
    {
        var native = new FakeForegroundApi(new(
            42,
            100,
            90,
            "TextEdit",
            "com.apple.TextEdit"))
        {
            IsTrusted = false,
        };
        var service = new MacForegroundTargetService(native);

        PlatformPermissionException exception = await Assert.ThrowsAsync<
            PlatformPermissionException>(() =>
                service.CaptureAsync(CancellationToken.None));

        Assert.Equal("Accessibility", exception.Permission);
    }

    [Fact]
    public async Task ForegroundTarget_FallsBackToFocusedWindowIdentity()
    {
        var service = new MacForegroundTargetService(
            new FakeForegroundApi(new(
                42,
                0,
                100,
                "TextEdit",
                "com.apple.TextEdit")));

        ForegroundTarget captured = await service.CaptureAsync(
            CancellationToken.None);

        Assert.Equal("0000002A:W:0000000000000064", captured.Id);
    }

    [Fact]
    public async Task ForegroundTarget_RejectsMissingFocusedControlAndWindow()
    {
        var service = new MacForegroundTargetService(
            new FakeForegroundApi(new(
                42,
                0,
                0,
                "TextEdit",
                "com.apple.TextEdit")));

        await Assert.ThrowsAsync<ForegroundTargetUnavailableException>(() =>
            service.CaptureAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Selection_RevalidatesTextAndTargetIdentity()
    {
        var native = new FakeSelectedTextApi("selected text");
        var service = new MacSelectedTextService(native);
        var target = new ForegroundTarget(
            "0000002A:E:0000000000000064",
            "TextEdit",
            "com.apple.TextEdit");

        var snapshot = await service.CaptureAsync(target, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.True(await service.RevalidateAsync(
            snapshot!,
            target,
            CancellationToken.None));

        native.Text = "changed";
        Assert.False(await service.RevalidateAsync(
            snapshot!,
            target,
            CancellationToken.None));
    }

    private sealed class FakeForegroundApi(MacForegroundSnapshot snapshot)
        : IMacForegroundApi
    {
        public bool IsTrusted { get; set; } = true;

        public MacForegroundSnapshot Snapshot { get; set; } = snapshot;

        public bool IsAccessibilityTrusted() => IsTrusted;

        public MacForegroundSnapshot Capture(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Snapshot;
        }
    }

    private sealed class FakeSelectedTextApi(string? text) : IMacSelectedTextApi
    {
        public string? Text { get; set; } = text;

        public string? GetSelectedText() => Text;
    }
}
