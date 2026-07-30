# DictaClone

DictaClone is a planned local-first voice-to-text application for Windows 11 x64.
Its core workflow is simple: hold a global shortcut, speak, release it, and insert
the transcription wherever the text cursor is active.

The project will provide local Whisper transcription, a Windows tray interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts will
remain local unless the user explicitly configures a cloud provider.

## Project status

Planning, Milestone 0, and Milestone 1 are complete. The repository contains the
pinned toolchain, solution/test scaffolding, a working WASAPI microphone probe,
measured local Whisper benchmarks, and the platform-neutral dictation workflow,
settings, hotkey, and deterministic text-processing core. Milestone 2 (tray UI,
status overlay, and global hooks) remains paused for review.

See the [implementation plan](docs/IMPLEMENTATION_PLAN.md) for the proposed
architecture, milestones, unit and regression tests, privacy constraints, and
release criteria.

See the [Milestone 0 results](docs/MILESTONE_0_RESULTS.md) for the target-machine
measurements and model decision.

See the [Milestone 1 results](docs/MILESTONE_1_RESULTS.md) for the workflow,
settings, text pipeline, test, and coverage results.

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

Speech models, the local SDK, NuGet caches, and generated benchmark output are
excluded from Git.

## License

A project license has not yet been selected.
