using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;

namespace DictaClone.Infrastructure;

internal static class SettingsDocumentCodec
{
    private static readonly JsonSerializerOptions CompactOptions =
        CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions IndentedOptions =
        CreateOptions(writeIndented: true);

    public static byte[] Serialize(DictaCloneSettings settings) =>
        JsonSerializer.SerializeToUtf8Bytes(settings, IndentedOptions);

    public static DecodedSettings Deserialize(ReadOnlyMemory<byte> document)
    {
        int schemaVersion = ReadSchemaVersion(document);
        DictaCloneSettings settings = schemaVersion switch
        {
            1 => MigrateSchema1(document),
            2 => MigrateSchema2(document),
            DictaCloneSettings.CurrentSchemaVersion =>
                JsonSerializer.Deserialize<DictaCloneSettings>(
                    document.Span,
                    CompactOptions) ?? throw new InvalidDataException(
                    "The settings document is empty."),
            _ => throw new InvalidDataException(
                $"Settings schema {schemaVersion} is not supported."),
        };

        var errors = SettingsValidator.Validate(settings);
        if (!errors.IsEmpty)
        {
            throw new InvalidDataException(
                $"Settings validation failed at {errors[0].Path}: " +
                errors[0].Message);
        }

        return new(settings, WasMigrated: schemaVersion !=
            DictaCloneSettings.CurrentSchemaVersion);
    }

    private static int ReadSchemaVersion(ReadOnlyMemory<byte> document)
    {
        using JsonDocument parsed = JsonDocument.Parse(document);
        JsonElement root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The settings document must contain an object.");
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    "schemaVersion",
                    StringComparison.OrdinalIgnoreCase) &&
                property.Value.TryGetInt32(out int schemaVersion))
            {
                return schemaVersion;
            }
        }

        throw new InvalidDataException(
            "The settings schema version is missing.");
    }

    private static DictaCloneSettings MigrateSchema1(
        ReadOnlyMemory<byte> document)
    {
        Schema1Settings old =
            JsonSerializer.Deserialize<Schema1Settings>(
                document.Span,
                CompactOptions) ?? throw new InvalidDataException(
                "The schema v1 settings document is empty.");

        return new(
            DictaCloneSettings.CurrentSchemaVersion,
            old.Audio,
            old.Transcription,
            new TextProcessingSettings(
                old.Text.Vocabulary,
                old.Text.Expansions,
                old.Text.EnableCorrections,
                WorkDomainPreset.General),
            old.Insertion,
            MigrateHotkeys(old.Hotkeys),
            new ApplicationPreferences(
                FirstRunCompleted: false,
                StartWithWindows: false,
                HistoryEnabled: false,
                HistoryLimit: 100),
            DictaCloneSettings.Default.SmartEdit);
    }

    private static DictaCloneSettings MigrateSchema2(
        ReadOnlyMemory<byte> document)
    {
        Schema2Settings old =
            JsonSerializer.Deserialize<Schema2Settings>(
                document.Span,
                CompactOptions) ?? throw new InvalidDataException(
                "The schema v2 settings document is empty.");

        return new(
            DictaCloneSettings.CurrentSchemaVersion,
            old.Audio,
            old.Transcription,
            old.Text,
            old.Insertion,
            MigrateHotkeys(old.Hotkeys),
            old.Preferences,
            DictaCloneSettings.Default.SmartEdit);
    }

    private static ImmutableArray<HotkeyBinding> MigrateHotkeys(
        ImmutableArray<HotkeyBinding> bindings)
    {
        HotkeyBinding safeSmartEditDefault = HotkeyDefaults.Bindings.Single(
            binding => binding.Action == HotkeyAction.SmartEdit);
        return bindings.Select(binding =>
            binding.Action == HotkeyAction.SmartEdit
                ? safeSmartEditDefault
                : binding).ToImmutableArray();
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }

    internal sealed record DecodedSettings(
        DictaCloneSettings Settings,
        bool WasMigrated);

    private sealed record Schema1Settings(
        int SchemaVersion,
        AudioSettings Audio,
        TranscriptionSettings Transcription,
        Schema1TextSettings Text,
        InsertionSettings Insertion,
        ImmutableArray<HotkeyBinding> Hotkeys);

    private sealed record Schema1TextSettings(
        ImmutableArray<VocabularyEntry> Vocabulary,
        ImmutableArray<TextExpansion> Expansions,
        bool EnableCorrections);

    private sealed record Schema2Settings(
        int SchemaVersion,
        AudioSettings Audio,
        TranscriptionSettings Transcription,
        TextProcessingSettings Text,
        InsertionSettings Insertion,
        ImmutableArray<HotkeyBinding> Hotkeys,
        ApplicationPreferences Preferences);
}
