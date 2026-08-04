using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;
using DictaClone.Infrastructure;

namespace DictaClone.Infrastructure.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task MissingSettings_ReturnsPrivacySafeDefaults()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(directory.Paths);

        SettingsLoadResult loaded = await store.LoadAsync(
            CancellationToken.None);

        Assert.True(loaded.IsNew);
        Assert.False(loaded.WasMigrated);
        Assert.False(loaded.Settings.Preferences.HistoryEnabled);
        Assert.False(loaded.Settings.Preferences.StartWithWindows);
        Assert.Equal(DictaCloneSettings.Default, loaded.Settings);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsAtomicallyWithoutTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(directory.Paths);
        DictaCloneSettings expected = DictaCloneSettings.Default with
        {
            Preferences = DictaCloneSettings.Default.Preferences with
            {
                FirstRunCompleted = true,
                HistoryEnabled = true,
                HistoryLimit = 42,
            },
            Text = DictaCloneSettings.Default.Text with
            {
                WorkDomain = WorkDomainPreset.SoftwareDevelopment,
                Vocabulary = [new("dot net", ".NET")],
                Expansions = [new("signature", "Kind regards")],
            },
        };

        await store.SaveAsync(expected, CancellationToken.None);
        SettingsLoadResult loaded = await store.LoadAsync(
            CancellationToken.None);

        Assert.Equivalent(expected, loaded.Settings, strict: true);
        Assert.False(loaded.IsNew);
        Assert.Empty(Directory.GetFiles(directory.Root, "*.tmp"));
    }

    [Fact]
    public async Task Schema1_IsMigratedAndRewrittenAsSchema2()
    {
        using var directory = new TemporaryDirectory();
        var transfer = new SettingsTransferService();
        string seedPath = Path.Combine(directory.Root, "seed.json");
        DictaCloneSettings seed = DictaCloneSettings.Default with
        {
            Audio = DictaCloneSettings.Default.Audio with
            {
                SilenceThreshold = 0.025,
            },
            Text = DictaCloneSettings.Default.Text with
            {
                Vocabulary = [new("jay son", "JSON")],
            },
        };
        await transfer.ExportAsync(seedPath, seed, CancellationToken.None);
        JsonObject document = JsonNode.Parse(
            await File.ReadAllTextAsync(seedPath))!.AsObject();
        document["schemaVersion"] = 1;
        _ = document.Remove("preferences");
        _ = document["text"]!.AsObject().Remove("workDomain");
        await File.WriteAllTextAsync(
            directory.Paths.SettingsFile,
            document.ToJsonString());
        using var store = new JsonSettingsStore(directory.Paths);

        SettingsLoadResult loaded = await store.LoadAsync(
            CancellationToken.None);

        Assert.True(loaded.WasMigrated);
        Assert.Equal(0.025, loaded.Settings.Audio.SilenceThreshold);
        Assert.Equal(
            seed.Text.Vocabulary.ToArray(),
            loaded.Settings.Text.Vocabulary.ToArray());
        Assert.Equal(
            WorkDomainPreset.General,
            loaded.Settings.Text.WorkDomain);
        Assert.False(loaded.Settings.Preferences.HistoryEnabled);
        string rewritten = await File.ReadAllTextAsync(
            directory.Paths.SettingsFile);
        Assert.Contains("\"schemaVersion\": 2", rewritten);
    }

    [Fact]
    public async Task CorruptSettings_AreQuarantinedAndDefaultsRecover()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Root);
        await File.WriteAllTextAsync(
            directory.Paths.SettingsFile,
            "{ this is not json");
        using var store = new JsonSettingsStore(directory.Paths);

        SettingsLoadResult loaded = await store.LoadAsync(
            CancellationToken.None);

        Assert.Equal(DictaCloneSettings.Default, loaded.Settings);
        Assert.NotNull(loaded.QuarantinedFilePath);
        Assert.True(File.Exists(loaded.QuarantinedFilePath));
        Assert.False(File.Exists(directory.Paths.SettingsFile));
        Assert.StartsWith(
            "settings.corrupt-",
            Path.GetFileName(loaded.QuarantinedFilePath));
    }

    [Fact]
    public async Task InaccessibleSettings_AreNotMisclassifiedOrQuarantined()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(directory.Paths);
        await store.SaveAsync(
            DictaCloneSettings.Default,
            CancellationToken.None);
        await using var exclusiveLock = new FileStream(
            directory.Paths.SettingsFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        await Assert.ThrowsAsync<IOException>(() =>
            store.LoadAsync(CancellationToken.None));

        Assert.True(File.Exists(directory.Paths.SettingsFile));
        Assert.Empty(Directory.GetFiles(directory.Root, "settings.corrupt-*"));
    }

    [Fact]
    public async Task InvalidOrCancelledSave_DoesNotReplaceValidSettings()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(directory.Paths);
        await store.SaveAsync(
            DictaCloneSettings.Default,
            CancellationToken.None);
        byte[] original = await File.ReadAllBytesAsync(
            directory.Paths.SettingsFile);
        DictaCloneSettings invalid = DictaCloneSettings.Default with
        {
            Preferences = DictaCloneSettings.Default.Preferences with
            {
                HistoryLimit = 0,
            },
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(invalid, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(
                DictaCloneSettings.Default,
                new CancellationToken(canceled: true)));

        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(directory.Paths.SettingsFile));
    }

    [Fact]
    public async Task ConcurrentSaves_AlwaysLeaveACompleteValidDocument()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonSettingsStore(directory.Paths);
        DictaCloneSettings first = DictaCloneSettings.Default with
        {
            Transcription = DictaCloneSettings.Default.Transcription with
            {
                Language = "auto",
            },
        };
        DictaCloneSettings second = DictaCloneSettings.Default with
        {
            Preferences = DictaCloneSettings.Default.Preferences with
            {
                HistoryLimit = 25,
            },
        };

        await Task.WhenAll(
            store.SaveAsync(first, CancellationToken.None),
            store.SaveAsync(second, CancellationToken.None));
        SettingsLoadResult loaded = await store.LoadAsync(
            CancellationToken.None);

        bool isFirst =
            loaded.Settings.Transcription.Language == "auto" &&
            loaded.Settings.Preferences.HistoryLimit == 100;
        bool isSecond =
            loaded.Settings.Transcription.Language == "en" &&
            loaded.Settings.Preferences.HistoryLimit == 25;
        Assert.True(isFirst || isSecond);
    }

    [Fact]
    public async Task ImportExport_RoundTripsAndRejectsUnknownSecretFields()
    {
        using var directory = new TemporaryDirectory();
        var transfer = new SettingsTransferService();
        string exportPath = Path.Combine(directory.Root, "export.json");
        await transfer.ExportAsync(
            exportPath,
            DictaCloneSettings.Default,
            CancellationToken.None);

        DictaCloneSettings imported = await transfer.ImportAsync(
            exportPath,
            CancellationToken.None);
        Assert.Equivalent(
            DictaCloneSettings.Default,
            imported,
            strict: true);

        JsonObject document = JsonNode.Parse(
            await File.ReadAllTextAsync(exportPath))!.AsObject();
        document["apiKey"] = "DO-NOT-PERSIST-SECRET";
        await File.WriteAllTextAsync(exportPath, document.ToJsonString());

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            transfer.ImportAsync(exportPath, CancellationToken.None));
    }

    [Fact]
    public async Task History_IsBoundedLoadableAndClearable()
    {
        using var directory = new TemporaryDirectory();
        using var history = new JsonTranscriptHistoryStore(directory.Paths);
        for (int index = 1; index <= 4; index++)
        {
            await history.AppendAsync(
                new(DateTimeOffset.UtcNow, $"entry {index}"),
                maximumEntries: 3,
                CancellationToken.None);
        }

        HistoryLoadResult loaded = await history.LoadAsync(
            CancellationToken.None);

        Assert.Equal(3, loaded.Entries.Length);
        Assert.Equal("entry 2", loaded.Entries[0].Text);
        Assert.Equal("entry 4", loaded.Entries[2].Text);
        await history.ClearAsync(CancellationToken.None);
        Assert.False(File.Exists(directory.Paths.HistoryFile));
    }

    [Fact]
    public async Task CorruptHistory_IsQuarantinedWithoutAffectingSettings()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Root);
        await File.WriteAllTextAsync(directory.Paths.HistoryFile, "broken");
        using var history = new JsonTranscriptHistoryStore(directory.Paths);

        HistoryLoadResult loaded = await history.LoadAsync(
            CancellationToken.None);

        Assert.Empty(loaded.Entries);
        Assert.NotNull(loaded.QuarantinedFilePath);
        Assert.True(File.Exists(loaded.QuarantinedFilePath));
        Assert.False(File.Exists(directory.Paths.HistoryFile));
    }

    [Fact]
    public async Task InaccessibleHistory_IsNotMisclassifiedOrQuarantined()
    {
        using var directory = new TemporaryDirectory();
        using var history = new JsonTranscriptHistoryStore(directory.Paths);
        await history.AppendAsync(
            new(DateTimeOffset.UtcNow, "keep me"),
            maximumEntries: 10,
            CancellationToken.None);
        await using var exclusiveLock = new FileStream(
            directory.Paths.HistoryFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        await Assert.ThrowsAsync<IOException>(() =>
            history.LoadAsync(CancellationToken.None));

        Assert.True(File.Exists(directory.Paths.HistoryFile));
        Assert.Empty(Directory.GetFiles(directory.Root, "history.corrupt-*"));
    }

    [Fact]
    public async Task DiagnosticsAndSupportBundle_ExcludeSensitiveContent()
    {
        using var directory = new TemporaryDirectory();
        const string secret = "DO-NOT-LOG-SECRET";
        const string transcript = "private transcript content";
        using (var diagnostics = new PrivacySafeDiagnosticLog(directory.Paths))
        {
            await diagnostics.WriteAsync(
                DiagnosticEventKind.Dictation,
                DiagnosticOutcome.Failed,
                TimeSpan.FromMilliseconds(123),
                new InvalidOperationException($"{secret} {transcript}"));
        }

        DictaCloneSettings settings = DictaCloneSettings.Default with
        {
            Audio = DictaCloneSettings.Default.Audio with
            {
                DeviceId = secret,
            },
            Text = DictaCloneSettings.Default.Text with
            {
                Vocabulary = [new(transcript, secret)],
                Expansions = [new(secret, transcript)],
            },
        };
        string bundlePath = Path.Combine(directory.Root, "support.zip");
        var bundles = new PrivacySafeSupportBundleService(directory.Paths);
        await bundles.CreateAsync(
            bundlePath,
            settings,
            CancellationToken.None);

        string diagnosticsText = await File.ReadAllTextAsync(
            directory.Paths.DiagnosticsFile);
        Assert.DoesNotContain(secret, diagnosticsText);
        Assert.DoesNotContain(transcript, diagnosticsText);
        Assert.Contains("InvalidOperationException", diagnosticsText);

        using ZipArchive archive = ZipFile.OpenRead(bundlePath);
        var collected = new StringBuilder();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            collected.Append(entry.FullName);
            using StreamReader reader = new(entry.Open());
            collected.Append(await reader.ReadToEndAsync());
        }

        string bundleText = collected.ToString();
        Assert.DoesNotContain(secret, bundleText);
        Assert.DoesNotContain(transcript, bundleText);
        Assert.DoesNotContain("history.json", bundleText);
        Assert.DoesNotContain("settings.json", bundleText);
        Assert.Contains("settings-summary.json", bundleText);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "DictaClone.Infrastructure.Tests",
                Guid.NewGuid().ToString("N"));
            Paths = new(Root);
        }

        public string Root { get; }

        public DictaCloneDataPaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
