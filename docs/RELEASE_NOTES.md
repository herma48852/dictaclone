# DictaClone 0.1.3 release notes

Release status: macOS 14+ compatibility prerelease. This patch fixes foreground
target capture in Chromium-based browser editors. The macOS archives are signed
with the qualification Mac's Apple Development identity for stable local
permissions; they are not Developer ID signed or Apple notarized. Windows
packages are unchanged and remain available from the 0.1.2 prerelease.

## Fixed

- Dictation now inserts into the Google Gemini and ChatGPT message editors in
  Google Chrome instead of reporting `No focused app is available for
  insertion`.
- Foreground capture now starts from the frontmost application's process ID and
  creates its Accessibility element directly. This avoids relying on the
  system-wide `AXFocusedApplication` query, which can transiently fail even
  while Accessibility permission is granted.
- Focused-control identity is preferred when available. Chromium windows that
  do not expose a focused control fall back to a stable focused-window or main-
  window identity while retaining the existing change-target safety check.
- Transient Accessibility `cannot complete` responses receive a short, bounded
  retry off the hotkey/event-tap thread.
- Revoked Accessibility permission now produces an explicit permission error
  instead of the generic missing-foreground-target message.
- A privacy-safe `--foreground-probe-delay` support command reports only
  Accessibility error codes, process IDs, and opaque element hashes. It never
  reads or prints page, field, or transcript text.

## Verification

- The macOS adapter suite covers focused-control changes, focused-window
  fallback, missing targets, and revoked Accessibility permission.
- The complete macOS test script passes on Apple Silicon.
- Self-contained Apple Silicon and Intel archives build successfully, carry a
  valid strict code signature, and pass the packaged-app smoke test.
- The signed Apple Silicon app was installed over 0.1.2 and retained its
  Microphone and Accessibility permissions.
- Live dictation was accepted in both Google Gemini and ChatGPT message editors
  in Google Chrome on August 14, 2026.

## Assets

- `DictaClone-0.1.3-osx-arm64.zip` for Apple Silicon Macs.
- `DictaClone-0.1.3-osx-x64.zip` for Intel Macs.
- Per-archive checksum files and the combined `SHA256SUMS.txt`.
- `MACOS_CLEAN_ROOM_INSTALLATION.md` for verification, installation,
  permissions, and removal.

## Known constraints

- These are development-signed qualification archives for local testing. They
  are not suitable for general direct distribution because they are not signed
  with a Developer ID Application certificate and do not include a stapled
  Apple notarization ticket.
- The Intel archive is cross-built and passes signature and packaged smoke
  verification under Rosetta on the qualification Mac; native Intel hardware
  remains untested.
- Smart Edit requires network access and a separately supplied provider API
  key. Ordinary dictation stays local and works offline after model download.
