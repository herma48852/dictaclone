using DictaClone.Core;

namespace DictaClone.Core.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void ProductName_IsStable()
    {
        Assert.Equal("DictaClone", ProductInfo.Name);
        Assert.Equal(new Version(0, 1, 3), ProductInfo.DevelopmentVersion);
        Assert.Equal(
            new Version(0, 1, 3, 0),
            typeof(ProductInfo).Assembly.GetName().Version);
    }

    [Fact]
    public void CoreAssembly_DoesNotReferencePlatformOrProviderAssemblies()
    {
        string[] forbiddenPrefixes =
        [
            "NAudio",
            "PresentationFramework",
            "System.Windows",
            "Whisper.net",
        ];

        string[] referencedAssemblies = typeof(ProductInfo)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (string prefix in forbiddenPrefixes)
        {
            Assert.DoesNotContain(
                referencedAssemblies,
                name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
