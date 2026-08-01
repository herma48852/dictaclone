using DictaClone.Core.Hotkeys;

namespace DictaClone.Windows.Input;

public sealed class ShortcutRecordingSession
{
    private readonly HashSet<PhysicalModifier> _pressedModifiers = [];
    private HotkeyModifiers _capturedModifiers;

    public bool IsComplete { get; private set; }

    public HotkeyChord? Process(RawInputEvent input)
    {
        if (IsComplete || input.IsInjected)
        {
            return null;
        }

        if (input.Control.Modifier is { } modifier)
        {
            if (input.IsPressed)
            {
                if (_pressedModifiers.Add(modifier))
                {
                    _capturedModifiers |= Normalize(modifier);
                }
            }
            else
            {
                _pressedModifiers.Remove(modifier);
                if (_pressedModifiers.Count == 0 &&
                    _capturedModifiers != HotkeyModifiers.None)
                {
                    return Complete(new(_capturedModifiers));
                }
            }

            return null;
        }

        if (input.IsPressed && input.Control.PrimaryKey is { } primaryKey)
        {
            return Complete(new(_capturedModifiers, primaryKey));
        }

        return null;
    }

    private HotkeyChord Complete(HotkeyChord chord)
    {
        IsComplete = true;
        return chord;
    }

    private static HotkeyModifiers Normalize(PhysicalModifier modifier) =>
        modifier switch
        {
            PhysicalModifier.LeftControl or PhysicalModifier.RightControl =>
                HotkeyModifiers.Control,
            PhysicalModifier.LeftAlt or PhysicalModifier.RightAlt =>
                HotkeyModifiers.Alt,
            PhysicalModifier.LeftShift or PhysicalModifier.RightShift =>
                HotkeyModifiers.Shift,
            PhysicalModifier.LeftWindows or PhysicalModifier.RightWindows =>
                HotkeyModifiers.Windows,
            _ => throw new ArgumentOutOfRangeException(
                nameof(modifier),
                modifier,
                "Unknown physical modifier."),
        };
}
