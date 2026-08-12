using DictaClone.Core.Hotkeys;
using DictaClone.Mac.Input;

namespace DictaClone.Mac.Tests;

public sealed class MacInputMapperTests
{
    [Theory]
    [InlineData(55, PhysicalModifier.LeftWindows)]
    [InlineData(54, PhysicalModifier.RightWindows)]
    [InlineData(56, PhysicalModifier.LeftShift)]
    [InlineData(59, PhysicalModifier.LeftControl)]
    [InlineData(61, PhysicalModifier.RightAlt)]
    public void KeyboardModifier_MapsPhysicalSide(
        ushort keyCode,
        PhysicalModifier expected)
    {
        Assert.True(MacInputMapper.TryMapKeyboard(keyCode, out var control));
        Assert.Equal(expected, control.Modifier);
    }

    [Theory]
    [InlineData(49, HotkeyKey.Space)]
    [InlineData(53, HotkeyKey.Escape)]
    [InlineData(0, HotkeyKey.A)]
    [InlineData(6, HotkeyKey.Z)]
    [InlineData(122, HotkeyKey.F1)]
    [InlineData(90, HotkeyKey.F20)]
    public void KeyboardPrimary_MapsMacVirtualKey(
        ushort keyCode,
        HotkeyKey expected)
    {
        Assert.True(MacInputMapper.TryMapKeyboard(keyCode, out var control));
        Assert.Equal(expected, control.PrimaryKey);
    }

    [Fact]
    public void ModifierPressed_UsesMacFlagMask()
    {
        Assert.True(MacInputMapper.IsModifierPressed(55, 1UL << 20));
        Assert.False(MacInputMapper.IsModifierPressed(55, 0));
        Assert.True(MacInputMapper.IsModifierPressed(58, 1UL << 19));
    }

    [Fact]
    public void ModifierTracker_DistinguishesBothPhysicalControlKeys()
    {
        const ulong ControlFlag = 1UL << 18;
        var tracker = new MacModifierStateTracker();

        Assert.True(tracker.Update(59, ControlFlag));
        Assert.True(tracker.Update(62, ControlFlag));
        Assert.False(tracker.Update(59, ControlFlag));
        Assert.False(tracker.Update(62, 0));
    }

    [Theory]
    [InlineData(2, HotkeyKey.MouseMiddle)]
    [InlineData(3, HotkeyKey.MouseButton4)]
    [InlineData(4, HotkeyKey.MouseButton5)]
    public void MouseButton_MapsSupportedButtons(long button, HotkeyKey key)
    {
        Assert.True(MacInputMapper.TryMapMouse(button, out var control));
        Assert.Equal(key, control.PrimaryKey);
    }

    [Fact]
    public void MediaKey_MapsVolumeDownWithoutConflatingF11()
    {
        Assert.True(MacInputMapper.TryMapMediaKey(1, out var mediaControl));
        Assert.Equal(HotkeyKey.VolumeDown, mediaControl.PrimaryKey);

        Assert.True(MacInputMapper.TryMapKeyboard(103, out var f11Control));
        Assert.Equal(HotkeyKey.F11, f11Control.PrimaryKey);
        Assert.NotEqual(mediaControl, f11Control);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(16)]
    public void MediaKey_DoesNotMapOtherSystemControls(int mediaKeyType)
    {
        Assert.False(MacInputMapper.TryMapMediaKey(mediaKeyType, out _));
    }
}
