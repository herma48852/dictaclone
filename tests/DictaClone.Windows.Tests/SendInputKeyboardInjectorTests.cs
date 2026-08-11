using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class SendInputKeyboardInjectorTests
{
    [Fact]
    public void InputStructureSize_MatchesNativeWindowsLayout()
    {
        int expected = Environment.Is64BitProcess ? 40 : 28;

        Assert.Equal(expected, SendInputKeyboardInjector.InputStructureSize);
    }

    [Fact]
    public void TerminalPaste_UsesPhysicalControlShiftVChord()
    {
        ClipboardInsertionKeystroke keystroke =
            SendInputKeyboardInjector.GetClipboardInsertionKeystroke(
                ClipboardInsertionShortcut.TerminalPaste);

        Assert.Equal(0x56, keystroke.VirtualKey);
        Assert.True(keystroke.Shift);
        Assert.True(keystroke.Control);
        Assert.False(keystroke.Alt);
        Assert.True(keystroke.UseScanCodes);
    }
}
