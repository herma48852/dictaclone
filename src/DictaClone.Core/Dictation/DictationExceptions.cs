namespace DictaClone.Core.Dictation;

public sealed class ForegroundTargetUnavailableException()
    : InvalidOperationException("No foreground application is available.");

public sealed class ForegroundTargetChangedException()
    : InvalidOperationException(
        "The foreground application changed before insertion.");

public sealed class ElevatedTargetException()
    : InvalidOperationException(
        "Windows blocked input to an elevated application.");

public sealed class ClipboardContentionException(Exception? innerException = null)
    : InvalidOperationException(
        "The Windows clipboard is currently busy.",
        innerException);

public sealed class InputInjectionException(Exception? innerException = null)
    : InvalidOperationException(
        "Windows did not accept the requested keyboard input.",
        innerException);
