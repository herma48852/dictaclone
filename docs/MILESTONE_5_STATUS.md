# Milestone 5 Status

Milestone 5 is **implemented and accepted** as of 2026-08-04. Its objective is
to make DictaClone's accepted live workflow durable, recoverable, configurable,
and supportable without weakening its local-first privacy boundary.

## Scope

- Versioned, atomic settings persistence under the user's local application-data
  directory.
- Migration from every committed settings schema, beginning with schema v1.
- Corrupt-settings quarantine with safe-default recovery.
- Editable vocabulary, text expansions, work-domain presets, and explicit
  settings import/export.
- Optional text-only transcript history, disabled by default, plus copy-last
  recovery when insertion fails.
- Start-with-Windows, first-run microphone/model guidance, actionable permission
  help, keyboard accessibility, per-monitor DPI, and multi-monitor overlay
  behavior.
- Structured diagnostics that exclude transcripts, clipboard content, selected
  text, window titles, and secrets, plus a privacy-safe support bundle.

## Exit gates

- Schema v1 migrates to the current schema without losing existing settings.
- Atomic save, concurrent/cancelled save, import/export, missing file, invalid
  file, and corrupt-file quarantine tests pass.
- Secrets never appear in settings, diagnostic logs, support bundles, crash
  messages, or test snapshots.
- History remains off by default, stores final text only when enabled, obeys its
  retention limit, and can be copied or cleared.
- First-run, startup registration, permission guidance, accessible settings,
  high-DPI, and multi-monitor behavior have automated seams and a manual review
  checklist.
- The clean Release regression and coverage gates pass before user review.

## Current progress

- Milestone 4 was accepted and pushed as `3a3263a`.
- Existing settings, text-processing, UI, startup, and Windows adapter seams
  were audited.
- Schema v2 settings are stored at `%LocalAppData%\DictaClone\settings.json`.
  Saves use same-directory staging, disk flush, and atomic replacement. Schema
  v1 migrates forward automatically; malformed documents are renamed to a
  timestamped `settings.corrupt-*.json` file and safe defaults are loaded.
- The settings window has General, Knowledge, and Privacy & recovery tabs.
  Vocabulary, whole-utterance expansions, and General, Software development,
  Business, and Academic recognition presets are editable and persisted.
  Settings import/export rejects unknown fields and never includes a secret
  field.
- Transcript history remains disabled by default. When explicitly enabled, it
  stores final text only, enforces a 1-500 entry retention limit, and provides
  copy and confirmed clear actions. Copy last result becomes available as soon
  as final text exists, including when focus-safe insertion subsequently fails.
- Start with Windows uses the current-user Run key and an exact executable
  command. First-run setup, microphone privacy guidance, keyboard labels and tab
  navigation, per-monitor DPI awareness, and foreground-monitor overlay
  placement are wired into the desktop app.
- Structured JSON-lines diagnostics store event type, outcome, duration, and
  exception type only. Support bundles contain a non-sensitive system summary,
  counts/modes, and those diagnostics; settings, history, vocabulary text,
  microphone IDs, transcripts, clipboard content, window titles, and exception
  messages are excluded.

## Automated evidence

The final gate was:

```powershell
.\scripts\Test.ps1 -Configuration Release -Clean -Coverage
```

| Check | Result |
| --- | ---: |
| Release build | Pass, 0 warnings and 0 errors |
| Total automated tests | 229 passed across the coverage and isolated E2E runs |
| App tests | 44 passed |
| Audio tests | 21 passed |
| Core tests | 41 passed |
| End-to-end insertion tests | 4 passed |
| Infrastructure persistence/privacy tests | 12 passed |
| Integration tests | 2 passed |
| Speech tests | 18 passed |
| Text tests | 21 passed |
| Windows adapter tests | 66 passed |
| Core line coverage | 93.88% (537/572), 90% required |
| C# formatter verification | Pass |
| Repository whitespace check | Pass |
| Milestone 5 PowerShell syntax | Pass |
| Bounded app startup/shutdown smoke | Pass, exit code 0 |

The regressions cover missing and corrupt documents, v1-to-v2 migration,
atomic/concurrent/cancelled saves, locked-file classification, import/export,
secret-field rejection, history opt-in/retention/clear, copy-last after failed
insertion, startup command construction, microphone error guidance, accessible
settings/history controls, multi-monitor placement math, and diagnostic/support
bundle redaction.

## Manual review findings

The first Knowledge-tab review correctly changed the recognized transcript from
`jay son` to `JSON`, but standard Emacs accepted neither normal Paste Mode nor
the original local Typing Mode input. Paste Mode sends `Ctrl+V`, which standard
Emacs assigns to scrolling. Typing Mode was using Windows Unicode packet input;
Windows reported those packets as sent, so DictaClone showed a green pill, but
Emacs did not consume them.

