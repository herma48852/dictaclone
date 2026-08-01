# Milestone 2 Status

Milestone 2 is **complete and accepted**. Implementation, automated quality
gates, and the manual foreground-application matrix all passed. Milestone 3 has
not started.

## Delivered

- Single-instance guard based on a per-user named mutex.
- Notification-area icon with settings and exit actions.
- Non-activating, topmost status overlay for recording, processing, success,
  and failure states.
- Low-level Windows keyboard and mouse hooks.
- Hold and toggle shortcut interpretation with repeat and injected-input
  suppression.
- Modifier-only, keyboard, F13-F24 foot-pedal-style, and mouse-button shortcut
  recording.
- Runtime shortcut conflict checking and rebinding.
- Crash-safe controller cleanup paths, a bounded smoke-test script, and a
  repeatable manual-test launcher.

The tray app currently previews shortcut and overlay behavior. Live microphone
capture and transcription are intentionally deferred to Milestone 3.

## Automated verification

The final clean Release run on the target Windows 11 x64 laptop used:

```powershell
.\scripts\Test.ps1 -Configuration Release -Clean -Coverage
```

| Check | Result |
| --- | ---: |
| Restore and Release build | Pass, 0 warnings and 0 errors |
| All automated tests | 137 passed, 0 failed, 0 skipped |
| Application tests | 23 passed |
| Windows hook/input tests | 43 passed |
| Process end-to-end tests | 3 passed |
| Other Core/audio/speech/text/integration tests | 68 passed |
| `DictaClone.Core` merged line coverage | 95.95% (450/469) |
| Required Core line coverage | 90% |
| Full clean build/test/coverage wall time | 272.7 seconds |

The Windows suite installs and removes a real low-level hook and runs 1,000
synthetic shortcut cycles, producing exactly 1,000 starts and 1,000 stops. The
application suite creates a real overlay HWND and confirms the
`WS_EX_NOACTIVATE` extended style. Process regressions verify normal smoke-mode
shutdown and noninteractive duplicate-instance exit.

## Lifecycle defect diagnosis

The unexpected smoke-test delay was caused by the launcher, not application
shutdown. Development builds are framework-dependent, while this laptop's .NET
10 runtime is installed under
the repository rather than globally. Directly launching `DictaClone.App.exe`
left the Windows apphost waiting on its runtime error UI before `Program.Main`
could run. Launching `DictaClone.App.dll` through `.dotnet\dotnet.exe` completes
the same smoke lifecycle with exit code 0 in about 2.2 seconds.

Repeated direct-EXE runs also created multiple hidden apphost processes. All
known stale instances were explicitly stopped before the corrected test ran.
The temporary diagnostic watchdog was removed. Both manual targets and the app
now launch through the repository runtime, and the smoke script enforces a
ten-second hard timeout while cleaning up its exact child process.

## Manual acceptance results

The user ran the review harness on the target Windows 11 x64 laptop:

```powershell
.\scripts\Invoke-Milestone2ManualTest.ps1 -Target TestTarget -NoBuild
```

| Exit criterion | Result |
| --- | ---: |
| Global trigger works with the test target focused | Pass |
| Global trigger works with Notepad focused | Pass |
| Global trigger works with Edge focused | Pass |
| Global trigger works with VS Code focused | Pass |
| Overlay remains non-activating and does not take keyboard focus | Pass |
| Tray/settings shortcut workflow behaves as expected | Pass |
| Exiting the tray app removes shortcut behavior | Pass |

The user reported the complete manual test passed on July 31, 2026. Audio
capture, transcription, and text insertion were not expected in this milestone;
they are assigned to Milestones 3 and 4.

## Milestone 2 exit gate

| Exit criterion | Status |
| --- | ---: |
| Trigger works in Notepad, Edge, VS Code, and the test target | Pass |
| Overlay never takes keyboard focus | Pass |
| 1,000 synthetic cycles produce exactly one start and stop each | Pass |
