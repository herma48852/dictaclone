using System.Collections.Immutable;
using System.Text.Json;
using DictaClone.Core.Contracts;

namespace DictaClone.Infrastructure;

public sealed class JsonTranscriptHistoryStore : ITranscriptHistoryStore, IDisposable
{
    private const int HistorySchemaVersion = 1;
    private readonly string _historyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonTranscriptHistoryStore(DictaCloneDataPaths? paths = null)
    {
        _historyPath = (paths ?? DictaCloneDataPaths.Default).HistoryFile;
    }

    public async Task<HistoryLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadWithoutLockAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(
        TranscriptHistoryEntry entry,
        int maximumEntries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntries, 500);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HistoryLoadResult loaded = await LoadWithoutLockAsync(
                cancellationToken).ConfigureAwait(false);
            ImmutableArray<TranscriptHistoryEntry> entries =
            [
                .. loaded.Entries,
                entry,
            ];
            if (entries.Length > maximumEntries)
            {
                entries = [.. entries[^maximumEntries..]];
            }

            await SaveWithoutLockAsync(entries, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_historyPath))
            {
                File.Delete(_historyPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task<HistoryLoadResult> LoadWithoutLockAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_historyPath))
        {
            return new(ImmutableArray<TranscriptHistoryEntry>.Empty);
        }

        try
        {
            byte[] document = await File.ReadAllBytesAsync(
                _historyPath,
                cancellationToken).ConfigureAwait(false);
            HistoryDocument? history = JsonSerializer.Deserialize<HistoryDocument>(
                document);
            if (history is null ||
                history.SchemaVersion != HistorySchemaVersion ||
                history.Entries.IsDefault ||
                history.Entries.Any(entry =>
                    string.IsNullOrWhiteSpace(entry.Text)))
            {
                throw new InvalidDataException(
                    "Transcript history is not valid.");
            }

            return new(history.Entries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is InvalidDataException or JsonException or
                NotSupportedException)
        {
            return new(
                ImmutableArray<TranscriptHistoryEntry>.Empty,
                QuarantineCorruptFile());
        }
    }

    private Task SaveWithoutLockAsync(
        ImmutableArray<TranscriptHistoryEntry> entries,
        CancellationToken cancellationToken) =>
        AtomicFileWriter.WriteAsync(
            _historyPath,
            JsonSerializer.SerializeToUtf8Bytes(new HistoryDocument(
                HistorySchemaVersion,
                entries)),
            cancellationToken);

    private string QuarantineCorruptFile()
    {
        string directory = Path.GetDirectoryName(_historyPath) ??
            throw new InvalidOperationException(
                "The history path has no parent directory.");
        string quarantinePath = Path.Combine(
            directory,
            $"history.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Move(_historyPath, quarantinePath);
        return quarantinePath;
    }

    private sealed record HistoryDocument(
        int SchemaVersion,
        ImmutableArray<TranscriptHistoryEntry> Entries);
}
