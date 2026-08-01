namespace DictaClone.Audio;

public sealed class AudioCaptureDeviceException : InvalidOperationException
{
    public AudioCaptureDeviceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
