using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Input;
using DictaClone.Mac.Insertion;
using DictaClone.Mac.Permissions;

namespace DictaClone.Mac.Input;

public sealed partial class MacHotkeyEventSource : IHotkeyEventSource
{
    private const uint KeyDown = 10;
    private const uint KeyUp = 11;
    private const uint FlagsChanged = 12;
    private const uint OtherMouseDown = 25;
    private const uint OtherMouseUp = 26;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE;
    private const uint TapDisabledByUserInput = 0xFFFFFFFF;
    private const int KeyboardKeycodeField = 9;
    private const int MouseButtonNumberField = 3;
    private const int SourceUserDataField = 42;

    private readonly object _sync = new();
    private readonly EventTapCallback _callback;
    private readonly MacModifierStateTracker _modifierStates = new();
    private ShortcutInterpreter? _interpreter;
    private Thread? _thread;
    private TaskCompletionSource? _started;
    private nint _eventTap;
    private nint _runLoop;
    private bool _disposed;

    public MacHotkeyEventSource()
    {
        _callback = EventTap;
    }

    public event EventHandler<HotkeyEvent>? Triggered;

    public async Task StartAsync(
        IReadOnlyCollection<HotkeyBinding> bindings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopAsync(cancellationToken).ConfigureAwait(false);

        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _interpreter = new ShortcutInterpreter(bindings);
            _started = started;
            _thread = new Thread(RunEventTap)
            {
                IsBackground = true,
                Name = "DictaClone macOS global shortcut tap",
            };
            _thread.Start();
        }

        await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Thread? thread;
        nint runLoop;
        ImmutableArray<HotkeyEvent> resetEvents;
        lock (_sync)
        {
            thread = _thread;
            runLoop = _runLoop;
            resetEvents = _interpreter?.Reset() ?? [];
            _modifierStates.Reset();
            _thread = null;
            _interpreter = null;
        }

        if (runLoop != nint.Zero)
        {
            CFRunLoopStop(runLoop);
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            if (!thread.Join(TimeSpan.FromSeconds(2)))
            {
                throw new InvalidOperationException(
                    "The macOS shortcut event tap did not stop in time.");
            }
        }

        foreach (HotkeyEvent resetEvent in resetEvents)
        {
            Publish(resetEvent);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
    }

    private void RunEventTap()
    {
        nint eventTap = nint.Zero;
        nint source = nint.Zero;
        nint mode = nint.Zero;
        try
        {
            ulong mask =
                (1UL << (int)KeyDown) |
                (1UL << (int)KeyUp) |
                (1UL << (int)FlagsChanged) |
                (1UL << (int)OtherMouseDown) |
                (1UL << (int)OtherMouseUp);
            eventTap = CGEventTapCreate(
                tap: 1,
                place: 0,
                options: 0,
                mask,
                _callback,
                nint.Zero);
            if (eventTap == nint.Zero)
            {
                throw new MacPermissionDeniedException(
                    "Accessibility",
                    "macOS denied the global keyboard event tap. Enable Accessibility for DictaClone, then restart it.");
            }

            source = CFMachPortCreateRunLoopSource(
                nint.Zero,
                eventTap,
                order: 0);
            if (source == nint.Zero)
            {
                throw new InvalidOperationException(
                    "macOS could not create a shortcut run-loop source.");
            }

            nint runLoop = CFRunLoopGetCurrent();
            mode = CreateCfString("kCFRunLoopDefaultMode");
            lock (_sync)
            {
                _eventTap = eventTap;
                _runLoop = runLoop;
            }

            CFRunLoopAddSource(runLoop, source, mode);
            CGEventTapEnable(eventTap, enable: true);
            _started?.TrySetResult();
            CFRunLoopRun();
            CFRunLoopRemoveSource(runLoop, source, mode);
        }
        catch (Exception exception)
        {
            _started?.TrySetException(exception);
        }
        finally
        {
            lock (_sync)
            {
                _eventTap = nint.Zero;
                _runLoop = nint.Zero;
            }

            Release(mode);
            Release(source);
            Release(eventTap);
        }
    }

