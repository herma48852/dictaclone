# Milestone 3 Status

Milestone 3 is an **automated-pass review candidate**. Audio capture, local
transcription, app composition, and automated exit gates are implemented. Live
microphone transcription and disconnected-network operation remain for manual
acceptance, so Milestone 4 has not started.

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
  conversion, and stop-timeout failures.
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
| All automated tests | 174 passed, 0 failed, 0 skipped |
| Application tests | 31 passed |
| Audio tests | 16 passed |
| Core tests | 40 passed |
| Speech tests | 17 passed |
| Windows input/hook tests | 44 passed |
| Text/integration/end-to-end tests | 26 passed |
| `DictaClone.Core` merged line coverage | 96.65% (462/478) |
| Required Core line coverage | 90% |
| Clean build/test/coverage wall time | 65 seconds |
| PowerShell syntax and repository whitespace | Pass |
| C# formatting | Pass |

The real default-microphone diagnostic captured 0.97 seconds, converted it to
31,040 bytes of 16 kHz mono PCM, reported a live peak, and correctly classified
the quiet input as silence.

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

## Remaining manual acceptance

Start the review build with:

```powershell
.\scripts\Invoke-Milestone3ManualTest.ps1 -NoBuild
```

Verify:

1. Hold `Ctrl+Win+Space`, speak a short sentence, and confirm the level meter
   moves only while recording.
2. Release Space and confirm the recognized text appears after local
   transcription. It should not be inserted into the focused app yet.
3. Use `Ctrl+Win+Left/Right` to switch virtual desktops and confirm no red pill
   or recording appears.
4. Open tray settings, select the system default and a specific microphone,
   adjust model/language/silence settings, and repeat dictation.
5. After one successful warm-up, disconnect the network and confirm another
   dictation succeeds from the installed model.
6. Exit from the notification-area menu and confirm the tray process stops.

Closing another test window does not stop DictaClone; the tray process must be
exited from its own menu. After this manual matrix passes, Milestone 3 can be
promoted to complete and Milestone 4 can begin.
