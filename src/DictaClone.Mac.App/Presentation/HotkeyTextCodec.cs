using DictaClone.Core.Hotkeys;

namespace DictaClone.Mac.Presentation;

internal static class HotkeyTextCodec
{
    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        Add(parts, chord.Modifiers, HotkeyModifiers.Control, "Control");
        Add(parts, chord.Modifiers, HotkeyModifiers.Alt, "Option");
        Add(parts, chord.Modifiers, HotkeyModifiers.Shift, "Shift");
        Add(parts, chord.Modifiers, HotkeyModifiers.Windows, "Command");
        if (chord.PrimaryKey is { } key)
        {
            parts.Add(key.ToString());
        }

        return string.Join('+', parts);
    }

    public static bool TryParse(string text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        HotkeyKey? primaryKey = null;
        foreach (string rawPart in text.Split(
                     '+',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                case "⌃":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                case "OPTION":
                case "OPT":
                case "⌥":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                case "⇧":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                case "COMMAND":
                case "CMD":
                case "META":
                case "⌘":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    if (primaryKey.HasValue ||
                        !Enum.TryParse(rawPart, ignoreCase: true, out HotkeyKey key))
                    {
                        return false;
                    }

                    primaryKey = key;
                    break;
            }
        }

        chord = new(modifiers, primaryKey);
        return chord.IsValid;
    }

    private static void Add(
        List<string> parts,
        HotkeyModifiers actual,
        HotkeyModifiers expected,
        string label)
    {
        if (actual.HasFlag(expected))
        {
            parts.Add(label);
        }
    }
}
