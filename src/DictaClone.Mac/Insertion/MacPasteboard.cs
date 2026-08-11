using System.Runtime.InteropServices;
using DictaClone.Mac.Interop;

namespace DictaClone.Mac.Insertion;

internal interface IMacPasteboard
{
    long ChangeCount { get; }

    MacPasteboardSnapshot Capture();

    void SetText(string text);

    void Restore(MacPasteboardSnapshot snapshot);
}

internal sealed record MacPasteboardSnapshot(
    IReadOnlyList<IReadOnlyList<MacPasteboardValue>> Items);

internal sealed record MacPasteboardValue(string Type, byte[] Data);

internal sealed class NativeMacPasteboard : IMacPasteboard
{
    private const string Utf8PlainText = "public.utf8-plain-text";

    private static nint Pasteboard => ObjectiveC.Send(
        ObjectiveC.Class("NSPasteboard"),
        "generalPasteboard");

    public long ChangeCount => ObjectiveC.SendInt64(
        Pasteboard,
        "changeCount");

    public MacPasteboardSnapshot Capture()
    {
        nint items = ObjectiveC.Send(Pasteboard, "pasteboardItems");
        int itemCount = checked((int)ObjectiveC.SendInt64(items, "count"));
        var capturedItems = new List<IReadOnlyList<MacPasteboardValue>>(
            itemCount);

        for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            nint item = ObjectiveC.Send(
                items,
                "objectAtIndex:",
                itemIndex);
            nint types = ObjectiveC.Send(item, "types");
            int typeCount = checked((int)ObjectiveC.SendInt64(types, "count"));
            var values = new List<MacPasteboardValue>(typeCount);

            for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
            {
                nint type = ObjectiveC.Send(
                    types,
                    "objectAtIndex:",
                    typeIndex);
                string? typeName = ObjectiveC.GetString(type);
                nint data = ObjectiveC.Send(item, "dataForType:", type);
                if (typeName is null || data == nint.Zero)
                {
                    continue;
                }

                int length = checked((int)ObjectiveC.SendInt64(data, "length"));
                var bytes = new byte[length];
                if (length > 0)
                {
                    nint source = ObjectiveC.Send(data, "bytes");
                    Marshal.Copy(source, bytes, 0, length);
                }

                values.Add(new(typeName, bytes));
            }

            capturedItems.Add(values);
        }

        return new(capturedItems);
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = ObjectiveC.SendInt64(Pasteboard, "clearContents");
        nint value = ObjectiveC.CreateString(text);
        nint type = ObjectiveC.CreateString(Utf8PlainText);
        try
        {
            if (!ObjectiveC.SendBool(
                    Pasteboard,
                    "setString:forType:",
                    value,
                    type))
            {
                throw new MacPasteboardException(
                    "macOS did not publish text to the pasteboard.");
            }
        }
        finally
        {
            MacNative.CFRelease(type);
            MacNative.CFRelease(value);
        }
    }

    public void Restore(MacPasteboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        nint pasteboard = Pasteboard;
        _ = ObjectiveC.SendInt64(pasteboard, "clearContents");
        if (snapshot.Items.Count == 0)
        {
            return;
        }

        nint restoredItems = ObjectiveC.Send(
            ObjectiveC.Class("NSMutableArray"),
            "array");
        foreach (IReadOnlyList<MacPasteboardValue> sourceItem in snapshot.Items)
        {
            nint item = ObjectiveC.Send(
                ObjectiveC.Send(ObjectiveC.Class("NSPasteboardItem"), "alloc"),
                "init");
            try
            {
                foreach (MacPasteboardValue value in sourceItem)
                {
                    RestoreValue(item, value);
                }

                ObjectiveC.SendVoid(restoredItems, "addObject:", item);
            }
            finally
            {
                _ = ObjectiveC.Send(item, "release");
            }
        }

        if (ObjectiveC.Send(
                pasteboard,
                "writeObjects:",
                restoredItems) == nint.Zero)
        {
            throw new MacPasteboardException(
                "macOS did not restore the previous pasteboard items.");
        }
    }

    private static unsafe void RestoreValue(
        nint item,
        MacPasteboardValue value)
    {
        nint type = ObjectiveC.CreateString(value.Type);
        try
        {
            fixed (byte* bytes = value.Data)
            {
                nint data = ObjectiveC.Send(
                    ObjectiveC.Class("NSData"),
                    "dataWithBytes:length:",
                    (nint)bytes,
                    checked((nuint)value.Data.Length));
                if (!ObjectiveC.SendBool(
                        item,
                        "setData:forType:",
                        data,
                        type))
                {
                    throw new MacPasteboardException(
                        $"macOS did not restore pasteboard type {value.Type}.");
                }
            }
        }
        finally
        {
            MacNative.CFRelease(type);
        }
    }
}

internal sealed class MacPasteboardException(string message)
    : ExternalException(message);
