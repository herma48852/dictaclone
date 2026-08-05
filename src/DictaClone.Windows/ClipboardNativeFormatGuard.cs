using System.Runtime.InteropServices;

namespace DictaClone.Windows;

internal static partial class ClipboardNativeFormatGuard
{
    public static void EnsureCaptureConsistent(int capturedFormatCount)
    {
        int availableFormatCount = CountClipboardFormats();
        if (CaptureLostContent(
                availableFormatCount,
                capturedFormatCount))
        {
            throw new ClipboardFormatUnavailableException(
                "Clipboard formats were available but could not be captured.");
        }
    }

    public static void EnsureRestoreConsistent(int expectedFormatCount)
    {
        int availableFormatCount = CountClipboardFormats();
        if (RestoreLostContent(
                expectedFormatCount,
                availableFormatCount))
        {
            throw new ClipboardFormatUnavailableException(
                "Clipboard restore completed without publishing its formats.");
        }
    }

    internal static bool CaptureLostContent(
        int availableFormatCount,
        int capturedFormatCount) =>
        availableFormatCount > 0 && capturedFormatCount == 0;

    internal static bool RestoreLostContent(
        int expectedFormatCount,
        int availableFormatCount) =>
        expectedFormatCount > 0 && availableFormatCount == 0;

    [LibraryImport("user32.dll")]
    private static partial int CountClipboardFormats();
}

internal sealed class ClipboardFormatUnavailableException(string message)
    : ExternalException(message);
