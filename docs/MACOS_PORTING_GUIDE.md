# DictaClone macOS Porting Guide

## Purpose

DictaClone currently uses WPF for its Windows desktop shell. WPF cannot run on macOS; although .NET is cross-platform, Microsoft defines WPF as Windows-only. See the [Microsoft WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/).

The macOS port should preserve the shared dictation workflow while replacing the UI and operating-system integrations. The preferred approach is:

1. Keep reusable application logic in C#/.NET.
2. Use Avalonia for UI that should be shared between Windows and macOS.
3. Implement microphone, hotkey, foreground-target, clipboard, text-insertion, and permission behavior behind platform-specific adapters.
4. Use native Apple APIs where reliable desktop integration requires them.

## Recommended architecture

```text
Shared .NET code
├── dictation workflow and state
├── transcription orchestration
├── text processing
├── settings models
└── platform-neutral interfaces

Platform implementations
├── Windows
│   ├── WPF application shell
│   ├── Win32 foreground target and hotkeys
│   ├── WASAPI/NAudio microphone capture
│   └── Windows clipboard and SendInput
└── macOS
    ├── Avalonia UI with AppKit interop
    ├── macOS foreground target and global hotkeys
    ├── AVAudioEngine/Core Audio microphone capture
    └── NSPasteboard, CGEvent, and Accessibility APIs
```

WPF, Win32 handles, Windows Forms clipboard types, and other Windows-only types must not leak into shared projects. Shared code should depend only on interfaces and platform-neutral models.

## UI framework choices

### Recommended: Avalonia with native macOS adapters

