# DictaClone

DictaClone is a local-first voice-to-text application under development for
Windows 11 x64. Its target workflow is simple: hold a global shortcut, speak,
release it, and insert the transcription wherever the text cursor is active.

The project will provide local Whisper transcription, a Windows tray interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts will
remain local unless the user explicitly configures a cloud provider.

## Project status

Milestones 0 through 6 are implemented and accepted. The tray app captures the
microphone, transcribes locally, and inserts into the original foreground target
through sequence-safe Paste Mode or normally clipboard-free Typing Mode. Native
GNU Emacs uses a clipboard-preserving `Ctrl+Y` compatibility path because its
standard key map does not paste with `Ctrl+V` and did not accept DictaClone's
synthetic character stream. Milestone 5 persistence, recovery, knowledge,
diagnostics, and desktop polish is accepted. Milestone 6 Smart Edit and
selected-text editing is accepted. Its cloud path is off by default, keeps
provider keys in Windows Credential Manager, and revalidates the foreground
target and exact selection before replacement. Milestone 7 packaging is
implemented and its automated release qualification passes. Manual installer,
portable, offline-restart, and uninstall acceptance is deferred until testing
can be performed on a new Windows 11 x64 laptop.

The default dictation trigger is `Ctrl+Win+Space`; the primary key prevents
Windows' `Ctrl+Win+Arrow` virtual-desktop shortcuts from starting a recording.
Milestone 4 adds focus-safe clipboard paste and delayed typing into the
original foreground application.

See the [implementation plan](docs/IMPLEMENTATION_PLAN.md) for the proposed
architecture, milestones, unit and regression tests, privacy constraints, and
release criteria.

See the [Milestone 0 results](docs/MILESTONE_0_RESULTS.md) for the target-machine
measurements and model decision.

See the [Milestone 1 results](docs/MILESTONE_1_RESULTS.md) for the workflow,
settings, text pipeline, test, and coverage results.

See the [Milestone 2 results](docs/MILESTONE_2_STATUS.md) for the implemented UI
and hook scope, automated evidence, smoke-test diagnosis, and manual acceptance
results.

See the [Milestone 3 status](docs/MILESTONE_3_STATUS.md) for audio/transcription
scope, model/corpus evidence, live deadlock diagnosis, and acceptance notes.

See the [Milestone 4 status](docs/MILESTONE_4_STATUS.md) for cursor-insertion
scope, test gates, and current implementation progress.

See the [Milestone 5 status](docs/MILESTONE_5_STATUS.md) for persistence,
knowledge, recovery, diagnostics, and desktop-polish progress.

See the [Milestone 6 status](docs/MILESTONE_6_STATUS.md) for Smart Edit privacy,
provider, selected-text safety, automated evidence, and manual-review steps.

See the [Milestone 7 status](docs/MILESTONE_7_STATUS.md) for packaging behavior,
release artifacts and checksums, automated qualification, and the pending
manual-review steps.

See the [clean-room installation and use guide](docs/CLEAN_ROOM_INSTALLATION.md)
for checksum verification, installer and portable setup, first-run model
download, offline dictation, and removal on Windows 11 x64.

See the [macOS porting guide](docs/MACOS_PORTING_GUIDE.md) for the planned
cross-platform boundaries and native macOS replacements.

## Initial target

- Windows 11 x64
- C# and .NET 10
- WPF desktop application
- Local Whisper speech recognition
- Self-contained, non-administrator installation

## Development

Prerequisites for source builds are Windows 11 x64, Windows PowerShell 5.1 or
later, Git, and internet access for the initial SDK and NuGet package downloads.
Release artifact generation additionally requires Inno Setup 6.7.3; a
current-user installation is sufficient. `ISCC.exe` may be on `PATH`, supplied through
`DICTACLONE_ISCC`, or passed with `-InnoCompilerPath`.

Install the pinned repository-local SDK, then run a clean restore, build, and
test:

```powershell
.\scripts\Install-DotNet.ps1
.\scripts\Test.ps1 -Clean
```

The build and test scripts perform their locked restore before cleaning, so a
retry can recover after an interrupted initial download without deleting
generated directories manually.

Run the enforced Core coverage gate with:

```powershell
.\scripts\Test.ps1 -Clean -Coverage
```

Optional Milestone 0 hardware checks:

```powershell
.\scripts\Invoke-AudioProbe.ps1
.\scripts\Download-Models.ps1
.\scripts\Invoke-Milestone0Benchmark.ps1
```

Run the Milestone 2 process smoke test and launch its manual review harness with:

```powershell
.\scripts\Invoke-Milestone2SmokeTest.ps1 -NoBuild
.\scripts\Invoke-Milestone2ManualTest.ps1 -Target TestTarget -NoBuild
```

Run the real local speech regression and Milestone 3 manual review with:

```powershell
.\scripts\Invoke-Milestone3Regression.ps1 -NoBuild
.\scripts\Invoke-Milestone3ManualTest.ps1 -NoBuild
```

Run the focused Milestone 4 insertion regression and launch its manual review
target with:

```powershell
.\scripts\Invoke-Milestone4Regression.ps1 -NoBuild
.\scripts\Invoke-Milestone4ManualTest.ps1 -Target TestTarget -NoBuild
```

Run the offline Milestone 6 regression and launch its Smart Edit manual review
with:

```powershell
.\scripts\Invoke-Milestone6Regression.ps1 -NoBuild
.\scripts\Invoke-Milestone6ManualTest.ps1 -NoBuild
```

Build and validate the Windows x64 release, then print the complete Milestone 7
manual review with:

```powershell
.\scripts\Invoke-Milestone7Regression.ps1
.\scripts\Invoke-Milestone7ManualTest.ps1
```

For clean-room testing, build from a clean accepted commit on the build machine
and transfer the complete `artifacts\release\<version>` directory, including
the installer, portable ZIP, `CLEAN_ROOM_INSTALLATION.md`, manifest, and
`SHA256SUMS.txt`. Follow the copied guide for installation and ordinary use. For
the complete acceptance review, clone the same commit on the clean test machine
and either place that directory at the same repository-relative location or
pass its full path explicitly:

```powershell
.\scripts\Invoke-Milestone7ManualTest.ps1 `
    -ReleaseDirectory 'D:\DictaClone-0.1.0'
```

Leave the keyboard, mouse, clipboard, and foreground window untouched while
the manual guide's desktop stress command runs. The installer and portable
application are self-contained; the SDK and Inno Setup are needed only when
building or running source-based automated tests, not when exercising those
release artifacts.

Release creation refuses a dirty worktree by default. The regression command
uses a dirty qualification build so an uncommitted milestone can be reviewed;
rebuild distribution artifacts from a clean accepted commit.

The optional live-provider contract test is excluded from every normal test
command. It runs only when an API key is present and the caller supplies the
script's explicit paid-call switch.

Speech models, the local SDK, NuGet caches, and generated benchmark output are
excluded from Git.

## License

A project license has not yet been selected.
