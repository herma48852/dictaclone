# DictaClone implementation plan

Status: Milestones 0-6 complete and accepted; Milestone 7 is next.

Last reviewed: 2026-08-04

## 1. Product goal

Build an independent Windows 11 x64 voice-to-text desktop app named **DictaClone**. It will reproduce the useful workflow of DictaFlow without copying its code, branding, text, or visual assets:

1. Put the cursor in any app.
2. Hold a global trigger and speak.
3. Release the trigger.
4. Transcribe locally, optionally clean up the text, and insert it at the original cursor.

The app will be local-first, usable without an account or subscription, and installable on this laptop. Audio and transcripts will not leave the machine unless the user explicitly enables an optional cloud Smart Edit provider.

## 2. Reference behavior and scope

The official DictaFlow site and getting-started guide currently describe these Windows behaviors:

- Hold-to-talk dictation, with `Ctrl + Win` as the initial Windows trigger.
- Separate normal dictation, Smart Edit, and character-by-character Typing Mode triggers.
- Normal clipboard insertion plus delayed typing for Citrix, RDP, VMware, terminals, and clipboard-hostile apps.
- Mid-sentence corrections such as “actually” and “I mean.”
- Selected-text rewriting, custom vocabulary, text expansions, work domains, custom instructions, model choices, microphone selection, silence sensitivity, and a toggle-recording mode.
- A tray-oriented interface which records only while the trigger is held.

DictaClone will target functional parity in staged milestones:

| Capability | V1 release | Later enhancement |
| --- | --- | --- |
| Tray app and non-focus-stealing recording indicator | Yes | Themes and richer waveform |
| Hold-to-talk and toggle mode | Yes | Mouse/foot-pedal presets |
| Local microphone transcription | Yes | Additional optimized inference runtimes |
| Clipboard paste and delayed typing | Yes | Per-application automatic mode profiles |
| Custom hotkeys and audio device | Yes | Shortcut import/export |
| Silence rejection and cancel action | Yes | Neural voice activity detection |
| Custom vocabulary and text expansions | Yes | Vocabulary import/export |
| Mid-sentence correction | Yes | More advanced local rewriting |
| Smart Edit and selected-text editing | Yes, through an explicitly configured provider | Bundled on-device generative model if performance is acceptable |
| Local history/recovery | Opt-in, text only | Search and tagging |
| Accounts, billing, word quotas, telemetry | No | Not planned |
| macOS | No | Planned after the Windows release; see `docs/MACOS_PORTING_GUIDE.md` |
| Mobile, Telegram, or browser extensions | No | Not planned |

## 3. Target machine and feasibility

Observed on this laptop:

- Windows 11 x64, version 25H2, build 26200.8875.
- Intel Core i7-1370P, 20 logical processors.
- 31.66 GiB physical RAM.
- Intel Iris Xe graphics.
- Visual Studio Community 2022 17.13 and .NET SDK 9.0.203.
- Multiple microphone endpoints, including the built-in microphone array and USB/Bluetooth devices.

This is sufficient for CPU-local Whisper inference. The project will use .NET 10 LTS, which is supported through November 2028. Installing the .NET 10 SDK is therefore the first toolchain task; the released app will be self-contained and will not require the user to install a .NET runtime.

The first performance spike will compare English `base.en` and `small.en` Whisper models on this laptop. `base.en` is the provisional fast default; `small.en` is the provisional accuracy option. The benchmark, not an assumption, will make the final choice.

## 4. Technical design

### Application stack

- **UI:** C# 14, .NET 10, WPF, x64.
- **Process model:** one non-elevated, single-instance tray process.
- **Audio:** WASAPI capture through NAudio; convert to 16 kHz, mono, 16-bit PCM in memory.
- **Speech recognition:** Whisper.net/whisper.cpp CPU runtime, behind an `ITranscriptionEngine` interface.
- **Hotkeys:** Win32 low-level keyboard and mouse hooks. A modifier-only chord such as `Ctrl + Win` needs press and release events and cannot be implemented correctly with `RegisterHotKey` alone.
- **Text insertion:** clipboard paste first; Win32 `SendInput` Unicode/scancode typing with configurable delay for Typing Mode.
- **Settings:** versioned JSON under `%LocalAppData%\DictaClone`, written atomically.
- **Secrets:** Windows Credential Manager or DPAPI; never plain-text settings.
- **Packaging:** self-contained `win-x64` publish plus a per-user installer. Installation must not require administrator rights.

