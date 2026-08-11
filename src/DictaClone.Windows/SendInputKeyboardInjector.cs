using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DictaClone.Core.Dictation;

namespace DictaClone.Windows;

[SupportedOSPlatform("windows")]
internal sealed partial class SendInputKeyboardInjector : IKeyboardInjector
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MapVirtualKeyToScanCode = 0;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyMenu = 0x12;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyV = 0x56;
    private const ushort VirtualKeyY = 0x59;
    private const ushort VirtualKeyC = 0x43;
    private const ushort VirtualKeyW = 0x57;

    internal static int InputStructureSize => Marshal.SizeOf<NativeInput>();

    public void SendClipboardInsert(ClipboardInsertionShortcut shortcut)
    {
        ClipboardInsertionKeystroke keystroke =
            GetClipboardInsertionKeystroke(shortcut);
        SendVirtualKeyChord(
            keystroke.VirtualKey,
            keystroke.Shift,
            keystroke.Control,
            keystroke.Alt,
            keystroke.UseScanCodes);
    }

    internal static ClipboardInsertionKeystroke GetClipboardInsertionKeystroke(
        ClipboardInsertionShortcut shortcut) => shortcut switch
        {
            ClipboardInsertionShortcut.StandardPaste => new(
                VirtualKeyV,
                Shift: false,
                Control: true,
                Alt: false,
                UseScanCodes: false),
            ClipboardInsertionShortcut.TerminalPaste => new(
                VirtualKeyV,
                Shift: true,
                Control: true,
                Alt: false,
                UseScanCodes: true),
            ClipboardInsertionShortcut.EmacsYank => new(
                VirtualKeyY,
                Shift: false,
                Control: true,
                Alt: false,
                UseScanCodes: false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(shortcut),
                shortcut,
                "Unknown clipboard insertion shortcut."),
        };

    internal static void SendSelectionCopy(bool emacs) => SendVirtualKeyChord(
        emacs ? VirtualKeyW : VirtualKeyC,
        shift: false,
        control: !emacs,
        alt: emacs,
        useScanCodes: false);

    public void SendUnicode(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var inputs = new NativeInput[text.Length * 2];
        int inputIndex = 0;
        foreach (char character in text)
        {
            inputs[inputIndex++] = CreateKeyboardInput(
                virtualKey: 0,
                scanCode: character,
                KeyEventUnicode);
            inputs[inputIndex++] = CreateKeyboardInput(
                virtualKey: 0,
                scanCode: character,
                KeyEventUnicode | KeyEventKeyUp);
        }

        Send(inputs);
    }

    public void SendVirtualKey(VirtualKey key) => SendVirtualKeyChord(
        (ushort)key,
        shift: false,
        control: false,
        alt: false,
        useScanCodes: true);

    public bool TrySendMappedCharacter(char character)
    {
        nint keyboardLayout = GetKeyboardLayout(threadId: 0);
        short mapping = VkKeyScan(character, keyboardLayout);
        if (mapping == -1)
        {
            return false;
        }

        ushort virtualKey = unchecked((byte)(mapping & 0xFF));
        int modifiers = (mapping >> 8) & 0xFF;
        SendVirtualKeyChord(
            virtualKey,
            shift: (modifiers & 1) != 0,
            control: (modifiers & 2) != 0,
            alt: (modifiers & 4) != 0,
            useScanCodes: true,
            keyboardLayout);
        return true;
    }

    private static void SendVirtualKeyChord(
        ushort virtualKey,
        bool shift,
        bool control,
        bool alt,
        bool useScanCodes,
        nint keyboardLayout = default)
    {
        var pressed = new List<ushort>(capacity: 3);
        if (shift)
        {
            pressed.Add(VirtualKeyShift);
        }

        if (control)
        {
            pressed.Add(VirtualKeyControl);
        }

        if (alt)
        {
            pressed.Add(VirtualKeyMenu);
        }

        var inputs = new List<NativeInput>((pressed.Count * 2) + 2);
        foreach (ushort modifier in pressed)
        {
            inputs.Add(CreateVirtualKeyInput(
                modifier,
                keyUp: false,
                useScanCodes,
                keyboardLayout));
        }

        inputs.Add(CreateVirtualKeyInput(
            virtualKey,
            keyUp: false,
            useScanCodes,
            keyboardLayout));
        inputs.Add(CreateVirtualKeyInput(
            virtualKey,
            keyUp: true,
            useScanCodes,
            keyboardLayout));

        for (int index = pressed.Count - 1; index >= 0; index--)
        {
            inputs.Add(CreateVirtualKeyInput(
                pressed[index],
                keyUp: true,
                useScanCodes,
                keyboardLayout));
        }

        Send(inputs.ToArray());
    }

    private static NativeInput CreateVirtualKeyInput(
        ushort virtualKey,
        bool keyUp,
        bool useScanCodes,
        nint keyboardLayout)
    {
        uint flags = keyUp ? KeyEventKeyUp : 0;
        ushort scanCode = 0;
        if (useScanCodes)
        {
            flags |= KeyEventScanCode;
            scanCode = unchecked((ushort)MapVirtualKey(
                virtualKey,
                MapVirtualKeyToScanCode,
                keyboardLayout == nint.Zero
                    ? GetKeyboardLayout(threadId: 0)
                    : keyboardLayout));
        }

        return CreateKeyboardInput(
            useScanCodes ? (ushort)0 : virtualKey,
            scanCode,
            flags);
    }

    private static NativeInput CreateKeyboardInput(
        ushort virtualKey,
        ushort scanCode,
        uint flags) => new()
        {
            Type = InputKeyboard,
            Union = new()
            {
                Keyboard = new()
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                },
            },
        };

    private static unsafe void Send(NativeInput[] inputs)
    {
        fixed (NativeInput* pointer = inputs)
        {
            uint sent = SendInput(
                unchecked((uint)inputs.Length),
                pointer,
                Marshal.SizeOf<NativeInput>());
            if (sent != inputs.Length)
            {
                throw new InputInjectionException(new Win32Exception(
                    Marshal.GetLastWin32Error()));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        // INPUT is a union of MOUSEINPUT, KEYBDINPUT, and HARDWAREINPUT.
        // Including the largest member is required because SendInput rejects a
        // cbSize smaller than the native INPUT structure, even for key events.
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(
        uint inputCount,
        NativeInput* inputs,
        int inputSize);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint threadId);

    [LibraryImport("user32.dll", EntryPoint = "VkKeyScanExW")]
    private static partial short VkKeyScan(
        ushort character,
        nint keyboardLayout);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static partial uint MapVirtualKey(
        uint code,
        uint mapType,
        nint keyboardLayout);
}

internal readonly record struct ClipboardInsertionKeystroke(
    ushort VirtualKey,
    bool Shift,
    bool Control,
    bool Alt,
    bool UseScanCodes);
