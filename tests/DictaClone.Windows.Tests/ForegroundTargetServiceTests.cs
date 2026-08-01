using DictaClone.Core.Dictation;
using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class ForegroundTargetServiceTests
{
    [Fact]
    public async Task Capture_RecordsOpaqueIdentityAndMetadata()
    {
        var windows = new FakeForegroundWindowApi
        {
            Snapshot = new(
                (nint)0x1234,
                ProcessId: 42,
                ProcessName: "notepad",
                WindowClass: "Notepad",
                IsElevatedAboveCurrentProcess: true),
        };
        var service = new ForegroundTargetService(windows);

        ForegroundTarget target = await service.CaptureAsync(
            CancellationToken.None);

        Assert.Equal("0000000000001234:0000002A", target.Id);
        Assert.Equal("notepad", target.ProcessName);
        Assert.Equal("Notepad", target.WindowClass);
        Assert.True(target.IsElevated);
    }

    [Fact]
    public async Task Validation_RequiresTheSameWindowAndProcess()
    {
        var windows = new FakeForegroundWindowApi
        {
            Snapshot = new((nint)10, 20, "first", "class", false),
        };
        var service = new ForegroundTargetService(windows);
        ForegroundTarget target = await service.CaptureAsync(
            CancellationToken.None);

        Assert.True(await service.IsCurrentAsync(
            target,
            CancellationToken.None));

        windows.Snapshot = windows.Snapshot with { WindowHandle = (nint)11 };
        Assert.False(await service.IsCurrentAsync(
            target,
            CancellationToken.None));

        windows.Snapshot = windows.Snapshot with
        {
            WindowHandle = (nint)10,
            ProcessId = 21,
        };
        Assert.False(await service.IsCurrentAsync(
            target,
            CancellationToken.None));
    }

    [Fact]
    public async Task MissingWindowAndCancellation_AreReported()
    {
        var service = new ForegroundTargetService(
            new FakeForegroundWindowApi());

        await Assert.ThrowsAsync<ForegroundTargetUnavailableException>(() =>
            service.CaptureAsync(CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CaptureAsync(new(canceled: true)));
    }

    private sealed class FakeForegroundWindowApi : IForegroundWindowApi
    {
        public ForegroundWindowSnapshot Snapshot { get; set; }

        public ForegroundWindowSnapshot Capture() => Snapshot;
    }
}