WPF is preferred over WinUI 3 for this project because the app is primarily a tray utility, requires direct Win32 integration, and does not benefit enough from an additional Windows App SDK deployment layer.

### Component boundaries

| Component | Responsibility |
| --- | --- |
| `DictaClone.App` | WPF startup, tray icon, settings window, first-run flow, status overlay |
| `DictaClone.Core` | Dictation state machine, use cases, domain models, interfaces; no Win32 or UI references |
| `DictaClone.Audio` | Device enumeration, WASAPI capture, resampling, level metering, silence detection |
| `DictaClone.Speech` | Whisper model discovery/download, integrity checks, warm-up, transcription |
| `DictaClone.Text` | Normalization, correction rules, vocabulary, expansions, Smart Edit abstraction |
| `DictaClone.Windows` | Hooks, foreground-window tracking, clipboard transaction, `SendInput`, single-instance handling |
| `DictaClone.Infrastructure` | Settings, encrypted secrets, local history, structured diagnostic events |
| `DictaClone.TestTarget` | Purpose-built WPF text target used by automated insertion tests |

All operating-system and provider dependencies will sit behind interfaces. Unit tests will drive the complete dictation workflow with fake clock, audio source, transcriber, clipboard, foreground window, and text injector.

### Dictation pipeline

`Idle -> Recording -> Transcribing -> Cleaning -> Inserting -> Idle`

Any active state can move to `Cancelled` or `Faulted`, perform cleanup, and return to `Idle`.

Important behavior:

- Trigger down snapshots the foreground window, selected mode, and audio device, then starts capture and displays the overlay without activating it.
- Trigger up stops capture exactly once. Repeated hook messages and key auto-repeat are ignored.
- `Escape` cancels a recording or pending insertion.
- Silence or an accidental very short press produces no text.
- Only one operation runs at a time. A second trigger during processing gives visible “busy” feedback.
- The original target is checked before insertion. DictaClone will not steal focus or blindly type into a different foreground app.
- All buffers, hooks, temporary clipboard data, and cancellation tokens are released on success, cancellation, error, application exit, and device removal.

### Transcription and cleanup modes

**Dictation mode**

- Runs Whisper locally.
- Applies punctuation/whitespace normalization, custom vocabulary, text expansions, and conservative correction rules.
- Never sends audio or text to a network service.

**Smart Edit mode**

- Runs local transcription first.
- Optionally includes the selected text and active application class in a structured edit request.
- Sends text only to the explicitly configured provider by default; sending audio would require a separate opt-in.
- Supports correction, formatting, rewriting, and instructions such as “make this a list.”
- Is visibly disabled until a provider is configured. There will be no silent cloud fallback.

The provider will be accessed through `ISmartEditProvider`. Tests will use a deterministic fake. The initial implementation may support an OpenAI-compatible HTTPS endpoint with a bring-your-own key, while keeping the interface suitable for a future local provider.

### Text insertion

Two user-selectable strategies will be implemented:

1. **Paste mode:** snapshot the Windows clipboard, set Unicode text, send `Ctrl+V`, and restore the prior clipboard only if its sequence number shows that the user or target app has not changed it during the transaction. Retry transient clipboard-lock errors with a short bounded backoff.
2. **Typing mode:** send text character by character without touching the clipboard. Use Unicode events for ordinary local apps and mapped virtual-key/scancode events where a remote target requires them. Expose a 0–100 ms character delay.

Known Windows boundary: a normal non-elevated process cannot inject input into a higher-integrity elevated process because of UIPI. DictaClone will report this clearly rather than running elevated by default.

### Privacy and data handling

- Microphone capture occurs only while the trigger is held or while the user has explicitly enabled toggle recording.
- Audio remains in memory and is discarded immediately after the operation.
- No telemetry, account, word counting, or automatic upload.
- Local history is off by default. If enabled, it stores final text only and has clear/delete controls.
- Diagnostic logs contain state transitions, durations, error codes, and component versions, but no audio, transcript, selected text, clipboard content, API keys, or full window titles.
- Model files are downloaded over HTTPS to a staging file, verified by expected size and SHA-256 hash, then atomically moved into place.
- Cloud Smart Edit shows the provider and data class before first use and can be disabled at any time.

## 5. Repository layout

