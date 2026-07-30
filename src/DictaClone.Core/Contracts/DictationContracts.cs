using DictaClone.Core.Dictation;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;

namespace DictaClone.Core.Contracts;

public interface IAudioCaptureService
{
    Task<IAudioCaptureSession> StartAsync(
        AudioSettings settings,
        CancellationToken cancellationToken);
}

public interface IAudioCaptureSession : IAsyncDisposable
{
    Task<CapturedAudio> StopAsync(CancellationToken cancellationToken);

    Task CancelAsync(CancellationToken cancellationToken);
}

public interface ITranscriptionEngine
{
    Task<string> TranscribeAsync(
        CapturedAudio audio,
        TranscriptionSettings settings,
        CancellationToken cancellationToken);
}

public interface ITextProcessor
{
    Task<string> ProcessAsync(
        string transcript,
        DictationMode mode,
        TextProcessingSettings settings,
        CancellationToken cancellationToken);
}

public interface ISmartEditProvider
{
    Task<string> EditAsync(
        SmartEditRequest request,
        CancellationToken cancellationToken);
}

public interface IForegroundTargetService
{
    Task<ForegroundTarget> CaptureAsync(CancellationToken cancellationToken);

    Task<bool> IsCurrentAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken);
}

public interface ITextInsertionService
{
    Task InsertAsync(
        string text,
        ForegroundTarget target,
        InsertionSettings settings,
        CancellationToken cancellationToken);
}

public interface IHotkeyEventSource : IAsyncDisposable
{
    event EventHandler<HotkeyEvent>? Triggered;

    Task StartAsync(
        IReadOnlyCollection<HotkeyBinding> bindings,
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public sealed record SmartEditRequest(
    string Instruction,
    string? SelectedText,
    string ProcessName,
    string WindowClass);

public sealed record HotkeyEvent(
    HotkeyAction Action,
    HotkeyEventKind Kind,
    bool IsInjected);

public enum HotkeyEventKind
{
    Pressed,
    Released,
}
