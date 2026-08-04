using System.Collections.Immutable;
using DictaClone.Core.Settings;

namespace DictaClone.Core.Contracts;

public interface ISettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        DictaCloneSettings settings,
        CancellationToken cancellationToken);
}

public sealed record SettingsLoadResult(
    DictaCloneSettings Settings,
    bool IsNew,
    bool WasMigrated,
    string? QuarantinedFilePath = null);

public interface ISettingsTransferService
{
    Task ExportAsync(
        string destinationPath,
        DictaCloneSettings settings,
        CancellationToken cancellationToken);

    Task<DictaCloneSettings> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken);
}

public interface ITranscriptHistoryStore
{
    Task<HistoryLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task AppendAsync(
        TranscriptHistoryEntry entry,
        int maximumEntries,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed record HistoryLoadResult(
    ImmutableArray<TranscriptHistoryEntry> Entries,
    string? QuarantinedFilePath = null);

public sealed record TranscriptHistoryEntry(
    DateTimeOffset CreatedUtc,
    string Text);

public interface IStartupRegistrationService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public interface IDiagnosticLog
{
    ValueTask WriteAsync(
        DiagnosticEventKind eventKind,
        DiagnosticOutcome outcome,
        TimeSpan? duration = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default);
}

public enum DiagnosticEventKind
{
    ApplicationStartup,
    ApplicationShutdown,
    SettingsLoad,
    SettingsSave,
    SettingsImport,
    SettingsExport,
    Dictation,
    HistoryWrite,
    SupportBundle,
}

public enum DiagnosticOutcome
{
    Started,
    Succeeded,
    Failed,
    Cancelled,
    Recovered,
}

public interface ISupportBundleService
{
    Task CreateAsync(
        string destinationPath,
        DictaCloneSettings settings,
        CancellationToken cancellationToken);
}
