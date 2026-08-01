using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;

namespace DictaClone.App.Presentation;

public sealed class TriggerUiController : IDisposable
{
    private static readonly TimeSpan ProcessingPreviewDuration =
        TimeSpan.FromMilliseconds(350);

    private readonly IStatusOverlay _overlay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _previewCancellation;

    public TriggerUiController(
        IStatusOverlay overlay,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _delay = delay ?? Task.Delay;
    }

    public async Task HandleAsync(HotkeyEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        var previewCancellation = new CancellationTokenSource();
        _previewCancellation = previewCancellation;

        if (inputEvent.Action == HotkeyAction.Cancel &&
            inputEvent.Kind == HotkeyEventKind.Pressed)
        {
            _overlay.ShowStatus(OverlayStatus.Failure, "Dictation cancelled");
            return;
        }

        if (inputEvent.Kind == HotkeyEventKind.Pressed)
        {
            _overlay.ShowStatus(
                OverlayStatus.Recording,
                GetRecordingLabel(inputEvent.Action));
            return;
        }

        _overlay.ShowStatus(OverlayStatus.Processing);

        try
        {
            await _delay(
                ProcessingPreviewDuration,
                previewCancellation.Token);
            _overlay.ShowStatus(
                OverlayStatus.Success,
                "✓  Shortcut detected");
        }
        catch (OperationCanceledException)
            when (previewCancellation.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
    }

    private static string GetRecordingLabel(HotkeyAction action) =>
        action switch
        {
            HotkeyAction.Dictation => "●  Listening…",
            HotkeyAction.SmartEdit => "●  Smart Edit listening…",
            HotkeyAction.TypingMode => "●  Typing Mode listening…",
            HotkeyAction.Cancel => "Cancelling…",
            _ => "●  Listening…",
        };
}
