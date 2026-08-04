using System.IO.Compression;
using System.Text.Json;
using DictaClone.Core;
using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;

namespace DictaClone.Infrastructure;

public sealed class PrivacySafeSupportBundleService : ISupportBundleService
{
    private readonly DictaCloneDataPaths _paths;

    public PrivacySafeSupportBundleService(DictaCloneDataPaths? paths = null)
    {
        _paths = paths ?? DictaCloneDataPaths.Default;
    }

    public async Task CreateAsync(
        string destinationPath,
        DictaCloneSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(settings);
        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(
                "The support bundle path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous))
            using (var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: true))
            {
                await WriteJsonEntryAsync(
                    archive,
                    "system.json",
                    new
                    {
                        createdUtc = DateTimeOffset.UtcNow,
                        product = ProductInfo.Name,
                        version = ProductInfo.DevelopmentVersion.ToString(),
                        operatingSystem = Environment.OSVersion.VersionString,
                        framework = System.Runtime.InteropServices
                            .RuntimeInformation.FrameworkDescription,
                        architecture = System.Runtime.InteropServices
                            .RuntimeInformation.ProcessArchitecture.ToString(),
                    },
                    cancellationToken).ConfigureAwait(false);
                await WriteJsonEntryAsync(
                    archive,
                    "settings-summary.json",
                    new
                    {
                        settings.SchemaVersion,
                        settings.Transcription.Model,
                        settings.Transcription.Language,
                        settings.Text.WorkDomain,
                        vocabularyEntries = settings.Text.Vocabulary.Length,
                        expansionEntries = settings.Text.Expansions.Length,
                        settings.Insertion.Mode,
                        typingDelayMilliseconds =
                            settings.Insertion.CharacterDelay.TotalMilliseconds,
                        settings.Preferences.StartWithWindows,
                        settings.Preferences.HistoryEnabled,
                        settings.Preferences.HistoryLimit,
                        hotkeyBindings = settings.Hotkeys.Length,
                    },
                    cancellationToken).ConfigureAwait(false);

                if (File.Exists(_paths.DiagnosticsFile))
                {
                    ZipArchiveEntry diagnostics = archive.CreateEntry(
                        "diagnostics.jsonl",
                        CompressionLevel.Optimal);
                    await using Stream destination = diagnostics.Open();
                    await using var source = new FileStream(
                        _paths.DiagnosticsFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 16_384,
                        FileOptions.Asynchronous);
                    await source.CopyToAsync(destination, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            name,
            CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
