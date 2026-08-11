namespace DictaClone.Core.Dictation;

public class AudioCaptureException(
    string message,
    Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public class PlatformPermissionException(
    string permission,
    string message)
    : InvalidOperationException(message)
{
    public string Permission { get; } = permission;
}

public sealed class ForegroundTargetUnavailableException()
    : InvalidOperationException("No foreground application is available.");

public sealed class ForegroundTargetChangedException()
    : InvalidOperationException(
        "The foreground application changed before insertion.");

public sealed class ElevatedTargetException()
    : InvalidOperationException(
        "The operating system blocked input to the target application.");

public sealed class ClipboardContentionException(Exception? innerException = null)
    : InvalidOperationException(
        "The system clipboard is currently busy.",
        innerException);

public sealed class InputInjectionException(Exception? innerException = null)
    : InvalidOperationException(
        "The operating system did not accept the requested keyboard input.",
        innerException);

public sealed class SelectionChangedException()
    : InvalidOperationException(
        "The selected text changed before Smart Edit replacement.");

public sealed class SmartEditNotConfiguredException()
    : InvalidOperationException("Smart Edit is not configured.");

public sealed class SmartEditAuthenticationException()
    : InvalidOperationException("The Smart Edit provider rejected the API key.");

public sealed class SmartEditRateLimitException(TimeSpan? retryAfter = null)
    : InvalidOperationException("The Smart Edit provider rate limit was reached.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class SmartEditUnavailableException(Exception? innerException = null)
    : InvalidOperationException(
        "The Smart Edit provider is unavailable.",
        innerException);

public sealed class SmartEditResponseException(Exception? innerException = null)
    : InvalidOperationException(
        "The Smart Edit provider returned an invalid response.",
        innerException);

public sealed class SmartEditTimeoutException(Exception? innerException = null)
    : TimeoutException("The Smart Edit provider timed out.", innerException);

public sealed class SmartEditRequestTooLargeException()
    : InvalidOperationException(
        "The Smart Edit instruction or selection is too large to send safely.");
