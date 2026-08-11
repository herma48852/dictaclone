using DictaClone.Core.Dictation;

namespace DictaClone.Audio;

public sealed class AudioCaptureDeviceException : AudioCaptureException
{
    public AudioCaptureDeviceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
