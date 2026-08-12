using System.Globalization;
using System.Runtime.InteropServices;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;

namespace DictaClone.Mac.Insertion;

public sealed class MacTextInsertionService : ITextInsertionService
{
    private const int ClipboardAttempts = 10;
    private static readonly TimeSpan ClipboardRetryDelay =
        TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ClipboardReadyDelay =
        TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ClipboardRestoreDelay =
        TimeSpan.FromMilliseconds(250);
    private readonly IMacPasteboard _pasteboard;
    private readonly IMacKeyboardInjector _keyboard;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MacTextInsertionService()
        : this(new NativeMacPasteboard(), new MacKeyboardInjector(), Task.Delay)
    {
    }

    internal MacTextInsertionService(
        IMacPasteboard pasteboard,
        IMacKeyboardInjector keyboard,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _pasteboard = pasteboard ??
            throw new ArgumentNullException(nameof(pasteboard));
        _keyboard = keyboard ??
            throw new ArgumentNullException(nameof(keyboard));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public Task InsertAsync(
        string text,
        ForegroundTarget target,
        InsertionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CharacterDelay < TimeSpan.Zero ||
            settings.CharacterDelay > TimeSpan.FromMilliseconds(100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Character delay must be between zero and 100 milliseconds.");
        }

        return settings.Mode switch
        {
            TextInsertionMode.Paste => PasteAsync(text, cancellationToken),
            TextInsertionMode.DelayedTyping => TypeAsync(
                text,
                settings.CharacterDelay,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    public async Task<bool> TryCopyTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _pasteboard.SetText(text);
                return true;
            }
            catch (ExternalException) when (attempt + 1 < ClipboardAttempts)
            {
                await _delay(
                    TimeSpan.FromTicks(
                        ClipboardRetryDelay.Ticks * (attempt + 1L)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task PasteAsync(
        string text,
        CancellationToken cancellationToken)
    {
        MacPasteboardSnapshot snapshot = await CaptureStableAsync(
            cancellationToken).ConfigureAwait(false);
        await ExecuteWithRetryAsync(
            () => _pasteboard.SetText(text),
            cancellationToken).ConfigureAwait(false);
        long insertionSequence = _pasteboard.ChangeCount;

        try
        {
            await _delay(ClipboardReadyDelay, cancellationToken)
                .ConfigureAwait(false);
            if (_pasteboard.ChangeCount != insertionSequence)
            {
                throw new ClipboardContentionException();
            }

            _keyboard.Paste();
            await _delay(ClipboardRestoreDelay, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ClipboardContentionException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is ExternalException or InvalidOperationException)
        {
            throw new InputInjectionException(exception);
        }
        finally
        {
            if (_pasteboard.ChangeCount == insertionSequence)
            {
                await ExecuteWithRetryAsync(
                    () => _pasteboard.Restore(snapshot),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task TypeAsync(
        string text,
        TimeSpan characterDelay,
        CancellationToken cancellationToken)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(
            normalized);
        bool first = true;
        while (elements.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!first && characterDelay > TimeSpan.Zero)
            {
                await _delay(characterDelay, cancellationToken)
                    .ConfigureAwait(false);
            }

            string element = elements.GetTextElement();
            try
            {
                switch (element)
                {
                    case "\n":
                        _keyboard.PressReturn();
                        break;
                    case "\t":
                        _keyboard.PressTab();
                        break;
                    default:
                        _keyboard.TypeText(element);
                        break;
                }
            }
            catch (Exception exception)
                when (exception is ExternalException or InvalidOperationException)
            {
                throw new InputInjectionException(exception);
            }

            first = false;
        }
    }

    private async Task<MacPasteboardSnapshot> CaptureStableAsync(
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long before = _pasteboard.ChangeCount;
                MacPasteboardSnapshot snapshot = _pasteboard.Capture();
                if (before == _pasteboard.ChangeCount)
                {
                    return snapshot;
                }
            }
            catch (ExternalException exception)
            {
                lastException = exception;
            }

            await DelayBeforeRetryAsync(attempt, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new ClipboardContentionException(lastException);
    }

    private async Task ExecuteWithRetryAsync(
        Action operation,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                operation();
                return;
            }
            catch (ExternalException exception)
            {
                lastException = exception;
            }

            await DelayBeforeRetryAsync(attempt, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new ClipboardContentionException(lastException);
    }

    private Task DelayBeforeRetryAsync(
        int attempt,
        CancellationToken cancellationToken)
    {
        if (attempt + 1 >= ClipboardAttempts)
        {
            return Task.CompletedTask;
        }

        TimeSpan delay = TimeSpan.FromTicks(
            ClipboardRetryDelay.Ticks * (attempt + 1L));
        return _delay(delay, cancellationToken);
    }
}
