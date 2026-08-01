using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
using DictaClone.Speech;

namespace DictaClone.Speech.Tests;

public sealed class WhisperTranscriptionEngineTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"DictaClone-EngineTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SilentOrEmptyAudio_DoesNotRequireAModel()
    {
        using var manager = new WhisperModelManager(
            _temporaryDirectory,
            new FailingContentSource());
        await using var engine = new WhisperTranscriptionEngine(manager);
        var audio = new CapturedAudio(
            ReadOnlyMemory<byte>.Empty,
            16_000,
            1,
            TimeSpan.Zero,
            IsSilent: true);

        string transcript = await engine.TranscribeAsync(
            audio,
            DictaCloneSettings.Default.Transcription,
            CancellationToken.None);

        Assert.Equal(string.Empty, transcript);
    }

    [Theory]
    [InlineData(48_000, 1, 2)]
    [InlineData(16_000, 2, 2)]
    [InlineData(16_000, 1, 1)]
    public async Task InvalidAudioFormat_IsRejected(
        int sampleRate,
        int channels,
        int byteCount)
    {
        using var manager = new WhisperModelManager(
            _temporaryDirectory,
            new FailingContentSource());
        await using var engine = new WhisperTranscriptionEngine(manager);
        var audio = new CapturedAudio(
            new byte[byteCount],
            sampleRate,
            channels,
            TimeSpan.FromMilliseconds(10),
            IsSilent: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            engine.TranscribeAsync(
                audio,
                DictaCloneSettings.Default.Transcription,
                CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class FailingContentSource : IModelContentSource
    {
        public Task CopyToAsync(
            Uri source,
            Stream destination,
            IProgress<long>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No download expected.");
    }
}
