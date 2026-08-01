using System.Buffers.Binary;
using System.Text;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using Whisper.net;

namespace DictaClone.Speech;

public sealed class WhisperTranscriptionEngine :
    ITranscriptionEngine,
    IModelProgressSource,
    IAsyncDisposable
{
    private readonly WhisperModelManager _modelManager;
    private readonly bool _ownsModelManager;
    private readonly SemaphoreSlim _worker = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModel;
    private bool _disposed;
    private int _disposeStarted;

    public WhisperTranscriptionEngine(
        WhisperModelManager modelManager,
        bool ownsModelManager = false)
    {
        _modelManager = modelManager ??
            throw new ArgumentNullException(nameof(modelManager));
        _ownsModelManager = ownsModelManager;
    }

    public event EventHandler<ModelDownloadProgressEventArgs>?
        ModelProgressChanged;

    public async Task<bool> WarmUpIfAvailableAsync(
        TranscriptionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!await _modelManager
                .IsModelAvailableAsync(settings.Model, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string path = _modelManager.GetModelPath(settings.Model);
            LoadFactory(settings.Model, path);
            return true;
        }
        finally
        {
            _worker.Release();
        }
    }

    public async Task<string> TranscribeAsync(
        CapturedAudio audio,
        TranscriptionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateAudio(audio);

        if (audio.Pcm16.IsEmpty || audio.IsSilent)
        {
            return string.Empty;
        }

        await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.Equals(
                    _loadedModel,
                    settings.Model,
                    StringComparison.OrdinalIgnoreCase))
            {
                var progress =
                    new InlineProgress<ModelDownloadProgressEventArgs>(
                        PublishModelProgress);
                WhisperModelLocation location = await _modelManager
                    .EnsureModelAsync(
                        settings.Model,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                LoadFactory(settings.Model, location.Path);
            }

            WhisperProcessorBuilder builder = _factory!
                .CreateBuilder()
                .WithThreads(ResolveThreadCount(settings.WorkerThreads));
            builder = string.Equals(
                settings.Language,
                "auto",
                StringComparison.OrdinalIgnoreCase)
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(settings.Language);

            if (!string.IsNullOrWhiteSpace(settings.InitialPrompt))
            {
                builder = builder.WithPrompt(settings.InitialPrompt);
            }

            using WhisperProcessor processor = builder.Build();
            float[] samples = ConvertToFloatSamples(audio.Pcm16.Span);
            var transcript = new StringBuilder();

            await foreach (SegmentData segment in processor
                .ProcessAsync(samples, cancellationToken)
                .ConfigureAwait(false))
            {
                transcript.Append(segment.Text);
            }

            return transcript.ToString().Trim();
        }
        finally
        {
            _worker.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        await _worker.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _factory?.Dispose();
            _factory = null;
            _loadedModel = null;
            if (_ownsModelManager)
            {
                _modelManager.Dispose();
            }
        }
        finally
        {
            _worker.Release();
            _worker.Dispose();
        }
    }

    private void LoadFactory(string modelName, string modelPath)
    {
        if (string.Equals(
                _loadedModel,
                modelName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        WhisperFactory replacement = WhisperFactory.FromPath(modelPath);
        WhisperFactory? previous = _factory;
        _factory = replacement;
        _loadedModel = modelName;
        previous?.Dispose();
    }

    private void PublishModelProgress(ModelDownloadProgressEventArgs progress)
    {
        Delegate[] handlers =
            ModelProgressChanged?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((EventHandler<ModelDownloadProgressEventArgs>)handler)(
                    this,
                    progress);
            }
            catch (Exception)
            {
                // UI progress observers cannot stop model preparation.
            }
        }
    }

    private static int ResolveThreadCount(int configured) => configured == 0
        ? Math.Clamp(Environment.ProcessorCount / 2, 1, 12)
        : Math.Clamp(configured, 1, 64);

    private static void ValidateAudio(CapturedAudio audio)
    {
        if (audio.SampleRate != 16_000 || audio.ChannelCount != 1)
        {
            throw new ArgumentException(
                "Whisper input must be 16 kHz mono PCM16 audio.",
                nameof(audio));
        }

        if (audio.Pcm16.Length % sizeof(short) != 0)
        {
            throw new ArgumentException(
                "PCM16 input must contain complete 16-bit samples.",
                nameof(audio));
        }
    }

    private static float[] ConvertToFloatSamples(ReadOnlySpan<byte> pcm16)
    {
        var samples = new float[pcm16.Length / sizeof(short)];
        for (int index = 0; index < samples.Length; index++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16.Slice(index * sizeof(short), sizeof(short)));
            samples[index] = sample / 32768f;
        }

        return samples;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public interface IModelProgressSource
{
    event EventHandler<ModelDownloadProgressEventArgs>? ModelProgressChanged;
}
