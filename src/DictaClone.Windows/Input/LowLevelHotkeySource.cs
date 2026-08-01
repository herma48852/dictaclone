using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;

namespace DictaClone.Windows.Input;

public sealed partial class LowLevelHotkeySource : IHotkeyEventSource
{
    private const int KeyboardHookId = 13;
    private const int MouseHookId = 14;
    private const uint KeyboardInjectedFlag = 0x10;
    private const uint MouseInjectedFlag = 0x01;

    private readonly object _sync = new();
    private readonly HookProcedure _keyboardProcedure;
    private readonly HookProcedure _mouseProcedure;
    private ShortcutInterpreter? _interpreter;
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _disposed;

    public LowLevelHotkeySource()
    {
        _keyboardProcedure = KeyboardHookCallback;
        _mouseProcedure = MouseHookCallback;
    }

    public event EventHandler<HotkeyEvent>? Triggered;

    public Task StartAsync(
        IReadOnlyCollection<HotkeyBinding> bindings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Global low-level hooks require Windows.");
        }

        lock (_sync)
        {
            if (_keyboardHook != nint.Zero || _mouseHook != nint.Zero)
            {
                throw new InvalidOperationException(
                    "The global input hooks are already running.");
            }

            _interpreter = new(bindings);
            nint module = NativeMethods.GetModuleHandle(null);
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                KeyboardHookId,
                _keyboardProcedure,
                module,
                0);
            if (_keyboardHook == nint.Zero)
            {
                _interpreter = null;
                throw CreateWin32Exception("keyboard");
            }

            _mouseHook = NativeMethods.SetWindowsHookEx(
                MouseHookId,
                _mouseProcedure,
                module,
                0);
            if (_mouseHook == nint.Zero)
            {
                _ = NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = nint.Zero;
                _interpreter = null;
                throw CreateWin32Exception("mouse");
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<HotkeyEvent> releaseEvents;
        int? error = null;

        lock (_sync)
        {
            releaseEvents = _interpreter?.Reset() ?? [];
            _interpreter = null;

            if (_mouseHook != nint.Zero)
            {
                if (!NativeMethods.UnhookWindowsHookEx(_mouseHook))
                {
                    error = Marshal.GetLastWin32Error();
                }

                _mouseHook = nint.Zero;
            }

            if (_keyboardHook != nint.Zero)
            {
                if (!NativeMethods.UnhookWindowsHookEx(_keyboardHook))
                {
                    error ??= Marshal.GetLastWin32Error();
                }

                _keyboardHook = nint.Zero;
            }
        }

        Publish(releaseEvents);

        return error.HasValue
            ? Task.FromException(new Win32Exception(
                error.Value,
                "A global input hook could not be removed."))
            : Task.CompletedTask;
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

    private nint KeyboardHookCallback(
        int code,
        nint message,
        nint data)
    {
        try
        {
            uint messageId = unchecked((uint)message);
            if (code >= 0 && IsKeyboardMessage(messageId))
            {
                var hookData = Marshal.PtrToStructure<KeyboardHookData>(data);
                if (WindowsInputMapper.TryMapKeyboard(
                        hookData.VirtualKey,
                        out RawInputControl control))
                {
                    Process(new(
                        control,
                        WindowsInputMapper.IsPressedMessage(messageId),
                        (hookData.Flags & KeyboardInjectedFlag) != 0));
                }
            }
        }
        catch (Exception)
        {
            // A hook callback must always continue the system hook chain.
        }

        return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
    }

    private nint MouseHookCallback(
        int code,
        nint message,
        nint data)
    {
        try
        {
            uint messageId = unchecked((uint)message);
            if (code >= 0)
            {
                var hookData = Marshal.PtrToStructure<MouseHookData>(data);
                if (WindowsInputMapper.TryMapMouse(
                        messageId,
                        hookData.MouseData,
                        out RawInputControl control))
                {
                    Process(new(
                        control,
                        WindowsInputMapper.IsPressedMessage(messageId),
                        (hookData.Flags & MouseInjectedFlag) != 0));
                }
            }
        }
        catch (Exception)
        {
            // A hook callback must always continue the system hook chain.
        }

        return NativeMethods.CallNextHookEx(nint.Zero, code, message, data);
    }

    private static bool IsKeyboardMessage(uint message) =>
        message is 0x0100 or 0x0101 or 0x0104 or 0x0105;

    private void Process(RawInputEvent input)
    {
        ImmutableArray<HotkeyEvent> events;
        lock (_sync)
        {
            events = _interpreter?.Process(input) ?? [];
        }

        Publish(events);
    }

    private void Publish(IEnumerable<HotkeyEvent> events)
    {
        Delegate[] handlers = Triggered?.GetInvocationList() ?? [];

        foreach (HotkeyEvent inputEvent in events)
        {
            foreach (Delegate handler in handlers)
            {
                try
                {
                    ((EventHandler<HotkeyEvent>)handler)(this, inputEvent);
                }
                catch (Exception)
                {
                    // Subscribers cannot be allowed to break the hook chain.
                }
            }
        }
    }

    private static Win32Exception CreateWin32Exception(string hookType) =>
        new(
            Marshal.GetLastWin32Error(),
            $"The global {hookType} hook could not be installed.");

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardHookData
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseHookData
    {
        public readonly NativePoint Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint HookProcedure(int code, nint message, nint data);

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
        internal static partial nint GetModuleHandle(
            [MarshalAs(UnmanagedType.LPWStr)] string? moduleName);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        internal static partial nint SetWindowsHookEx(
            int hookId,
            HookProcedure procedure,
            nint module,
            uint threadId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWindowsHookEx(nint hook);

        [LibraryImport("user32.dll")]
        internal static partial nint CallNextHookEx(
            nint hook,
            int code,
            nint message,
            nint data);
    }
}