The first attempted correction added a regression that failed with `unicode:A`
and passed with `mapped:A`. Typing Mode was changed to prefer physical
virtual-key/scancode events for every representable local character, not only
remote-desktop targets, while retaining Unicode fallback for emoji and
characters unavailable on the active keyboard layout. The four real Windows
insertion tests and the full 225-test Release/coverage gate passed after that
change. The user's live Emacs retest still failed, proving that mapped typing was
not a sufficient compatibility strategy.

The current correction detects the native `emacs` foreground process and uses
the existing sequence-safe clipboard transaction with Emacs's standard `C-y`
(`yank`) command for both normal Dictation and the Typing Mode hotkey. The
[GNU Emacs manual](https://www.gnu.org/software/emacs/manual/html_node/emacs/Yanking.html)
confirms that `C-y` checks for newer system-clipboard text and inserts it. Other
target applications retain their existing Paste Mode or clipboard-free Typing
Mode behavior. Native Emacs Typing Mode therefore has a documented clipboard
exception, but the prior clipboard is still restored. Two new regression cases
cover Paste and Typing settings; the focused Windows suite passes 66/66.

After closing only the verified repository DictaClone process, the post-fix
Release build passed with zero warnings and errors. The complete 227-test
Release coverage run passed, including all four end-to-end Windows insertion
and process-lifecycle cases, and Core coverage remained 93.88% (537/572). The
user then confirmed that the corrected Release build inserted the configured
replacement into the live native GNU Emacs buffer. The Emacs compatibility
defect is resolved.

During the subsequent Notepad review, insertion initially worked and then the
user reported that speech was no longer detected. Initial read-only diagnosis
found that the running process is the corrected Release build, persisted audio
settings remain at the normal `0.012` silence threshold with `base.en`/English,
and Windows still exposes both capture endpoints. DictaClone follows the system
default, currently the NexiGo webcam microphone. Two-second in-memory probes
from both NexiGo and the Realtek laptop array received correctly sized audio
buffers and nonzero ambient peaks, so device enumeration, permission, and basic
WASAPI delivery are working. The user's subsequent five-second spoken probe
produced 159,680 bytes of 16 kHz mono PCM, a 0.3143 peak, and `IsSilent: false`.
That rules out insufficient microphone level and the silence classifier. A new
in-memory `capture-transcribe` developer diagnostic then reproduced the defect:
a second five-second spoken sample had a clear 0.1038 peak but a 0.00787
whole-recording RMS, so the old classifier marked it silent against the 0.012
threshold and never invoked Whisper. The separate `base.en` PowerShell error
was harmless because that model is already the diagnostic's default.

The classifier now evaluates 20 ms windows and requires at least 150 ms of
above-threshold activity. This prevents pauses elsewhere in a recording from
diluting clear speech while still rejecting a brief noise spike. Regressions
cover both a five-second, low-whole-average speech fixture and a loud 100 ms
noise burst. The focused Audio suite passes 21/21, and the updated in-memory
diagnostic then classified the user's 0.01089 whole-recording RMS sample as
speech and Whisper returned a coherent transcript. This confirms the corrected
capture-to-transcription path end to end.

The corrected Release solution builds with zero warnings and errors. All 225
non-E2E tests passed under coverage, Core coverage remained 93.88% (537/572),
and all four live Windows E2E cases passed together in an isolated run. During
two full coverage runs, the E2E insertion case separately observed partial
synthetic typing and an externally emptied system clipboard; those live desktop
resources were concurrently disturbed, and the same unchanged E2E suite passed
4/4 when rerun alone. The user then confirmed that the corrected Release app
detected normal speech, ran transcription, and inserted the result into Notepad.
One minor recognition error occurred while the user was speaking quickly; this
is transcription accuracy rather than a recurrence of the silence-detection
defect. The windowed speech-detection fix is accepted. The user then directed
that Milestone 5 be committed and pushed and that work proceed to Milestone 6,
which records acceptance of this milestone.

## Manual review

First make sure no older DictaClone tray instance is running, then launch:

```powershell
.\scripts\Invoke-Milestone5ManualTest.ps1 -NoBuild
```

The script prints the numbered review for first-run and restart persistence,
knowledge rules, copy-last recovery, opt-in history, settings transfer, support
bundle contents, Windows microphone guidance, startup registration,
keyboard-only access, mixed-DPI/multi-monitor placement, and the
`Ctrl+Win+Left/Right` virtual-desktop regression. Exit DictaClone from its tray
menu when the review is complete.
