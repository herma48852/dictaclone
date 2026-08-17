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
    public async Task KeychainStore_UsesNativeGenericPasswordOperations()
    {
        var native = new FakeMacKeychainApi();
        var store = new MacKeychainSecretStore(native);

        await store.WriteAsync("api-key", "super-secret", CancellationToken.None);
        string? result = await store.ReadAsync("api-key", CancellationToken.None);
        await store.DeleteAsync("api-key", CancellationToken.None);
        string? deleted = await store.ReadAsync(
            "api-key",
            CancellationToken.None);

        Assert.Equal("super-secret", result);
        Assert.Null(deleted);
        Assert.Equal(
            ["write:api-key", "read:api-key", "delete:api-key", "read:api-key"],
            native.Calls);
    }

    [Fact]
    public async Task NativeKeychainStore_MissingSecretReturnsNull()
    {
        var store = new MacKeychainSecretStore();

        string? result = await store.ReadAsync(
            "missing-test-" + Guid.NewGuid().ToString("N"),
            CancellationToken.None);

        Assert.Null(result);
    }

    private sealed class FakeMacKeychainApi : IMacKeychainApi
    {
        private readonly Dictionary<string, string> _secrets = [];

        public List<string> Calls { get; } = [];

        public string? Read(string name)
        {
            Calls.Add($"read:{name}");
            return _secrets.GetValueOrDefault(name);
        }

        public void Write(string name, string value)
        {
            Calls.Add($"write:{name}");
            _secrets[name] = value;
        }

        public void Delete(string name)
        {
            Calls.Add($"delete:{name}");
            _secrets.Remove(name);
        }
    }
}
