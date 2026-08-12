#import <AVFoundation/AVFoundation.h>
#import <AppKit/AppKit.h>
#import <ApplicationServices/ApplicationServices.h>
#import <IOKit/hidsystem/IOLLEvent.h>
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

__attribute__((visibility("default")))
int32_t DictaCloneDecodeMediaKeyEvent(
    CGEventRef event,
    int32_t *keyType,
    int32_t *isPressed)
{
    if (event == NULL || keyType == NULL || isPressed == NULL)
    {
        return 0;
    }

    @autoreleasepool
    {
        NSEvent *nativeEvent = [NSEvent eventWithCGEvent:event];
        if (nativeEvent == nil ||
            nativeEvent.type != NSEventTypeSystemDefined ||
            nativeEvent.subtype != NX_SUBTYPE_AUX_CONTROL_BUTTONS)
        {
            return 0;
        }

        uint32_t data = (uint32_t)nativeEvent.data1;
        int32_t state = (int32_t)((data >> 8) & 0xff);
        if (state != NX_KEYDOWN && state != NX_KEYUP)
        {
            return 0;
        }

        *keyType = (int32_t)((data >> 16) & 0xffff);
        *isPressed = state == NX_KEYDOWN ? 1 : 0;
        return 1;
    }
}
