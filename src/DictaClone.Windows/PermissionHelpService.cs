using System.Diagnostics;

namespace DictaClone.Windows;

public sealed class PermissionHelpService
{
    public const string MicrophonePrivacyUri = "ms-settings:privacy-microphone";

    public static void OpenMicrophonePrivacySettings() =>
        Process.Start(new ProcessStartInfo
        {
            FileName = MicrophonePrivacyUri,
            UseShellExecute = true,
        });
}
