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

    public async Task<SelectedTextSnapshot?> CaptureAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        string? text = await Task.Run(
            () => _native.GetSelectedText(target, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(text)
            ? null
            : new SelectedTextSnapshot(text, CreateFingerprint(text, target.Id));
    }

    public async Task<bool> RevalidateAsync(
        SelectedTextSnapshot snapshot,
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        string? current = await Task.Run(
            () => _native.GetSelectedText(target, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return false;
        }

        byte[] expected = Convert.FromHexString(snapshot.Fingerprint);
        byte[] actual = Convert.FromHexString(
            CreateFingerprint(current, target.Id));
        return CryptographicOperations.FixedTimeEquals(
            expected,
            actual);
    }

    private static string CreateFingerprint(string text, string targetId) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(targetId + "\n" + text)));
}

internal interface IMacSelectedTextApi
{
    string? GetSelectedText(
        ForegroundTarget target,
        CancellationToken cancellationToken);
}

internal sealed class NativeMacSelectedTextApi : IMacSelectedTextApi
{
    public string? GetSelectedText(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetProcessId(target.Id, out int processId))
        {
            return null;
        }

        nint application = MacNative.AXUIElementCreateApplication(processId);
        if (application == nint.Zero)
        {
            return null;
        }

        try
        {
            nint focused = NativeMacForegroundApi.CopyAttribute(
                application,
                "AXFocusedUIElement",
                cancellationToken);
            if (focused == nint.Zero)
            {
                return null;
            }

            try
            {
                if (MacNative.AXUIElementGetPid(focused, out int focusedPid) != 0 ||
                    focusedPid != processId)
                {
                    return null;
                }

                nint selected = NativeMacForegroundApi.CopyAttribute(
                    focused,
                    "AXSelectedText",
                    cancellationToken);
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
            MacNative.CFRelease(application);
        }
    }

    internal static bool TryGetProcessId(string targetId, out int processId)
    {
        processId = 0;
        int separator = targetId.IndexOf(':');
        return separator == 8 &&
            int.TryParse(
                targetId.AsSpan(0, separator),
                System.Globalization.NumberStyles.HexNumber,
                provider: null,
                out processId) &&
            processId > 0;
    }
}
