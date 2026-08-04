using System.Diagnostics;
using System.Reflection;
using DictaClone.Core.Contracts;
using Microsoft.Win32;

namespace DictaClone.Windows;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    internal const string ValueName = "DictaClone";
    private readonly IRunRegistry _registry;
    private readonly string _command;

    public StartupRegistrationService()
        : this(new CurrentUserRunRegistry(), CreateCurrentCommand())
    {
    }

    internal StartupRegistrationService(
        IRunRegistry registry,
        string command)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        _command = command;
    }

    public bool IsEnabled => string.Equals(
        _registry.GetValue(ValueName),
        _command,
        StringComparison.Ordinal);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _registry.SetValue(ValueName, _command);
        }
        else
        {
            _registry.DeleteValue(ValueName);
        }
    }

    internal static string CreateCommand(
        string processPath,
        string entryAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        bool isDotNetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
        return isDotNetHost
            ? $"\"{processPath}\" \"{entryAssemblyPath}\""
            : $"\"{processPath}\"";
    }

    private static string CreateCurrentCommand()
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The DictaClone process path is unavailable.");
        string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            assemblyPath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        return CreateCommand(
            processPath,
            assemblyPath ?? processPath);
    }
}

internal interface IRunRegistry
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

internal sealed class CurrentUserRunRegistry : IRunRegistry
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(name, defaultValue: null) as string;
    }

    public void SetValue(string name, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
