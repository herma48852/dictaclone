using DictaClone.Windows;

namespace DictaClone.Windows.Tests;

public sealed class ClipboardNativeFormatGuardTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(4, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(4, 2, false)]
    public void CaptureGuard_DistinguishesEmptyFromLostClipboard(
        int availableFormats,
        int capturedFormats,
        bool expected)
    {
        Assert.Equal(
            expected,
            ClipboardNativeFormatGuard.CaptureLostContent(
                availableFormats,
                capturedFormats));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 3, false)]
    [InlineData(1, 0, true)]
    [InlineData(4, 0, true)]
    [InlineData(4, 1, false)]
    public void RestoreGuard_DetectsMissingPublishedFormats(
        int expectedFormats,
        int availableFormats,
        bool expected)
    {
        Assert.Equal(
            expected,
            ClipboardNativeFormatGuard.RestoreLostContent(
                expectedFormats,
                availableFormats));
    }
}
