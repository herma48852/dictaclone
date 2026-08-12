# DictaClone 0.1.2 release notes

Release status: pre-release qualification build for Windows 11 x64. A macOS
14+ port is implemented; Apple-silicon and Intel development bundles pass
build, signature, and packaged-app smoke checks, and its automated suites pass
in a normal Terminal session. An Apple-Development-signed Apple Silicon bundle
also passes the primary live permission and dictation workflow, with Paste Mode
in TextEdit, native GNU Emacs, a browser field, and Terminal plus a successful
Typing Mode insertion. The broader interactive matrix, Developer ID, and
notarization qualification remain. A post-download offline restart and TextEdit
dictation also succeeded, and forced repeated bundle opens left one process.
The generated per-user LaunchAgent also passed validation and launched the
installed application when bootstrapped; disabling the setting removed it
cleanly.
Changing the focused application during dictation also correctly rejected
insertion into both the original and replacement targets.
Typing Mode also preserved an exact clipboard sentinel while inserting text.
Paste Mode restored an exact clipboard sentinel after insertion as well.

## Reliability improvements

- Quiet speech is less likely to be rejected before transcription. The default
  silence threshold is now `0.006`; schema-5 migration updates the former
  `0.012` default while preserving thresholds the user customized.
- Paste Mode now retries transient clipboard contention for approximately 1.1
  seconds with bounded incremental backoff. Copying the last result and copying
  transcript-history entries use the same resilient behavior on Windows and
  macOS without blocking the macOS menu-bar UI.
- Capture failures now distinguish an empty microphone stream, audio below the
  silence threshold, and speech that Whisper captured but could not recognize.
  These messages remain privacy-safe and contain no audio or transcript text.
- Automated regressions cover quiet speech that the former default rejected,
  extended clipboard contention on both platforms, schema migration, and the
  distinct capture failure messages. The complete macOS suite passed in a
  normal Terminal session after the v0.1.2 rebase and parity changes.

## Live compatibility confirmation

- Normal and deliberately garbled speech were transcribed and inserted in
  Notepad without reproducing the previous clipboard failure.
- Normal dictation was transcribed and inserted into Codex CLI through Windows
  Terminal without reproducing the previous false no-speech result.

## Included

- Global hold-to-talk dictation with configurable shortcuts.
- Local Whisper transcription with verified `base.en` and `small.en` model
  downloads.
- Sequence-safe Paste Mode, clipboard-free Typing Mode, and GNU Emacs paste
  compatibility.
- Foreground-aware paste shortcuts: `Ctrl+Shift+V` for Windows Terminal and
  Codex CLI, and clipboard-preserving `Ctrl+Y` for native GNU Emacs.
- A Win-free `Ctrl+Shift+Space` dictation default whose primary key is consumed
  by DictaClone, avoiding leaked shortcut commands in Emacs. Exact legacy
  defaults migrate automatically while customized hotkeys remain unchanged.
- Durable settings, vocabulary, expansions, optional transcript history,
  privacy-safe diagnostics, and settings recovery.
- Optional selected-text Smart Edit through an explicitly configured provider.
  Smart Edit is disabled by default and its API key is stored in Windows
  Credential Manager.
- A portable self-contained `win-x64` archive and a non-administrator,
  per-user installer.
- A clean-room guide covering checksum verification, installation, first use,
  offline dictation, portable use, and removal.
- A .NET 10/Avalonia macOS menu-bar app with native Core Audio, Core Graphics,
  Accessibility, pasteboard, Keychain, and LaunchAgent adapters for Apple
  Silicon and Intel.
- macOS adapter tests, a native permission/device probe, a dedicated clean-room
  guide, and scripted app-bundle creation, signing, notarization, verification,
  dual-architecture archives, and checksums.

## Installation behavior

- The installer places binaries under
  `%LocalAppData%\Programs\DictaClone` and creates current-user Start menu
  shortcuts.
- The installer does not enable Start with Windows. That registration is made
  only after the user selects **Start DictaClone when I sign in to Windows** in
  DictaClone settings and applies the setting.
- Uninstall removes application binaries, shortcuts, and DictaClone's startup
  registration. By default it preserves settings, downloaded models, and
  diagnostics under `%LocalAppData%\DictaClone`; interactive uninstall offers
  an explicit choice to delete that data.

## Known release constraints

- Release artifacts are not yet Authenticode-signed. Windows SmartScreen may
  warn before launch; verify `SHA256SUMS.txt` before running an artifact.
- Dictation into elevated applications is blocked by the normal Windows input
  integrity boundary unless DictaClone is also elevated. DictaClone is not
  elevated by default.
- Smart Edit requires network access and a separately supplied provider API
  key. Ordinary dictation stays local and works offline after model download.
- The macOS app is not yet a public release artifact. Its development-signed
  Apple Silicon primary insertion workflow is qualified, but the remaining
  interactive and Intel-hardware matrix still requires completion together
  with a Developer ID-signed and Apple-notarized bundle. The .NET 10/Xcode
  development builds and smoke checks pass for both supported architectures.
