using System.Security.Cryptography;
using System.Text;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Mac.Foreground;
using DictaClone.Mac.Interop;

namespace DictaClone.Mac.Selection;

public sealed class MacSelectedTextService : ISelectedTextService
{
    private readonly IMacSelectedTextApi _native;

    public MacSelectedTextService()
        : this(new NativeMacSelectedTextApi())
    {
    }

    internal MacSelectedTextService(IMacSelectedTextApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public Task<SelectedTextSnapshot?> CaptureAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        string? text = _native.GetSelectedText();
        return Task.FromResult(string.IsNullOrEmpty(text)
            ? null
            : new SelectedTextSnapshot(text, CreateFingerprint(text, target.Id)));
    }

    public Task<bool> RevalidateAsync(
        SelectedTextSnapshot snapshot,
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        string? current = _native.GetSelectedText();
        if (current is null)
        {
            return Task.FromResult(false);
        }

        byte[] expected = Convert.FromHexString(snapshot.Fingerprint);
        byte[] actual = Convert.FromHexString(
            CreateFingerprint(current, target.Id));
        return Task.FromResult(CryptographicOperations.FixedTimeEquals(
            expected,
            actual));
    }

    private static string CreateFingerprint(string text, string targetId) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(targetId + "\n" + text)));
}

internal interface IMacSelectedTextApi
{
    string? GetSelectedText();
}

internal sealed class NativeMacSelectedTextApi : IMacSelectedTextApi
{
    public string? GetSelectedText()
    {
        nint system = MacNative.AXUIElementCreateSystemWide();
        if (system == nint.Zero)
        {
            return null;
        }

        try
        {
            nint focused = NativeMacForegroundApi.CopyAttribute(
                system,
                "AXFocusedUIElement");
            if (focused == nint.Zero)
            {
                return null;
            }

            try
            {
                nint selected = NativeMacForegroundApi.CopyAttribute(
                    focused,
                    "AXSelectedText");
                if (selected == nint.Zero)
                {
                    return null;
                }

                try
                {
                    return ObjectiveC.GetString(selected);
                }
                finally
                {
                    MacNative.CFRelease(selected);
                }
            }
            finally
            {
                MacNative.CFRelease(focused);
            }
        }
        finally
        {
            MacNative.CFRelease(system);
        }
    }
}
