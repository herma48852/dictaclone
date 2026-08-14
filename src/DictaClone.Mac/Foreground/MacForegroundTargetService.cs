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

    public async Task<ForegroundTarget> CaptureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAccessibilityPermission();
        MacForegroundSnapshot snapshot = await Task.Run(
            () => _native.Capture(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (snapshot.ProcessId <= 0 || snapshot.FocusedTargetHash == 0)
        {
            throw new ForegroundTargetUnavailableException();
        }

        return new ForegroundTarget(
            CreateId(snapshot),
            snapshot.ProcessName,
            snapshot.BundleIdentifier,
            IsElevated: false);
    }

    public async Task<bool> IsCurrentAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAccessibilityPermission();
        MacForegroundSnapshot current = await Task.Run(
            () => _native.Capture(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return string.Equals(
            target.Id,
            CreateId(current),
            StringComparison.Ordinal);
    }

    private static string CreateId(MacForegroundSnapshot snapshot) =>
        $"{snapshot.ProcessId:X8}:{snapshot.FocusedTargetKind}:" +
        $"{snapshot.FocusedTargetHash:X16}";

    private void EnsureAccessibilityPermission()
    {
        if (!_native.IsAccessibilityTrusted())
        {
            throw new PlatformPermissionException(
                "Accessibility",
                "Accessibility permission is required to identify the focused control.");
        }
    }
}

internal interface IMacForegroundApi
{
    bool IsAccessibilityTrusted();

    MacForegroundSnapshot Capture(CancellationToken cancellationToken);
}

internal readonly record struct MacForegroundSnapshot(
    int ProcessId,
    nuint FocusedElementHash,
    nuint FocusedWindowHash,
    string ProcessName,
    string BundleIdentifier)
{
    public nuint FocusedTargetHash => FocusedElementHash != 0
        ? FocusedElementHash
        : FocusedWindowHash;

    public char FocusedTargetKind => FocusedElementHash != 0 ? 'E' : 'W';
}

internal sealed class NativeMacForegroundApi : IMacForegroundApi
{
    private const int AxErrorCannotComplete = -25204;
    private const int AttributeAttempts = 10;
    private static readonly TimeSpan AttributeRetryDelay =
        TimeSpan.FromMilliseconds(25);

    public bool IsAccessibilityTrusted() => MacNative.AXIsProcessTrusted();

    public MacForegroundSnapshot Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nint workspace = ObjectiveC.Send(
            ObjectiveC.Class("NSWorkspace"),
            "sharedWorkspace");
        nint runningApplication = ObjectiveC.Send(
            workspace,
            "frontmostApplication");
        if (runningApplication == nint.Zero)
        {
            return default;
        }

        int processId = checked((int)ObjectiveC.SendInt64(
            runningApplication,
            "processIdentifier"));
        if (processId <= 0)
        {
            return default;
        }

        nint application = MacNative.AXUIElementCreateApplication(processId);
        if (application == nint.Zero)
        {
            return default;
        }

        nint system = MacNative.AXUIElementCreateSystemWide();
        try
        {
            nuint focusedElementHash = CaptureAttributeHash(
                application,
                "AXFocusedUIElement",
                processId,
                cancellationToken);
            if (focusedElementHash == 0 && system != nint.Zero)
            {
                focusedElementHash = CaptureAttributeHash(
                    system,
                    "AXFocusedUIElement",
                    processId,
                    cancellationToken);
            }

            nuint focusedWindowHash = CaptureAttributeHash(
                application,
                "AXFocusedWindow",
                processId,
                cancellationToken);
            if (focusedWindowHash == 0)
            {
                focusedWindowHash = CaptureAttributeHash(
                    application,
                    "AXMainWindow",
                    processId,
                    cancellationToken);
            }

            string processName = ReadApplicationString(
                runningApplication,
                "localizedName") ?? GetProcessName(processId);
            string bundleIdentifier = ReadApplicationString(
                runningApplication,
                "bundleIdentifier") ?? string.Empty;

            return new(
                processId,
                focusedElementHash,
                focusedWindowHash,
                processName,
                bundleIdentifier);
        }
        finally
        {
            if (system != nint.Zero)
            {
                MacNative.CFRelease(system);
            }

            MacNative.CFRelease(application);
        }
    }

    private static nuint CaptureAttributeHash(
        nint source,
        string name,
        int expectedProcessId,
        CancellationToken cancellationToken)
    {
        nint element = CopyAttribute(source, name, cancellationToken);
        if (element == nint.Zero)
        {
            return 0;
        }

        try
        {
            if (MacNative.AXUIElementGetPid(element, out int processId) != 0 ||
                processId != expectedProcessId)
            {
                return 0;
            }

            return MacNative.CFHash(element);
        }
        finally
        {
            MacNative.CFRelease(element);
        }
    }

    internal static nint CopyAttribute(
        nint element,
        string name,
        CancellationToken cancellationToken = default)
    {
        nint attribute = ObjectiveC.CreateString(name);
        try
        {
            for (int attempt = 0; attempt < AttributeAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int error = MacNative.AXUIElementCopyAttributeValue(
                    element,
                    attribute,
                    out nint value);
                if (error == 0)
                {
                    return value;
                }

                if (error != AxErrorCannotComplete ||
                    attempt + 1 >= AttributeAttempts)
                {
                    return nint.Zero;
                }

                Thread.Sleep(AttributeRetryDelay);
            }

            return nint.Zero;
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
