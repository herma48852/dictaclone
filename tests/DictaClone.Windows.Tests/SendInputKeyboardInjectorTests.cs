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
}
