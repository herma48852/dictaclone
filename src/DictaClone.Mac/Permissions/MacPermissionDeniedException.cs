using DictaClone.Core.Dictation;

namespace DictaClone.Mac.Permissions;

public sealed class MacPermissionDeniedException(
    string permission,
    string message)
    : PlatformPermissionException(permission, message);
