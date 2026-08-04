using System.Collections.Immutable;
using DictaClone.Core.Hotkeys;

namespace DictaClone.Core.Settings;

public sealed record DictaCloneSettings(
    int SchemaVersion,
    AudioSettings Audio,
    TranscriptionSettings Transcription,
    TextProcessingSettings Text,
    InsertionSettings Insertion,
    ImmutableArray<HotkeyBinding> Hotkeys,
    ApplicationPreferences Preferences,
    SmartEditSettings SmartEdit)
{
    public const int CurrentSchemaVersion = 3;

    public static DictaCloneSettings Default { get; } = new(
        CurrentSchemaVersion,
        new AudioSettings(null, 0.012, TimeSpan.FromMinutes(2)),
        new TranscriptionSettings("base.en", "en", 0),
        new TextProcessingSettings(
            ImmutableArray<VocabularyEntry>.Empty,
            ImmutableArray<TextExpansion>.Empty,
            EnableCorrections: true,
            WorkDomainPreset.General),
        new InsertionSettings(TextInsertionMode.Paste, TimeSpan.FromMilliseconds(10)),
        HotkeyDefaults.Bindings,
        new ApplicationPreferences(
            FirstRunCompleted: false,
            StartWithWindows: false,
            HistoryEnabled: false,
            HistoryLimit: 100),
        new SmartEditSettings(
            Enabled: false,
            Provider: SmartEditProviderKind.OpenAIResponses,
            Endpoint: "https://api.openai.com/v1/responses",
            Model: "gpt-5.6-sol",
            RequestTimeout: TimeSpan.FromSeconds(30),
            MaximumRetries: 1,
            CustomInstructions: null));
}

public sealed record AudioSettings(
    string? DeviceId,
    double SilenceThreshold,
    TimeSpan MaximumDuration);

public sealed record TranscriptionSettings(
    string Model,
    string Language,
    int WorkerThreads,
    string? InitialPrompt = null);

public sealed record TextProcessingSettings(
    ImmutableArray<VocabularyEntry> Vocabulary,
    ImmutableArray<TextExpansion> Expansions,
    bool EnableCorrections,
    WorkDomainPreset WorkDomain = WorkDomainPreset.General);

public sealed record VocabularyEntry(string SpokenForm, string WrittenForm);

public sealed record TextExpansion(string Trigger, string Replacement);

public enum WorkDomainPreset
{
    General,
    SoftwareDevelopment,
    Business,
    Academic,
}

public enum TextInsertionMode
{
    Paste,
    DelayedTyping,
}

public sealed record InsertionSettings(
    TextInsertionMode Mode,
    TimeSpan CharacterDelay);

public sealed record ApplicationPreferences(
    bool FirstRunCompleted,
    bool StartWithWindows,
    bool HistoryEnabled,
    int HistoryLimit);

public sealed record SmartEditSettings(
    bool Enabled,
    SmartEditProviderKind Provider,
    string Endpoint,
    string Model,
    TimeSpan RequestTimeout,
    int MaximumRetries,
    string? CustomInstructions);

public enum SmartEditProviderKind
{
    OpenAIResponses,
}
