# DictaClone

DictaClone is a local-first voice-to-text application under development for
Windows 11 x64. Its target workflow is simple: hold a global shortcut, speak,
release it, and insert the transcription wherever the text cursor is active.

The project will provide local Whisper transcription, a Windows tray interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts will
remain local unless the user explicitly configures a cloud provider.

## Project status

Milestones 0 through 4 are implemented and accepted. The tray app captures the
microphone, transcribes locally, and inserts into the original foreground target
through sequence-safe Paste Mode or clipboard-free Typing Mode. Its 200-test
clean regression suite passes with 97.14% Core line coverage, and the Milestone
4 manual insertion test passed user review.

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

See the [macOS porting guide](docs/MACOS_PORTING_GUIDE.md) for the planned
cross-platform boundaries and native macOS replacements.

## Initial target

- Windows 11 x64
- C# and .NET 10
- WPF desktop application
- Local Whisper speech recognition
- Self-contained, non-administrator installation

## Development

Install the pinned repository-local SDK, then run a clean restore, build, and
test:

```powershell
.\scripts\Install-DotNet.ps1
.\scripts\Test.ps1 -Clean
```

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

Speech models, the local SDK, NuGet caches, and generated benchmark output are
excluded from Git.

## License

A project license has not yet been selected.
