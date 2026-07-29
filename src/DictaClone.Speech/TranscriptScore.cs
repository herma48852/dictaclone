namespace DictaClone.Speech;

public sealed record TranscriptScore(
    int ReferenceWordCount,
    int EditDistance,
    double WordErrorRate);
