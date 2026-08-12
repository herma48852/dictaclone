# DictaClone macOS porting guide

Last reviewed: August 11, 2026

## Current decision and status

The macOS port uses .NET 10, Avalonia 12.1, and native Apple adapters. It keeps
the accepted dictation, transcription, text-processing, persistence, history,
diagnostics, and Smart Edit workflow in shared C# projects. WPF and Win32 remain
the Windows shell and platform layer; Avalonia, AppKit, Core Foundation, Core
Graphics, Core Audio, Accessibility, and Keychain Services provide the macOS
shell and integrations.

The implementation now includes every planned engineering milestone:

1. Shared input models, shortcut interpretation, presentation controllers, and
   platform-neutral error contracts are extracted from Windows-only assemblies.
2. An Avalonia menu-bar app supplies settings, history, status, first-run, and
   shutdown behavior without showing a Dock icon.
3. Native macOS implementations cover microphone capture and discovery, global
   hotkeys, foreground-target capture, selected-text capture, pasteboard-safe
   paste, delayed typing, permissions, Keychain secrets, start at login, and
   single-instance enforcement.
4. Apple Silicon and Intel self-contained bundle creation, hardened-runtime
   signing, notarization, stapling, checksums, and smoke verification are
   scripted.
5. Adapter tests and a native permission/device probe are present.

The local development qualification now passes with repository-pinned .NET SDK
10.0.302 and Xcode 26.6 (17F113). The Avalonia app builds without warnings, both
`osx-arm64` and `osx-x64` self-contained bundles publish and validate, and both
packaged executables pass their non-UI smoke test (the Intel check uses Rosetta
on the Apple-silicon build host). The adapter test assembly also builds without
warnings. On August 11, 2026, all cross-platform and macOS automated suites
passed from `scripts/macos/test.sh` in a normal Terminal session. The same day,
an Apple-Development-signed, hardened-runtime `osx-arm64` bundle completed the
primary interactive path on Apple Silicon: LaunchServices bundle launch,
Microphone and Accessibility authorization, the `Control+Shift+Space` global
shortcut, live microphone capture, local transcription, and Paste Mode
insertion into TextEdit and native GNU Emacs. DictaClone also transcribed text
entered into the project's live development session. The broader
denial/revocation, offline, lifecycle, insertion-mode, application, and Intel
portions of the acceptance matrix remain. Developer ID signing and Apple
notarization additionally require the release owner's distribution certificate
and notary profile.

## Supported target

- macOS 14 Sonoma or newer.
- Apple Silicon (`osx-arm64`) and Intel (`osx-x64`).
- .NET 10 self-contained application bundles; end users do not install .NET.
- Avalonia 12.1.0 desktop UI.
- Whisper.net 1.9.1 with the Core ML runtime package on macOS.

Avalonia 12 requires .NET 10. Its current platform policy lists macOS 26 as
Tier 1 and macOS 14 and 15 as Tier 2. The app bundle deliberately sets macOS 14
as its deployment floor so it follows the .NET 10 supported-OS range while
remaining usable on the two earlier supported macOS releases.

## Architecture

```text
Shared .NET 10
├── DictaClone.Core             workflow contracts, settings, hotkeys
├── DictaClone.Desktop          reusable presentation/controller behavior
├── DictaClone.Speech           Whisper model management and transcription
├── DictaClone.Text             deterministic text processing
└── DictaClone.Infrastructure   settings, history, diagnostics, Smart Edit

Windows                             macOS
├── DictaClone.App (WPF)            ├── DictaClone.Mac.App (Avalonia/AppKit)
├── DictaClone.Windows (Win32)      └── DictaClone.Mac (native Apple adapters)
└── DictaClone.Audio (NAudio)
```

Windows-only types must not enter `DictaClone.Core` or `DictaClone.Desktop`.
Platform target identifiers remain opaque to shared code, and all mutations
flow through the existing contracts.

