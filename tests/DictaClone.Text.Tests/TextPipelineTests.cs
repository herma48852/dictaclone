using System.Collections.Immutable;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;

namespace DictaClone.Text.Tests;

public sealed class TextPipelineTests
{
    [Theory]
    [InlineData("  hello   world  !", "Hello world!")]
    [InlineData("hello,world", "Hello, world")]
    [InlineData("already Fine.", "Already Fine.")]
    [InlineData("", "")]
    public void Normalizer_CleansConservativeProse(
        string input,
        string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalizer_PreservesMultilineIndentationAndUnicode()
    {
        const string Input = "  α = 1;  \r\n\treturn α; \r\n";

        string result = TranscriptNormalizer.Normalize(Input);

        Assert.Equal("  α = 1;\n\treturn α;", result);
    }

    [Fact]
    public void Vocabulary_UsesLongestWholePhraseAndPreservesWrittenForm()
    {
        ImmutableArray<VocabularyEntry> vocabulary =
        [
            new("kube", "wrong"),
            new("kube control", "kubectl"),
            new("see sharp", "C#"),
            new("home variable", "$HOME"),
        ];

        string result = VocabularyProcessor.Apply(
            "kube control and see sharp, home variable, not kubernetes",
            vocabulary);

        Assert.Equal("kubectl and C#, $HOME, not kubernetes", result);
    }

    [Fact]
    public void Vocabulary_IgnoresBlankEntries()
    {
        string result = VocabularyProcessor.Apply(
            "unchanged",
            [new VocabularyEntry(" ", "replacement")]);

        Assert.Equal("unchanged", result);
    }

    [Theory]
    [InlineData("signature", "Kind regards,\nAda", "Kind regards,\nAda")]
    [InlineData("Signature.", "Kind regards,\nAda", "Kind regards,\nAda")]
    [InlineData("use signature here", "Kind regards", "use signature here")]
    public void Expansion_RequiresTheWholeUtterance(
        string input,
        string replacement,
        string expected)
    {
        string result = TextExpansionProcessor.Apply(
            input,
            [new TextExpansion("signature", replacement)]);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Send it Tuesday, actually Wednesday.", "Send it Wednesday.")]
    [InlineData("Use the small model, I mean the base model.", "Use the base model.")]
    [InlineData("I wanted tea actually coffee.", "I wanted coffee.")]
    [InlineData("Red actually blue.", "Blue.")]
    public void Corrections_ReplaceOnlyAComparableTrailingPhrase(
        string input,
        string expected)
    {
        Assert.Equal(expected, ConservativeCorrectionProcessor.Apply(input));
    }

    [Theory]
    [InlineData(
        "Keep this sentence. actually replace with far too many individual words at once.",
        "Keep this sentence. replace with far too many individual words at once.")]
    [InlineData("actually blue.", "blue.")]
    public void Corrections_FallBackToRemovingOnlyTheMarker(
        string input,
        string expected)
    {
        Assert.Equal(expected, ConservativeCorrectionProcessor.Apply(input));
    }

    [Fact]
    public void Corrections_HandleLinesIndependently()
    {
        string result = ConservativeCorrectionProcessor.Apply(
            "Pick red, actually blue.\nPick up, I mean down.");

        Assert.Equal("Pick blue.\nPick down.", result);
    }

    [Fact]
    public async Task Pipeline_AppliesCorrectionVocabularyExpansionAndNormalization()
    {
        var processor = new DeterministicTextProcessor();
        var settings = new TextProcessingSettings(
            [new VocabularyEntry("jay son", "JSON")],
            [new TextExpansion("postal address", "123 Main Street")],
            EnableCorrections: true);

        string corrected = await processor.ProcessAsync(
            "use old format, actually jay son",
            DictationMode.Dictation,
            settings,
            CancellationToken.None);
        string expanded = await processor.ProcessAsync(
            "postal address.",
            DictationMode.Typing,
            settings,
            CancellationToken.None);

        Assert.Equal("Use JSON", corrected);
        Assert.Equal("123 Main Street", expanded);
    }

    [Fact]
    public async Task Pipeline_CanDisableCorrections()
    {
        var processor = new DeterministicTextProcessor();
        var settings = new TextProcessingSettings(
            ImmutableArray<VocabularyEntry>.Empty,
            ImmutableArray<TextExpansion>.Empty,
            EnableCorrections: false);

        string result = await processor.ProcessAsync(
            "red, actually blue",
            DictationMode.SmartEdit,
            settings,
            CancellationToken.None);

        Assert.Equal("Red, actually blue", result);
    }

    [Fact]
    public async Task Pipeline_ValidatesArgumentsAndCancellation()
    {
        var processor = new DeterministicTextProcessor();
        TextProcessingSettings settings = DictaCloneSettings.Default.Text;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => processor.ProcessAsync(
                null!,
                DictationMode.Dictation,
                settings,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => processor.ProcessAsync(
                "text",
                DictationMode.Dictation,
                null!,
                CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(
                "text",
                DictationMode.Dictation,
                settings,
                new(canceled: true)));
    }

    [Fact]
    public void Components_RejectNullText()
    {
        Assert.Throws<ArgumentNullException>(
            () => TranscriptNormalizer.Normalize(null!));
        Assert.Throws<ArgumentNullException>(
            () => VocabularyProcessor.Apply(
                null!,
                ImmutableArray<VocabularyEntry>.Empty));
        Assert.Throws<ArgumentNullException>(
            () => TextExpansionProcessor.Apply(
                null!,
                ImmutableArray<TextExpansion>.Empty));
        Assert.Throws<ArgumentNullException>(
            () => ConservativeCorrectionProcessor.Apply(null!));
    }
}
