using DictaClone.Core.Hotkeys;

namespace DictaClone.Windows.Input;

public readonly record struct RawInputControl
{
    private RawInputControl(
        PhysicalModifier? modifier,
        HotkeyKey? primaryKey)
    {
        Modifier = modifier;
        PrimaryKey = primaryKey;
    }

    public PhysicalModifier? Modifier { get; }

    public HotkeyKey? PrimaryKey { get; }

    public bool IsModifier => Modifier.HasValue;

    public static RawInputControl ForModifier(PhysicalModifier modifier) =>
        new(modifier, null);

    public static RawInputControl ForPrimaryKey(HotkeyKey primaryKey) =>
        new(null, primaryKey);
}

public readonly record struct RawInputEvent(
    RawInputControl Control,
    bool IsPressed,
    bool IsInjected = false);
