# DictaClone 0.1.0 release notes

Release status: pre-release qualification build for Windows 11 x64.

## Included

- Global hold-to-talk dictation with configurable shortcuts.
- Local Whisper transcription with verified `base.en` and `small.en` model
  downloads.
- Sequence-safe Paste Mode, clipboard-free Typing Mode, and GNU Emacs paste
  compatibility.
- Durable settings, vocabulary, expansions, optional transcript history,
  privacy-safe diagnostics, and settings recovery.
- Optional selected-text Smart Edit through an explicitly configured provider.
  Smart Edit is disabled by default and its API key is stored in Windows
  Credential Manager.
- A portable self-contained `win-x64` archive and a non-administrator,
  per-user installer.

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