```text
dictaclone/
  DictaClone.slnx
  Directory.Build.props
  Directory.Packages.props
  global.json
  src/
    DictaClone.App/
    DictaClone.Core/
    DictaClone.Audio/
    DictaClone.Speech/
    DictaClone.Text/
    DictaClone.Windows/
    DictaClone.Infrastructure/
  tests/
    DictaClone.Core.Tests/
    DictaClone.Audio.Tests/
    DictaClone.Speech.Tests/
    DictaClone.Windows.Tests/
    DictaClone.IntegrationTests/
    DictaClone.EndToEndTests/
    DictaClone.TestTarget/
    Fixtures/
      audio/
      transcripts/
  installer/
  scripts/
  docs/
```

Central package management and a pinned .NET SDK will make builds reproducible. Nullable reference types, deterministic builds, built-in .NET analyzers, and warnings-as-errors will be enabled from the first commit.

## 6. Implementation milestones

### Milestone 0 — Toolchain and performance spike

Deliver:

- Install/pin a current .NET 10 SDK.
- Create the solution, build properties, test projects, and local PowerShell build/test entry points.
- Run Whisper.net CPU inference against fixed WAV fixtures using `base.en` and `small.en`.
- Record model size, load time, real-time factor, peak working set, and transcript quality on this laptop.
- Prove a minimal WASAPI microphone capture and enumerate friendly device names.

Exit gate:

- Clean restore/build/test from one documented command.
- At least one model transcribes a 10-second fixture faster than real time.
- A model/runtime choice is recorded with measured evidence.

### Milestone 1 — Core workflow as pure code

Deliver:

- Dictation state machine and cancellation/error recovery.
- Immutable settings models and validation.
- Hotkey chord model and conflict rules.
- Text-normalization, vocabulary, expansion, and conservative “actually/I mean” correction pipeline.
- Interfaces for every Windows, audio, transcription, and Smart Edit dependency.

Exit gate:

- Core has no Windows/UI package dependency.
- State transition, repeat-key, cancellation, silence, failure, and concurrency unit tests pass.
- Core line coverage is at least 90%.

### Milestone 2 — Tray UI, overlay, and hooks

Deliver:

- Single-instance tray application and settings window.
- Non-activating always-on-top status pill for recording, processing, success, and failure.
- Hold and toggle triggers, shortcut recorder, conflict detection, keyboard/mouse/foot-pedal event support.
- Clean hook teardown on exit and crash-safe startup behavior.

Exit gate:

- The trigger works while Notepad, Edge, VS Code, and the test target are focused.
- The overlay never takes keyboard focus.
- A 1,000-cycle synthetic hook test produces exactly one start and one stop per cycle.

### Milestone 3 — Audio capture and local transcription

Deliver:

- Input-device selection and default-device following.
- In-memory WASAPI capture, resampling, level meter, duration limit, silence threshold, and device-disconnect handling.
- Model manager with progress, cancellation, checksum verification, and offline reuse.
- Whisper engine warm-up, configurable language/model, custom-vocabulary prompt, and bounded worker concurrency.

Exit gate:

- Fixed audio regression corpus passes the transcript-quality threshold.
- Silence, clipped audio, empty input, device removal, corrupt model, and cancelled model download are covered.
- After model installation, normal dictation works with the network disconnected.

### Milestone 4 — Reliable cursor insertion

Deliver:

- Foreground-target snapshot and validation.
- Clipboard transaction with sequence-aware restoration.
- Unicode/scancode Typing Mode with configurable delay.
- Error reporting for elevated targets, lost focus, blocked input, and clipboard contention.

Exit gate:

- Automated end-to-end insertion passes for single line, multiline, punctuation, non-ASCII text, emoji/surrogate pairs, and long text in `DictaClone.TestTarget`.
- The clipboard is preserved in Paste mode and untouched in Typing Mode, except
  for documented target-specific compatibility adapters that use the same
  snapshot, sequence-check, and restoration transaction. Native GNU Emacs uses
  such an adapter with its `C-y` (`yank`) command.
- Manual compatibility checks pass in Notepad, Edge/Chrome, VS Code, Windows Terminal, Word/Outlook if installed, and an available RDP/Citrix session.

### Milestone 5 — Knowledge, settings, recovery, and polish

Deliver:

