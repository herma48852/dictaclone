using System.Runtime.InteropServices;

namespace DictaClone.Mac.Interop;

internal static partial class MacNative
{
    internal const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    internal const string AppKit =
        "/System/Library/Frameworks/AppKit.framework/AppKit";
    internal const string AudioToolbox =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    internal const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    internal const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    internal const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    internal const string PermissionShim = "DictaClonePermissions";

    [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint objc_getClass(string name);

    [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint sel_registerName(string name);

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(nint value);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFRetain(nint value);

    [LibraryImport(CoreFoundation)]
    internal static partial nuint CFHash(nint value);

    [LibraryImport(
        CoreFoundation,
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CFStringCreateWithCString(
        nint allocator,
        string value,
        uint encoding);

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool AXIsProcessTrusted();

    [LibraryImport(ApplicationServices)]
    internal static partial nint AXUIElementCreateSystemWide();

    [LibraryImport(ApplicationServices)]
    internal static partial int AXUIElementCopyAttributeValue(
        nint element,
        nint attribute,
        out nint value);

    [LibraryImport(ApplicationServices)]
    internal static partial int AXUIElementGetPid(
        nint element,
        out int processId);

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool CGPreflightListenEventAccess();

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool CGRequestListenEventAccess();

    [LibraryImport(PermissionShim)]
    internal static partial int DictaClonePermissionShimVersion();

    [LibraryImport(PermissionShim)]
    internal static partial int DictaCloneAccessibilityPermissionStatus();

    [LibraryImport(PermissionShim)]
    internal static partial int DictaCloneRequestAccessibilityPermission();

    [LibraryImport(PermissionShim)]
    internal static partial int DictaCloneInputMonitoringPermissionStatus();

    [LibraryImport(PermissionShim)]
    internal static partial int DictaCloneRequestInputMonitoringPermission();

    [LibraryImport(PermissionShim)]
    internal static partial void DictaCloneRequestMicrophonePermission(
        nint completion);

    [LibraryImport(PermissionShim)]
    internal static partial int DictaCloneDecodeMediaKeyEvent(
        nint keyboardEvent,
        out int mediaKeyType,
        out int isPressed);
}

internal static partial class ObjectiveC
{
    private const uint Utf8Encoding = 0x08000100;

    internal static nint Class(string name) => MacNative.objc_getClass(name);

    internal static nint Selector(string name) =>
        MacNative.sel_registerName(name);

    internal static nint Send(nint receiver, string selector) =>
        Send(receiver, Selector(selector));

    internal static nint Send(nint receiver, nint selector) =>
        NativeMessage.Send(receiver, selector);

    internal static nint Send(
        nint receiver,
        string selector,
        nint argument) =>
        NativeMessage.SendIntPtr(receiver, Selector(selector), argument);

    internal static nint Send(
        nint receiver,
        string selector,
        nint first,
        nint second) =>
        NativeMessage.SendTwoIntPtr(
            receiver,
            Selector(selector),
            first,
            second);

    internal static nint Send(
        nint receiver,
        string selector,
        nint first,
        nuint second) =>
        NativeMessage.SendIntPtrUInt(
            receiver,
            Selector(selector),
            first,
            second);

    internal static long SendInt64(nint receiver, string selector) =>
        NativeMessage.SendInt64(receiver, Selector(selector));

    internal static bool SendBool(
        nint receiver,
        string selector,
        nint first,
        nint second) =>
        NativeMessage.SendBoolTwoIntPtr(
            receiver,
            Selector(selector),
            first,
            second);

    internal static void SendVoid(
        nint receiver,
        string selector,
        nint argument) =>
        NativeMessage.SendVoidIntPtr(
            receiver,
            Selector(selector),
            argument);

    internal static nint CreateString(string value) =>
        MacNative.CFStringCreateWithCString(
            nint.Zero,
            value,
            Utf8Encoding);

    internal static string? GetString(nint value)
    {
        if (value == nint.Zero)
        {
            return null;
        }

        nint utf8 = Send(value, "UTF8String");
        return utf8 == nint.Zero
            ? null
            : Marshal.PtrToStringUTF8(utf8);
    }

    private static partial class NativeMessage
    {
        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        internal static partial nint Send(nint receiver, nint selector);

        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        internal static partial nint SendIntPtr(
            nint receiver,
            nint selector,
            nint argument);

        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        internal static partial nint SendTwoIntPtr(
            nint receiver,
            nint selector,
            nint first,
            nint second);

        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        internal static partial nint SendIntPtrUInt(
            nint receiver,
            nint selector,
            nint first,
            nuint second);

        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        internal static partial long SendInt64(nint receiver, nint selector);

        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static partial bool SendBoolTwoIntPtr(
            nint receiver,
            nint selector,
            nint first,
            nint second);

        [LibraryImport(MacNative.ObjectiveC, EntryPoint = "objc_msgSend")]
        internal static partial void SendVoidIntPtr(
            nint receiver,
            nint selector,
            nint argument);
    }
}
