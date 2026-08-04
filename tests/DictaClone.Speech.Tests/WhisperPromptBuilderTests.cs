using DictaClone.Core.Settings;
using DictaClone.Speech;

namespace DictaClone.Speech.Tests;

public sealed class WhisperPromptBuilderTests
{
    [Fact]
    public void Vocabulary_IsConvertedToBoundedDistinctPrompt()
    {
        VocabularyEntry[] vocabulary =
        [
            new("codex", "Codex"),
            new("CODEx", "duplicate"),
            new("whisper net", "Whisper.net"),
            new(" ", "ignored"),
        ];

        string prompt = Assert.IsType<string>(
            WhisperPromptBuilder.FromVocabulary(vocabulary));

        Assert.Equal(
            "Preferred vocabulary: codex means Codex, " +
            "whisper net means Whisper.net",
            prompt);
        Assert.True(prompt.Length <= WhisperPromptBuilder.MaximumPromptLength);
    }

    [Fact]
    public void EmptyVocabulary_HasNoPrompt()
    {
        Assert.Null(WhisperPromptBuilder.FromVocabulary([]));
        Assert.Throws<ArgumentNullException>(
            () => WhisperPromptBuilder.FromVocabulary(null!));
    }

    [Fact]
    public void WorkDomain_AddsBoundedPromptTermsWithoutCustomVocabulary()
    {
        string prompt = Assert.IsType<string>(
            WhisperPromptBuilder.FromVocabulary(
                [],
                WorkDomainPreset.SoftwareDevelopment));

        Assert.Contains("Software development", prompt);
        Assert.Contains("C#", prompt);
        Assert.Contains("Kubernetes", prompt);
        Assert.True(prompt.Length <= WhisperPromptBuilder.MaximumPromptLength);
    }
}
