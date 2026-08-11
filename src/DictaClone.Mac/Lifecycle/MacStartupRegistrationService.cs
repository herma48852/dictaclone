using System.Security;
using DictaClone.Core.Contracts;

namespace DictaClone.Mac.Lifecycle;

public sealed class MacStartupRegistrationService : IStartupRegistrationService
{
    private const string FileName = "com.dictaclone.desktop.plist";
    private readonly string _launchAgentPath;
    private readonly string _applicationPath;

    public MacStartupRegistrationService(
        string? launchAgentsDirectory = null,
        string? applicationPath = null)
    {
        string directory = launchAgentsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");
        _launchAgentPath = Path.Combine(directory, FileName);
        _applicationPath = applicationPath ?? ResolveApplicationPath();
    }

    public bool IsEnabled => File.Exists(_launchAgentPath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(_launchAgentPath))
            {
                File.Delete(_launchAgentPath);
            }

            return;
        }

        string directory = Path.GetDirectoryName(_launchAgentPath) ??
            throw new InvalidOperationException(
                "The LaunchAgent path has no parent directory.");
        Directory.CreateDirectory(directory);
        string escapedPath = SecurityElement.Escape(_applicationPath) ??
            throw new InvalidOperationException(
                "The application path could not be encoded.");
        string document = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>com.dictaclone.desktop</string>
              <key>ProgramArguments</key>
              <array>
                <string>/usr/bin/open</string>
                <string>-gja</string>
                <string>{escapedPath}</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
            </dict>
            </plist>
            """;
        string temporary = _launchAgentPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, document);
        File.Move(temporary, _launchAgentPath, overwrite: true);
    }

    private static string ResolveApplicationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The DictaClone executable path is unavailable.");
    }
}
