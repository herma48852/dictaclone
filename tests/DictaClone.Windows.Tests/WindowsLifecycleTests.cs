using DictaClone.Core.Hotkeys;
using DictaClone.Windows;
using DictaClone.Windows.Input;

namespace DictaClone.Windows.Tests;

public sealed class WindowsLifecycleTests
{
    [Fact]
    public void SingleInstanceGuard_AllowsOnlyOneOwnerAndCanReacquire()
    {
        string name = $"DictaClone.Tests.{Guid.NewGuid():N}";

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.NotNull(first);
        Assert.False(SingleInstanceGuard.TryAcquire(name, out var second));
        Assert.Null(second);

        first!.Dispose();
        first.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var reacquired));
        reacquired!.Dispose();
    }

    [Fact]
    public void SingleInstanceGuard_RejectsBlankName()
    {
        Assert.Throws<ArgumentException>(
            () => SingleInstanceGuard.TryAcquire(" ", out _));
    }

    [Fact]
    public async Task LowLevelHooks_InstallStopAndDisposeCleanly()
    {
        var hooks = new LowLevelHotkeySource();

        await hooks.StartAsync(
            HotkeyDefaults.Bindings,
            CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hooks.StartAsync(
                HotkeyDefaults.Bindings,
                CancellationToken.None));
        await hooks.StopAsync(CancellationToken.None);
        await hooks.DisposeAsync();
        await hooks.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => hooks.StartAsync(
                HotkeyDefaults.Bindings,
                CancellationToken.None));
    }

    [Fact]
    public async Task LowLevelHooks_ValidateArgumentsAndCancellation()
    {
        await using var hooks = new LowLevelHotkeySource();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => hooks.StartAsync(null!, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hooks.StartAsync(
                HotkeyDefaults.Bindings,
                new(canceled: true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hooks.StopAsync(new(canceled: true)));
    }
}
