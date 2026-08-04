using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DictaClone.Core.Contracts;

namespace DictaClone.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialSecretStore : ISecretStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string TargetPrefix = "DictaClone/";

    public Task<string?> ReadAsync(
        string name,
        CancellationToken cancellationToken)
    {
        string target = GetTarget(name);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(target, CredentialTypeGeneric, 0, out nint pointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new Win32Exception(error);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(
                pointer);
            if (credential.CredentialBlob == nint.Zero ||
                credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(string.Empty);
            }

            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public unsafe Task WriteAsync(
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        string target = GetTarget(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] secret = Encoding.Unicode.GetBytes(value);
        nint blob = Marshal.AllocHGlobal(secret.Length);
        nint targetPointer = Marshal.StringToHGlobalUni(target);
        nint userPointer = Marshal.StringToHGlobalUni(Environment.UserName);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = targetPointer,
                CredentialBlobSize = checked((uint)secret.Length),
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = userPointer,
            };
            if (!CredWrite(&credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }
        finally
        {
            Array.Clear(secret);
            Marshal.Copy(secret, 0, blob, secret.Length);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(targetPointer);
            Marshal.FreeHGlobal(userPointer);
        }
    }

    public Task DeleteAsync(
        string name,
        CancellationToken cancellationToken)
    {
        string target = GetTarget(name);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(target, CredentialTypeGeneric, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error);
            }
        }

        return Task.CompletedTask;
    }

    private static string GetTarget(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 200 || name.Contains('\\'))
        {
            throw new ArgumentException("Secret name is invalid.", nameof(name));
        }

        return TargetPrefix + name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW",
        SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CredWrite(NativeCredential* credential,
        uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW",
        SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags,
        out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW",
        SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(nint buffer);
}
