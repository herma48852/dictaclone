using System.Text;
using System.Text.RegularExpressions;

namespace DictaClone.Text;

public static partial class TranscriptNormalizer
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string normalizedNewlines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        if (!normalizedNewlines.Contains('\n'))
        {
            return NormalizeProseLine(normalizedNewlines);
        }

        string[] lines = normalizedNewlines.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimEnd();
        }

        return string.Join('\n', lines).Trim('\n');
    }

    private static string NormalizeProseLine(string text)
    {
        string result = HorizontalWhitespace().Replace(text.Trim(), " ");
        result = SpaceBeforePunctuation().Replace(result, "$1");
        result = MissingSpaceAfterPunctuation().Replace(result, "$1 ");

        if (result.Length > 0 && char.IsLower(result[0]))
        {
            result = char.ToUpperInvariant(result[0]) + result[1..];
        }

        return result;
    }

    [GeneratedRegex(@"[^\S\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@"\s+([,.;:!?])", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(
        @"([,;:!?])(?=[\p{L}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex MissingSpaceAfterPunctuation();
}
