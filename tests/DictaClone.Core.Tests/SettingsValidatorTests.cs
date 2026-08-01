using System.Collections.Immutable;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;

namespace DictaClone.Core.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Defaults_AreValidAndLocalFirst()
    {
        DictaCloneSettings settings = DictaCloneSettings.Default;

        Assert.Empty(SettingsValidator.Validate(settings));
        Assert.Equal("base.en", settings.Transcription.Model);
        Assert.Equal(0, settings.Transcription.WorkerThreads);
        Assert.Equal(TextInsertionMode.Paste, settings.Insertion.Mode);
        Assert.True(settings.Text.EnableCorrections);
        Assert.Null(settings.Audio.DeviceId);
    }

    [Fact]
    public void NullSettings_AreRejected()
    {
        SettingsValidationError error = Assert.Single(
            SettingsValidator.Validate(null));

        Assert.Equal("$", error.Path);
        Assert.Equal("required", error.Code);
    }

    [Fact]
    public void InvalidScalarValues_ReportEveryProblem()
    {
        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            SchemaVersion = 99,
            Audio = new AudioSettings(
                "device",
                double.NaN,
                TimeSpan.FromMilliseconds(500)),
            Transcription = new TranscriptionSettings("", " ", 65),
            Insertion = new InsertionSettings(
                TextInsertionMode.DelayedTyping,
                TimeSpan.FromMilliseconds(101)),
        };

        var errors = SettingsValidator.Validate(settings);

        Assert.Collection(
            errors,
            error => Assert.Equal("SchemaVersion", error.Path),
            error => Assert.Equal("Audio.SilenceThreshold", error.Path),
            error => Assert.Equal("Audio.MaximumDuration", error.Path),
            error => Assert.Equal("Transcription.Model", error.Path),
            error => Assert.Equal("Transcription.Language", error.Path),
            error => Assert.Equal("Transcription.WorkerThreads", error.Path),
            error => Assert.Equal("Insertion.CharacterDelay", error.Path));
        Assert.All(
            errors,
            error => Assert.True(
                error.Code is "range" or "required" or "unsupported"));
    }

    [Fact]
    public void TextPairs_RejectBlankAndDuplicateTriggers()
    {
        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            Text = new TextProcessingSettings(
                [
                    new VocabularyEntry("", "replacement"),
                    new VocabularyEntry("kubernetes", "Kubernetes"),
                    new VocabularyEntry("KUBERNETES", "K8s"),
                ],
                [
                    new TextExpansion("sign off", ""),
                    new TextExpansion("address", "first"),
                    new TextExpansion("ADDRESS", "second"),
                ],
                EnableCorrections: false),
        };

        var errors = SettingsValidator.Validate(settings);

        Assert.Equal(4, errors.Length);
        Assert.Equal(2, errors.Count(error => error.Code == "required"));
        Assert.Equal(2, errors.Count(error => error.Code == "duplicate"));
    }

    [Fact]
    public void Hotkeys_RejectEmptyInvalidAndConflictingBindings()
    {
        DictaCloneSettings empty = DictaCloneSettings.Default with
        {
            Hotkeys = ImmutableArray<HotkeyBinding>.Empty,
        };
        Assert.Contains(
            SettingsValidator.Validate(empty),
            error => error.Path == "Hotkeys" && error.Code == "required");

        var duplicateChord = new HotkeyChord(HotkeyModifiers.Control);
        DictaCloneSettings invalid = DictaCloneSettings.Default with
        {
            Hotkeys =
            [
                new(HotkeyAction.Dictation, default),
                new(HotkeyAction.SmartEdit, duplicateChord),
                new(HotkeyAction.TypingMode, duplicateChord),
            ],
        };

        var errors = SettingsValidator.Validate(invalid);

        Assert.Contains(
            errors,
            error => error.Path == "Hotkeys[0].Chord" && error.Code == "invalid");
        Assert.Contains(
            errors,
            error => error.Path == "Hotkeys" && error.Code == "conflict");
    }

    [Fact]
    public void Hotkeys_RejectDuplicateActionsAndUnknownEnums()
    {
        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            Hotkeys =
            [
                new(
                    HotkeyAction.Dictation,
                    new HotkeyChord(HotkeyModifiers.Control)),
                new(
                    HotkeyAction.Dictation,
                    new HotkeyChord(HotkeyModifiers.Alt)),
                new(
                    (HotkeyAction)999,
                    new HotkeyChord(HotkeyModifiers.Shift),
                    Activation: (HotkeyActivation)999),
            ],
        };

        var errors = SettingsValidator.Validate(settings);

        Assert.Contains(
            errors,
            error =>
                error.Path == "Hotkeys[1].Action" &&
                error.Code == "duplicate");
        Assert.Contains(
            errors,
            error =>
                error.Path == "Hotkeys[2]" &&
                error.Code == "invalid");
    }

    [Fact]
    public void TextCollections_MustBeInitialized()
    {
        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            Text = new TextProcessingSettings(
                default,
                default,
                EnableCorrections: true),
        };

        var errors = SettingsValidator.Validate(settings);

        Assert.Contains(
            errors,
            error => error.Path == "Text.Vocabulary" && error.Code == "required");
        Assert.Contains(
            errors,
            error => error.Path == "Text.Expansions" && error.Code == "required");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.PositiveInfinity)]
    public void SilenceThreshold_RejectsValuesOutsideUnitRange(double threshold)
    {
        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            Audio = DictaCloneSettings.Default.Audio with
            {
                SilenceThreshold = threshold,
            },
        };

        Assert.Contains(
            SettingsValidator.Validate(settings),
            error => error.Path == "Audio.SilenceThreshold");
    }

    [Fact]
    public void TranscriptionPrompt_HasABoundedLength()
    {
        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            Transcription = DictaCloneSettings.Default.Transcription with
            {
                InitialPrompt = new string('x', 2049),
            },
        };

        Assert.Contains(
            SettingsValidator.Validate(settings),
            error =>
                error.Path == "Transcription.InitialPrompt" &&
                error.Code == "length");
    }
}