## Windows-to-macOS implementation map

| Capability | Windows | Implemented macOS replacement |
| --- | --- | --- |
| Settings/history UI | WPF | Avalonia Fluent windows |
| Tray/menu UI | notification-area icon | Avalonia `TrayIcon`/native menu |
| Floating status | nonactivating WPF window | nonactivating Avalonia window |
| Global hotkeys | low-level Win32 hook | `CGEventTap` on a background CFRunLoop |
| Microphone | NAudio/WASAPI | Core Audio Audio Queue input |
| Device enumeration | MMDevice | Core Audio AudioObject APIs |
| Foreground target | Win32 window/process identity | `NSWorkspace` PID plus focused `AXUIElement` identity |
| Selected text | UI Automation | Accessibility selected-text attribute |
| Paste Mode | Windows clipboard and `SendInput` | full-format `NSPasteboard` snapshot plus `CGEvent` Command-V |
| Typing Mode | Unicode `SendInput` | Unicode `CGEvent` keyboard events by grapheme |
| API-key storage | Windows Credential Manager | login Keychain via `/usr/bin/security` |
| Start at login | current-user Run key | per-user LaunchAgent |
| Packaging | EXE/portable ZIP | signed and notarized `.app` ZIP |

The macOS implementation uses direct native interop rather than binding an
additional AppKit package. This keeps the native boundary small and allows the
adapter tests to substitute in-memory pasteboard, target, keyboard, audio, and
process collaborators. A minimal Objective-C shim, compiled for the target
architecture by Xcode during packaging, bridges AVFoundation's block-based
microphone-consent callback into the managed permission service.

## Permissions and privacy

DictaClone exposes three macOS permission states separately:

- **Microphone** is required for recording.
- **Accessibility** is required to identify the focused control, capture an
  exact selection, and validate the insertion target.
- **Input Monitoring** is reported for diagnostics but is not separately
  required once Accessibility is authorized. Accessibility grants the active
  event tap the listening and posting access DictaClone needs.

The settings window refreshes each state when it becomes active. Its Microphone
button makes the AVFoundation consent request that registers DictaClone with
macOS; if access was already denied, it opens **System Settings > Privacy &
Security > Microphone** for manual recovery. The Accessibility and Input
Monitoring buttons use the corresponding Apple request APIs before opening
their matching system controls. Permission
failures are actionable platform errors, not generic audio or insertion
failures. Prompts are requested only from explicit settings actions; the
application does not loop permission requests.

Ordinary dictation remains local. Models and settings are stored under
`~/Library/Application Support/DictaClone`. Optional transcript history is off
by default. Smart Edit is off by default, makes a network request only after
explicit configuration, and stores its API key in the user's login Keychain as
service `com.dictaclone.desktop`.

## Input and insertion safety

The shared shortcut model retains its serialized modifier names for settings
compatibility. The macOS UI renders them as Control, Option, Shift, and Command;
the existing `Windows` modifier bit means Command on macOS. Defaults are:

| Action | Shortcut | Activation |
| --- | --- | --- |
| Dictation | Control+Shift+Space | hold |
| Typing Mode | Control+Option+Space | hold |
| Cancel | Control+Option+Escape | press |
| Smart Edit | Option+Shift+Space | disabled until configured |

The event tap ignores DictaClone's own synthetic keyboard events and suppresses
the primary key of a recognized shortcut so it does not leak into the target.

Paste Mode captures the target before recording and revalidates it before
insertion. It snapshots every pasteboard item and type as binary data, writes
the result, sends Command-V, and restores the snapshot only while DictaClone
still owns the pasteboard transaction. A concurrent user or application change
is never overwritten. Temporary pasteboard failures use bounded retries.

Typing Mode never reads or changes the pasteboard. It sends Unicode by composed
grapheme so emoji and combining characters are not split, maps newlines and tabs
to their keys, honors the configured delay, and reports blocked input as a
permission failure.

