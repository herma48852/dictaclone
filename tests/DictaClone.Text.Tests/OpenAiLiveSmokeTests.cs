using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;
using DictaClone.Text;

namespace DictaClone.Text.Tests;

public sealed class OpenAiLiveSmokeTests
{
    [Fact]
    [Trait("Category", "LiveProvider")]
    public async Task ExplicitOptIn_CanCallConfiguredLiveProvider()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "DICTACLONE_RUN_LIVE_SMART_EDIT"),
                "1",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "DICTACLONE_OPENAI_API_KEY")))
        {
            return;
        }

        string model = Environment.GetEnvironmentVariable(
            "DICTACLONE_OPENAI_MODEL") ??
            DictaCloneSettings.Default.SmartEdit.Model;
        var provider = new OpenAiResponsesSmartEditProvider(
            new HttpClient(),
            new EnvironmentSecretStore());
        var request = new SmartEditRequest(
            "Correct the capitalization only.",
            "hello world",
            "live-smoke-test",
            "none",
            DictaCloneSettings.Default.Text,
            DictaCloneSettings.Default.SmartEdit with
            {
                Enabled = true,
                Model = model,
            });

        string result = await provider.EditAsync(
            request,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    private sealed class EnvironmentSecretStore : ISecretStore
    {
        public Task<string?> ReadAsync(string name,
            CancellationToken cancellationToken) => Task.FromResult(
                Environment.GetEnvironmentVariable(
                    "DICTACLONE_OPENAI_API_KEY"));

        public Task WriteAsync(string name, string value,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string name,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
