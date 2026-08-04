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

        if (settings.Audio is null)
        {
            errors.Add(new("Audio", "required", "Audio settings are required."));
        }
        else
        {
            ValidateAudio(settings.Audio, errors);
        }

        if (settings.Transcription is null)
        {
            errors.Add(new(
                "Transcription",
                "required",
                "Transcription settings are required."));
        }
        else
        {
            ValidateTranscription(settings.Transcription, errors);
        }

        if (settings.Text is null)
        {
            errors.Add(new("Text", "required", "Text settings are required."));
        }
        else
        {
            ValidateText(settings.Text, errors);
        }

        if (settings.Insertion is null)
        {
            errors.Add(new(
                "Insertion",
                "required",
                "Insertion settings are required."));
        }
        else
        {
            ValidateInsertion(settings.Insertion, errors);
        }

        ValidateHotkeys(settings.Hotkeys, errors);

        if (settings.Preferences is null)
        {
            errors.Add(new(
                "Preferences",
                "required",
                "Application preferences are required."));
        }
        else
        {
            ValidatePreferences(settings.Preferences, errors);
        }

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

        if (settings.InitialPrompt?.Length > 2048)
        {
            errors.Add(new(
                "Transcription.InitialPrompt",
                "length",
                "The initial prompt cannot exceed 2,048 characters."));
        }
    }

    private static void ValidateText(
        TextProcessingSettings settings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (!Enum.IsDefined(settings.WorkDomain))
        {
            errors.Add(new(
                "Text.WorkDomain",
                "invalid",
                "Work domain must be a recognized preset."));
        }

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
        if (!Enum.IsDefined(settings.Mode))
        {
            errors.Add(new(
                "Insertion.Mode",
                "invalid",
                "Insertion mode must be recognized."));
        }

        if (settings.CharacterDelay < TimeSpan.Zero ||
            settings.CharacterDelay > TimeSpan.FromMilliseconds(100))
        {
            errors.Add(new(
                "Insertion.CharacterDelay",
                "range",
                "Character delay must be between 0 and 100 milliseconds."));
        }
    }

    private static void ValidatePreferences(
        ApplicationPreferences settings,
        ImmutableArray<SettingsValidationError>.Builder errors)
    {
        if (settings.HistoryLimit is < 1 or > 500)
        {
            errors.Add(new(
                "Preferences.HistoryLimit",
                "range",
                "History limit must be between 1 and 500 entries."));
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

        var assignedActions = new HashSet<HotkeyAction>();
        for (int index = 0; index < bindings.Length; index++)
        {
            HotkeyBinding binding = bindings[index];
            if (!bindings[index].Chord.IsValid)
            {
                errors.Add(new(
                    $"Hotkeys[{index}].Chord",
                    "invalid",
                    "A chord must contain a modifier or primary key."));
            }

            if (!Enum.IsDefined(binding.Action) ||
                !Enum.IsDefined(binding.Activation))
            {
                errors.Add(new(
                    $"Hotkeys[{index}]",
                    "invalid",
                    "Hotkey action and activation must be recognized values."));
            }
            else if (!assignedActions.Add(binding.Action))
            {
                errors.Add(new(
                    $"Hotkeys[{index}].Action",
                    "duplicate",
                    "Each action can have only one binding."));
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
