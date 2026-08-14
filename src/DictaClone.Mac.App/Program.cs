using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DictaClone.Core.Dictation;
using DictaClone.Mac.Audio;
using DictaClone.Mac.Foreground;
using DictaClone.Mac.Lifecycle;
using DictaClone.Mac.Permissions;

namespace DictaClone.Mac;

internal static class Program
{
    internal static bool OpenSettingsOnStart { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        int foregroundProbeArgument = Array.FindIndex(
            args,
            value => string.Equals(
                value,
                "--foreground-probe-delay",
                StringComparison.OrdinalIgnoreCase));
        if (foregroundProbeArgument >= 0)
        {
            return RunForegroundProbe(args, foregroundProbeArgument);
        }

        bool smokeTest = args.Contains(
            "--smoke-test",
            StringComparer.OrdinalIgnoreCase);
        if (smokeTest)
        {
            return RunSmokeTest();
        }

        OpenSettingsOnStart = args.Contains(
            "--settings",
            StringComparer.OrdinalIgnoreCase);

        if (!MacSingleInstanceGuard.TryAcquire(
                out MacSingleInstanceGuard? instanceGuard))
        {
            return 0;
        }

        using (instanceGuard)
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions
            {
                ShowInDock = false,
            })
            .LogToTrace();

    private static int RunSmokeTest()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("The macOS smoke test requires macOS.");
            return 1;
        }

        string[] requiredFiles =
        [
            "DictaClone.Mac.App.dll",
            "libAvaloniaNative.dylib",
            "libDictaClonePermissions.dylib",
            "libhostfxr.dylib",
        ];
        foreach (string fileName in requiredFiles)
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Missing packaged file: {path}");
                return 1;
            }
        }

        var permissions = new MacPermissionService();
        _ = permissions.Inspect();
        if (!permissions.IsMicrophoneRequestAvailable())
        {
            Console.Error.WriteLine(
                "The native microphone permission bridge is unavailable.");
            return 1;
        }
        if (new MacMicrophoneDeviceService().GetActiveCaptureDevices().Count == 0)
        {
            Console.Error.WriteLine("Microphone device discovery returned no choices.");
            return 1;
        }

        Console.WriteLine("DictaClone macOS packaged-app smoke test passed.");
        return 0;
    }

    private static int RunForegroundProbe(string[] args, int argumentIndex)
    {
        if (argumentIndex + 1 >= args.Length ||
            !double.TryParse(
                args[argumentIndex + 1],
                System.Globalization.CultureInfo.InvariantCulture,
                out double delaySeconds) ||
            delaySeconds is < 0 or > 30)
        {
            Console.Error.WriteLine(
                "--foreground-probe-delay requires a value from 0 to 30 seconds.");
            return 64;
        }

        Task.Delay(TimeSpan.FromSeconds(delaySeconds))
            .GetAwaiter()
            .GetResult();
        MacForegroundProbeResult result = MacForegroundProbe.Capture();
        Console.WriteLine(
            $"AccessibilityTrusted={result.AccessibilityTrusted}");
        Console.WriteLine(
            $"SystemElementAvailable={result.SystemElementAvailable}");
        WriteProbeAttribute(result.FocusedApplication);
        foreach (MacForegroundProbeEntry attribute in result.Attributes)
        {
            WriteProbeAttribute(attribute);
        }

        try
        {
            ForegroundTarget target = new MacForegroundTargetService()
                .CaptureAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine(
                $"Capture=Success Process={target.ProcessName} " +
                $"Bundle={target.WindowClass} Target={target.Id}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Capture=Failure Error={exception.GetType().Name}");
        }

        return 0;
    }

    private static void WriteProbeAttribute(
        MacForegroundProbeEntry attribute) =>
        Console.WriteLine(
            $"Source={attribute.Source} Attribute={attribute.Attribute} " +
            $"CopyError={attribute.CopyError} PidError={attribute.PidError} " +
            $"Pid={attribute.ProcessId} Hash={attribute.Hash:X16}");
}
