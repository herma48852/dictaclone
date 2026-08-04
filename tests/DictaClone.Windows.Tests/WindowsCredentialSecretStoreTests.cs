using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class WindowsCredentialSecretStoreTests
{
    [Fact]
    public async Task CredentialStore_RoundTripsAndDeletesPerUserSecret()
    {
        var store = new WindowsCredentialSecretStore();
        string name = $"test/{Guid.NewGuid():N}";
        string value = $"secret-{Guid.NewGuid():N}";
        try
        {
            Assert.Null(await store.ReadAsync(name, CancellationToken.None));

            await store.WriteAsync(name, value, CancellationToken.None);

            Assert.Equal(value,
                await store.ReadAsync(name, CancellationToken.None));
        }
        finally
        {
            await store.DeleteAsync(name, CancellationToken.None);
        }

        Assert.Null(await store.ReadAsync(name, CancellationToken.None));
    }
}
