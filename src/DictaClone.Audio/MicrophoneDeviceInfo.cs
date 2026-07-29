namespace DictaClone.Audio;

public sealed record MicrophoneDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefault);
