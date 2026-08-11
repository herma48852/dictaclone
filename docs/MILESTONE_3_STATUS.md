# Milestone 3 Status

Milestone 3 is **accepted for progression**. Audio capture, local
transcription, app composition, and automated exit gates are implemented. The
live-microphone review exposed a WPF startup deadlock that prevented microphone
completion from being dispatched. A dump of the hung process identified the
exact blocking stack. The corrected lifecycle passes the full automated gate
and the end-to-end hold/speak/release retest now displays recognized text.
Settings, virtual-desktop isolation, clean tray exit, and disconnected-network
operation remain documented as follow-up compatibility checks. The user
accepted the core live workflow and authorized Milestone 4 to begin.

## Delivered

### Audio capture

- Runtime microphone selection plus a system-default option that resolves the
  current default endpoint at the start of every recording.
- In-memory WASAPI capture with no temporary audio file.
- Native-format conversion to 16 kHz, mono, 16-bit PCM for Whisper.
- Arbitrary-channel mixing, WDL resampling, clipping-safe PCM conversion, RMS
  and peak metering, minimum-speech and silence detection, bounded buffers, and
  a maximum-duration stop.
- Cancellation, idempotent cleanup, and actionable device-open, device-removal,
  conversion, and stop-timeout failures. Native microphone shutdown occurs
  outside the capture-buffer lock so a final WASAPI callback cannot deadlock.
- Capture creation/start and native shutdown run independently of WPF's
  dispatcher synchronization context; a single five-second deadline covers
  native stop initiation and its completion event.
- A live level meter in the non-activating recording pill.

### Local speech recognition

- A pinned `base.en`/`small.en` manifest containing filename, byte length,
  SHA-256, and HTTPS source.
- Offline reuse of an already valid model without contacting the content
  source.
- Cancellable downloads to same-directory staging files, progress reporting,
  length/SHA-256 verification, atomic replacement, and guaranteed partial-file
  cleanup.
- A warmed Whisper factory, one bounded inference worker, configurable model,
  English/automatic language choice, CPU-thread setting, and a bounded custom
  vocabulary prompt.
- Runtime settings for microphone, model, language, and silence sensitivity.
  Persistence remains assigned to Milestone 5.

### Tray workflow

- Hold the global dictation chord, speak, and release to stop capture and run
  local Whisper plus the deterministic text pipeline.
- The resulting transcript is shown in the non-focus-stealing status pill.
  Cursor insertion is intentionally deferred to Milestone 4.
- Model preparation and failures are visible; Escape cancellation propagates
  through recording, model download, and inference.
- Release now shows `Finishing microphone…` while capture stops and changes to
  `Transcribing locally…` only after usable audio reaches Whisper.
- WPF startup, smoke shutdown, and failure shutdown are asynchronous; the UI
  dispatcher is never synchronously blocked on application initialization.
- The default dictation trigger changed from `Ctrl+Win` to
  `Ctrl+Win+Space` after user testing showed that the modifier-only chord
  collided with `Ctrl+Win+Left/Right` virtual-desktop navigation. A permanent
  regression proves the modifier prefix alone cannot start dictation.

## Automated verification

The final clean Release gate used:

```powershell
.\scripts\Test.ps1 -Configuration Release -Clean -Coverage
```

| Check | Result |
| --- | ---: |
| Restore and Release build | Pass, 0 warnings and 0 errors |
| All automated tests | 177 passed, 0 failed, 0 skipped |
| Application tests | 31 passed |
| Audio tests | 19 passed |
| Core tests | 40 passed |
| Speech tests | 17 passed |
| Windows input/hook tests | 44 passed |
| Text/integration/end-to-end tests | 26 passed |
| `DictaClone.Core` merged line coverage | 96.65% (462/478) |
| Required Core line coverage | 90% |
| Clean build/test/coverage wall time | 73 seconds |
| PowerShell syntax and repository whitespace | Pass |
| C# formatting | Pass |

The final post-fix default-microphone diagnostic captured 1.01 seconds,
converted it to 32,320 bytes of 16 kHz mono PCM, reported a live peak, and
correctly classified the quiet input as silence.

