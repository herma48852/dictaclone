using System.Diagnostics;
using System.Runtime.InteropServices;
using DictaClone.Mac.Interop;

namespace DictaClone.Mac.Permissions;

public enum MacPermissionState
{
    NotDetermined,
    Denied,
    Authorized,
    Restricted,
    Unknown,
}

public sealed record MacPermissionSnapshot(
    MacPermissionState Microphone,
    MacPermissionState Accessibility,
    MacPermissionState InputMonitoring)
{
    public bool CanCaptureGlobalShortcuts =>
        Accessibility == MacPermissionState.Authorized;
}

public sealed class MacPermissionService
{
    private const string AVFoundation =
        "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";
    private readonly bool _isSupported = OperatingSystem.IsMacOS();

    public MacPermissionSnapshot Inspect() => _isSupported
        ? new(
            InspectMicrophone(),
            MacNative.DictaCloneAccessibilityPermissionStatus() == 1
                ? MacPermissionState.Authorized
                : MacPermissionState.Denied,
            MacNative.DictaCloneInputMonitoringPermissionStatus() == 1
                ? MacPermissionState.Authorized
                : MacPermissionState.Denied)
        : new(
            MacPermissionState.Unknown,
            MacPermissionState.Unknown,
            MacPermissionState.Unknown);

    public bool RequestInputMonitoring() =>
        _isSupported &&
        MacNative.DictaCloneRequestInputMonitoringPermission() == 1;

    public bool RequestAccessibility() =>
        _isSupported &&
        MacNative.DictaCloneRequestAccessibilityPermission() == 1;

    public bool IsMicrophoneRequestAvailable() =>
        !_isSupported || MacNative.DictaClonePermissionShimVersion() == 1;

    public async Task<MacPermissionState> RequestMicrophoneAsync()
    {
        if (!_isSupported)
        {
            return MacPermissionState.Unknown;
        }

        MacPermissionState current = InspectMicrophone();
        if (current != MacPermissionState.NotDetermined)
        {
            return current;
        }

        nint avFoundation = NativeLibrary.Load(AVFoundation);
        nint mediaType = ObjectiveC.CreateString("soun");
        try
        {
            nint deviceClass = ObjectiveC.Class("AVCaptureDevice");
            if (deviceClass == nint.Zero)
            {
                return MacPermissionState.Unknown;
            }

            bool granted = await NativeMicrophonePermissionRequest
                .InvokeAsync().ConfigureAwait(false);
            return granted
                ? MacPermissionState.Authorized
                : MacPermissionState.Denied;
        }
        finally
        {
            MacNative.CFRelease(mediaType);
            NativeLibrary.Free(avFoundation);
        }
    }

    public void OpenMicrophoneSettings() =>
        OpenPrivacyPaneIfSupported("Privacy_Microphone");

    public void OpenAccessibilitySettings() =>
        OpenPrivacyPaneIfSupported("Privacy_Accessibility");

    public void OpenInputMonitoringSettings() =>
        OpenPrivacyPaneIfSupported("Privacy_ListenEvent");

    private void OpenPrivacyPaneIfSupported(string anchor)
    {
        if (_isSupported)
        {
            OpenPrivacyPane(anchor);
        }
    }

    private static MacPermissionState InspectMicrophone()
    {
        nint avFoundation = NativeLibrary.Load(AVFoundation);
        nint mediaType = ObjectiveC.CreateString("soun");
        try
        {
            nint deviceClass = ObjectiveC.Class("AVCaptureDevice");
            if (deviceClass == nint.Zero)
            {
                return MacPermissionState.Unknown;
            }

            nint status = ObjectiveC.Send(
                deviceClass,
                "authorizationStatusForMediaType:",
                mediaType);
            return status switch
            {
                0 => MacPermissionState.NotDetermined,
                1 => MacPermissionState.Restricted,
                2 => MacPermissionState.Denied,
                3 => MacPermissionState.Authorized,
                _ => MacPermissionState.Unknown,
            };
        }
        finally
        {
            MacNative.CFRelease(mediaType);
            NativeLibrary.Free(avFoundation);
        }
    }

    private static void OpenPrivacyPane(string anchor)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            UseShellExecute = false,
            ArgumentList =
            {
                $"x-apple.systempreferences:com.apple.preference.security?{anchor}",
            },
        });
    }
}

internal static class NativeMicrophonePermissionRequest
{
    private static readonly object Gate = new();
    private static readonly CompletionHandler Handler = Complete;
    private static readonly nint HandlerPointer =
        Marshal.GetFunctionPointerForDelegate(Handler);
    private static TaskCompletionSource<bool>? _pending;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CompletionHandler(int value);

    internal static Task<bool> InvokeAsync()
    {
        TaskCompletionSource<bool> completion;
        lock (Gate)
        {
            if (_pending is not null)
            {
                return _pending.Task;
            }

            completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = completion;
        }

        try
        {
            MacNative.DictaCloneRequestMicrophonePermission(HandlerPointer);
        }
        catch (Exception exception)
        {
            lock (Gate)
            {
                if (ReferenceEquals(_pending, completion))
                {
                    _pending = null;
                }
            }

            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    private static void Complete(int value)
    {
        TaskCompletionSource<bool>? completion;
        lock (Gate)
        {
            completion = _pending;
            _pending = null;
        }

        completion?.TrySetResult(value != 0);
    }
}
