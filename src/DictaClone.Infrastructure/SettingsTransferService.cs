using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;

namespace DictaClone.Infrastructure;

public sealed class SettingsTransferService : ISettingsTransferService
{
    public Task ExportAsync(
        string destinationPath,
        DictaCloneSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(settings);
        var errors = SettingsValidator.Validate(settings);
        if (!errors.IsEmpty)
        {
            throw new InvalidDataException(
                $"Settings validation failed at {errors[0].Path}: " +
                errors[0].Message);
        }

        return AtomicFileWriter.WriteAsync(
            destinationPath,
            SettingsDocumentCodec.Serialize(settings),
            cancellationToken);
    }

    public async Task<DictaCloneSettings> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        byte[] document = await File.ReadAllBytesAsync(
            sourcePath,
            cancellationToken).ConfigureAwait(false);
        return SettingsDocumentCodec.Deserialize(document).Settings;
    }
}