- Custom vocabulary, text expansions, work-domain presets, and import/export.
- Optional local transcript history and “copy last result” recovery.
- Start-with-Windows option, first-run microphone/model flow, actionable permission help, accessible keyboard navigation, and high-DPI/multi-monitor behavior.
- App-local structured diagnostics and a privacy-safe support bundle.

Exit gate:

- Settings migrate forward from each committed schema version.
- Secrets never appear in settings, logs, crash reports, or test snapshots.
- The app recovers from corrupted settings by quarantining the bad file and starting with safe defaults.

### Milestone 6 — Smart Edit and selected-text editing

Deliver:

- Provider configuration and encrypted key storage.
- Prompt/request builder with explicit selected-text boundaries, vocabulary, work domain, and custom instructions.
- Selection capture and replacement using the same clipboard-safety rules.
- Timeouts, cancellation, retry policy, rate-limit feedback, offline behavior, and preview/copy recovery after a provider failure.

Exit gate:

- Unit and contract tests cover every edit intent and provider error shape.
- No standard test contacts a live paid service.
- An opt-in live-provider smoke test can run with an environment-provided secret.
- Selected text is replaced only after the target and selection are revalidated.

### Milestone 7 — Packaging and release qualification

Deliver:

- Self-contained `win-x64` release and per-user installer/uninstaller.
- Optional startup registration created only with consent and removed on uninstall.
- Version metadata, third-party notices, model-license documentation, release notes, and rollback instructions.
- Repeatable release and checksum scripts.

Exit gate:

- Install, upgrade, repair, uninstall, and portable launch are tested on Windows 11 x64.
- A clean Windows user profile can install, download a model, dictate into Notepad, restart, dictate offline, and uninstall without remnants other than deliberately retained user data.
- All automated and manual release gates below pass.

## 7. Test strategy

### Unit tests

Use xUnit with ordinary .NET assertions and deterministic fakes. Unit tests must not require a microphone, desktop focus, network, model download, or paid provider.

Primary suites:

- State machine: legal/illegal transitions, key repeat, rapid press/release, cancel in every state, overlapping triggers, shutdown during work.
- Audio logic: PCM conversion, channel mixing, sample counts, duration, RMS/peak
  calculation, windowed minimum speech activity, silence threshold, and buffer
  limits.
- Text logic: whitespace, punctuation, casing, correction phrases, vocabulary boundaries, expansions, multiline/code preservation, Unicode.
- Settings: defaults, validation, unknown fields, schema migrations, atomic-write recovery.
- Hotkeys: normalization, left/right modifiers, modifier-only chords, collisions, toggle semantics, injected-event filtering.
- Insertion planning: paste versus typing, delay validation, target mismatch, surrogate pairs, line endings.
- Smart Edit: request boundaries, custom instructions, selection rules, timeout/cancellation/error mapping, secret redaction.
- Model management: manifests, paths, hash checks, partial download cleanup, atomic replacement.

Coverage gates:

- `DictaClone.Core`: 90% line coverage and 85% branch coverage.
- All non-UI production assemblies combined: 80% line coverage.
- Coverage is a floor, not a substitute for scenario assertions.

### Component and integration tests

- Feed recorded PCM buffers through the real resampler and WAV reader.
- Run the real Whisper engine against a small, versioned audio corpus when the model fixture is available.
- Exercise settings and history against a temporary isolated local-app-data directory.
- Test clipboard retry/restoration on an STA thread.
- Start `DictaClone.TestTarget`, focus a known control, inject text, and read back the received value through a test IPC channel.
- Use a local fake HTTP server for Smart Edit success, malformed payload, 401, 429, 500, slow response, disconnect, and cancellation cases.

### End-to-end tests

The application will support internal test seams selected only in test builds:

- An audio-file source in place of the physical microphone.
- A deterministic fake transcriber/provider where the test is about Windows insertion rather than recognition.
- A test-target handshake to remove focus timing guesses.

This permits a stable automated path:

`trigger -> recording -> transcription -> cleanup -> target validation -> insertion -> clipboard restoration`

The release build will not expose test IPC or test source switches.

### Speech regression corpus

Store short, license-compatible WAV fixtures and matching normalized expected text. Include:

- Plain English statements and questions.
- Hesitation, self-correction, and false starts.
- Names, acronyms, file paths, code terms, numbers, dates, and punctuation.
- Quiet speech, background noise, clipping, leading/trailing silence, and pure silence.
- Several microphone types/sample rates.
- At least one supported non-English fixture when multilingual mode is enabled.

