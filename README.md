# DictaClone

DictaClone is a local-first voice-to-text application under development for
Windows 11 x64. Its target workflow is simple: hold a global shortcut, speak,
release it, and insert the transcription wherever the text cursor is active.

The project will provide local Whisper transcription, a Windows tray interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts will
remain local unless the user explicitly configures a cloud provider.

## Project status

Planning through Milestone 2 is complete. Milestone 3 is an automated-pass
review candidate: the tray app now captures the selected/default microphone,
shows a live level meter, transcribes locally with a verified Whisper model,
and displays the recognized text without persisting audio. Its 174-test clean
regression suite passes with 96.65% Core line coverage. Live microphone and
offline manual acceptance remain before Milestone 3 is complete.

The default dictation trigger is `Ctrl+Win+Space`; the primary key prevents
Windows' `Ctrl+Win+Arrow` virtual-desktop shortcuts from starting a recording.
Text insertion into the focused application remains Milestone 4.

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
scope, model/corpus evidence, and the remaining manual acceptance checklist.

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

Speech models, the local SDK, NuGet caches, and generated benchmark output are
excluded from Git.

## License

A project license has not yet been selected.
