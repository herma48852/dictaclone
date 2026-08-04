using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;
using DictaClone.Windows.Input;

namespace DictaClone.Windows.Tests;

public sealed class ShortcutInterpreterTests
{
    private static readonly RawInputControl LeftControl =
        RawInputControl.ForModifier(PhysicalModifier.LeftControl);
    private static readonly RawInputControl RightControl =
        RawInputControl.ForModifier(PhysicalModifier.RightControl);
    private static readonly RawInputControl LeftWindows =
        RawInputControl.ForModifier(PhysicalModifier.LeftWindows);
    private static readonly RawInputControl RightWindows =
        RawInputControl.ForModifier(PhysicalModifier.RightWindows);
    private static readonly RawInputControl Space =
        RawInputControl.ForPrimaryKey(HotkeyKey.Space);

    [Fact]
    public void ThousandHoldCycles_EmitExactlyOneStartAndStopPerCycle()
    {
        var interpreter = new ShortcutInterpreter(
            [HotkeyDefaults.Bindings[0]]);
        var events = new List<HotkeyEvent>(2000);

        for (int cycle = 0; cycle < 1000; cycle++)
        {
            events.AddRange(interpreter.Process(new(LeftControl, IsPressed: true)));
            events.AddRange(interpreter.Process(new(LeftWindows, IsPressed: true)));
            events.AddRange(interpreter.Process(new(Space, IsPressed: true)));
            events.AddRange(interpreter.Process(new(Space, IsPressed: true)));
            events.AddRange(interpreter.Process(new(Space, IsPressed: false)));
            events.AddRange(interpreter.Process(new(LeftWindows, IsPressed: false)));
            events.AddRange(interpreter.Process(new(LeftControl, IsPressed: false)));
        }

        Assert.Equal(2000, events.Count);
        Assert.Equal(
            1000,
            events.Count(inputEvent => inputEvent.Kind == HotkeyEventKind.Pressed));
        Assert.Equal(
            1000,
            events.Count(inputEvent => inputEvent.Kind == HotkeyEventKind.Released));

        for (int index = 0; index < events.Count; index += 2)
        {
            Assert.Equal(HotkeyEventKind.Pressed, events[index].Kind);
            Assert.Equal(HotkeyEventKind.Released, events[index + 1].Kind);
            Assert.Equal(HotkeyAction.Dictation, events[index].Action);
            Assert.False(events[index].IsInjected);
        }
    }

    [Fact]
    public void LeftAndRightModifiers_AreLogicallyEquivalent()
    {
        var interpreter = new ShortcutInterpreter(
            [HotkeyDefaults.Bindings[0]]);

        Assert.Empty(interpreter.Process(new(RightControl, IsPressed: true)));
        Assert.Empty(interpreter.Process(new(RightWindows, IsPressed: true)));
        HotkeyEvent pressed = Assert.Single(interpreter.Process(new(
            Space,
            IsPressed: true)));
        HotkeyEvent released = Assert.Single(
            interpreter.Process(new(RightControl, IsPressed: false)));

        Assert.Equal(HotkeyEventKind.Pressed, pressed.Kind);
        Assert.Equal(HotkeyEventKind.Released, released.Kind);
    }

    [Fact]
    public void VirtualDesktopModifierPrefix_DoesNotStartDictation()
    {
        var interpreter = new ShortcutInterpreter(
            [HotkeyDefaults.Bindings[0]]);

        Assert.Empty(interpreter.Process(new(LeftControl, IsPressed: true)));
        Assert.Empty(interpreter.Process(new(LeftWindows, IsPressed: true)));
        Assert.Empty(interpreter.Process(new(LeftWindows, IsPressed: false)));
        Assert.Empty(interpreter.Process(new(LeftControl, IsPressed: false)));
    }

    [Fact]
    public void InjectedRepeatAndUnmappedEvents_AreIgnored()
    {
        var interpreter = new ShortcutInterpreter(
            [HotkeyDefaults.Bindings[0]]);

        Assert.Empty(interpreter.Process(new(
            LeftControl,
            IsPressed: true,
            IsInjected: true)));
        Assert.Empty(interpreter.Process(default));
        Assert.Empty(interpreter.Process(new(LeftControl, IsPressed: true)));
        Assert.Empty(interpreter.Process(new(LeftControl, IsPressed: true)));
    }