## Audio and transcription

`MacAudioCaptureService` uses an Audio Queue configured for 16 kHz, mono,
signed 16-bit PCM, matching the shared transcription contract. It implements
level reporting, cancellation, silence metrics, maximum duration, buffer
cleanup, and optional device selection. The device service always offers
**Follow system default microphone**, then enumerates physical input devices
through Core Audio.

The macOS app references `Whisper.net.Runtime.CoreML`. Whisper.net selects the
available native runtime and can use Core ML acceleration on supported Macs.
The existing verified model downloader and hash checks are shared unchanged.

## User interface and lifecycle

The app is an agent-style menu-bar application (`LSUIElement`) and normally has
no Dock icon. The menu exposes dictation state, settings, history, copy-last,
and exit. Settings include microphone/model/language, insertion mode and delay,
all shortcuts, vocabulary, expansions, domain, history, start at login,
permissions, import/export, diagnostics/support bundle creation, and Smart Edit.

A per-user single-instance guard prevents two copies from competing for the
event tap or pasteboard. Enabling start at login writes
`~/Library/LaunchAgents/com.dictaclone.desktop.plist`; disabling it removes only
that registration. Shutdown stops the hotkey loop and active workflow before
disposing native and persistence resources.

## Build and developer checks

Install the prerequisites once:

```zsh
brew install --cask dotnet-sdk
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
dotnet --version   # must resolve 10.0.302 with this repository's global.json
xcodebuild -version
```

Then restore and run the cross-platform and macOS suites:

```zsh
./scripts/macos/test.sh
```

Inspect permission and microphone discovery without launching the full shell:

```zsh
dotnet run --project tools/DictaClone.MacProbe
dotnet run --project tools/DictaClone.MacProbe -- --capture-seconds 3
```

The first command does not request permissions. The capture command exercises
the real microphone path and may trigger the system consent flow.

Build and verify a development bundle for the current or named architecture:

```zsh
./scripts/macos/build-app.sh
./scripts/macos/build-app.sh osx-x64
./scripts/macos/verify-app.sh artifacts/macos/0.1.2/osx-arm64/DictaClone.app
```

With no identity configured, `build-app.sh` uses an ad-hoc signature for local
testing. Because an ad-hoc signature's designated requirement is its changing
code hash, macOS privacy toggles from an earlier development build may look
enabled while applying only to that older build. Reset and grant the three
permissions once after creating the final qualification bundle, then do not
rebuild it during the manual matrix. Direct distribution requires a
**Developer ID Application** identity
and an Apple notary profile:

For stable local TCC qualification, an Apple Development identity can be used
without a secure timestamp:

```zsh
export DICTACLONE_CODESIGN_IDENTITY='Apple Development: Example (TEAMID)'
./scripts/macos/build-app.sh osx-arm64
```

```zsh
export DICTACLONE_CODESIGN_IDENTITY='Developer ID Application: Example (TEAMID)'
xcrun notarytool store-credentials DictaCloneNotary
export DICTACLONE_NOTARY_PROFILE='DictaCloneNotary'
./scripts/macos/build-app.sh osx-arm64
./scripts/macos/notarize-app.sh artifacts/macos/0.1.2/osx-arm64/DictaClone.app
./scripts/macos/verify-app.sh artifacts/macos/0.1.2/osx-arm64/DictaClone.app
```

`sign-app.sh` signs nested Mach-O files from the inside out and intentionally
does not use `codesign --deep`. The final app is signed with the hardened
runtime and a minimal entitlement set: CoreCLR JIT execution and microphone
input. It does not carry `get-task-allow`, allow arbitrary unsigned executable
memory, disable library validation, or enable App Sandbox. `notarize-app.sh`
submits with `notarytool`, waits,
staples the ticket, and validates it. The release command builds both
architectures and creates a combined checksum file. When both release
environment variables shown above are set, it also notarizes, staples, and
repackages each architecture before calculating the final checksums:

