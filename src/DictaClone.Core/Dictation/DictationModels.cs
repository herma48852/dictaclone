namespace DictaClone.Core.Dictation;

public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    Cleaning,
    Inserting,
    Cancelled,
    Faulted,
}

public enum DictationMode
{
    Dictation,
    SmartEdit,
    Typing,
}

public enum DictationStartOutcome
{
    Started,
    IgnoredAlreadyActive,
    Cancelled,
    Failed,
}

public enum DictationOutcome
{
    Completed,
    NoSpeech,
    Cancelled,
    IgnoredNotRecording,
    Failed,
}

public enum DictationFailureStage
{
    ForegroundTarget,
    AudioCapture,
    Transcription,
    TextProcessing,
    TextInsertion,
}

public sealed record DictationFailure(DictationFailureStage Stage, string ErrorCode);

public sealed record DictationStartResult(
    DictationStartOutcome Outcome,
    DictationFailure? Failure = null);

public sealed record DictationResult(
    DictationOutcome Outcome,
    string? Text = null,
    DictationFailure? Failure = null);

public sealed record DictationStateChangedEvent(
    DictationState Previous,
    DictationState Current);

public sealed record CapturedAudio(
    ReadOnlyMemory<byte> Pcm16,
    int SampleRate,
    int ChannelCount,
    TimeSpan Duration,
    bool IsSilent);

public sealed record ForegroundTarget(
    string Id,
    string ProcessName,
    string WindowClass,
    bool IsElevated = false);
