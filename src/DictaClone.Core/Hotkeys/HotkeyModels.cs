using System.Collections.Immutable;

namespace DictaClone.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
}

public enum PhysicalModifier
{
    LeftControl,
    RightControl,
    LeftAlt,
    RightAlt,
    LeftShift,
    RightShift,
    LeftWindows,
    RightWindows,
}

public enum HotkeyKey
{
    Space,
    Enter,
    Escape,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
    MouseMiddle,
    MouseButton4,
    MouseButton5,
}

public enum HotkeyAction
{
    Dictation,
    SmartEdit,
    TypingMode,
    Cancel,
}

public enum HotkeyActivation
{
    Hold,
    Toggle,
}

public readonly record struct HotkeyChord(
    HotkeyModifiers Modifiers,
    HotkeyKey? PrimaryKey = null)
{
    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Control |
        HotkeyModifiers.Alt |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Windows;

    public bool IsValid =>
        (Modifiers & ~AllModifiers) == HotkeyModifiers.None &&
        (Modifiers != HotkeyModifiers.None || PrimaryKey.HasValue) &&
        (!PrimaryKey.HasValue || Enum.IsDefined(PrimaryKey.Value));

    public static HotkeyChord FromPhysicalModifiers(
        IEnumerable<PhysicalModifier> modifiers,
        HotkeyKey? primaryKey = null)
    {
        ArgumentNullException.ThrowIfNull(modifiers);

        HotkeyModifiers normalized = HotkeyModifiers.None;
        foreach (PhysicalModifier modifier in modifiers)
        {
            normalized |= modifier switch
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
                    nameof(modifiers),
                    modifier,
                    "Unknown physical modifier."),
            };
        }

        return new HotkeyChord(normalized, primaryKey);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        AddIfSet(parts, HotkeyModifiers.Control, "Ctrl");
        AddIfSet(parts, HotkeyModifiers.Alt, "Alt");
        AddIfSet(parts, HotkeyModifiers.Shift, "Shift");
        AddIfSet(parts, HotkeyModifiers.Windows, "Win");

        if (PrimaryKey is { } key)
        {
            parts.Add(key.ToString());
        }

        return parts.Count == 0 ? "(empty)" : string.Join('+', parts);
    }

    private void AddIfSet(
        List<string> parts,
        HotkeyModifiers modifier,
        string label)
    {
        if (Modifiers.HasFlag(modifier))
        {
            parts.Add(label);
        }
    }
}

public sealed record HotkeyBinding(
    HotkeyAction Action,
    HotkeyChord Chord,
    bool Enabled = true,
    HotkeyActivation Activation = HotkeyActivation.Hold);

public sealed record HotkeyConflict(
    HotkeyAction First,
    HotkeyAction Second,
    HotkeyChord Chord);

public static class HotkeyConflictDetector
{
    public static ImmutableArray<HotkeyConflict> Find(
        IEnumerable<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        HotkeyBinding[] enabled = bindings.Where(binding => binding.Enabled).ToArray();
        var conflicts = ImmutableArray.CreateBuilder<HotkeyConflict>();

        for (int firstIndex = 0; firstIndex < enabled.Length; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < enabled.Length;
                 secondIndex++)
            {
                if (enabled[firstIndex].Chord == enabled[secondIndex].Chord)
                {
                    conflicts.Add(new(
                        enabled[firstIndex].Action,
                        enabled[secondIndex].Action,
                        enabled[firstIndex].Chord));
                }
            }
        }

        return conflicts.ToImmutable();
    }
}

public static class HotkeyDefaults
{
    public static ImmutableArray<HotkeyBinding> Bindings { get; } =
    [
        new(
            HotkeyAction.Dictation,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Windows,
                HotkeyKey.Space)),
        new(
            HotkeyAction.SmartEdit,
            new HotkeyChord(
                HotkeyModifiers.Alt |
                HotkeyModifiers.Shift,
                HotkeyKey.Space),
            Enabled: false),
        new(
            HotkeyAction.TypingMode,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                HotkeyKey.Space)),
        new(
            HotkeyAction.Cancel,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Windows,
                HotkeyKey.Escape)),
    ];
}
