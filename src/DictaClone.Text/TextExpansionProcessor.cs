using System.Collections.Immutable;
using DictaClone.Core.Settings;

namespace DictaClone.Text;

public static class TextExpansionProcessor
{
    public static string Apply(
        string text,
        ImmutableArray<TextExpansion> expansions)
    {
        ArgumentNullException.ThrowIfNull(text);

        string candidate = text.Trim();
        TextExpansion? match = expansions.FirstOrDefault(
            expansion => string.Equals(
                expansion.Trigger.Trim().TrimEnd('.', '!', '?'),
                candidate.TrimEnd('.', '!', '?'),
                StringComparison.OrdinalIgnoreCase));

        return match?.Replacement ?? text;
    }
}
