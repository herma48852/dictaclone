using System.Collections.Immutable;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;

namespace DictaClone.Windows.Input;

public sealed class ShortcutInterpreter
{
    private readonly BindingState[] _bindings;
    private readonly HashSet<PhysicalModifier> _pressedModifiers = [];
    private readonly HashSet<HotkeyKey> _pressedPrimaryKeys = [];

    public ShortcutInterpreter(IEnumerable<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings
            .Where(binding => binding.Enabled)
            .Select(binding => new BindingState(binding))
            .ToArray();
    }

    public ImmutableArray<HotkeyEvent> Process(RawInputEvent input)
    {
        if (input.IsInjected || !UpdatePressedControls(input))
        {
            return [];
        }

        HotkeyModifiers modifiers = GetLogicalModifiers();
        var events = ImmutableArray.CreateBuilder<HotkeyEvent>();

        foreach (BindingState state in _bindings)
        {
            bool chordIsDown =
                modifiers == state.Binding.Chord.Modifiers &&
                (!state.Binding.Chord.PrimaryKey.HasValue ||
                 _pressedPrimaryKeys.Contains(
                     state.Binding.Chord.PrimaryKey.Value));

            if (chordIsDown == state.ChordWasDown)
            {
                continue;
            }

            state.ChordWasDown = chordIsDown;
            if (state.Binding.Activation == HotkeyActivation.Toggle)
            {
                if (chordIsDown)
                {
                    state.ActionIsActive = !state.ActionIsActive;
                    events.Add(CreateEvent(
                        state.Binding.Action,
                        state.ActionIsActive));
                }
            }
            else
            {
                state.ActionIsActive = chordIsDown;
                events.Add(CreateEvent(
                    state.Binding.Action,
                    state.ActionIsActive));
            }
        }

        return events.ToImmutable();
    }

    public ImmutableArray<HotkeyEvent> Reset()
    {
        var events = ImmutableArray.CreateBuilder<HotkeyEvent>();

        foreach (BindingState state in _bindings)
        {
            if (state.ActionIsActive)
            {
                events.Add(CreateEvent(state.Binding.Action, isActive: false));
            }

            state.ActionIsActive = false;
            state.ChordWasDown = false;
        }

        _pressedModifiers.Clear();
        _pressedPrimaryKeys.Clear();
        return events.ToImmutable();
    }

    private static HotkeyEvent CreateEvent(
        HotkeyAction action,
        bool isActive) =>
        new(
            action,
            isActive ? HotkeyEventKind.Pressed : HotkeyEventKind.Released,
            IsInjected: false);

    private bool UpdatePressedControls(RawInputEvent input)
    {
        if (input.Control.Modifier is { } modifier)
        {
            return input.IsPressed
                ? _pressedModifiers.Add(modifier)
                : _pressedModifiers.Remove(modifier);
        }

        if (input.Control.PrimaryKey is { } primaryKey)
        {
            return input.IsPressed
                ? _pressedPrimaryKeys.Add(primaryKey)
                : _pressedPrimaryKeys.Remove(primaryKey);
        }

        return false;
    }

    private HotkeyModifiers GetLogicalModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;

        foreach (PhysicalModifier modifier in _pressedModifiers)
        {
            modifiers |= modifier switch
            {
                PhysicalModifier.LeftControl or PhysicalModifier.RightControl =>
                    HotkeyModifiers.Control,
                PhysicalModifier.LeftAlt or PhysicalModifier.RightAlt =>
                    HotkeyModifiers.Alt,
                PhysicalModifier.LeftShift or PhysicalModifier.RightShift =>
                    HotkeyModifiers.Shift,
                PhysicalModifier.LeftWindows or PhysicalModifier.RightWindows =>
                    HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None,
            };
        }

        return modifiers;
    }

    private sealed class BindingState(HotkeyBinding binding)
    {
        public HotkeyBinding Binding { get; } = binding;

        public bool ChordWasDown { get; set; }

        public bool ActionIsActive { get; set; }
    }
}
