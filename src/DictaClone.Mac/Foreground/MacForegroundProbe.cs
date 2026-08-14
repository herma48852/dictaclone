using System.Runtime.InteropServices;
using DictaClone.Mac.Interop;

namespace DictaClone.Mac.Foreground;

internal static class MacForegroundProbe
{
    public static MacForegroundProbeResult Capture()
    {
        nint appKit = NativeLibrary.Load(MacNative.AppKit);
        try
        {
            return CaptureWithAppKitLoaded();
        }
        finally
        {
            NativeLibrary.Free(appKit);
        }
    }

    private static MacForegroundProbeResult CaptureWithAppKitLoaded()
    {
        bool trusted = MacNative.AXIsProcessTrusted();
        nint system = MacNative.AXUIElementCreateSystemWide();
        if (system == nint.Zero)
        {
            return new(trusted, false, default, []);
        }

        try
        {
            nint workspace = ObjectiveC.Send(
                ObjectiveC.Class("NSWorkspace"),
                "sharedWorkspace");
            nint runningApplication = ObjectiveC.Send(
                workspace,
                "frontmostApplication");
            int expectedProcessId = runningApplication == nint.Zero
                ? 0
                : checked((int)ObjectiveC.SendInt64(
                    runningApplication,
                    "processIdentifier"));
            nint application = expectedProcessId <= 0
                ? nint.Zero
                : MacNative.AXUIElementCreateApplication(expectedProcessId);
            MacForegroundProbeEntry focusedApplication = CaptureElement(
                "workspace",
                "AXApplication",
                application);
            if (application == nint.Zero)
            {
                return new(trusted, true, focusedApplication, []);
            }

            try
            {
                MacForegroundProbeEntry[] attributes =
                [
                    CaptureAttribute(
                        system,
                        "system",
                        "AXFocusedUIElement",
                        retainValue: false,
                        out _),
                    CaptureAttribute(
                        application,
                        "application",
                        "AXFocusedUIElement",
                        retainValue: false,
                        out _),
                    CaptureAttribute(
                        application,
                        "application",
                        "AXFocusedWindow",
                        retainValue: false,
                        out _),
                    CaptureAttribute(
                        application,
                        "application",
                        "AXMainWindow",
                        retainValue: false,
                        out _),
                ];
                return new(trusted, true, focusedApplication, attributes);
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

    private static MacForegroundProbeEntry CaptureElement(
        string sourceName,
        string attributeName,
        nint element)
    {
        if (element == nint.Zero)
        {
            return new(sourceName, attributeName, -1, 0, 0, 0);
        }

        int pidError = MacNative.AXUIElementGetPid(
            element,
            out int processId);
        return new(
            sourceName,
            attributeName,
            0,
            pidError,
            processId,
            MacNative.CFHash(element));
    }

    private static MacForegroundProbeEntry CaptureAttribute(
        nint source,
        string sourceName,
        string attributeName,
        bool retainValue,
        out nint retainedValue)
    {
        retainedValue = nint.Zero;
        nint attribute = ObjectiveC.CreateString(attributeName);
        try
        {
            int copyError = MacNative.AXUIElementCopyAttributeValue(
                source,
                attribute,
                out nint value);
            if (copyError != 0 || value == nint.Zero)
            {
                return new(
                    sourceName,
                    attributeName,
                    copyError,
                    0,
                    0,
                    0);
            }

            try
            {
                int pidError = MacNative.AXUIElementGetPid(
                    value,
                    out int processId);
                nuint hash = MacNative.CFHash(value);
                if (retainValue)
                {
                    retainedValue = value;
                }

                return new(
                    sourceName,
                    attributeName,
                    copyError,
                    pidError,
                    processId,
                    hash);
            }
            finally
            {
                if (!retainValue)
                {
                    MacNative.CFRelease(value);
                }
            }
        }
        finally
        {
            MacNative.CFRelease(attribute);
        }
    }
}

internal sealed record MacForegroundProbeResult(
    bool AccessibilityTrusted,
    bool SystemElementAvailable,
    MacForegroundProbeEntry FocusedApplication,
    IReadOnlyList<MacForegroundProbeEntry> Attributes);

internal readonly record struct MacForegroundProbeEntry(
    string Source,
    string Attribute,
    int CopyError,
    int PidError,
    int ProcessId,
    nuint Hash);
