using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;

namespace DictaClone.Text;

public sealed class DeterministicTextProcessor : ITextProcessor
{
    public Task<string> ProcessAsync(
        string transcript,
        DictationMode mode,
        TextProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        string text = TranscriptNormalizer.Normalize(transcript);

        if (settings.EnableCorrections)
        {
            text = ConservativeCorrectionProcessor.Apply(text);
        }

        text = VocabularyProcessor.Apply(text, settings.Vocabulary);
        text = TextExpansionProcessor.Apply(text, settings.Expansions);
        text = TranscriptNormalizer.Normalize(text);

        return Task.FromResult(text);
    }
}
