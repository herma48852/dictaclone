using System.Collections.Immutable;

namespace DictaClone.Speech;

public sealed record WhisperModelDefinition(
    string Name,
    string FileName,
    long Length,
    string Sha256,
    Uri DownloadUri);

public static class WhisperModelCatalog
{
    private static readonly ImmutableDictionary<string, WhisperModelDefinition>
        Models = new Dictionary<string, WhisperModelDefinition>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["base.en"] = new(
                "base.en",
                "ggml-base.en.bin",
                147_964_211,
                "A03779C86DF3323075F5E796CB2CE5029F00EC8869EEE3FDFB897AFE36C6D002",
                new("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin?download=true")),
            ["small.en"] = new(
                "small.en",
                "ggml-small.en.bin",
                487_614_201,
                "C6138D6D58ECC8322097E0F987C32F1BE8BB0A18532A3F88F734D1BBF9C41E5D",
                new("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin?download=true")),
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<WhisperModelDefinition> AvailableModels =>
        Models.Values.ToArray();

    public static WhisperModelDefinition Get(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        return Models.TryGetValue(modelName, out WhisperModelDefinition? model)
            ? model
            : throw new ArgumentOutOfRangeException(
                nameof(modelName),
                modelName,
                "The requested Whisper model is not supported.");
    }
}
