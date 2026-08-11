using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DictaClone.Mac.Audio;
using DictaClone.Mac.Lifecycle;
using DictaClone.Mac.Permissions;

namespace DictaClone.Mac;

internal static class Program
{
    internal static bool OpenSettingsOnStart { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
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
}
