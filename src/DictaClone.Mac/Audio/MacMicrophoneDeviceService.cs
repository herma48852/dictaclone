using System.Runtime.InteropServices;

namespace DictaClone.Mac.Audio;

public sealed record MacMicrophoneDevice(
    string? Id,
    string FriendlyName,
    bool IsDefault)
{
    public override string ToString() => IsDefault
        ? $"{FriendlyName} (default)"
        : FriendlyName;
}

public sealed class MacMicrophoneDeviceService
{
    private readonly IMacAudioDeviceApi _native;

    public MacMicrophoneDeviceService()
        : this(new NativeMacAudioDeviceApi())
    {
    }

    internal MacMicrophoneDeviceService(IMacAudioDeviceApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public IReadOnlyList<MacMicrophoneDevice> GetActiveCaptureDevices() =>
        _native.GetActiveInputDevices();
}

internal interface IMacAudioDeviceApi
{
    IReadOnlyList<MacMicrophoneDevice> GetActiveInputDevices();
}

internal sealed partial class NativeMacAudioDeviceApi : IMacAudioDeviceApi
{
    private const uint SystemObject = 1;
    private const uint HardwareDevices = 0x64657623;
    private const uint DefaultInputDevice = 0x64496E20;
    private const uint ObjectName = 0x6C6E616D;
    private const uint DeviceUid = 0x75696420;
    private const uint DeviceStreams = 0x73746D23;
    private const uint ScopeGlobal = 0x676C6F62;
    private const uint ScopeInput = 0x696E7074;

    public IReadOnlyList<MacMicrophoneDevice> GetActiveInputDevices()
    {
        uint defaultDevice = ReadUInt32(
            SystemObject,
            new(DefaultInputDevice, ScopeGlobal, 0));
        uint[] devices = ReadUInt32Array(
            SystemObject,
            new(HardwareDevices, ScopeGlobal, 0));
        return devices
            .Where(HasInputChannels)
            .Select(device => new MacMicrophoneDevice(
                ReadString(device, new(DeviceUid, ScopeGlobal, 0)),
                ReadString(device, new(ObjectName, ScopeGlobal, 0)) ??
                    $"Audio device {device}",
                device == defaultDevice))
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .Prepend(new MacMicrophoneDevice(
                Id: null,
                FriendlyName: "Follow system default microphone",
                IsDefault: true))
            .ToArray();
    }

    private static bool HasInputChannels(uint device)
    {
        var address = new AudioObjectPropertyAddress(
            DeviceStreams,
            ScopeInput,
            0);
        return AudioObjectGetPropertyDataSize(
            device,
            ref address,
            qualifierDataSize: 0,
            nint.Zero,
            out uint size) == 0 && size >= sizeof(uint);
    }

    private static uint ReadUInt32(
        uint audioObject,
        AudioObjectPropertyAddress address)
    {
        uint size = sizeof(uint);
        uint value = 0;
        unsafe
        {
            int status = AudioObjectGetPropertyData(
                audioObject,
                ref address,
                qualifierDataSize: 0,
                nint.Zero,
                ref size,
                (nint)(&value));
            return status == 0 ? value : 0;
        }
    }

    private static uint[] ReadUInt32Array(
        uint audioObject,
        AudioObjectPropertyAddress address)
    {
        if (AudioObjectGetPropertyDataSize(
                audioObject,
                ref address,
                qualifierDataSize: 0,
                nint.Zero,
                out uint size) != 0 || size == 0)
        {
            return [];
        }

        nint data = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (AudioObjectGetPropertyData(
                    audioObject,
                    ref address,
                    qualifierDataSize: 0,
                    nint.Zero,
                    ref size,
                    data) != 0)
            {
                return [];
            }

            var values = new uint[size / sizeof(uint)];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((uint)Marshal.ReadInt32(
                    data,
                    index * sizeof(uint)));
            }

            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private static string? ReadString(
        uint audioObject,
        AudioObjectPropertyAddress address)
    {
        uint size = checked((uint)IntPtr.Size);
        nint value = nint.Zero;
        unsafe
        {
            int status = AudioObjectGetPropertyData(
                audioObject,
                ref address,
                qualifierDataSize: 0,
                nint.Zero,
                ref size,
                (nint)(&value));
            return status == 0 ? Interop.ObjectiveC.GetString(value) : null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct AudioObjectPropertyAddress(
        uint Selector,
        uint Scope,
        uint Element);

    [LibraryImport(
        "/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
    private static partial int AudioObjectGetPropertyDataSize(
        uint audioObject,
        ref AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        nint qualifierData,
        out uint dataSize);

    [LibraryImport(
        "/System/Library/Frameworks/CoreAudio.framework/CoreAudio")]
    private static partial int AudioObjectGetPropertyData(
        uint audioObject,
        ref AudioObjectPropertyAddress address,
        uint qualifierDataSize,
        nint qualifierData,
        ref uint dataSize,
        nint data);
}
