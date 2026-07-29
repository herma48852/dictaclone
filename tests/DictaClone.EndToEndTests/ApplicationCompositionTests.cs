using DictaClone.App;

namespace DictaClone.EndToEndTests;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public void ApplicationAssemblyReferencesCore()
    {
        string[] references = typeof(AppAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("DictaClone.Core", references);
    }
}
