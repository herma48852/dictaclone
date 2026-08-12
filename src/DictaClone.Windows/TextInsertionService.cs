using System.Runtime.InteropServices;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;

namespace DictaClone.Windows;

public sealed class TextInsertionService : ITextInsertionService
{
    private static readonly TimeSpan DefaultClipboardRetryDelay =
        TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan DefaultClipboardReadyDelay =
        TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan DefaultClipboardRestoreDelay =
        TimeSpan.FromMilliseconds(250);
    private const int DefaultClipboardAttempts = 10;

    private readonly IClipboardBackend _clipboard;
    private readonly IKeyboardInjector _keyboard;
    private readonly IStaThreadRunner _staThreads;
    private readonly IInsertionDelay _delay;
    private readonly int _clipboardAttempts;
    private readonly TimeSpan _clipboardRetryDelay;
    private readonly TimeSpan _clipboardReadyDelay;
    private readonly TimeSpan _clipboardRestoreDelay;

    public TextInsertionService()
        : this(
            new WindowsClipboardBackend(),
            new SendInputKeyboardInjector(),
            new StaThreadRunner(),
            new InsertionDelay(),
            DefaultClipboardAttempts,
            DefaultClipboardRetryDelay,
            DefaultClipboardReadyDelay,
            DefaultClipboardRestoreDelay)
    {
    }

    internal TextInsertionService(
        IClipboardBackend clipboard,
        IKeyboardInjector keyboard,
        IStaThreadRunner staThreads,
        IInsertionDelay delay,
        int clipboardAttempts = DefaultClipboardAttempts,
        TimeSpan? clipboardRetryDelay = null,
        TimeSpan? clipboardReadyDelay = null,
        TimeSpan? clipboardRestoreDelay = null)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _staThreads = staThreads ?? throw new ArgumentNullException(nameof(staThreads));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        ArgumentOutOfRangeException.ThrowIfLessThan(clipboardAttempts, 1);
        _clipboardAttempts = clipboardAttempts;
        _clipboardRetryDelay =
            clipboardRetryDelay ?? DefaultClipboardRetryDelay;
        _clipboardReadyDelay =
            clipboardReadyDelay ?? DefaultClipboardReadyDelay;
        _clipboardRestoreDelay =
            clipboardRestoreDelay ?? DefaultClipboardRestoreDelay;
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
        cancellationToken.ThrowIfCancellationRequested();

        if (target.IsElevated)
        {
            throw new ElevatedTargetException();
        }

        if (settings.CharacterDelay < TimeSpan.Zero ||
            settings.CharacterDelay > TimeSpan.FromMilliseconds(100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Character delay must be between zero and 100 milliseconds.");
        }

        if (EmacsTargetDetector.IsNativeEmacs(target))
        {
            return _staThreads.RunAsync(
                () => InsertWithClipboard(
                    text,
                    ClipboardInsertionShortcut.EmacsYank,
                    cancellationToken),
                cancellationToken);
        }

        return settings.Mode switch
        {
            TextInsertionMode.Paste => _staThreads.RunAsync(
                () => InsertWithClipboard(
                    text,
                    WindowsTerminalTargetDetector.IsWindowsTerminal(target)
                        ? ClipboardInsertionShortcut.TerminalPaste
                        : ClipboardInsertionShortcut.StandardPaste,
                    cancellationToken),
                cancellationToken),
            TextInsertionMode.DelayedTyping => TypeAsync(
                text,
                settings.CharacterDelay,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.Mode,
                "Unknown text insertion mode."),
        };
    }

