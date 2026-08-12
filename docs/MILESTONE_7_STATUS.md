# Milestone 7 status — Packaging and release qualification

Status: Implementation and automated qualification complete. Manual acceptance
is deferred until a new Windows 11 x64 laptop is available.

Last updated: 2026-08-12

## Delivered

- A reproducible, self-contained `win-x64` publish that does not require a
  separately installed .NET runtime.
- A versioned portable ZIP and a non-administrator, per-user Inno Setup
  installer. The installer uses a stable application identity for repair and
  upgrade and installs under `%LocalAppData%\Programs\DictaClone`.
- Start-with-Windows remains an application setting and is never enabled by the
  installer. Uninstall always removes DictaClone's startup registration.
- Interactive uninstall offers an explicit choice to retain or delete settings,
  downloaded models, history, and diagnostics. Silent uninstall retains user
  data unless `/PURGEUSERDATA` is supplied.
- Product/version metadata, third-party notices, speech-model license and hash
  documentation, release notes, and rollback instructions.
- Repeatable PowerShell commands for publishing, installer compilation,
  checksums, artifact inspection, installer lifecycle testing, desktop stress,
  and the complete milestone regression.
- Additional clipboard-format, false-empty snapshot, exact-publish, ownership,
  and contention regressions. A deterministic 100-dictation controller stress
  and 50 clipboard-transaction stress are part of the automated suite.
- The 0.1.2 reliability update lowers the default silence threshold while
  preserving customized values, extends bounded clipboard retries, and reports
  empty capture, quiet audio, and unrecognized speech separately.

## Qualification artifacts

The current review build is under `artifacts\release\0.1.2`. Its
`release-manifest.json` records the exact source commit, clean-worktree state,
file sizes, and per-artifact SHA-256 values; `SHA256SUMS.txt` covers every file
distributed from the release directory. These generated values are the
authoritative artifact record and avoid duplicating checksums in maintained
documentation.

This is an unsigned, pre-release qualification build. Windows SmartScreen may
warn before launch; compare every artifact with `SHA256SUMS.txt`.

Generated release artifacts are excluded from Git and are not present in a
fresh clone. For clean-room review, transfer the complete
`artifacts\release\<version>` directory from the build machine. Its
`CLEAN_ROOM_INSTALLATION.md` gives checksum, installation, first-use, offline,
portable, and removal instructions. The manual-review script accepts the
directory's location through `-ReleaseDirectory`. Building the artifacts from
source requires Inno Setup 6.7.3; running the self-contained installer and
portable application does not.

## Automated evidence

- The clean Release build completed with zero warnings and zero errors.
- All 318 non-desktop, offline unit/integration/regression cases passed,
  including all 28 macOS adapter tests on Windows.
- `DictaClone.Core` line coverage is **94.99%** (720/758), above the required
  90% gate.
- All three standard real-desktop E2E cases passed with direct Windows desktop
  access.
- Artifact validation confirmed version 0.1.2, self-contained `win-x64`
  architecture, required legal/release files, checksum integrity, and a bounded
  portable-process smoke test.
- The isolated installer lifecycle passed non-admin per-user install, explicit
  startup consent, synthetic 0.0.1-to-0.1.0 upgrade, settings/startup retention,
  repair, uninstall, and retained-user-data checks.
- Post-test inspection found no temporary installation directory, DictaClone
  startup value, or Installed Apps entry.

Live 0.1.2 acceptance on 2026-08-12 confirmed normal and deliberately garbled
speech in Notepad and normal dictation into Codex CLI through Windows Terminal.
Neither the prior clipboard-contention failure nor the false no-speech result
recurred.

The optional 50-cycle **real desktop** stress remains a manual review item. It
cannot be made deterministic while another program changes the foreground
window or clipboard. The test prints a hands-off warning and is excluded from
ordinary automated runs; its underlying 50 clipboard transactions and 100
dictation-controller cycles are covered deterministically without desktop
contention.

## Manual review

Run this from PowerShell at the repository root:

```powershell
.\scripts\Invoke-Milestone7ManualTest.ps1
```

The command does not install or launch anything. It prints exact numbered steps
and complete control names for:

1. the hands-off 50-cycle real-desktop stress;
2. checksum verification and portable first-run/model/dictation;
3. non-admin per-user installation and initial startup-setting state;
4. installed offline dictation after exit and restart;
5. explicit startup consent and installer repair;
6. uninstall with deliberate user-data retention; and
7. optional reinstall/uninstall with complete user-data purge.

Report success or the numbered step and exact displayed message. At the user's
direction on 2026-08-05, this implementation checkpoint may be committed while
manual acceptance remains explicitly deferred. It is not yet a manually
accepted release.
