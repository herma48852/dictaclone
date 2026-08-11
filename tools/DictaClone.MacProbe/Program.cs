using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;
using DictaClone.Mac.Audio;
using DictaClone.Mac.Permissions;

if (!OperatingSystem.IsMacOS())
{
    Console.Error.WriteLine("The DictaClone macOS probe requires macOS.");
    return 1;
}

MacPermissionSnapshot permissions = new MacPermissionService().Inspect();
Console.WriteLine($"Microphone: {permissions.Microphone}");
Console.WriteLine($"Accessibility: {permissions.Accessibility}");
Console.WriteLine($"Input Monitoring: {permissions.InputMonitoring}");
Console.WriteLine("Input devices:");
foreach (MacMicrophoneDevice device in new MacMicrophoneDeviceService()
             .GetActiveCaptureDevices())
{
    Console.WriteLine($"- {device.FriendlyName} [{device.Id ?? "follow-default"}]");
}

int captureArgument = Array.FindIndex(
    args,
    value => string.Equals(
        value,
        "--capture-seconds",
        StringComparison.OrdinalIgnoreCase));
if (captureArgument < 0)
{
    return 0;
}

if (captureArgument + 1 >= args.Length ||
    !double.TryParse(
        args[captureArgument + 1],
        System.Globalization.CultureInfo.InvariantCulture,
        out double seconds) ||
    seconds is <= 0 or > 10)
{
    Console.Error.WriteLine("--capture-seconds requires a value from 0 to 10.");
    return 64;
}

var capture = new MacAudioCaptureService();
await using IAudioCaptureSession session = await capture.StartAsync(
    DictaCloneSettings.Default.Audio,
    CancellationToken.None);
if (session is IAudioLevelSource levels)
{
    levels.LevelChanged += (_, eventArgs) =>
        Console.Write($"\rPeak: {eventArgs.Peak:P0}   ");
}

await Task.Delay(TimeSpan.FromSeconds(seconds));
var audio = await session.StopAsync(CancellationToken.None);
Console.WriteLine();
Console.WriteLine(
    $"Captured {audio.Duration.TotalSeconds:F2}s, {audio.Pcm16.Length} bytes, silent={audio.IsSilent}");
return audio.Pcm16.IsEmpty ? 2 : 0;
