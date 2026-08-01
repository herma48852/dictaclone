using DictaClone.Core.Hotkeys;
using DictaClone.Windows.Input;

namespace DictaClone.Windows.Tests;

public sealed class ShortcutRecordingTests
{
    [Fact]
    public void ModifierOnlyChord_CompletesOnFinalRelease()
    {
        var recording = new ShortcutRecordingSession();
        RawInputControl control =
            RawInputControl.ForModifier(PhysicalModifier.RightControl);
        RawInputControl windows =
            RawInputControl.ForModifier(PhysicalModifier.LeftWindows);

        Assert.Null(recording.Process(new(control, IsPressed: true)));
        Assert.Null(recording.Process(new(windows, IsPressed: true)));
        Assert.Null(recording.Process(new(control, IsPressed: false)));
        HotkeyChord chord = Assert.IsType<HotkeyChord>(
            recording.Process(new(windows, IsPressed: false)));

        Assert.Equal(
            HotkeyModifiers.Control | HotkeyModifiers.Windows,
            chord.Modifiers);
        Assert.Null(chord.PrimaryKey);
        Assert.True(recording.IsComplete);
    }

    [Fact]
    public void KeyboardAndMousePrimaryKeys_CompleteImmediately()
    {
        var keyboard = new ShortcutRecordingSession();
        _ = keyboard.Process(new(
            RawInputControl.ForModifier(PhysicalModifier.LeftAlt),
            IsPressed: true));
        HotkeyChord keyboardChord = Assert.IsType<HotkeyChord>(
            keyboard.Process(new(
                RawInputControl.ForPrimaryKey(HotkeyKey.F18),
                IsPressed: true)));

        var mouse = new ShortcutRecordingSession();
        HotkeyChord mouseChord = Assert.IsType<HotkeyChord>(
            mouse.Process(new(
                RawInputControl.ForPrimaryKey(HotkeyKey.MouseButton4),
                IsPressed: true)));

        Assert.Equal(HotkeyModifiers.Alt, keyboardChord.Modifiers);
        Assert.Equal(HotkeyKey.F18, keyboardChord.PrimaryKey);
        Assert.Equal(HotkeyKey.MouseButton4, mouseChord.PrimaryKey);
    }

    [Fact]
    public void InjectedReleaseAndPostCompletionEvents_AreIgnored()
    {
        var recording = new ShortcutRecordingSession();
        RawInputControl shift =
            RawInputControl.ForModifier(PhysicalModifier.LeftShift);

        Assert.Null(recording.Process(new(
            shift,
            IsPressed: true,
            IsInjected: true)));
        Assert.Null(recording.Process(new(shift, IsPressed: false)));
        Assert.Null(recording.Process(default));

        HotkeyChord chord = Assert.IsType<HotkeyChord>(
            recording.Process(new(
                RawInputControl.ForPrimaryKey(HotkeyKey.A),
                IsPressed: true)));
        Assert.Equal(HotkeyKey.A, chord.PrimaryKey);
        Assert.Null(recording.Process(new(shift, IsPressed: true)));
    }
}
