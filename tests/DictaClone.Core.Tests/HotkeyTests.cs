using DictaClone.Core.Hotkeys;

namespace DictaClone.Core.Tests;

public sealed class HotkeyTests
{
    [Fact]
    public void PhysicalModifiers_NormalizeLeftAndRightKeys()
    {
        HotkeyChord left = HotkeyChord.FromPhysicalModifiers(
            [PhysicalModifier.LeftControl, PhysicalModifier.LeftWindows]);
        HotkeyChord right = HotkeyChord.FromPhysicalModifiers(
            [PhysicalModifier.RightControl, PhysicalModifier.RightWindows]);

        Assert.Equal(left, right);
        Assert.True(left.IsValid);
        Assert.Equal("Ctrl+Win", left.ToString());
    }

    [Fact]
    public void PhysicalModifiers_NormalizeEveryModifierAndPrimaryKey()
    {
        HotkeyChord chord = HotkeyChord.FromPhysicalModifiers(
            [
                PhysicalModifier.LeftControl,
                PhysicalModifier.RightAlt,
                PhysicalModifier.LeftShift,
                PhysicalModifier.RightWindows,
            ],
            HotkeyKey.Space);

        Assert.Equal(
            HotkeyModifiers.Control |
            HotkeyModifiers.Alt |
            HotkeyModifiers.Shift |
            HotkeyModifiers.Windows,
            chord.Modifiers);
        Assert.Equal(HotkeyKey.Space, chord.PrimaryKey);
        Assert.Equal("Ctrl+Alt+Shift+Win+Space", chord.ToString());
    }

    [Fact]
    public void ModifierOnlyChord_IsValid()
    {
        var chord = new HotkeyChord(
            HotkeyModifiers.Control | HotkeyModifiers.Windows);

        Assert.True(chord.IsValid);
        Assert.Null(chord.PrimaryKey);
    }

    [Fact]
    public void EmptyAndUnknownModifierChords_AreInvalid()
    {
        Assert.False(default(HotkeyChord).IsValid);
        Assert.Equal("(empty)", default(HotkeyChord).ToString());

        var unknown = new HotkeyChord((HotkeyModifiers)128);
        Assert.False(unknown.IsValid);
    }

    [Fact]
    public void Conflicts_IncludeOnlyIdenticalEnabledChords()
    {
        var chord = new HotkeyChord(
            HotkeyModifiers.Control | HotkeyModifiers.Windows);
        HotkeyBinding[] bindings =
        [
            new(HotkeyAction.Dictation, chord),
            new(HotkeyAction.SmartEdit, chord),
            new(HotkeyAction.TypingMode, chord, Enabled: false),
            new(
                HotkeyAction.Cancel,
                new HotkeyChord(HotkeyModifiers.Control, HotkeyKey.Escape)),
        ];

        HotkeyConflict conflict = Assert.Single(
            HotkeyConflictDetector.Find(bindings));
        Assert.Equal(HotkeyAction.Dictation, conflict.First);
        Assert.Equal(HotkeyAction.SmartEdit, conflict.Second);
        Assert.Equal(chord, conflict.Chord);
    }

    [Fact]
    public void Defaults_AreValidAndConflictFree()
    {
        Assert.Equal(4, HotkeyDefaults.Bindings.Length);
        Assert.All(HotkeyDefaults.Bindings, binding => Assert.True(binding.Chord.IsValid));
        Assert.Empty(HotkeyConflictDetector.Find(HotkeyDefaults.Bindings));
    }

    [Fact]
    public void NullCollections_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => HotkeyChord.FromPhysicalModifiers(null!));
        Assert.Throws<ArgumentNullException>(
            () => HotkeyConflictDetector.Find(null!));
    }

    [Fact]
    public void UnknownPhysicalModifier_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HotkeyChord.FromPhysicalModifiers([(PhysicalModifier)999]));
    }
}
