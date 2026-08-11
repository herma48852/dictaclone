#import <AVFoundation/AVFoundation.h>
#import <ApplicationServices/ApplicationServices.h>
#include <stdint.h>

typedef void (*DictaClonePermissionCompletion)(int32_t granted);

__attribute__((visibility("default")))
int32_t DictaClonePermissionShimVersion(void)
{
    return 1;
}

__attribute__((visibility("default")))
int32_t DictaCloneAccessibilityPermissionStatus(void)
{
    return AXIsProcessTrusted() ? 1 : 0;
}

__attribute__((visibility("default")))
int32_t DictaCloneRequestAccessibilityPermission(void)
{
    NSDictionary *options = @{
        (__bridge NSString *)kAXTrustedCheckOptionPrompt: @YES
    };
    return AXIsProcessTrustedWithOptions(
        (__bridge CFDictionaryRef)options) ? 1 : 0;
}

__attribute__((visibility("default")))
int32_t DictaCloneInputMonitoringPermissionStatus(void)
{
    return CGPreflightListenEventAccess() ? 1 : 0;
}

__attribute__((visibility("default")))
int32_t DictaCloneRequestInputMonitoringPermission(void)
{
    return CGRequestListenEventAccess() ? 1 : 0;
}

__attribute__((visibility("default")))
void DictaCloneRequestMicrophonePermission(
    DictaClonePermissionCompletion completion)
{
    if (completion == NULL)
    {
        return;
    }

    [AVCaptureDevice
        requestAccessForMediaType:AVMediaTypeAudio
        completionHandler:^(BOOL granted)
        {
            completion(granted ? 1 : 0);
        }];
}
