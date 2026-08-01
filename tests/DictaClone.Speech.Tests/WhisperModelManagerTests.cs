using System.Security.Cryptography;
using System.Text;
using DictaClone.Speech;

namespace DictaClone.Speech.Tests;

public sealed class WhisperModelManagerTests : IDisposable
{
    private static readonly byte[] ValidModelBytes =
        Encoding.UTF8.GetBytes("small verified model fixture");

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"DictaClone-SpeechTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ValidInstalledModel_IsReusedWithoutNetworkAccess()
    {
        WhisperModelDefinition definition = CreateDefinition();
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(_temporaryDirectory, definition.FileName),
            ValidModelBytes);
        var source = new FakeContentSource(ValidModelBytes)
        {
            FailIfCalled = true,
        };
        using var manager = CreateManager(source, definition);

        WhisperModelLocation location = await manager.EnsureModelAsync(
            definition.Name,
            cancellationToken: CancellationToken.None);

        Assert.True(location.ReusedExistingFile);
        Assert.Equal(0, source.CallCount);
        Assert.True(await manager.IsModelAvailableAsync(definition.Name));
    }

    [Fact]
    public async Task MissingModel_DownloadsVerifiesAndReportsProgress()
    {
        WhisperModelDefinition definition = CreateDefinition();
        var source = new FakeContentSource(ValidModelBytes);
        using var manager = CreateManager(source, definition);
        var updates = new List<ModelDownloadProgressEventArgs>();
        var progress = new InlineProgress<ModelDownloadProgressEventArgs>(
            updates.Add);

        WhisperModelLocation location = await manager.EnsureModelAsync(
            definition.Name,
            progress,
            CancellationToken.None);

        Assert.False(location.ReusedExistingFile);
        Assert.Equal(1, source.CallCount);
        Assert.Equal(ValidModelBytes, await File.ReadAllBytesAsync(location.Path));
        Assert.Contains(updates,
            update => update.Stage == ModelDownloadStage.Checking);
        Assert.Contains(updates,
            update => update.Stage == ModelDownloadStage.Downloading);
        Assert.Contains(updates,
            update => update.Stage == ModelDownloadStage.Verifying);
        ModelDownloadProgressEventArgs ready = Assert.Single(
            updates,
            update => update.Stage == ModelDownloadStage.Ready);
        Assert.Equal(1, ready.Fraction);
    }

    [Fact]
    public async Task CorruptInstalledModel_IsAtomicallyReplaced()
    {
        WhisperModelDefinition definition = CreateDefinition();
        Directory.CreateDirectory(_temporaryDirectory);
        string destination = Path.Combine(
            _temporaryDirectory,
            definition.FileName);
        await File.WriteAllBytesAsync(
            destination,
            Enumerable.Repeat((byte)0xFF, ValidModelBytes.Length).ToArray());
        var source = new FakeContentSource(ValidModelBytes);
        using var manager = CreateManager(source, definition);

        WhisperModelLocation location = await manager.EnsureModelAsync(
            definition.Name,
            cancellationToken: CancellationToken.None);

        Assert.False(location.ReusedExistingFile);
        Assert.Equal(ValidModelBytes, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(
            _temporaryDirectory,
            "*.partial-*"));
    }

    [Fact]
    public async Task CorruptDownload_IsRejectedAndPartialFileIsRemoved()
    {
        WhisperModelDefinition definition = CreateDefinition();
        byte[] corrupt = Enumerable
            .Repeat((byte)0xCC, ValidModelBytes.Length)
            .ToArray();
        using var manager = CreateManager(
            new FakeContentSource(corrupt),
            definition);

        await Assert.ThrowsAsync<ModelIntegrityException>(() =>
            manager.EnsureModelAsync(
                definition.Name,
                cancellationToken: CancellationToken.None));

        Assert.False(File.Exists(manager.GetModelPath(definition.Name)));
        Assert.Empty(Directory.GetFiles(
            _temporaryDirectory,
            "*.partial-*"));
    }

    [Fact]
    public async Task CancelledDownload_RemovesPartialFile()
    {
        WhisperModelDefinition definition = CreateDefinition();
        var source = new BlockingContentSource();
        using var manager = CreateManager(source, definition);
        using var cancellation = new CancellationTokenSource();
        Task<WhisperModelLocation> ensure = manager.EnsureModelAsync(
            definition.Name,
            cancellationToken: cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ensure);
        Assert.False(File.Exists(manager.GetModelPath(definition.Name)));
        Assert.Empty(Directory.GetFiles(
            _temporaryDirectory,
            "*.partial-*"));
    }

    [Fact]
    public async Task ConcurrentEnsure_DownloadsOnlyOnce()
    {
        WhisperModelDefinition definition = CreateDefinition();
        var source = new FakeContentSource(
            ValidModelBytes,
            TimeSpan.FromMilliseconds(25));
        using var manager = CreateManager(source, definition);

        WhisperModelLocation[] locations = await Task.WhenAll(
            manager.EnsureModelAsync(definition.Name),
            manager.EnsureModelAsync(definition.Name));

        Assert.Equal(1, source.CallCount);
        Assert.Contains(locations, location => !location.ReusedExistingFile);
        Assert.Contains(locations, location => location.ReusedExistingFile);
    }

    [Fact]
    public async Task UnknownModelAndDisposedManager_AreRejected()
    {
        WhisperModelDefinition definition = CreateDefinition();
        var manager = CreateManager(
            new FakeContentSource(ValidModelBytes),
            definition);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => manager.GetModelPath("unknown"));
        manager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.IsModelAvailableAsync(definition.Name));
        manager.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private WhisperModelManager CreateManager(
        IModelContentSource source,
        WhisperModelDefinition definition) =>
        new(_temporaryDirectory, source, [definition]);

    private static WhisperModelDefinition CreateDefinition() =>
        new(
            "test-model",
            "test-model.bin",
            ValidModelBytes.Length,
            Convert.ToHexString(SHA256.HashData(ValidModelBytes)),
            new("https://example.invalid/test-model.bin"));

    private sealed class FakeContentSource(
        byte[] content,
        TimeSpan delay = default) : IModelContentSource
    {
        public bool FailIfCalled { get; init; }

        public int CallCount { get; private set; }

        public async Task CopyToAsync(
            Uri source,
            Stream destination,
            IProgress<long>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (FailIfCalled)
            {
                throw new InvalidOperationException("Network access was not expected.");
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            await destination.WriteAsync(content, cancellationToken);
            progress?.Report(content.Length);
        }
    }

    private sealed class BlockingContentSource : IModelContentSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CopyToAsync(
            Uri source,
            Stream destination,
            IProgress<long>? progress,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(
                ValidModelBytes.AsMemory(0, 3),
                cancellationToken);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
