# DictaClone

DictaClone is a local-first voice-to-text application for Windows 11 x64 with a
macOS 14+ port now in qualification. Its workflow is simple: hold a global
shortcut, speak, release it, and insert the transcription wherever the text
cursor is active.

The project provides local Whisper transcription, a tray or menu-bar interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts
remain local unless the user explicitly configures a cloud provider.

## Project status

Windows Milestones 0 through 6 are implemented and accepted. The tray app captures the
microphone, transcribes locally, and inserts into the original foreground target
through sequence-safe Paste Mode or normally clipboard-free Typing Mode.
Windows Terminal targets, including Codex CLI, use the terminal's
`Ctrl+Shift+V` paste path. Native GNU Emacs uses a clipboard-preserving
`Ctrl+Y` compatibility path because its standard key map does not paste with
`Ctrl+V` and did not accept DictaClone's synthetic character stream. Milestone
5 persistence, recovery, knowledge, diagnostics, and desktop polish is
accepted. Milestone 6 Smart Edit and
selected-text editing is accepted. Its cloud path is off by default, keeps
provider keys in Windows Credential Manager, and revalidates the foreground
target and exact selection before replacement. Milestone 7 packaging is
implemented and its automated release qualification passes. Manual installer,
portable, offline-restart, and uninstall acceptance is deferred until testing
can be performed on a new Windows 11 x64 laptop.

The default dictation trigger is `Ctrl+Shift+Space`. DictaClone consumes the
recognized shortcut's primary key so it does not execute a command in the
foreground application. Existing exact default `Ctrl+Win+Space` bindings are
migrated automatically; customized bindings are preserved. Milestone 4 adds
focus-safe clipboard paste and delayed typing into the original foreground
application.

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

The macOS implementation now covers the complete planned engineering sequence:
shared presentation/input extraction, an Avalonia menu-bar shell, native global
hotkeys and permissions, Core Audio capture, Core ML-enabled Whisper,
foreground and exact-selection tracking, sequence-safe paste and grapheme
typing, Keychain and LaunchAgent persistence, adapter tests, and dual-architecture
sign/notarize/release scripts. With .NET SDK 10.0.302 and Xcode 26.6, both
Apple-silicon and Intel self-contained bundles now publish, sign, and pass their
packaged-app smoke checks, and all cross-platform/macOS automated test suites
pass in a normal Terminal session. On August 11, 2026, an
Apple-Development-signed Apple Silicon bundle also passed the primary live
workflow: Microphone and Accessibility authorization, global shortcut capture,
local transcription, and Paste Mode insertion into TextEdit, native GNU Emacs,
browser fields, and Terminal. Typing Mode also completed a live insertion.
An offline restart and subsequent local TextEdit dictation also succeeded.
Two forced bundle opens also left exactly one running process.
The validated per-user LaunchAgent successfully launched the installed app.
Disabling start-at-login then removed the registration cleanly.
Changing from TextEdit to a browser during dictation correctly inserted into
neither target.
Typing Mode inserted text without changing an exact clipboard sentinel.
Paste Mode also restored an exact clipboard sentinel after insertion.
Final qualification still requires the rest of the interactive
clean-room matrix, a Developer ID distribution identity, and Apple
notarization.

See the [Windows clean-room installation and use guide](docs/CLEAN_ROOM_INSTALLATION.md)
for downloading the qualified GitHub release assets, checksum verification,
installer and portable setup, first-run model download, offline dictation, and
removal on Windows 11 x64.

See the [macOS porting guide](docs/MACOS_PORTING_GUIDE.md) for the implemented
architecture, milestone record, developer commands, and remaining qualification
gates. The [macOS clean-room guide](docs/MACOS_CLEAN_ROOM_INSTALLATION.md) covers
installation, permissions, offline use, acceptance, and removal.

## Platforms

- Windows 11 x64: WPF/Win32/NAudio, implemented and automatically qualified.
- macOS 14+ on Apple Silicon and Intel: Avalonia/native Apple APIs, implemented;
  automated bundles and the development-signed Apple Silicon primary insertion
  workflow are qualified, with the broader interactive and distribution matrix
  pending.
- C# 14 and .NET 10, local Whisper speech recognition, and self-contained
  end-user packages on both platforms.

## Windows development

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

For clean-room testing of version 0.1.2, open the
[GitHub prerelease](https://github.com/herma48852/dictaclone/releases/tag/v0.1.2),
expand **Assets**, and download the installer, the specifically named portable
ZIP, `CLEAN_ROOM_INSTALLATION.md`, `release-manifest.json`, and
`SHA256SUMS.txt` into one folder. Do not use GitHub's automatically generated
**Source code (zip)** as the portable application. Follow the downloaded guide
for checksum verification, installation, and ordinary use.

As an offline alternative, transfer the complete
`artifacts\release\<version>` directory from the trusted build machine. For the
complete acceptance review, clone tag `v0.1.2` on the clean test machine and
pass the full path of the downloaded or transferred release folder explicitly:

```powershell
.\scripts\Invoke-Milestone7ManualTest.ps1 `
    -ReleaseDirectory 'D:\DictaClone-0.1.2'
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

## macOS development

macOS source builds require macOS 14 or newer, the repository-pinned .NET SDK
10.0.302, Git, and full Xcode selected with `xcode-select`. Homebrew is only a
convenient way to install the SDK; end users need none of these tools.

```zsh
brew install --cask dotnet-sdk
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
./scripts/macos/test.sh
dotnet run --project tools/DictaClone.MacProbe
./scripts/macos/build-app.sh
```

The default build is self-contained for the host architecture and ad-hoc signed
for local testing. Set `DICTACLONE_CODESIGN_IDENTITY` to a Developer ID
Application identity for distribution. Set `DICTACLONE_NOTARY_PROFILE` to a
stored `notarytool` profile before running `scripts/macos/notarize-app.sh`.
`scripts/macos/new-release.sh` tests and builds both `osx-arm64` and `osx-x64`
archives with checksums. When both signing and notary variables are set, it
notarizes, staples, and repackages both archives before producing the final
checksums.

## License

A project license has not yet been selected.
