# Milestone 2 Status

Milestone 2 is an **automated-pass review candidate**. Implementation and the
automated quality gates are complete. The manual foreground-application matrix
is the remaining acceptance gate, so Milestone 3 has not started.

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
shutdown. Development builds
are framework-dependent, while this laptop's .NET 10 runtime is installed under
the repository rather than globally. Directly launching `DictaClone.App.exe`
left the Windows apphost waiting on its runtime error UI before `Program.Main`
could run. Launching `DictaClone.App.dll` through `.dotnet\dotnet.exe` completes
the same smoke lifecycle with exit code 0 in about 2.2 seconds.

Repeated direct-EXE runs also created multiple hidden apphost processes. All
known stale instances were explicitly stopped before the corrected test ran.
The temporary diagnostic watchdog was removed. Both manual targets and the app
now launch through the repository runtime, and the smoke script enforces a
ten-second hard timeout while cleaning up its exact child process.

## Manual acceptance checklist

Start a clean review session with:

```powershell
.\scripts\Invoke-Milestone2ManualTest.ps1 -Target TestTarget -NoBuild
```

Then verify:

1. Focus the test target, hold `Ctrl+Win`, and confirm the red **Listening** pill
   appears without moving keyboard focus.
2. Release the keys and confirm **Working** followed by **Shortcut detected**.
3. Repeat the trigger with Notepad, Edge, and VS Code focused. The overlay must
   never receive typed input or keyboard focus.
4. Double-click the DictaClone notification-area icon, record a new shortcut,
   and confirm a conflicting binding is rejected.
5. Exit DictaClone from its notification-area menu and confirm the shortcut no
   longer produces an overlay.

Exit the running tray app before invoking the launcher a second time. After this
matrix is accepted, the milestone status can be promoted to complete and its
final results recorded.