The bounded tray-process smoke exits in about 1.5 seconds. Smoke mode skips
heavy model warm-up so it remains a focused tray/hook lifecycle test; the real
speech regression separately loads, uses, and disposes the native model.

## Fixed speech regression corpus

The local, already-installed `base.en` model was run without downloading any
asset:

```powershell
.\scripts\Invoke-Milestone3Regression.ps1 `
    -Model base.en `
    -MaximumWordErrorRate 0.15 `
    -NoBuild
```

| Case | Word error rate | Threshold | Result |
| --- | ---: | ---: | ---: |
| Original 11-second JFK fixture | 0.0% | 15% | Pass |
| Deterministic leading/trailing silence | 4.5% | 15% | Pass |
| Deterministic amplified/clipped audio | 0.0% | 15% | Pass |
| Pure silence | No transcript | No false positive | Pass |

Unit/component regressions additionally cover empty input, sub-block input,
stereo mixing, 48 kHz resampling, clipping bounds, duration caps, default-device
re-resolution, simulated device removal, corrupt installed/downloaded models,
cancelled download cleanup, model-download serialization, prompt bounds,
capture/inference cancellation, busy handling, and observer failures.

## Live-review diagnosis and fix

During the first manual hold/speak/release test, the blue pill remained on
`Transcribing locally…` and no text appeared. The affected process was sampled
for three seconds: CPU usage, working set, and thread count were all unchanged.
That ruled out slow Whisper inference and showed the process was waiting.

The pill had been shown before `StopAsync` completed, so its text also obscured
the actual stage. The first correction split the phases and hardened the audio
lock ordering. It passed the full automated gate and a real console capture,
but the second user retest still stalled at `Finishing microphone…`. That proved
the initial lock diagnosis was incomplete.

The second hung instance, PID 76352, remained fully idle. Its managed dump
showed the WPF dispatcher blocked in `Program`'s Startup handler at
`controller.StartAsync(...).GetAwaiter().GetResult()`. Startup had already
installed the hooks and entered asynchronous model warm-up, leaving a nested
message pump that could show the pill but could not run the required dispatcher
continuations.

This also explains the console/tray discrepancy. [NAudio 2.3.0 captures
`SynchronizationContext.Current` in `WasapiCapture` and posts
`RecordingStopped` back to it](https://github.com/naudio/NAudio/blob/v2.3.0/NAudio.Wasapi/WasapiCapture.cs#L68-L75).
The console diagnostic had no WPF context; the tray app did.

Startup now awaits asynchronously. Capture construction and stop initiation
also run without the WPF context, so audio completion no longer depends on the
dispatcher. The existing process smoke is forced across an asynchronous yield,
making it a regression against future sync-over-async startup. Two new audio
regressions verify context-free capture construction and a non-blocking stop
call when a fake native driver stalls. The post-fix clean Release gate passes
all 177 tests with no warnings. The user then repeated the live
hold/speak/release workflow and saw the recognized text, confirming the fix
end to end.

## Follow-up compatibility checks

Start the review build with:

```powershell
.\scripts\Invoke-Milestone3ManualTest.ps1 -NoBuild
```

Accepted:

1. Hold the current default `Ctrl+Shift+Space`, speak, and release; recognized text appears in the
   status pill. **Passed in live user testing.** Cursor insertion is
   intentionally deferred to Milestone 4.

Still useful to verify during release qualification:

1. Confirm the level meter moves only while recording.
2. Use `Ctrl+Win+Left/Right` to switch virtual desktops and confirm no red pill
   or recording appears.
3. Open tray settings, select the system default and a specific microphone,
   adjust model/language/silence settings, and repeat dictation.
4. After one successful warm-up, disconnect the network and confirm another
   dictation succeeds from the installed model.
5. Exit from the notification-area menu and confirm the tray process stops.

Closing another test window does not stop DictaClone; the tray process must be
exited from its own menu. These checks remain visible rather than being
recorded as passes without evidence.
