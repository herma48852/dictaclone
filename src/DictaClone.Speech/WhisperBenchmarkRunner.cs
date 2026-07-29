using System.Diagnostics;
using System.Text;
using Whisper.net;

namespace DictaClone.Speech;

public sealed class WhisperBenchmarkRunner
{
    public static async Task<WhisperBenchmarkResult> RunAsync(
        string modelName,
        string modelPath,
        string wavePath,
        TimeSpan audioDuration,
        string expectedTranscript,
        int threadCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(wavePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTranscript);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The Whisper model was not found.", modelPath);
        }

        if (!File.Exists(wavePath))
        {
            throw new FileNotFoundException("The benchmark WAV fixture was not found.", wavePath);
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(audioDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var samplerCancellation = new CancellationTokenSource();
        Task<long> peakWorkingSetTask = SamplePeakWorkingSetAsync(samplerCancellation.Token);

        var loadStopwatch = Stopwatch.StartNew();
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory
            .CreateBuilder()
            .WithLanguage("en")
            .WithThreads(threadCount)
            .Build();
        loadStopwatch.Stop();

        var transcript = new StringBuilder();
        var inferenceStopwatch = Stopwatch.StartNew();

        await using (var waveStream = File.OpenRead(wavePath))
        {
            await foreach (SegmentData segment in processor
                .ProcessAsync(waveStream, cancellationToken)
                .ConfigureAwait(false))
            {
                transcript.Append(segment.Text);
            }
        }

        inferenceStopwatch.Stop();
        await samplerCancellation.CancelAsync().ConfigureAwait(false);
        long peakWorkingSetBytes = await peakWorkingSetTask.ConfigureAwait(false);

        string transcriptText = transcript.ToString().Trim();
        TranscriptScore score = TranscriptScorer.Score(expectedTranscript, transcriptText);

        return new WhisperBenchmarkResult(
            modelName,
            Path.GetFullPath(modelPath),
            new FileInfo(modelPath).Length,
            audioDuration,
            loadStopwatch.Elapsed,
            inferenceStopwatch.Elapsed,
            inferenceStopwatch.Elapsed.TotalSeconds / audioDuration.TotalSeconds,
            peakWorkingSetBytes,
            threadCount,
            transcriptText,
            score);
    }

    private static async Task<long> SamplePeakWorkingSetAsync(
        CancellationToken cancellationToken)
    {
        using Process process = Process.GetCurrentProcess();
        long peakWorkingSet = process.WorkingSet64;

        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken)
                    .ConfigureAwait(false);
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            process.Refresh();
            return Math.Max(peakWorkingSet, process.WorkingSet64);
        }
    }
}
