using DictaClone.Core.Hotkeys;
using DictaClone.Core.Input;

namespace DictaClone.Mac.Input;

public static class MacInputMapper
{
    public static bool TryMapKeyboard(
        ushort keyCode,
        out RawInputControl control)
    {
        if (TryMapModifier(keyCode, out PhysicalModifier modifier))
        {
            control = RawInputControl.ForModifier(modifier);
            return true;
        }

        if (TryMapPrimaryKey(keyCode, out HotkeyKey primaryKey))
        {
            control = RawInputControl.ForPrimaryKey(primaryKey);
            return true;
        }

        control = default;
        return false;
    }

    public static bool TryMapMouse(
        long buttonNumber,
        out RawInputControl control)
    {
        HotkeyKey? key = buttonNumber switch
        {
            2 => HotkeyKey.MouseMiddle,
            3 => HotkeyKey.MouseButton4,
            4 => HotkeyKey.MouseButton5,
            _ => null,
        };
        control = key is { } mapped
            ? RawInputControl.ForPrimaryKey(mapped)
            : default;
        return key.HasValue;
    }

    public static bool TryMapMediaKey(
        int mediaKeyType,
        out RawInputControl control)
    {
        const int SoundDown = 1;
        if (mediaKeyType == SoundDown)
        {
            control = RawInputControl.ForPrimaryKey(HotkeyKey.VolumeDown);
            return true;
        }

        control = default;
        return false;
    }

    public static bool IsModifierPressed(ushort keyCode, ulong flags)
    {
        const ulong Shift = 1UL << 17;
        const ulong Control = 1UL << 18;
        const ulong Option = 1UL << 19;
        const ulong Command = 1UL << 20;

        return keyCode switch
        {
            55 or 54 => (flags & Command) != 0,
            56 or 60 => (flags & Shift) != 0,
            58 or 61 => (flags & Option) != 0,
            59 or 62 => (flags & Control) != 0,
            _ => false,
        };
    }

    private static bool TryMapModifier(
        ushort keyCode,
        out PhysicalModifier modifier)
    {
        modifier = keyCode switch
        {
            55 => PhysicalModifier.LeftWindows,
            54 => PhysicalModifier.RightWindows,
            56 => PhysicalModifier.LeftShift,
            60 => PhysicalModifier.RightShift,
            58 => PhysicalModifier.LeftAlt,
            61 => PhysicalModifier.RightAlt,
            59 => PhysicalModifier.LeftControl,
            62 => PhysicalModifier.RightControl,
            _ => default,
        };
        return keyCode is 55 or 54 or 56 or 60 or 58 or 61 or 59 or 62;
    }

    private static bool TryMapPrimaryKey(
        ushort keyCode,
        out HotkeyKey key)
    {
        key = keyCode switch
        {
            49 => HotkeyKey.Space,
            36 => HotkeyKey.Enter,
            53 => HotkeyKey.Escape,
            0 => HotkeyKey.A,
            11 => HotkeyKey.B,
            8 => HotkeyKey.C,
            2 => HotkeyKey.D,
            14 => HotkeyKey.E,
            3 => HotkeyKey.F,
            5 => HotkeyKey.G,
            4 => HotkeyKey.H,
            34 => HotkeyKey.I,
            38 => HotkeyKey.J,
            40 => HotkeyKey.K,
            37 => HotkeyKey.L,
            46 => HotkeyKey.M,
            45 => HotkeyKey.N,
            31 => HotkeyKey.O,
            35 => HotkeyKey.P,
            12 => HotkeyKey.Q,
            15 => HotkeyKey.R,
            1 => HotkeyKey.S,
            17 => HotkeyKey.T,
            32 => HotkeyKey.U,
            9 => HotkeyKey.V,
            13 => HotkeyKey.W,
            7 => HotkeyKey.X,
            16 => HotkeyKey.Y,
            6 => HotkeyKey.Z,
            122 => HotkeyKey.F1,
            120 => HotkeyKey.F2,
            99 => HotkeyKey.F3,
            118 => HotkeyKey.F4,
            96 => HotkeyKey.F5,
            97 => HotkeyKey.F6,
            98 => HotkeyKey.F7,
            100 => HotkeyKey.F8,
            101 => HotkeyKey.F9,
            109 => HotkeyKey.F10,
            103 => HotkeyKey.F11,
            111 => HotkeyKey.F12,
            105 => HotkeyKey.F13,
            107 => HotkeyKey.F14,
            113 => HotkeyKey.F15,
            106 => HotkeyKey.F16,
            64 => HotkeyKey.F17,
            79 => HotkeyKey.F18,
            80 => HotkeyKey.F19,
            90 => HotkeyKey.F20,
            _ => default,
        };

        return keyCode is 49 or 36 or 53 or
            0 or 11 or 8 or 2 or 14 or 3 or 5 or 4 or 34 or 38 or 40 or
            37 or 46 or 45 or 31 or 35 or 12 or 15 or 1 or 17 or 32 or
            9 or 13 or 7 or 16 or 6 or
            122 or 120 or 99 or 118 or 96 or 97 or 98 or 100 or 101 or
            109 or 103 or 111 or 105 or 107 or 113 or 106 or 64 or 79 or
            80 or 90;
    }
}

internal sealed class MacModifierStateTracker
{
    private readonly HashSet<ushort> _pressedKeyCodes = [];

    public bool Update(ushort keyCode, ulong flags)
    {
        if (_pressedKeyCodes.Remove(keyCode))
        {
            return false;
        }

        if (MacInputMapper.IsModifierPressed(keyCode, flags))
        {
            _pressedKeyCodes.Add(keyCode);
            return true;
        }

        return false;
    }

    public void Reset() => _pressedKeyCodes.Clear();
}