[Avalonia](https://docs.avaloniaui.net/docs/welcome) is a cross-platform .NET UI framework that supports Windows and macOS, including Apple Silicon and Intel. Its C#, XAML, data-binding, and view-model concepts are similar to WPF, making it the most direct route to retaining the existing .NET code and development model. Avalonia documents the important differences in its [WPF migration guide](https://docs.avaloniaui.net/docs/migration/wpf/).

Avalonia can replace settings windows, status displays, and other visible controls. It does not replace platform-specific features such as `SendInput`, WASAPI, or foreground-window detection; these still require native macOS implementations.

### Native alternative: SwiftUI and AppKit

The most native option is a SwiftUI application with AppKit integration. SwiftUI can implement settings and standard windows, while AppKit supplies desktop-specific facilities such as [`NSStatusItem`](https://developer.apple.com/documentation/appkit/nsstatusitem) for a menu-bar item.

This option provides the closest macOS look, behavior, and platform integration, but requires a substantially larger Swift rewrite or a maintained boundary between a Swift shell and the shared .NET engine.

### Why .NET MAUI is not the first choice

.NET MAUI supports macOS through Mac Catalyst, as described in [Microsoft's supported-platform documentation](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms). That is attractive when sharing an application with iOS, but DictaClone is primarily a desktop utility that depends on menu-bar behavior, global shortcuts, accessibility permissions, and interaction with other desktop applications. Avalonia with native adapters, or a native SwiftUI/AppKit shell, is a better fit.

## Windows-to-macOS mapping

| DictaClone capability | Windows implementation | macOS replacement |
|---|---|---|
| Settings and status UI | WPF | Avalonia or SwiftUI |
| System-tray/menu icon | Windows notification area | AppKit `NSStatusItem` |
| Floating status pill | WPF window | Avalonia window or AppKit floating panel |
| Global hotkey | Win32 keyboard hook or hotkey registration | macOS global hotkey/event-tap API |
| Microphone capture | NAudio/WASAPI | AVAudioEngine/Core Audio |
| Foreground-app detection | Win32 foreground-window APIs | NSWorkspace and Accessibility APIs |
| Paste Mode | Windows clipboard | `NSPasteboard` |
| Typing Mode | Win32 `SendInput` | `CGEvent` and Accessibility APIs |
| Elevated-target protection | Windows process integrity levels | macOS permission and target-access checks |
| Application packaging | Windows executable/installer | Signed and notarized `.app` bundle |

Apple's [`AXUIElement`](https://developer.apple.com/documentation/applicationservices/axuielement_h?changes=latest_ma_2) APIs allow assistive applications to inspect and interact with other applications' accessible UI elements. DictaClone's macOS foreground-target and insertion adapters will need to handle applications that expose incomplete accessibility information or temporarily reject requests.

## macOS permissions

The application should request permissions only when the associated feature is first needed and provide clear recovery instructions when access is denied.

Expected permission areas include:

- Microphone access for recording speech.
- Accessibility access for inspecting the active control and inserting text where required.
- Input Monitoring if the selected global-hotkey implementation requires it.

Permission state must be treated as an explicit application condition, not as a generic insertion or recording failure. Automated logic should never repeatedly trigger system permission prompts.

## Shared interface boundaries

The existing platform-neutral contracts should remain the seam for a future macOS implementation. At minimum, platform-specific services should be isolated behind interfaces for:

- Audio capture and device enumeration.
- Global-hotkey registration.
- Foreground-target capture and revalidation.
- Clipboard transactions.
- Keyboard/text insertion.
- Tray or menu-bar commands.
- Status presentation.
- Permission inspection and guidance.

Foreground targets should be represented by opaque platform identifiers. Shared code should compare identities through the platform service rather than interpreting a Windows handle or macOS accessibility object.

## Text-insertion behavior

The macOS version should preserve the same safety properties as Windows:

### Paste Mode

- Capture the intended target before recording begins.
- Revalidate the target immediately before insertion.
- Preserve all practical clipboard formats, not only plain text.
- Use bounded retries for temporary pasteboard contention.
- Restore the clipboard only if DictaClone still owns the clipboard transaction.
- Never overwrite a clipboard change made by the user or another application.

### Typing Mode

- Do not read or modify the clipboard.
- Preserve Unicode text, emoji, line endings, and tabs.
- Apply the configured per-character delay.
- Report denied Accessibility or Input Monitoring permission as an actionable error.

## Testing strategy

Most shared unit and workflow tests should run unchanged on both operating systems. Each platform adapter then needs its own unit and integration tests.

The macOS test suite should include:

- Foreground-target capture and changed-target rejection.
- Pasteboard preservation and concurrent-change handling.
- Unicode, emoji, multiline, punctuation, and long-text insertion.
- Typing Mode verification that the pasteboard is untouched.
- Temporary pasteboard contention and bounded retry behavior.
- Missing and denied permission states.
- Microphone start, cancellation, completion, and device-loss behavior.
- Hotkey press, hold, release, and collision behavior.
- Real insertion targets such as TextEdit, a browser field, Terminal, and a dedicated test application.
- Apple Silicon and Intel validation where both architectures remain supported.

Signing, hardened-runtime, sandbox, and notarization checks should be included in release regression testing because development builds can behave differently from distributed applications.

## Suggested port sequence

1. Keep the current Windows implementation stable and enforce platform boundaries with tests.
2. Move reusable view models and presentation state out of the WPF project.
3. Prototype an Avalonia settings window and status surface on Windows and macOS.
4. Implement macOS menu-bar and global-hotkey adapters.
5. Implement Core Audio microphone capture.
6. Implement foreground-target tracking and permission diagnostics.
7. Implement and regression-test Paste Mode and Typing Mode.
8. Add signed and notarized macOS packaging.
9. Run the full application corpus against common native, browser, terminal, and remote-desktop targets.

## Current project guidance

The current separation among `DictaClone.Core`, `DictaClone.Windows`, and the WPF application shell is aligned with this plan. New Windows functionality should continue to enter the shared workflow through interfaces rather than direct WPF or Win32 dependencies. This will allow the macOS port to replace adapters one at a time without rewriting the dictation workflow.
