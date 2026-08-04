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

public interface IAudioLevelSource
{
    event EventHandler<AudioLevelChangedEvent>? LevelChanged;
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

public interface ISelectedTextService
{
    Task<SelectedTextSnapshot?> CaptureAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken);

    Task<bool> RevalidateAsync(
        SelectedTextSnapshot snapshot,
        ForegroundTarget target,
        CancellationToken cancellationToken);
}

public interface ISecretStore
{
    Task<string?> ReadAsync(string name, CancellationToken cancellationToken);

    Task WriteAsync(
        string name,
        string value,
        CancellationToken cancellationToken);

    Task DeleteAsync(string name, CancellationToken cancellationToken);
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
    string WindowClass,
    TextProcessingSettings TextSettings,
    SmartEditSettings ProviderSettings);

public sealed record SelectedTextSnapshot(
    string Text,
    string Fingerprint);

public sealed record HotkeyEvent(
    HotkeyAction Action,
    HotkeyEventKind Kind,
    bool IsInjected);

public enum HotkeyEventKind
{
    Pressed,
    Released,
}

public sealed record AudioLevelChangedEvent(double RootMeanSquare, double Peak);
