using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class WindowsAssemblyTests
{
    [Fact]
    public void WindowsAssemblyLoadsOnTargetPlatform()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        Assert.Equal(
            "DictaClone.Windows",
            typeof(WindowsAssemblyMarker).Assembly.GetName().Name);
    }
}