Track:

- Word error rate for plain transcription.
- Required-token accuracy for names, acronyms, numbers, and vocabulary overrides.
- False-positive text on silence.
- Model load time, transcription real-time factor, and peak memory.

Golden files will be reviewed intentionally. A test command will never update expected transcripts automatically.

### Regression policy

- Every reported defect first receives the smallest automated reproduction possible.
- Bug fixes are not complete until that regression test fails before the fix and passes after it.
- Audio, text, settings, clipboard, and provider regressions remain permanently versioned.
- Machine- or app-specific issues that cannot be automated receive a numbered manual case with environment and exact expected result.
- Before release, run unit tests, integration tests, end-to-end tests, the fixed audio corpus, 100 repeated dictations, installer tests, and the application compatibility matrix.

## 8. Release quality gates

The first release is complete only when all of these are true:

- The app installs and runs as a non-admin Windows 11 x64 tray app on this laptop.
- Recording begins within 100 ms of trigger down and stops on trigger up, measured after warm-up.
- The status overlay does not steal focus.
- For a 10-second English fixture, the provisional target is release-to-insertion p95 of 2.5 seconds with the chosen fast model. Milestone 0 may revise this only with recorded benchmark evidence.
- The fast model stays below 1 GiB warmed working set; an accuracy model may use up to 2 GiB.
- Silence produces no inserted text in all silence corpus cases.
- Normal dictation works offline after the model is downloaded.
- Paste and Typing Mode reproduce exact Unicode fixture text in the automated target.
- Cancelling or failing never inserts partial text.
- No standard logs or persisted settings contain recorded audio, transcript text, clipboard contents, selected text, or secrets.
- Fifty consecutive end-to-end cycles leave no stuck hotkey, audio capture, clipboard replacement, orphan process, or temporary audio file.
- Installer upgrade preserves settings; uninstall removes binaries and startup registration.
- All automated test gates pass from a clean checkout/publish directory.

## 9. Principal risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Local inference is too slow or inaccurate | Benchmark two models first; warm the model; cap threads; keep GPU/OpenVINO/Vulkan runtimes as measured follow-ups |
| Global hook causes stuck or duplicate recordings | Explicit state machine, injected-event filtering, repeat suppression, focus-independent hook stress tests |
| Clipboard contents are lost | Sequence-aware transaction, bounded retries, restore tests, Typing Mode alternative |
| Target focus changes while processing | Snapshot and revalidate the foreground target; show recoverable result instead of typing elsewhere |
| RDP/Citrix drops characters | Scancode backend, adjustable delay, test profiles, manual validation on an available remote session |
| Input into elevated apps fails | Detect/document UIPI boundary; do not elevate the whole app by default |
| Whisper invents text from silence/noise | Minimum duration/energy gates, silence corpus, optional VAD after baseline |
| Model/provider dependency changes | Pin packages and model manifest; central versions; abstraction and contract tests |
| Smart Edit leaks sensitive data | Off by default, text-only default, explicit consent, visible provider, encrypted key, redacted logs |
| “Clone” creates branding/IP confusion | Use DictaClone branding and original assets/code; reproduce public behavior only |

## 10. Implementation defaults to confirm or revise

Unless changed before coding, implementation will proceed with these defaults:

- Windows-only, English-first, local transcription.
- .NET 10 WPF and self-contained x64 packaging.
- `Ctrl + Win + Space` hold-to-talk, `Alt + Shift + Space` Smart Edit, and `Ctrl + Alt + Space` Typing Mode, all configurable. Each default has a primary key, preventing modifier-only triggers from interfering with Windows virtual-desktop shortcuts.
- Clipboard Paste mode by default; Typing Mode is opt-in per use or per app.
- No account, subscription, telemetry, or persisted audio.
- Transcript history disabled by default.
- Smart Edit disabled until the user configures a provider.

## 11. Research sources

- [DictaFlow product page](https://dictaflow.io/)
- [DictaFlow getting-started guide](https://dictaflow.io/getting-started.html)
- [DictaFlow privacy policy](https://dictaflow.io/privacy.html)
- [Microsoft .NET release and support policy](https://learn.microsoft.com/dotnet/core/releases-and-support)
- [Whisper.net project and supported Windows runtimes](https://github.com/sandrohanea/whisper.net)
- [Microsoft `SendInput` documentation](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput)
