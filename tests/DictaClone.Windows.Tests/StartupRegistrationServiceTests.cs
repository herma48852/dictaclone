using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void EnableAndDisable_UseOnlyThePerUserRunValue()
    {
        var registry = new FakeRunRegistry();
        var service = new StartupRegistrationService(
            registry,
            "\"C:\\Apps\\DictaClone.exe\"");

        Assert.False(service.IsEnabled);
        service.SetEnabled(enabled: true);
        Assert.True(service.IsEnabled);
        Assert.Equal(
            "\"C:\\Apps\\DictaClone.exe\"",
            registry.Values[StartupRegistrationService.ValueName]);

        service.SetEnabled(enabled: false);
        Assert.False(service.IsEnabled);
        Assert.Empty(registry.Values);
    }

    [Fact]
    public void ExistingDifferentCommand_IsNotReportedAsEnabled()
    {
        var registry = new FakeRunRegistry();
        registry.Values[StartupRegistrationService.ValueName] =
            "\"C:\\Old\\DictaClone.exe\"";
        var service = new StartupRegistrationService(
            registry,
            "\"C:\\New\\DictaClone.exe\"");

        Assert.False(service.IsEnabled);
    }

    [Theory]
    [InlineData(
        "C:\\dotnet\\dotnet.exe",
        "C:\\app\\DictaClone.App.dll",
        "\"C:\\dotnet\\dotnet.exe\" \"C:\\app\\DictaClone.App.dll\"")]
    [InlineData(
        "C:\\app\\DictaClone.App.exe",
        "C:\\app\\DictaClone.App.dll",
        "\"C:\\app\\DictaClone.App.exe\"")]
    public void Command_HandlesFrameworkDependentAndSelfContainedLaunches(
        string process,
        string assembly,
        string expected)
    {
        Assert.Equal(
            expected,
            StartupRegistrationService.CreateCommand(process, assembly));
    }

    private sealed class FakeRunRegistry : IRunRegistry
    {
        public Dictionary<string, string> Values { get; } =
            new(StringComparer.Ordinal);

        public string? GetValue(string name) =>
            Values.GetValueOrDefault(name);

        public void SetValue(string name, string value) =>
            Values[name] = value;

        public void DeleteValue(string name) => Values.Remove(name);
    }
}
