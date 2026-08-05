# Milestone 7 status — Packaging and release qualification

Status: Implementation and automated qualification complete. Manual acceptance
is deferred until a new Windows 11 x64 laptop is available.

Last updated: 2026-08-05

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

## Qualification artifacts

The current review build is under `artifacts\release\0.1.0`:

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `DictaClone-0.1.0-win-x64-portable.zip` | 75,455,316 | `df1b6cb0dcf858dd128b9cfc58d7b4d83c84bd8bc2484d3c248d079929076ee8` |
| `DictaClone-0.1.0-win-x64-setup.exe` | 56,276,778 | `93605be6c806f51e3912ff526a577c06042f5a4da8ba4e627b329a29246638ab` |

This is an unsigned, pre-release qualification build. Windows SmartScreen may
warn before launch; compare the artifact with `SHA256SUMS.txt`. Because the
milestone is intentionally uncommitted during review, its manifest records
`sourceDirty: true` and commit `59186b2`. Rebuild release artifacts from the
clean accepted commit before distribution.

## Automated evidence

- The clean Release build completed with zero warnings and zero errors.
- All 273 non-desktop, offline unit/integration/regression cases passed.
- `DictaClone.Core` line coverage is **94.61%** (614/649), above the required
  90% gate.
- All three standard real-desktop E2E cases passed with direct Windows desktop
  access. A sandboxed attempt lost foreground focus and failed one paste; the
  isolated case and complete desktop subset then passed outside that sandbox.
- Artifact validation confirmed version 0.1.0, self-contained `win-x64`
  architecture, required legal/release files, checksum integrity, and a bounded
  portable-process smoke test.
- The isolated installer lifecycle passed non-admin per-user install, explicit
  startup consent, synthetic 0.0.1-to-0.1.0 upgrade, settings/startup retention,
  repair, uninstall, and retained-user-data checks.
- Post-test inspection found no temporary installation directory, DictaClone
  startup value, or Installed Apps entry.

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