    [Fact]
    public void ExactModifiers_PreventOverlappingDefaultChords()
    {
        var interpreter = new ShortcutInterpreter(
            HotkeyDefaults.Bindings.Select(binding =>
                binding.Action == HotkeyAction.SmartEdit
                    ? binding with { Enabled = true }
                    : binding));
        RawInputControl shift =
            RawInputControl.ForModifier(PhysicalModifier.LeftShift);
        RawInputControl alt =
            RawInputControl.ForModifier(PhysicalModifier.LeftAlt);
        RawInputControl space =
            RawInputControl.ForPrimaryKey(HotkeyKey.Space);

        Assert.Empty(interpreter.Process(new(alt, IsPressed: true)));
        Assert.Empty(interpreter.Process(new(shift, IsPressed: true)));
        HotkeyEvent smartEdit = Assert.Single(
            interpreter.Process(new(space, IsPressed: true)));

        Assert.Equal(HotkeyAction.SmartEdit, smartEdit.Action);
        Assert.Equal(HotkeyEventKind.Pressed, smartEdit.Kind);
    }

    [Fact]
    public void ToggleBinding_ChangesStateOnlyOnEachPhysicalActivation()
    {
        var toggle = new HotkeyBinding(
            HotkeyAction.Dictation,
            new HotkeyChord(HotkeyModifiers.Control, HotkeyKey.Space),
            Activation: HotkeyActivation.Toggle);
        var interpreter = new ShortcutInterpreter([toggle]);
        RawInputControl space =
            RawInputControl.ForPrimaryKey(HotkeyKey.Space);
        var events = new List<HotkeyEvent>();

        events.AddRange(interpreter.Process(new(LeftControl, IsPressed: true)));
        events.AddRange(interpreter.Process(new(space, IsPressed: true)));
        events.AddRange(interpreter.Process(new(space, IsPressed: false)));
        events.AddRange(interpreter.Process(new(space, IsPressed: true)));
        events.AddRange(interpreter.Process(new(space, IsPressed: false)));

        Assert.Collection(
            events,
            inputEvent => Assert.Equal(HotkeyEventKind.Pressed, inputEvent.Kind),
            inputEvent => Assert.Equal(HotkeyEventKind.Released, inputEvent.Kind));
    }

    [Fact]
    public void Reset_ReleasesActiveHoldAndToggleBindings()
    {
        HotkeyBinding hold = HotkeyDefaults.Bindings[0];
        var toggle = new HotkeyBinding(
            HotkeyAction.TypingMode,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Windows,
                HotkeyKey.F13),
            Activation: HotkeyActivation.Toggle);
        var interpreter = new ShortcutInterpreter([hold, toggle]);

        _ = interpreter.Process(new(LeftControl, IsPressed: true));
        _ = interpreter.Process(new(LeftWindows, IsPressed: true));
        _ = interpreter.Process(new(Space, IsPressed: true));
        _ = interpreter.Process(new(
            RawInputControl.ForPrimaryKey(HotkeyKey.F13),
            IsPressed: true));

        var releases = interpreter.Reset();

        Assert.Equal(2, releases.Length);
        Assert.All(
            releases,
            inputEvent => Assert.Equal(
                HotkeyEventKind.Released,
                inputEvent.Kind));
        Assert.Empty(interpreter.Reset());
    }

    [Fact]
    public void DisabledBindings_DoNotEmit()
    {
        var interpreter = new ShortcutInterpreter(
            [HotkeyDefaults.Bindings[0] with { Enabled = false }]);

        _ = interpreter.Process(new(LeftControl, IsPressed: true));
        _ = interpreter.Process(new(LeftWindows, IsPressed: true));
        Assert.Empty(interpreter.Process(new(Space, IsPressed: true)));
    }

    [Fact]
    public void NullBindings_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ShortcutInterpreter(null!));
    }
}
