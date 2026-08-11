using System.Windows.Input;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Input;
using DictaClone.Windows.Input;

namespace DictaClone.App.Presentation;

public static class WpfInputMapper
{
    public static bool TryMapKey(Key key, out RawInputControl control)
    {
        if (TryMapModifier(key, out PhysicalModifier modifier))
        {
            control = RawInputControl.ForModifier(modifier);
            return true;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            var primaryKey = (HotkeyKey)(
                (int)HotkeyKey.A + ((int)key - (int)Key.A));
            control = RawInputControl.ForPrimaryKey(primaryKey);
            return true;
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            var primaryKey = (HotkeyKey)(
                (int)HotkeyKey.F1 + ((int)key - (int)Key.F1));
            control = RawInputControl.ForPrimaryKey(primaryKey);
            return true;
        }

        HotkeyKey? mapped = key switch
        {
            Key.Space => HotkeyKey.Space,
            Key.Enter or Key.Return => HotkeyKey.Enter,
            Key.Escape => HotkeyKey.Escape,
            _ => null,
        };
        control = mapped.HasValue
            ? RawInputControl.ForPrimaryKey(mapped.Value)
            : default;
        return mapped.HasValue;
    }

    public static bool TryMapMouse(
        MouseButton button,
        out RawInputControl control)
    {
        HotkeyKey? mapped = button switch
        {
            MouseButton.Middle => HotkeyKey.MouseMiddle,
            MouseButton.XButton1 => HotkeyKey.MouseButton4,
            MouseButton.XButton2 => HotkeyKey.MouseButton5,
            _ => null,
        };
        control = mapped.HasValue
            ? RawInputControl.ForPrimaryKey(mapped.Value)
            : default;
        return mapped.HasValue;
    }

    private static bool TryMapModifier(
        Key key,
        out PhysicalModifier modifier)
    {
        modifier = key switch
        {
            Key.LeftCtrl => PhysicalModifier.LeftControl,
            Key.RightCtrl => PhysicalModifier.RightControl,
            Key.LeftAlt => PhysicalModifier.LeftAlt,
            Key.RightAlt => PhysicalModifier.RightAlt,
            Key.LeftShift => PhysicalModifier.LeftShift,
            Key.RightShift => PhysicalModifier.RightShift,
            Key.LWin => PhysicalModifier.LeftWindows,
            Key.RWin => PhysicalModifier.RightWindows,
            _ => default,
        };

        return key is
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;
    }
}
