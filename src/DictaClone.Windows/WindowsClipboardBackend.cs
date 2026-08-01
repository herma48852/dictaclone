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
            return new(Data: null);
        }

        var snapshot = new DataObject();
        foreach (string format in source.GetFormats(autoConvert: false))
        {
            object? data = source.GetData(format, autoConvert: false);
            if (data is not null)
            {
                snapshot.SetData(
                    format,
                    autoConvert: false,
                    CloneClipboardValue(data));
            }
        }

        return new(snapshot);
    }

    public void SetUnicodeText(string text) =>
        Clipboard.SetText(text, TextDataFormat.UnicodeText);

    public void Restore(ClipboardSnapshot snapshot)
    {
        if (snapshot.Data is IDataObject data)
        {
            Clipboard.SetDataObject(data, copy: true);
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
