using System.Diagnostics;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Mac.Interop;

namespace DictaClone.Mac.Foreground;

public sealed class MacForegroundTargetService : IForegroundTargetService
{
    private readonly IMacForegroundApi _native;

    public MacForegroundTargetService()
        : this(new NativeMacForegroundApi())
    {
    }

    internal MacForegroundTargetService(IMacForegroundApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public Task<ForegroundTarget> CaptureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MacForegroundSnapshot snapshot = _native.Capture();
        if (snapshot.ProcessId <= 0 || snapshot.FocusedWindowHash == 0)
        {
            throw new ForegroundTargetUnavailableException();
        }

        return Task.FromResult(new ForegroundTarget(
            CreateId(snapshot),
            snapshot.ProcessName,
            snapshot.BundleIdentifier,
            IsElevated: false));
    }

    public Task<bool> IsCurrentAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        MacForegroundSnapshot current = _native.Capture();
        return Task.FromResult(string.Equals(
            target.Id,
            CreateId(current),
            StringComparison.Ordinal));
    }

    private static string CreateId(MacForegroundSnapshot snapshot) =>
        $"{snapshot.ProcessId:X8}:{snapshot.FocusedWindowHash:X16}";
}

internal interface IMacForegroundApi
{
    MacForegroundSnapshot Capture();
}

internal readonly record struct MacForegroundSnapshot(
    int ProcessId,
    nuint FocusedWindowHash,
    string ProcessName,
    string BundleIdentifier);

internal sealed class NativeMacForegroundApi : IMacForegroundApi
{
    public MacForegroundSnapshot Capture()
    {
        nint workspace = ObjectiveC.Send(
            ObjectiveC.Class("NSWorkspace"),
            "sharedWorkspace");
        nint application = ObjectiveC.Send(workspace, "frontmostApplication");
        if (application == nint.Zero)
        {
            return default;
        }

        int processId = checked((int)ObjectiveC.SendInt64(
            application,
            "processIdentifier"));
        string processName = ReadApplicationString(
            application,
            "localizedName") ?? GetProcessName(processId);
        string bundleIdentifier = ReadApplicationString(
            application,
            "bundleIdentifier") ?? string.Empty;

        return new(
            processId,
            CaptureFocusedWindowHash(processId),
            processName,
            bundleIdentifier);
    }

    private static nuint CaptureFocusedWindowHash(int expectedProcessId)
    {
        nint system = MacNative.AXUIElementCreateSystemWide();
        if (system == nint.Zero)
        {
            return 0;
        }

        try
        {
            nint application = CopyAttribute(system, "AXFocusedApplication");
            if (application == nint.Zero)
            {
                return 0;
            }

            try
            {
                if (MacNative.AXUIElementGetPid(application, out int processId) != 0 ||
                    processId != expectedProcessId)
                {
                    return 0;
                }

                nint window = CopyAttribute(application, "AXFocusedWindow");
                if (window == nint.Zero)
                {
                    return 0;
                }

                try
                {
                    return MacNative.CFHash(window);
                }
                finally
                {
                    MacNative.CFRelease(window);
                }
            }
            finally
            {
                MacNative.CFRelease(application);
            }
        }
        finally
        {
            MacNative.CFRelease(system);
        }
    }

    internal static nint CopyAttribute(nint element, string name)
    {
        nint attribute = ObjectiveC.CreateString(name);
        try
        {
            return MacNative.AXUIElementCopyAttributeValue(
                element,
                attribute,
                out nint value) == 0
                ? value
                : nint.Zero;
        }
        finally
        {
            MacNative.CFRelease(attribute);
        }
    }

    private static string? ReadApplicationString(
        nint application,
        string selector) =>
        ObjectiveC.GetString(ObjectiveC.Send(application, selector));

    private static string GetProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException or
                NotSupportedException)
        {
            return $"pid-{processId}";
        }
    }
}
