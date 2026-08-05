using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DictaClone.Windows;

internal sealed partial class WindowsClipboardBackend : IClipboardBackend
{
    public uint GetSequenceNumber() => GetClipboardSequenceNumber();

    public ClipboardSnapshot Capture()
    {
        IDataObject? source = Clipboard.GetDataObject();
        if (source is null)
        {
            ClipboardNativeFormatGuard.EnsureCaptureConsistent(
                capturedFormatCount: 0);
            return new(Data: null);
        }

        var snapshot = new DataObject();
        int capturedFormatCount = 0;
        foreach (string format in source.GetFormats(autoConvert: false))
        {
            object? data = source.GetData(format, autoConvert: false);
            if (data is not null)
            {
                snapshot.SetData(
                    format,
                    autoConvert: false,
                    CloneClipboardValue(data));
                capturedFormatCount++;
            }
        }

        ClipboardNativeFormatGuard.EnsureCaptureConsistent(
            capturedFormatCount);
        return new(snapshot);
    }

    public void SetUnicodeText(string text)
    {
        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        bool containsText = Clipboard.ContainsText(
            TextDataFormat.UnicodeText);
        string actual = containsText
            ? Clipboard.GetText(TextDataFormat.UnicodeText)
            : string.Empty;
        if (!string.Equals(text, actual, StringComparison.Ordinal))
        {
            throw new ClipboardFormatUnavailableException(
                "Clipboard text was not published after it was set.");
        }
    }

    public void Restore(ClipboardSnapshot snapshot)
    {
        if (snapshot.Data is IDataObject data)
        {
            int expectedFormatCount = data
                .GetFormats(autoConvert: false)
                .Length;
            Clipboard.SetDataObject(data, copy: true);
            ClipboardNativeFormatGuard.EnsureRestoreConsistent(
                expectedFormatCount);
        }
        else
        {
            Clipboard.Clear();
        }
    }

    private static object CloneClipboardValue(object value) => value switch
    {
        string text => text,
        byte[] bytes => bytes.ToArray(),
        MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
        ICloneable cloneable => cloneable.Clone(),
        _ => value,
    };

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
