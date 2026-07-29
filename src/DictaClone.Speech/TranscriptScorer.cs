using System.Globalization;
using System.Text;

namespace DictaClone.Speech;

public static class TranscriptScorer
{
    public static TranscriptScore Score(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        string[] referenceWords = Tokenize(expected);
        string[] actualWords = Tokenize(actual);
        int editDistance = CalculateEditDistance(referenceWords, actualWords);
        double wordErrorRate = referenceWords.Length == 0
            ? (actualWords.Length == 0 ? 0 : 1)
            : (double)editDistance / referenceWords.Length;

        return new TranscriptScore(referenceWords.Length, editDistance, wordErrorRate);
    }

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = new StringBuilder(text.Length);
        bool previousWasSpace = true;

        foreach (char character in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                normalized.Append(' ');
                previousWasSpace = true;
            }
        }

        return normalized.ToString().Trim();
    }

    private static string[] Tokenize(string text)
    {
        string normalized = Normalize(text);
        return normalized.Length == 0
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static int CalculateEditDistance(
        string[] referenceWords,
        string[] actualWords)
    {
        var previous = new int[actualWords.Length + 1];
        var current = new int[actualWords.Length + 1];

        for (int index = 0; index <= actualWords.Length; index++)
        {
            previous[index] = index;
        }

        for (int referenceIndex = 1; referenceIndex <= referenceWords.Length; referenceIndex++)
        {
            current[0] = referenceIndex;

            for (int actualIndex = 1; actualIndex <= actualWords.Length; actualIndex++)
            {
                int substitutionCost = string.Equals(
                    referenceWords[referenceIndex - 1],
                    actualWords[actualIndex - 1],
                    StringComparison.Ordinal)
                    ? 0
                    : 1;

                current[actualIndex] = Math.Min(
                    Math.Min(
                        current[actualIndex - 1] + 1,
                        previous[actualIndex] + 1),
                    previous[actualIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[actualWords.Length];
    }
}
