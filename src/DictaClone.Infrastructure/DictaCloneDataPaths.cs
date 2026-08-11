namespace DictaClone.Infrastructure;

public sealed record DictaCloneDataPaths(string RootDirectory)
{
    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string HistoryFile => Path.Combine(RootDirectory, "history.json");

    public string DiagnosticsFile => Path.Combine(
        RootDirectory,
        "diagnostics.jsonl");

    public static DictaCloneDataPaths Default { get; } = new(
        ResolveDefaultRootDirectory());

    private static string ResolveDefaultRootDirectory() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "DictaClone")
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "DictaClone");
}
