using DictaClone.Mac.Lifecycle;
using DictaClone.Mac.Security;

namespace DictaClone.Mac.Tests;

public sealed class MacPersistenceAdapterTests
{
    [Fact]
    public void StartupRegistration_WritesAndRemovesPerUserLaunchAgent()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DictaClone-MacStartup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MacStartupRegistrationService(
                directory,
                "/Applications/DictaClone & Test.app");

            service.SetEnabled(true);
            Assert.True(service.IsEnabled);
            string document = File.ReadAllText(
                Path.Combine(directory, "com.dictaclone.desktop.plist"));
            Assert.Contains("/usr/bin/open", document);
            Assert.Contains("DictaClone &amp; Test.app", document);

            service.SetEnabled(false);
            Assert.False(service.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task KeychainStore_UsesGenericPasswordWithoutLoggingSecret()
    {
        var commands = new FakeKeychainCommand();
        var store = new MacKeychainSecretStore(commands);

        await store.WriteAsync("api-key", "super-secret", CancellationToken.None);
        string? result = await store.ReadAsync("api-key", CancellationToken.None);
        await store.DeleteAsync("api-key", CancellationToken.None);

        Assert.Equal("stored-value", result);
        Assert.Equal("add-generic-password", commands.Calls[0][0]);
        Assert.Contains("com.dictaclone.desktop", commands.Calls[0]);
        Assert.Equal("find-generic-password", commands.Calls[1][0]);
        Assert.Equal("delete-generic-password", commands.Calls[2][0]);
    }

    private sealed class FakeKeychainCommand : IKeychainCommand
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<KeychainCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments.ToArray());
            return Task.FromResult(new KeychainCommandResult(
                ExitCode: 0,
                arguments[0] == "find-generic-password"
                    ? "stored-value\n"
                    : string.Empty));
        }
    }
}
