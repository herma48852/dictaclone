using System.Text.Json;
using DictaClone.Core.Contracts;

namespace DictaClone.Infrastructure;

public sealed class PrivacySafeDiagnosticLog : IDiagnosticLog, IDisposable
{
    private readonly string _diagnosticsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public PrivacySafeDiagnosticLog(DictaCloneDataPaths? paths = null)
    {
        _diagnosticsPath =
            (paths ?? DictaCloneDataPaths.Default).DiagnosticsFile;
    }

    public async ValueTask WriteAsync(
        DiagnosticEventKind eventKind,
        DiagnosticOutcome outcome,
        TimeSpan? duration = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var record = new DiagnosticRecord(
            DateTimeOffset.UtcNow,
            eventKind,
            outcome,
            duration.HasValue
                ? checked((long)Math.Round(duration.Value.TotalMilliseconds))
                : null,
            exception?.GetType().Name);
        byte[] line = JsonSerializer.SerializeToUtf8Bytes(record);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string directory = Path.GetDirectoryName(_diagnosticsPath) ??
                throw new InvalidOperationException(
                    "The diagnostics path has no parent directory.");
            Directory.CreateDirectory(directory);
            await using var stream = new FileStream(
                _diagnosticsPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private sealed record DiagnosticRecord(
        DateTimeOffset CreatedUtc,
        DiagnosticEventKind EventKind,
        DiagnosticOutcome Outcome,
        long? DurationMilliseconds,
        string? ErrorType);
}
