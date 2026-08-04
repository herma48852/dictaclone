using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;

namespace DictaClone.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonSettingsStore(DictaCloneDataPaths? paths = null)
    {
        _settingsPath = (paths ?? DictaCloneDataPaths.Default).SettingsFile;
    }

    public async Task<SettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new(
                    DictaCloneSettings.Default,
                    IsNew: true,
                    WasMigrated: false);
            }

            try
            {
                byte[] document = await File.ReadAllBytesAsync(
                    _settingsPath,
                    cancellationToken).ConfigureAwait(false);
                SettingsDocumentCodec.DecodedSettings decoded =
                    SettingsDocumentCodec.Deserialize(document);
                if (decoded.WasMigrated)
                {
                    await SaveWithoutLockAsync(
                        decoded.Settings,
                        cancellationToken).ConfigureAwait(false);
                }

                return new(
                    decoded.Settings,
                    IsNew: false,
                    decoded.WasMigrated);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is InvalidDataException or
                    System.Text.Json.JsonException or NotSupportedException)
            {
                string quarantinedPath = QuarantineCorruptFile();
                return new(
                    DictaCloneSettings.Default,
                    IsNew: false,
                    WasMigrated: false,
                    quarantinedPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        DictaCloneSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveWithoutLockAsync(settings, cancellationToken)
                .ConfigureAwait(false);
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

    private async Task SaveWithoutLockAsync(
        DictaCloneSettings settings,
        CancellationToken cancellationToken)
    {
        var errors = SettingsValidator.Validate(settings);
        if (!errors.IsEmpty)
        {
            throw new InvalidDataException(
                $"Settings validation failed at {errors[0].Path}: " +
                errors[0].Message);
        }

        await AtomicFileWriter.WriteAsync(
            _settingsPath,
            SettingsDocumentCodec.Serialize(settings),
            cancellationToken).ConfigureAwait(false);
    }

    private string QuarantineCorruptFile()
    {
        string directory = Path.GetDirectoryName(_settingsPath) ??
            throw new InvalidOperationException(
                "The settings path has no parent directory.");
        string quarantinePath = Path.Combine(
            directory,
            $"settings.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.Move(_settingsPath, quarantinePath);
        return quarantinePath;
    }
}
