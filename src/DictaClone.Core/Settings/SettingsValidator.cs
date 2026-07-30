using System.Collections.Immutable;
using DictaClone.Core.Hotkeys;

namespace DictaClone.Core.Settings;

public static class SettingsValidator
{
    public static ImmutableArray<SettingsValidationError> Validate(
        DictaCloneSettings? settings)
    {
        if (settings is null)
        {
            return [new("$", "required", "Settings are required.")];
        }

        var errors = ImmutableArray.CreateBuilder<SettingsValidationError>();

        if (settings.SchemaVersion != DictaCloneSettings.CurrentSchemaVersion)
        {
            errors.Add(new(
                nameof(settings.SchemaVersion),
                "unsupported",
                $"Schema version must be {DictaCloneSettings.CurrentSchemaVersion}."));
        }

        ValidateAudio(settings.Audio, errors);
        ValidateTranscription(settings.Transcription, errors);
        ValidateText(settings.Text, errors);
        ValidateInsertion(settings.Insertion, errors);
        ValidateHotkeys(settings.Hotkeys, errors);

        return errors.ToImmutable();
    }

    private static void ValidateAudio(
        AudioSettings settings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (!double.IsFinite(settings.SilenceThreshold) ||
            settings.SilenceThreshold is < 0 or > 1)
        {
            errors.Add(new(
                "Audio.SilenceThreshold",
                "range",
                "Silence threshold must be between 0 and 1."));
        }

        if (settings.MaximumDuration < TimeSpan.FromSeconds(1) ||
            settings.MaximumDuration > TimeSpan.FromMinutes(10))
        {
            errors.Add(new(
                "Audio.MaximumDuration",
                "range",
                "Maximum duration must be between 1 second and 10 minutes."));
        }
    }

    private static void ValidateTranscription(
        TranscriptionSettings settings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            errors.Add(new("Transcription.Model", "required", "A model is required."));
        }

        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            errors.Add(new(
                "Transcription.Language",
                "required",
                "A language is required."));
        }

        if (settings.WorkerThreads is < 0 or > 64)
        {
            errors.Add(new(
                "Transcription.WorkerThreads",
                "range",
                "Worker threads must be automatic (0) or between 1 and 64."));
        }
    }

    private static void ValidateText(
        TextProcessingSettings settings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (settings.Vocabulary.IsDefault)
        {
            errors.Add(new(
                "Text.Vocabulary",
                "required",
                "Vocabulary must be an initialized collection."));
        }
        else
        {
            ValidateUniquePairs(
                settings.Vocabulary.Select(
                    entry => (entry.SpokenForm, entry.WrittenForm)),
                "Text.Vocabulary",
                errors);
        }

        if (settings.Expansions.IsDefault)
        {
            errors.Add(new(
                "Text.Expansions",
                "required",
                "Expansions must be an initialized collection."));
            return;
        }

        ValidateUniquePairs(
            settings.Expansions.Select(
                entry => (entry.Trigger, entry.Replacement)),
            "Text.Expansions",
            errors);
    }

    private static void ValidateUniquePairs(
        IEnumerable<(string Trigger, string Replacement)> pairs,
        string path,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        var triggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        foreach ((string trigger, string replacement) in pairs)
        {
            if (string.IsNullOrWhiteSpace(trigger) ||
                string.IsNullOrWhiteSpace(replacement))
            {
                errors.Add(new(
                    $"{path}[{index}]",
                    "required",
                    "Trigger and replacement must not be blank."));
            }
            else if (!triggers.Add(trigger.Trim()))
            {
                errors.Add(new(
                    $"{path}[{index}].Trigger",
                    "duplicate",
                    "Triggers must be unique, ignoring case."));
            }

            index++;
        }
    }

    private static void ValidateInsertion(
        InsertionSettings settings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (settings.CharacterDelay < TimeSpan.Zero ||
            settings.CharacterDelay > TimeSpan.FromMilliseconds(500))
        {
            errors.Add(new(
                "Insertion.CharacterDelay",
                "range",
                "Character delay must be between 0 and 500 milliseconds."));
        }
    }

    private static void ValidateHotkeys(
        ImmutableArray<HotkeyBinding> bindings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (bindings.IsDefaultOrEmpty)
        {
            errors.Add(new("Hotkeys", "required", "At least one hotkey is required."));
            return;
        }

        for (int index = 0; index < bindings.Length; index++)
        {
            if (!bindings[index].Chord.IsValid)
            {
                errors.Add(new(
                    $"Hotkeys[{index}].Chord",
                    "invalid",
                    "A chord must contain a modifier or primary key."));
            }
        }

        foreach (HotkeyConflict conflict in HotkeyConflictDetector.Find(bindings))
        {
            errors.Add(new(
                "Hotkeys",
                "conflict",
                $"{conflict.First} and {conflict.Second} use {conflict.Chord}."));
        }
    }
}

public sealed record SettingsValidationError(
    string Path,
    string Code,
    string Message);
