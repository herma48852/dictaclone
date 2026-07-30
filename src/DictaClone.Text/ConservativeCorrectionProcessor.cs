using System.Text.RegularExpressions;

namespace DictaClone.Text;

public static partial class ConservativeCorrectionProcessor
{
    private const int MaximumReplacementWords = 6;

    public static string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = ApplyToLine(lines[index]);
        }

        return string.Join('\n', lines);
    }

    private static string ApplyToLine(string line)
    {
        Match match = CorrectionMarker().Match(line);
        while (match.Success)
        {
            int replacementStart = match.Index + match.Length;
            string replacementWithPunctuation = line[replacementStart..].Trim();
            string replacement = replacementWithPunctuation.TrimEnd('.', '!', '?');
            string punctuation = replacementWithPunctuation[replacement.Length..];
            string before = line[..match.Index].TrimEnd(' ', ',');

            string[] replacementWords = Words().Matches(replacement)
                .Select(word => word.Value)
                .ToArray();
            MatchCollection beforeWords = Words().Matches(before);

            if (replacementWords.Length is 0 or > MaximumReplacementWords ||
                beforeWords.Count < replacementWords.Length)
            {
                line = JoinCorrection(before, replacementWithPunctuation);
                break;
            }

            Match firstReplacedWord =
                beforeWords[beforeWords.Count - replacementWords.Length];
            int sentenceBoundary = LastSentenceBoundary(before);
            if (firstReplacedWord.Index <= sentenceBoundary)
            {
                line = JoinCorrection(before, replacementWithPunctuation);
                break;
            }

            string prefix = before[..firstReplacedWord.Index].TrimEnd();
            line = string.IsNullOrEmpty(prefix)
                ? string.Concat(Capitalize(replacement), punctuation)
                : string.Concat(prefix, " ", replacement, punctuation);
            match = CorrectionMarker().Match(line);
        }

        return line;
    }

    private static string JoinCorrection(string before, string replacement) =>
        string.IsNullOrEmpty(before)
            ? replacement
            : string.Concat(before, " ", replacement);

    private static string Capitalize(string text) =>
        text.Length > 0 && char.IsLower(text[0])
            ? char.ToUpperInvariant(text[0]) + text[1..]
            : text;

    private static int LastSentenceBoundary(string text)
    {
        int period = text.LastIndexOf('.');
        int question = text.LastIndexOf('?');
        int exclamation = text.LastIndexOf('!');
        return Math.Max(period, Math.Max(question, exclamation));
    }

    [GeneratedRegex(
        @"(?:,\s*)?\b(?:actually|i mean)\b\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrectionMarker();

    [GeneratedRegex(@"[\p{L}\p{N}_'-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
