using DictaClone.Mac.Permissions;

namespace DictaClone.Mac.Tests;

public sealed class MacPermissionSnapshotTests
{
    [Fact]
    public void GlobalShortcuts_RequireAccessibilityButNotRedundantInputMonitoring()
    {
        var snapshot = new MacPermissionSnapshot(
            MacPermissionState.Authorized,
            MacPermissionState.Authorized,
            MacPermissionState.Denied);

        Assert.True(snapshot.CanCaptureGlobalShortcuts);
    }

    [Fact]
    public void GlobalShortcuts_RemainBlockedWithoutAccessibility()
    {
        var snapshot = new MacPermissionSnapshot(
            MacPermissionState.Authorized,
            MacPermissionState.Denied,
            MacPermissionState.Authorized);

        Assert.False(snapshot.CanCaptureGlobalShortcuts);
    }
}
