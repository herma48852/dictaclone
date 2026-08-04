using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;

namespace DictaClone.Windows;

public sealed class SelectedTextService : ISelectedTextService
{
    private const int ClipboardAttempts = 5;
    private const int SelectionPollAttempts = 25;
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(20);
    private readonly IStaThreadRunner _staThreads;
    private readonly ISelectionClipboard _clipboard;
    private readonly ISelectionCopyInjector _copy;
    private readonly IInsertionDelay _delay;

    public SelectedTextService()
        : this(
            new StaThreadRunner(),
            new WindowsSelectionClipboard(),
            new SelectionCopyInjector(),
            new InsertionDelay())
    {
    }

    internal SelectedTextService(
        IStaThreadRunner staThreads,
        ISelectionClipboard clipboard,
        ISelectionCopyInjector copy,
        IInsertionDelay delay)
    {
        _staThreads = staThreads ?? throw new ArgumentNullException(nameof(staThreads));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async Task<SelectedTextSnapshot?> CaptureAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        SelectedTextSnapshot? result = null;
        await _staThreads.RunAsync(
            () => result = CaptureOnSta(target, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> RevalidateAsync(
        SelectedTextSnapshot snapshot,
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SelectedTextSnapshot? current = await CaptureAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (current is null ||
            !string.Equals(current.Text, snapshot.Text, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] expected = Convert.FromHexString(snapshot.Fingerprint);
        byte[] actual = Convert.FromHexString(current.Fingerprint);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private SelectedTextSnapshot? CaptureOnSta(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ClipboardSnapshot snapshot = CaptureClipboard(cancellationToken);
        uint ownedSequence = 0;
        try
        {
            ExecuteClipboard(_clipboard.Clear, cancellationToken);
            uint emptySequence = _clipboard.GetSequenceNumber();
            ownedSequence = emptySequence;
            _copy.SendCopy(EmacsTargetDetector.IsNativeEmacs(target));
            for (int attempt = 0; attempt < SelectionPollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint currentSequence = _clipboard.GetSequenceNumber();
                if (currentSequence != emptySequence)
                {
                    ownedSequence = currentSequence;
                    string? text = ReadClipboardText(cancellationToken);
                    if (string.IsNullOrEmpty(text))
                    {
                        return null;
                    }

                    return new(text, Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(text))));
                }

                _delay.Wait(PollDelay, cancellationToken);
            }

            return null;
        }
        finally
        {
            if (ownedSequence != 0 &&
                _clipboard.GetSequenceNumber() == ownedSequence)
            {
                ExecuteClipboard(
                    () => _clipboard.Restore(snapshot),
                    CancellationToken.None);
            }
        }
    }

    private string? ReadClipboardText(CancellationToken cancellationToken)
    {
        string? result = null;
        ExecuteClipboard(
            () => result = _clipboard.GetUnicodeText(),
            cancellationToken);
        return result;
    }

    private ClipboardSnapshot CaptureClipboard(CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                uint before = _clipboard.GetSequenceNumber();
                ClipboardSnapshot snapshot = _clipboard.Capture();
                if (before == _clipboard.GetSequenceNumber())
                {
                    return snapshot;
                }
            }
            catch (ExternalException exception)
            {
                last = exception;
            }

            _delay.Wait(PollDelay, cancellationToken);
        }

        throw new ClipboardContentionException(last);
    }

    private void ExecuteClipboard(Action action, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                action();
                return;
            }
            catch (ExternalException exception)
            {
                last = exception;
            }

            _delay.Wait(PollDelay, cancellationToken);
        }

        throw new ClipboardContentionException(last);
    }
}

internal interface ISelectionClipboard
{
    uint GetSequenceNumber();
    ClipboardSnapshot Capture();
    string? GetUnicodeText();
    void Clear();
    void Restore(ClipboardSnapshot snapshot);
}

internal interface ISelectionCopyInjector
{
    void SendCopy(bool emacs);
}

internal sealed class SelectionCopyInjector : ISelectionCopyInjector
{
    public void SendCopy(bool emacs) =>
        SendInputKeyboardInjector.SendSelectionCopy(emacs);
}

internal sealed partial class WindowsSelectionClipboard : ISelectionClipboard
{
    public uint GetSequenceNumber() => GetClipboardSequenceNumber();

    public ClipboardSnapshot Capture()
    {
        IDataObject? source = Clipboard.GetDataObject();
        if (source is null)
        {
            return new(null);
        }

        var copy = new DataObject();
        foreach (string format in source.GetFormats(autoConvert: false))
        {
            object? value = source.GetData(format, autoConvert: false);
            if (value is not null)
            {
                copy.SetData(
                    format,
                    autoConvert: false,
                    CloneClipboardValue(value));
            }
        }

        return new(copy);
    }

    private static object CloneClipboardValue(object value) => value switch
    {
        string text => text,
        byte[] bytes => bytes.ToArray(),
        MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
        ICloneable cloneable => cloneable.Clone(),
        _ => value,
    };

    public string? GetUnicodeText() => Clipboard.ContainsText(
        TextDataFormat.UnicodeText)
            ? Clipboard.GetText(TextDataFormat.UnicodeText)
            : null;

    public void Clear() => Clipboard.Clear();

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

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
