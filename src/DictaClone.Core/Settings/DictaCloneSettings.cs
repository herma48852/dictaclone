using System.Collections.Immutable;
using DictaClone.Core.Hotkeys;

namespace DictaClone.Core.Settings;

public sealed record DictaCloneSettings(
    int SchemaVersion,
    AudioSettings Audio,
    TranscriptionSettings Transcription,
    TextProcessingSettings Text,
    InsertionSettings Insertion,
    ImmutableArray<HotkeyBinding> Hotkeys)
{
    public const int CurrentSchemaVersion = 1;

    public static DictaCloneSettings Default { get; } = new(
        CurrentSchemaVersion,
        new AudioSettings(null, 0.012, TimeSpan.FromMinutes(2)),
        new TranscriptionSettings("base.en", "en", 0),
        new TextProcessingSettings(
            ImmutableArray<VocabularyEntry>.Empty,
            ImmutableArray<TextExpansion>.Empty,
            EnableCorrections: true),
        new InsertionSettings(TextInsertionMode.Paste, TimeSpan.FromMilliseconds(10)),
        HotkeyDefaults.Bindings);
}

public sealed record AudioSettings(
    string? DeviceId,
    double SilenceThreshold,
    TimeSpan MaximumDuration);

public sealed record TranscriptionSettings(
    string Model,
    string Language,
    int WorkerThreads);

public sealed record TextProcessingSettings(
    ImmutableArray<VocabularyEntry> Vocabulary,
    ImmutableArray<TextExpansion> Expansions,
    bool EnableCorrections);

public sealed record VocabularyEntry(string SpokenForm, string WrittenForm);

public sealed record TextExpansion(string Trigger, string Replacement);

public enum TextInsertionMode
{
    Paste,
    DelayedTyping,
}

public sealed record InsertionSettings(
    TextInsertionMode Mode,
    TimeSpan CharacterDelay);