```zsh
./scripts/macos/new-release.sh
```

## Final acceptance matrix

Automated adapter coverage includes shortcut mapping, target-change rejection,
selection identity, full-format pasteboard restoration, concurrent clipboard
change preservation, grapheme typing, audio lifecycle, LaunchAgent content,
and Keychain command construction. Release acceptance additionally requires
the following manual checks on a normal signed build:

- Fresh user profile installation and all three deny/grant/revoke permission
  paths.
- Hold/release/repeat/cancel shortcut behavior and collision handling.
- Microphone capture, device changes, cancellation, silence, and first model
  download followed by an offline restart.
- Paste and Typing Mode in TextEdit, browser fields, Terminal, and common rich
  text editors, including emoji, combining text, multiline text, and tabs.
- Changed-target and changed-selection rejection.
- Clipboard preservation, including rich formats and a concurrent external
  clipboard write.
- Smart Edit disabled/offline behavior and an explicitly authorized provider
  test.
- Start-at-login enable/disable, single-instance handling, clean shutdown, data
  retention, and full removal.
- Apple Silicon and Intel launch, signature, Gatekeeper, notarization, and
  stapling checks.

Follow `MACOS_CLEAN_ROOM_INSTALLATION.md` for the end-user acceptance run.

### Interactive qualification recorded August 11, 2026

The Apple Silicon qualification build used the stable designated requirement
from an Apple Development certificate, the hardened runtime, and the packaged
JIT and audio-input entitlements. After one TCC reset for
`com.dictaclone.desktop`, macOS reported Microphone and Accessibility as
authorized. Input Monitoring remained optional. Holding and releasing the
default shortcut transcribed speech locally and inserted the result into
TextEdit and native GNU Emacs without the earlier repeated permission-failure
sound.

This accepts the primary end-to-end path, not the entire matrix above. In
particular, Typing Mode, cancellation, target changes, permission revocation,
clipboard concurrency, browser and Terminal fields, rich-text behavior,
offline restart, login-item behavior, Intel hardware, Developer ID, Gatekeeper,
and notarization still need their listed acceptance runs.

## Milestone record

| Milestone | State | Evidence or remaining gate |
| --- | --- | --- |
| 0: boundary and toolchain decision | Implemented | Avalonia/native adapter architecture and pinned packages |
| 1: shared extraction | Implemented | `DictaClone.Desktop`, shared input/error contracts, cross-platform paths |
| 2: shell and lifecycle | Implemented | menu-bar app, settings, history, overlay, first run, single instance |
| 3: hotkeys, permissions, foreground | Implemented; primary path accepted on Apple Silicon | stable signed bundle authorized for Microphone and Accessibility; global hold/release shortcut worked; denial/revocation and target-change matrix remains |
| 4: audio and local speech | Implemented; primary path accepted on Apple Silicon | live Core Audio capture and local transcription succeeded; device, silence, cancellation, offline-restart, and Intel checks remain |
| 5: safe insertion and selected text | Implemented; TextEdit and Emacs Paste Mode accepted | transcribed text inserted into the original TextEdit and native GNU Emacs targets; Typing Mode, other targets, clipboard concurrency, and selected-text matrix remains |
| 6: persistence and Smart Edit | Implemented | shared stores, LaunchAgent, Keychain, diagnostics/support bundle |
| 7: packaging and release | Implemented; development-signed Apple Silicon path accepted; distribution acceptance pending | automated tests and dual-RID packaged smoke checks pass; an Apple-Development-signed arm64 bundle installed and launched successfully; Developer ID, Gatekeeper, notarization, Intel hardware, and the remaining manual matrix remain |

No Windows behavior is intentionally removed by this port. The Windows WPF app
now references the extracted desktop presentation assembly, while its Win32 and
NAudio implementations stay in place.
