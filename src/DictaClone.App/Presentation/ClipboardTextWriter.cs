using System.Runtime.InteropServices;

namespace DictaClone.App.Presentation;

internal sealed class ClipboardTextWriter
{
    private const int DefaultAttempts = 10;
    private static readonly TimeSpan DefaultRetryDelay =
        TimeSpan.FromMilliseconds(25);
    private readonly Action<string> _write;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _attempts;
    private readonly TimeSpan _retryDelay;

    public ClipboardTextWriter()
        : this(
            System.Windows.Clipboard.SetText,
            Task.Delay,
            DefaultAttempts,
            DefaultRetryDelay)
    {
    }

    internal ClipboardTextWriter(
        Action<string> write,
        Func<TimeSpan, CancellationToken, Task> delay,
        int attempts = DefaultAttempts,
        TimeSpan? retryDelay = null)
    {
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        _attempts = attempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    public async Task<bool> TryWriteAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int attempt = 0; attempt < _attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _write(text);
                return true;
            }
            catch (ExternalException) when (attempt + 1 < _attempts)
            {
                await _delay(
                    TimeSpan.FromTicks(_retryDelay.Ticks * (attempt + 1L)),
                    cancellationToken).ConfigureAwait(true);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }
}
