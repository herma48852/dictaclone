using System.Diagnostics;
using DictaClone.Windows;

namespace DictaClone.EndToEndTests;

public sealed class AppProcessLifecycleTests
{
    [Fact]
    public async Task RepositoryRuntime_StartsAndStopsTrayProcess()
    {
        string repositoryRoot = FindRepositoryRoot();
        string dotNet = Path.Combine(repositoryRoot, ".dotnet", "dotnet.exe");
        string appAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "DictaClone.App.dll");

        Assert.True(File.Exists(dotNet), $"Repository runtime not found: {dotNet}");
        Assert.True(
            File.Exists(appAssembly),
            $"Application assembly not found: {appAssembly}");

        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = dotNet,
                Arguments = $"\"{appAssembly}\" --smoke-test",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,
            },
        };
        Assert.True(process.Start());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            Assert.Fail("DictaClone did not exit within 10 seconds.");
        }

        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void DuplicateSmokeInstance_ExitsWithoutOpeningADialog()
    {
        Assert.True(SingleInstanceGuard.TryAcquire(
            "DictaClone.Desktop",
            out SingleInstanceGuard? guard));
        using (guard)
        using (Process process = StartSmokeProcess())
        {
            Assert.True(
                process.WaitForExit(milliseconds: 10_000),
                "The duplicate smoke process did not exit within 10 seconds.");
            Assert.Equal(2, process.ExitCode);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the DictaClone repository root.");
    }

    private static Process StartSmokeProcess()
    {
        string repositoryRoot = FindRepositoryRoot();
        string dotNet = Path.Combine(repositoryRoot, ".dotnet", "dotnet.exe");
        string appAssembly = Path.Combine(
            AppContext.BaseDirectory,
            "DictaClone.App.dll");
        var process = new Process
        {
            StartInfo = new()
            {
                FileName = dotNet,
                Arguments = $"\"{appAssembly}\" --smoke-test",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,
            },
        };
        Assert.True(process.Start());
        return process;
    }
}
