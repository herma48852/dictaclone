using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DictaClone.Core.Settings;

namespace DictaClone.Text;

public static class VocabularyProcessor
{
    public static string Apply(
        string text,
        ImmutableArray<VocabularyEntry> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(text);

        string result = text;
        foreach (VocabularyEntry entry in vocabulary
                     .OrderByDescending(item => item.SpokenForm.Length))
        {
            if (string.IsNullOrWhiteSpace(entry.SpokenForm))
            {
                continue;
            }

            string pattern =
                $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(entry.SpokenForm.Trim())}" +
                @"(?![\p{L}\p{N}_])";
            result = Regex.Replace(
                result,
                pattern,
                _ => entry.WrittenForm,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }

        return result;
    }
}
