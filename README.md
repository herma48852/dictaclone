# DictaClone

DictaClone is a local-first voice-to-text application under development for
Windows 11 x64. Its target workflow is simple: hold a global shortcut, speak,
release it, and insert the transcription wherever the text cursor is active.

The project will provide local Whisper transcription, a Windows tray interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts will
remain local unless the user explicitly configures a cloud provider.

## Project status

Planning through Milestone 2 is complete. The tray application, non-activating
status overlay, global keyboard/mouse hooks, shortcut recorder, conflict
validation, and lifecycle safeguards are implemented. Its 137-test clean
regression suite passes with 95.95% Core line coverage, and the manual
trigger/focus matrix passed in Notepad, Edge, VS Code, and the test target.

Live microphone capture and transcription are not connected to the tray app
yet; that is Milestone 3. The current app previews the trigger and overlay
lifecycle.

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

Speech models, the local SDK, NuGet caches, and generated benchmark output are
excluded from Git.

## License

A project license has not yet been selected.
