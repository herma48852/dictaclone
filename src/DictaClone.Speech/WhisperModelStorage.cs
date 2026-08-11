namespace DictaClone.Speech;

public static class WhisperModelStorage
{
    public const string ModelDirectoryEnvironmentVariable =
        "DICTACLONE_MODEL_DIRECTORY";

    public static string ResolveDefaultDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(
            ModelDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        string? repositoryModels = FindRepositoryModels(
            Environment.CurrentDirectory) ??
            FindRepositoryModels(AppContext.BaseDirectory);
        if (repositoryModels is not null)
        {
            return repositoryModels;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "DictaClone",
                "Models");
        }

        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "DictaClone", "Models");
    }

    private static string? FindRepositoryModels(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));

        while (directory is not null)
        {
            string modelDirectory = Path.Combine(directory.FullName, "models");
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(modelDirectory))
            {
                return modelDirectory;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
