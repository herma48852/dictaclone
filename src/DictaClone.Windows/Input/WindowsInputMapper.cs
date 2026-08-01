using DictaClone.Core.Hotkeys;

namespace DictaClone.Windows.Input;

public static class WindowsInputMapper
{
    public static bool TryMapKeyboard(
        uint virtualKey,
        out RawInputControl control)
    {
        if (TryMapModifier(virtualKey, out PhysicalModifier modifier))
        {
            control = RawInputControl.ForModifier(modifier);
            return true;
        }

        if (TryMapPrimaryKey(virtualKey, out HotkeyKey primaryKey))
        {
            control = RawInputControl.ForPrimaryKey(primaryKey);
            return true;
        }

        control = default;
        return false;
    }

    public static bool TryMapMouse(
        uint message,
        uint mouseData,
        out RawInputControl control)
    {
        HotkeyKey? key = message switch
        {
            0x0207 or 0x0208 => HotkeyKey.MouseMiddle,
            0x020B or 0x020C when HighWord(mouseData) == 1 =>
                HotkeyKey.MouseButton4,
            0x020B or 0x020C when HighWord(mouseData) == 2 =>
                HotkeyKey.MouseButton5,
            _ => null,
        };

        control = key.HasValue
            ? RawInputControl.ForPrimaryKey(key.Value)
            : default;
        return key.HasValue;
    }

    public static bool IsPressedMessage(uint message) =>
        message is 0x0100 or 0x0104 or 0x0207 or 0x020B;

    private static bool TryMapModifier(
        uint virtualKey,
        out PhysicalModifier modifier)
    {
        modifier = virtualKey switch
        {
            0xA2 => PhysicalModifier.LeftControl,
            0xA3 => PhysicalModifier.RightControl,
            0xA4 => PhysicalModifier.LeftAlt,
            0xA5 => PhysicalModifier.RightAlt,
            0xA0 => PhysicalModifier.LeftShift,
            0xA1 => PhysicalModifier.RightShift,
            0x5B => PhysicalModifier.LeftWindows,
            0x5C => PhysicalModifier.RightWindows,
            _ => default,
        };

        return virtualKey is
            0xA2 or 0xA3 or 0xA4 or 0xA5 or
            0xA0 or 0xA1 or 0x5B or 0x5C;
    }

    private static bool TryMapPrimaryKey(
        uint virtualKey,
        out HotkeyKey key)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            key = (HotkeyKey)((int)HotkeyKey.A + (virtualKey - 0x41));
            return true;
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            key = (HotkeyKey)((int)HotkeyKey.F1 + (virtualKey - 0x70));
            return true;
        }

        key = virtualKey switch
        {
            0x20 => HotkeyKey.Space,
            0x0D => HotkeyKey.Enter,
            0x1B => HotkeyKey.Escape,
            _ => default,
        };

        return virtualKey is 0x20 or 0x0D or 0x1B;
    }

    private static uint HighWord(uint value) => (value >> 16) & 0xFFFF;
}