    private nint EventTap(
        nint proxy,
        uint eventType,
        nint keyboardEvent,
        nint userInfo)
    {
        try
        {
            if (eventType is TapDisabledByTimeout or TapDisabledByUserInput)
            {
                nint tap;
                ImmutableArray<HotkeyEvent> resetEvents;
                lock (_sync)
                {
                    tap = _eventTap;
                    resetEvents = _interpreter?.Reset() ?? [];
                    _modifierStates.Reset();
                }

                if (tap != nint.Zero)
                {
                    CGEventTapEnable(tap, enable: true);
                }

                foreach (HotkeyEvent resetEvent in resetEvents)
                {
                    Publish(resetEvent);
                }

                return keyboardEvent;
            }

            bool isInjected = CGEventGetIntegerValueField(
                keyboardEvent,
                SourceUserDataField) == MacKeyboardInjector.SyntheticEventMarker;
            RawInputControl control;
            bool isPressed;
            if (eventType is KeyDown or KeyUp or FlagsChanged)
            {
                ushort keyCode = checked((ushort)CGEventGetIntegerValueField(
                    keyboardEvent,
                    KeyboardKeycodeField));
                if (!MacInputMapper.TryMapKeyboard(keyCode, out control))
                {
                    return keyboardEvent;
                }

                if (eventType == FlagsChanged)
                {
                    lock (_sync)
                    {
                        isPressed = _modifierStates.Update(
                            keyCode,
                            CGEventGetFlags(keyboardEvent));
                    }
                }
                else
                {
                    isPressed = eventType == KeyDown;
                }
            }
            else
            {
                long button = CGEventGetIntegerValueField(
                    keyboardEvent,
                    MouseButtonNumberField);
                if (!MacInputMapper.TryMapMouse(button, out control))
                {
                    return keyboardEvent;
                }

                isPressed = eventType == OtherMouseDown;
            }

            ImmutableArray<HotkeyEvent> events;
            bool suppressInput;
            lock (_sync)
            {
                if (_interpreter is null)
                {
                    return keyboardEvent;
                }

                events = _interpreter.Process(
                    new RawInputEvent(control, isPressed, isInjected),
                    out suppressInput);
            }

            foreach (HotkeyEvent hotkeyEvent in events)
            {
                Publish(hotkeyEvent);
            }

            return suppressInput ? nint.Zero : keyboardEvent;
        }
        catch (Exception)
        {
            return keyboardEvent;
        }
    }

    private void Publish(HotkeyEvent hotkeyEvent)
    {
        Delegate[] handlers = Triggered?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler<HotkeyEvent>)handler)(this, hotkeyEvent);
            }
            catch (Exception)
            {
                // A UI observer cannot stop the native event tap.
            }
        }
    }

    private static nint CreateCfString(string value) =>
        CFStringCreateWithCString(
            nint.Zero,
            value,
            encoding: 0x08000100);

    private static void Release(nint value)
    {
        if (value != nint.Zero)
        {
            CFRelease(value);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EventTapCallback(
        nint proxy,
        uint eventType,
        nint keyboardEvent,
        nint userInfo);

    [LibraryImport(
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial nint CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        ulong mask,
        EventTapCallback callback,
        nint userInfo);

    [LibraryImport(
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial void CGEventTapEnable(
        nint tap,
        [MarshalAs(UnmanagedType.I1)] bool enable);

    [LibraryImport(
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial long CGEventGetIntegerValueField(
        nint keyboardEvent,
        int field);

    [LibraryImport(
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial ulong CGEventGetFlags(nint keyboardEvent);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFMachPortCreateRunLoopSource(
        nint allocator,
        nint port,
        nint order);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial nint CFRunLoopGetCurrent();

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRunLoopAddSource(
        nint runLoop,
        nint source,
        nint mode);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRunLoopRemoveSource(
        nint runLoop,
        nint source,
        nint mode);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRunLoopRun();

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRunLoopStop(nint runLoop);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(
        nint allocator,
        string value,
        uint encoding);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint value);
}