    private void InsertWithClipboard(
        string text,
        ClipboardInsertionShortcut shortcut,
        CancellationToken cancellationToken)
    {
        ClipboardSnapshot snapshot = CaptureStableSnapshot(cancellationToken);
        ExecuteClipboard(
            () => _clipboard.SetUnicodeText(text),
            cancellationToken);
        uint insertionSequence = _clipboard.GetSequenceNumber();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _delay.Wait(_clipboardReadyDelay, cancellationToken);
            if (_clipboard.GetSequenceNumber() != insertionSequence)
            {
                throw new ClipboardContentionException();
            }

            _keyboard.SendClipboardInsert(shortcut);
            _delay.Wait(_clipboardRestoreDelay, cancellationToken);
        }
        finally
        {
            if (_clipboard.GetSequenceNumber() == insertionSequence)
            {
                ExecuteClipboard(
                    () => _clipboard.Restore(snapshot),
                    CancellationToken.None);
            }
        }
    }

    private ClipboardSnapshot CaptureStableSnapshot(
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < _clipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                uint before = _clipboard.GetSequenceNumber();
                ClipboardSnapshot snapshot = _clipboard.Capture();
                uint after = _clipboard.GetSequenceNumber();
                if (before == after)
                {
                    return snapshot;
                }
            }
            catch (ExternalException exception)
            {
                lastException = exception;
            }

            WaitBeforeRetry(attempt, cancellationToken);
        }

        throw new ClipboardContentionException(lastException);
    }

    private void ExecuteClipboard(
        Action operation,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < _clipboardAttempts; attempt++)
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

            WaitBeforeRetry(attempt, cancellationToken);
        }

        throw new ClipboardContentionException(lastException);
    }

    private void WaitBeforeRetry(
        int attempt,
        CancellationToken cancellationToken)
    {
        if (attempt + 1 < _clipboardAttempts)
        {
            _delay.Wait(
                TimeSpan.FromTicks(
                    _clipboardRetryDelay.Ticks * (attempt + 1L)),
                cancellationToken);
        }
    }

    private async Task TypeAsync(
        string text,
        TimeSpan characterDelay,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TextInputToken> tokens = TextInputPlanner.Tokenize(text);

        for (int index = 0; index < tokens.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextInputToken token = tokens[index];
            switch (token.Kind)
            {
                case TextInputTokenKind.Text:
                    bool sentMapped =
                        token.Value.Length == 1 &&
                        _keyboard.TrySendMappedCharacter(token.Value[0]);
                    if (!sentMapped)
                    {
                        _keyboard.SendUnicode(token.Value);
                    }

                    break;
                case TextInputTokenKind.Enter:
                    _keyboard.SendVirtualKey(VirtualKey.Enter);
                    break;
                case TextInputTokenKind.Tab:
                    _keyboard.SendVirtualKey(VirtualKey.Tab);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown text input token.");
            }

            if (index + 1 < tokens.Count && characterDelay > TimeSpan.Zero)
            {
                await _delay
                    .WaitAsync(characterDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}

internal static class EmacsTargetDetector
{
    public static bool IsNativeEmacs(ForegroundTarget target) =>
        string.Equals(
            target.ProcessName,
            "emacs",
            StringComparison.OrdinalIgnoreCase);
}

internal static class WindowsTerminalTargetDetector
{
    private const string ProcessName = "WindowsTerminal";
    private const string WindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    public static bool IsWindowsTerminal(ForegroundTarget target) =>
        string.Equals(
            target.ProcessName,
            ProcessName,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            target.WindowClass,
            WindowClass,
            StringComparison.OrdinalIgnoreCase);
}

internal static class TextInputPlanner
{
    public static IReadOnlyList<TextInputToken> Tokenize(string text)
    {
        var tokens = new List<TextInputToken>(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                tokens.Add(new(TextInputTokenKind.Enter, string.Empty));
            }
            else if (character == '\n')
            {
                tokens.Add(new(TextInputTokenKind.Enter, string.Empty));
            }
            else if (character == '\t')
            {
                tokens.Add(new(TextInputTokenKind.Tab, string.Empty));
            }
            else if (char.IsHighSurrogate(character) &&
                     index + 1 < text.Length &&
                     char.IsLowSurrogate(text[index + 1]))
            {
                tokens.Add(new(
                    TextInputTokenKind.Text,
                    text.Substring(index, length: 2)));
                index++;
            }
            else
            {
                tokens.Add(new(TextInputTokenKind.Text, character.ToString()));
            }
        }

        return tokens;
    }
}

internal enum TextInputTokenKind
{
    Text,
    Enter,
    Tab,
}

internal readonly record struct TextInputToken(
    TextInputTokenKind Kind,
    string Value);

internal enum VirtualKey : ushort
{
    Tab = 0x09,
    Enter = 0x0D,
}

internal enum ClipboardInsertionShortcut
{
    StandardPaste,
    TerminalPaste,
    EmacsYank,
}

internal interface IClipboardBackend
{
    uint GetSequenceNumber();

    ClipboardSnapshot Capture();

    void SetUnicodeText(string text);

    void Restore(ClipboardSnapshot snapshot);
}

internal sealed record ClipboardSnapshot(object? Data);

internal interface IKeyboardInjector
{
    void SendClipboardInsert(ClipboardInsertionShortcut shortcut);

    void SendUnicode(string text);

    void SendVirtualKey(VirtualKey key);

    bool TrySendMappedCharacter(char character);
}

internal interface IStaThreadRunner
{
    Task RunAsync(Action action, CancellationToken cancellationToken);
}

internal interface IInsertionDelay
{
    void Wait(TimeSpan delay, CancellationToken cancellationToken);

    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class InsertionDelay : IInsertionDelay
{
    public void Wait(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (cancellationToken.WaitHandle.WaitOne(delay))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public Task WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

internal sealed class StaThreadRunner : IStaThreadRunner
{
    public Task RunAsync(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "DictaClone Clipboard Transaction",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
