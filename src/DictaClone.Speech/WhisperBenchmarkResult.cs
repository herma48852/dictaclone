namespace DictaClone.Speech;

public sealed record WhisperBenchmarkResult(
    string ModelName,
    string ModelPath,
    long ModelSizeBytes,
    TimeSpan AudioDuration,
    TimeSpan ModelLoadDuration,
    TimeSpan InferenceDuration,
    double RealTimeFactor,
    long PeakWorkingSetBytes,
    int ThreadCount,
    string Transcript,
    TranscriptScore TranscriptScore);
