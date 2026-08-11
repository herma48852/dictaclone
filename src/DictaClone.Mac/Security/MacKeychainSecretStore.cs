using System.Diagnostics;
using DictaClone.Core.Contracts;

namespace DictaClone.Mac.Security;

public sealed class MacKeychainSecretStore : ISecretStore
{
    public const string ServiceName = "com.dictaclone.desktop";
    private readonly IKeychainCommand _commands;

    public MacKeychainSecretStore()
        : this(new SecurityToolKeychainCommand())
    {
    }

    internal MacKeychainSecretStore(IKeychainCommand commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public async Task<string?> ReadAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateName(name);
        KeychainCommandResult result = await _commands.RunAsync(
            [
                "find-generic-password",
                "-s", ServiceName,
                "-a", name,
                "-w",
            ],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 44)
        {
            return null;
        }

        EnsureSuccess(result, "read");
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    public async Task WriteAsync(
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrEmpty(value);
        KeychainCommandResult result = await _commands.RunAsync(
            [
                "add-generic-password",
                "-U",
                "-s", ServiceName,
                "-a", name,
                "-w", value,
            ],
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "write");
    }

    public async Task DeleteAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ValidateName(name);
        KeychainCommandResult result = await _commands.RunAsync(
            [
                "delete-generic-password",
                "-s", ServiceName,
                "-a", name,
            ],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 44)
        {
            EnsureSuccess(result, "delete");
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    private static void EnsureSuccess(
        KeychainCommandResult result,
        string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"macOS Keychain {operation} failed with exit code {result.ExitCode}.");
        }
    }
}

internal interface IKeychainCommand
{
    Task<KeychainCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed record KeychainCommandResult(
    int ExitCode,
    string StandardOutput);

internal sealed class SecurityToolKeychainCommand : IKeychainCommand
{
    public async Task<KeychainCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The macOS security tool could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        Task<string> errors = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _ = await errors.ConfigureAwait(false);
        return new(process.ExitCode, await output.ConfigureAwait(false));
    }
}
