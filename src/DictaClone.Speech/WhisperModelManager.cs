using System.Security.Cryptography;

namespace DictaClone.Speech;

public sealed class WhisperModelManager : IDisposable
{
    private readonly string _modelDirectory;
    private readonly IModelContentSource _contentSource;
    private readonly Dictionary<string, WhisperModelDefinition> _models;
    private readonly bool _ownsContentSource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public WhisperModelManager(string modelDirectory)
        : this(
            modelDirectory,
            new HttpModelContentSource(),
            WhisperModelCatalog.AvailableModels,
            ownsContentSource: true)
    {
    }

    public WhisperModelManager(
        string modelDirectory,
        IModelContentSource contentSource)
        : this(
            modelDirectory,
            contentSource,
            WhisperModelCatalog.AvailableModels,
            ownsContentSource: false)
    {
    }

    internal WhisperModelManager(
        string modelDirectory,
        IModelContentSource contentSource,
        IEnumerable<WhisperModelDefinition> models)
        : this(
            modelDirectory,
            contentSource,
            models,
            ownsContentSource: false)
    {
    }

    private WhisperModelManager(
        string modelDirectory,
        IModelContentSource contentSource,
        IEnumerable<WhisperModelDefinition> models,
        bool ownsContentSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _contentSource = contentSource ??
            throw new ArgumentNullException(nameof(contentSource));
        ArgumentNullException.ThrowIfNull(models);
        _models = models.ToDictionary(
            model => model.Name,
            StringComparer.OrdinalIgnoreCase);
        _ownsContentSource = ownsContentSource;
    }

    public string ModelDirectory => _modelDirectory;

    public string GetModelPath(string modelName) =>
        Path.Combine(
            _modelDirectory,
            GetModel(modelName).FileName);

    public async Task<bool> IsModelAvailableAsync(
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WhisperModelDefinition model = GetModel(modelName);
        string path = Path.Combine(_modelDirectory, model.FileName);
        return await VerifyFileAsync(path, model, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WhisperModelLocation> EnsureModelAsync(
        string modelName,
        IProgress<ModelDownloadProgressEventArgs>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WhisperModelDefinition model = GetModel(modelName);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(_modelDirectory);
            string destination = Path.Combine(_modelDirectory, model.FileName);
            Report(progress, model, ModelDownloadStage.Checking, 0);

            if (await VerifyFileAsync(
                    destination,
                    model,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                Report(
                    progress,
                    model,
                    ModelDownloadStage.Ready,
                    model.Length);
                return new(model, destination, ReusedExistingFile: true);
            }

            string stagingPath = destination +
                $".partial-{Guid.NewGuid():N}";

            try
            {
                await using (var staging = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var byteProgress = new InlineProgress<long>(bytes =>
                        Report(
                            progress,
                            model,
                            ModelDownloadStage.Downloading,
                            bytes));
                    await _contentSource
                        .CopyToAsync(
                            model.DownloadUri,
                            staging,
                            byteProgress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await staging
                        .FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                Report(
                    progress,
                    model,
                    ModelDownloadStage.Verifying,
                    model.Length);
                if (!await VerifyFileAsync(
                        stagingPath,
                        model,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new ModelIntegrityException(
                        $"The downloaded {model.Name} model failed size or SHA-256 verification.");
                }

                File.Move(stagingPath, destination, overwrite: true);
                Report(
                    progress,
                    model,
                    ModelDownloadStage.Ready,
                    model.Length);
                return new(model, destination, ReusedExistingFile: false);
            }
            finally
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
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
        if (_ownsContentSource && _contentSource is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private WhisperModelDefinition GetModel(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        return _models.TryGetValue(modelName, out WhisperModelDefinition? model)
            ? model
            : throw new ArgumentOutOfRangeException(
                nameof(modelName),
                modelName,
                "The requested Whisper model is not supported.");
    }

    private static async Task<bool> VerifyFileAsync(
        string path,
        WhisperModelDefinition model,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != model.Length)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256
            .HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexString(hash),
            model.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void Report(
        IProgress<ModelDownloadProgressEventArgs>? progress,
        WhisperModelDefinition model,
        ModelDownloadStage stage,
        long bytes)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress.Report(new(
                model.Name,
                stage,
                Math.Clamp(bytes, 0, model.Length),
                model.Length));
        }
        catch (Exception)
        {
            // Progress observers cannot invalidate a verified model operation.
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
