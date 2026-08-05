using DictaClone.Speech;

namespace DictaClone.Speech.Tests;

public sealed class WhisperModelCatalogTests
{
    [Theory]
    [InlineData(
        "base.en",
        "ggml-base.en.bin",
        147_964_211,
        "A03779C86DF3323075F5E796CB2CE5029F00EC8869EEE3FDFB897AFE36C6D002")]
    [InlineData(
        "small.en",
        "ggml-small.en.bin",
        487_614_201,
        "C6138D6D58ECC8322097E0F987C32F1BE8BB0A18532A3F88F734D1BBF9C41E5D")]
    public void SupportedDownloads_HavePinnedIdentityAndIntegrityMetadata(
        string name,
        string fileName,
        long length,
        string sha256)
    {
        WhisperModelDefinition model = WhisperModelCatalog.Get(name);

        Assert.Equal(fileName, model.FileName);
        Assert.Equal(length, model.Length);
        Assert.Equal(sha256, model.Sha256);
        Assert.Equal(Uri.UriSchemeHttps, model.DownloadUri.Scheme);
        Assert.Equal("huggingface.co", model.DownloadUri.Host);
    }
}
