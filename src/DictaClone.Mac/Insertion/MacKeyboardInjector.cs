using System.Runtime.InteropServices;

namespace DictaClone.Mac.Insertion;

internal interface IMacKeyboardInjector
{
    void Paste();

    void TypeText(string text);

    void PressReturn();

    void PressTab();
}

internal sealed partial class MacKeyboardInjector : IMacKeyboardInjector
{
    private const ulong CommandFlag = 1UL << 20;
    private const int SourceUserDataField = 42;
    internal const long SyntheticEventMarker = 0x4449435441434C4F;

    public void Paste() => SendKey(keyCode: 9, CommandFlag);

    public void TypeText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        nint down = CGEventCreateKeyboardEvent(
            nint.Zero,
            virtualKey: 0,
            keyDown: true);
        nint up = CGEventCreateKeyboardEvent(
            nint.Zero,
            virtualKey: 0,
            keyDown: false);
        if (down == nint.Zero || up == nint.Zero)
        {
            Release(down);
            Release(up);
            throw new InvalidOperationException(
                "macOS could not allocate a Unicode keyboard event.");
        }

        try
        {
            char[] characters = text.ToCharArray();
            CGEventKeyboardSetUnicodeString(
                down,
                checked((nuint)characters.Length),
                characters);
            CGEventKeyboardSetUnicodeString(
                up,
                checked((nuint)characters.Length),
                characters);
            MarkAndPost(down);
            MarkAndPost(up);
        }
        finally
        {
            Release(down);
            Release(up);
        }
    }

    public void PressReturn() => SendKey(keyCode: 36, flags: 0);

    public void PressTab() => SendKey(keyCode: 48, flags: 0);

    private static void SendKey(ushort keyCode, ulong flags)
    {
        nint down = CGEventCreateKeyboardEvent(
            nint.Zero,
            keyCode,
            keyDown: true);
        nint up = CGEventCreateKeyboardEvent(
            nint.Zero,
            keyCode,
            keyDown: false);
        if (down == nint.Zero || up == nint.Zero)
        {
            Release(down);
            Release(up);
            throw new InvalidOperationException(
                "macOS could not allocate a keyboard event.");
        }

        try
        {
            CGEventSetFlags(down, flags);
            CGEventSetFlags(up, flags);
            MarkAndPost(down);
            MarkAndPost(up);
        }
        finally
        {
            Release(down);
            Release(up);
        }
    }

    private static void MarkAndPost(nint keyboardEvent)
    {
        CGEventSetIntegerValueField(
            keyboardEvent,
            SourceUserDataField,
            SyntheticEventMarker);
        CGEventPost(tap: 0, keyboardEvent);
    }

    private static void Release(nint value)
    {
        if (value != nint.Zero)
        {
            CFRelease(value);
        }
    }

    [LibraryImport(
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial nint CGEventCreateKeyboardEvent(
        nint source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [LibraryImport(
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGEventSetFlags(nint keyboardEvent, ulong flags);

    [DllImport(
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventKeyboardSetUnicodeString(
        nint keyboardEvent,
        nuint length,
        [In] char[] text);

    [LibraryImport(
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGEventSetIntegerValueField(
        nint keyboardEvent,
        int field,
        long value);

    [LibraryImport(
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial void CGEventPost(uint tap, nint keyboardEvent);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint value);
}
