using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;

namespace DictaClone.Windows;

public sealed class ForegroundTargetService : IForegroundTargetService
{
    private readonly IForegroundWindowApi _windows;

    public ForegroundTargetService()
        : this(new ForegroundWindowApi())
    {
    }

    internal ForegroundTargetService(IForegroundWindowApi windows)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    public Task<ForegroundTarget> CaptureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ForegroundWindowSnapshot snapshot = _windows.Capture();
        if (snapshot.WindowHandle == nint.Zero || snapshot.ProcessId == 0)
        {
            throw new ForegroundTargetUnavailableException();
        }

        return Task.FromResult(new ForegroundTarget(
            CreateId(snapshot.WindowHandle, snapshot.ProcessId),
            snapshot.ProcessName,
            snapshot.WindowClass,
            snapshot.IsElevatedAboveCurrentProcess));
    }

    public Task<bool> IsCurrentAsync(
        ForegroundTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        ForegroundWindowSnapshot snapshot = _windows.Capture();
        string currentId = CreateId(
            snapshot.WindowHandle,
            snapshot.ProcessId);
        return Task.FromResult(string.Equals(
            target.Id,
            currentId,
            StringComparison.Ordinal));
    }

    private static string CreateId(nint windowHandle, uint processId) =>
        $"{unchecked((nuint)windowHandle):X16}:{processId:X8}";
}

internal interface IForegroundWindowApi
{
    ForegroundWindowSnapshot Capture();
}

internal readonly record struct ForegroundWindowSnapshot(
    nint WindowHandle,
    uint ProcessId,
    string ProcessName,
    string WindowClass,
    bool IsElevatedAboveCurrentProcess);

internal sealed partial class ForegroundWindowApi : IForegroundWindowApi
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    public ForegroundWindowSnapshot Capture()
    {
        nint window = GetForegroundWindow();
        if (window == nint.Zero)
        {
            return default;
        }

        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
        {
            return default;
        }

        uint targetIntegrityLevel = GetIntegrityLevel(processId);
        uint currentIntegrityLevel = GetIntegrityLevel(
            unchecked((uint)Environment.ProcessId));

        return new(
            window,
            processId,
            GetProcessName(processId),
            GetWindowClass(window),
            targetIntegrityLevel != 0 &&
            currentIntegrityLevel != 0 &&
            targetIntegrityLevel > currentIntegrityLevel);
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById(
                checked((int)processId));
            return process.ProcessName;
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException or
                NotSupportedException or Win32Exception)
        {
            return $"pid-{processId}";
        }
    }

    private static unsafe string GetWindowClass(nint window)
    {
        Span<char> className = stackalloc char[256];
        fixed (char* classNamePointer = className)
        {
            int length = GetClassName(
                window,
                classNamePointer,
                className.Length);
            return length > 0 ? new(className[..length]) : string.Empty;
        }
    }

    private static uint GetIntegrityLevel(uint processId)
    {
        nint process = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (process == nint.Zero)
        {
            return 0;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out nint token))
            {
                return 0;
            }

            try
            {
                _ = GetTokenInformation(
                    token,
                    TokenIntegrityLevel,
                    nint.Zero,
                    tokenInformationLength: 0,
                    out uint length);
                if (length == 0)
                {
                    return 0;
                }

                nint information = Marshal.AllocHGlobal(checked((int)length));
                try
                {
                    if (!GetTokenInformation(
                            token,
                            TokenIntegrityLevel,
                            information,
                            length,
                            out _))
                    {
                        return 0;
                    }

                    nint sid = Marshal.ReadIntPtr(information);
                    nint countAddress = GetSidSubAuthorityCount(sid);
                    if (countAddress == nint.Zero)
                    {
                        return 0;
                    }

                    byte count = Marshal.ReadByte(countAddress);
                    if (count == 0)
                    {
                        return 0;
                    }

                    nint levelAddress = GetSidSubAuthority(sid, count - 1u);
                    return levelAddress == nint.Zero
                        ? 0
                        : unchecked((uint)Marshal.ReadInt32(levelAddress));
                }
                finally
                {
                    Marshal.FreeHGlobal(information);
                }
            }
            finally
            {
                _ = CloseHandle(token);
            }
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(
        nint window,
        char* className,
        int maximumCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(
        nint process,
        uint desiredAccess,
        out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        nint token,
        int informationClass,
        nint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(
        nint sid,
        uint subAuthority);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
