using DictaClone.Core.Settings;

namespace DictaClone.Speech;

public static class WhisperPromptBuilder
{
    public const int MaximumPromptLength = 2048;

    public static string? FromVocabulary(
        IEnumerable<VocabularyEntry> vocabulary,
        WorkDomainPreset workDomain = WorkDomainPreset.General)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        string[] entries = vocabulary
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.SpokenForm) &&
                !string.IsNullOrWhiteSpace(entry.WrittenForm))
            .DistinctBy(entry => entry.SpokenForm, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
                $"{entry.SpokenForm.Trim()} means {entry.WrittenForm.Trim()}")
            .ToArray();

        IReadOnlyList<string> domainTerms =
            WorkDomainCatalog.GetPromptTerms(workDomain);
        string? domainPrompt = domainTerms.Count == 0
            ? null
            : $"Work domain: {WorkDomainCatalog.GetDisplayName(workDomain)}. " +
              $"Preferred terms: {string.Join(", ", domainTerms)}.";

        if (entries.Length == 0)
        {
            return domainPrompt;
        }

        string prefix = domainPrompt is null
            ? "Preferred vocabulary: "
            : domainPrompt + " Preferred vocabulary: ";
        var accepted = new List<string>();
        int length = prefix.Length;

        foreach (string entry in entries)
        {
            int separatorLength = accepted.Count == 0 ? 0 : 2;
            if (length + separatorLength + entry.Length > MaximumPromptLength)
            {
                break;
            }

            accepted.Add(entry);
            length += separatorLength + entry.Length;
        }

        return accepted.Count == 0
            ? null
            : prefix + string.Join(", ", accepted);
    }
}
