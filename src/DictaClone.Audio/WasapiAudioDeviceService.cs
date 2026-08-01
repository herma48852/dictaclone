using NAudio.CoreAudioApi;

namespace DictaClone.Audio;

public sealed class WasapiAudioDeviceService
{
    public static IReadOnlyList<MicrophoneDeviceInfo> GetActiveCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(
            DataFlow.Capture,
            Role.Multimedia);
        string defaultId = defaultDevice.ID;

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new MicrophoneDeviceInfo(
                device.ID,
                device.FriendlyName,
                string.Equals(
                    device.ID,
                    defaultId,
                    StringComparison.Ordinal)))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(
                device => device.FriendlyName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
