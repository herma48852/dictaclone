using System.Windows.Input;
using DictaClone.App.Presentation;
using DictaClone.Core.Hotkeys;
using DictaClone.Windows.Input;

namespace DictaClone.App.Tests;

public sealed class WpfInputMapperTests
{
    [Theory]
    [InlineData(Key.LeftCtrl, PhysicalModifier.LeftControl)]
    [InlineData(Key.RightAlt, PhysicalModifier.RightAlt)]
    [InlineData(Key.LeftShift, PhysicalModifier.LeftShift)]
    [InlineData(Key.RWin, PhysicalModifier.RightWindows)]
    public void KeyMapper_MapsPhysicalModifiers(
        Key key,
        PhysicalModifier expected)
    {
        Assert.True(WpfInputMapper.TryMapKey(
            key,
            out RawInputControl control));
        Assert.Equal(expected, control.Modifier);
    }

    [Theory]
    [InlineData(Key.A, HotkeyKey.A)]
    [InlineData(Key.Z, HotkeyKey.Z)]
    [InlineData(Key.F1, HotkeyKey.F1)]
    [InlineData(Key.F24, HotkeyKey.F24)]
    [InlineData(Key.Space, HotkeyKey.Space)]
    [InlineData(Key.Return, HotkeyKey.Enter)]
    [InlineData(Key.Escape, HotkeyKey.Escape)]
    public void KeyMapper_MapsPrimaryKeys(Key key, HotkeyKey expected)
    {
        Assert.True(WpfInputMapper.TryMapKey(
            key,
            out RawInputControl control));
        Assert.Equal(expected, control.PrimaryKey);
    }

    [Fact]
    public void KeyMapper_RejectsUnsupportedKey()
    {
        Assert.False(WpfInputMapper.TryMapKey(
            Key.D0,
            out RawInputControl control));
        Assert.Equal(default, control);
    }

    [Theory]
    [InlineData(MouseButton.Middle, HotkeyKey.MouseMiddle)]
    [InlineData(MouseButton.XButton1, HotkeyKey.MouseButton4)]
    [InlineData(MouseButton.XButton2, HotkeyKey.MouseButton5)]
    public void MouseMapper_MapsSupportedButtons(
        MouseButton button,
        HotkeyKey expected)
    {
        Assert.True(WpfInputMapper.TryMapMouse(
            button,
            out RawInputControl control));
        Assert.Equal(expected, control.PrimaryKey);
    }

    [Fact]
    public void MouseMapper_RejectsUnsupportedButton()
    {
        Assert.False(WpfInputMapper.TryMapMouse(MouseButton.Left, out _));
    }
}
