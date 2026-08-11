using DictaClone.Core.Hotkeys;
using DictaClone.Core.Input;
using DictaClone.Windows.Input;

namespace DictaClone.Windows.Tests;

public sealed class WindowsInputMapperTests
{
    [Theory]
    [InlineData(0xA2, PhysicalModifier.LeftControl)]
    [InlineData(0xA3, PhysicalModifier.RightControl)]
    [InlineData(0xA4, PhysicalModifier.LeftAlt)]
    [InlineData(0xA5, PhysicalModifier.RightAlt)]
    [InlineData(0xA0, PhysicalModifier.LeftShift)]
    [InlineData(0xA1, PhysicalModifier.RightShift)]
    [InlineData(0x5B, PhysicalModifier.LeftWindows)]
    [InlineData(0x5C, PhysicalModifier.RightWindows)]
    public void KeyboardMapper_MapsPhysicalModifiers(
        uint virtualKey,
        PhysicalModifier expected)
    {
        Assert.True(WindowsInputMapper.TryMapKeyboard(
            virtualKey,
            out RawInputControl control));
        Assert.Equal(expected, control.Modifier);
        Assert.True(control.IsModifier);
    }

    [Theory]
    [InlineData(0x41, HotkeyKey.A)]
    [InlineData(0x5A, HotkeyKey.Z)]
    [InlineData(0x70, HotkeyKey.F1)]
    [InlineData(0x87, HotkeyKey.F24)]
    [InlineData(0x20, HotkeyKey.Space)]
    [InlineData(0x0D, HotkeyKey.Enter)]
    [InlineData(0x1B, HotkeyKey.Escape)]
    public void KeyboardMapper_MapsPrimaryKeys(
        uint virtualKey,
        HotkeyKey expected)
    {
        Assert.True(WindowsInputMapper.TryMapKeyboard(
            virtualKey,
            out RawInputControl control));
        Assert.Equal(expected, control.PrimaryKey);
        Assert.False(control.IsModifier);
    }

    [Fact]
    public void KeyboardMapper_RejectsUnsupportedKeys()
    {
        Assert.False(WindowsInputMapper.TryMapKeyboard(
            0x30,
            out RawInputControl control));
        Assert.Equal(default, control);
    }

    [Theory]
    [InlineData(0x0207, 0u, HotkeyKey.MouseMiddle)]
    [InlineData(0x0208, 0u, HotkeyKey.MouseMiddle)]
    [InlineData(0x020B, 0x00010000u, HotkeyKey.MouseButton4)]
    [InlineData(0x020C, 0x00020000u, HotkeyKey.MouseButton5)]
    public void MouseMapper_MapsSupportedButtons(
        uint message,
        uint mouseData,
        HotkeyKey expected)
    {
        Assert.True(WindowsInputMapper.TryMapMouse(
            message,
            mouseData,
            out RawInputControl control));
        Assert.Equal(expected, control.PrimaryKey);
    }

    [Fact]
    public void MouseMapper_RejectsUnsupportedMessagesAndButtons()
    {
        Assert.False(WindowsInputMapper.TryMapMouse(
            0x0201,
            0,
            out _));
        Assert.False(WindowsInputMapper.TryMapMouse(
            0x020B,
            0x00030000,
            out _));
    }

    [Theory]
    [InlineData(0x0100, true)]
    [InlineData(0x0104, true)]
    [InlineData(0x0207, true)]
    [InlineData(0x020B, true)]
    [InlineData(0x0101, false)]
    [InlineData(0x0208, false)]
    public void PressedMessages_AreClassified(uint message, bool expected)
    {
        Assert.Equal(expected, WindowsInputMapper.IsPressedMessage(message));
    }
}
