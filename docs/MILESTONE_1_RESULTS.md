# Milestone 1 Results

Milestone 1 implements DictaClone's workflow as platform-neutral code. It does
not install global hooks, display a tray interface, capture live audio, or
insert text through Win32 yet; those implementations begin in later milestones.

## Delivered

### Dictation workflow

`DictationCoordinator` owns the hold-to-talk lifecycle:

```text
Idle -> Recording -> Transcribing -> Cleaning -> Inserting -> Idle
```

Active work can transition through `Cancelled` or `Faulted` and always attempts
to return to `Idle`. The coordinator:

- ignores repeated or overlapping start events;
- snapshots the foreground target before capture;
- rejects silent, empty, or blank transcription results;
- revalidates the target before insertion;
- propagates cancellation to processing dependencies;
- disposes capture sessions after success, cancellation, or failure;
- reports privacy-safe failure stages and exception type names without
  transcript or exception-message content; and
- isolates the workflow from exceptions raised by UI state observers.

The workflow depends only on Core contracts for audio capture, transcription,
text processing, foreground-target tracking, insertion, global trigger events,
and Smart Edit. No Windows, UI, NAudio, or Whisper package is referenced by
`DictaClone.Core`.

### Settings and hotkeys

The settings snapshot is composed of immutable records and immutable
collections. Validation covers schema version, audio thresholds and duration,
model/language/thread choices, insertion delay, initialized and unique text
rules, valid hotkeys, and enabled-binding collisions.

Hotkeys support:

- modifier-only chords such as the default `Ctrl+Win`;
- logical normalization of left/right physical modifiers;
- keyboard and mouse primary keys;
- stable chord display; and
- exact enabled-binding conflict detection.

### Deterministic text processing

`DictaClone.Text` now provides a local deterministic pipeline for:

- conservative prose whitespace and punctuation normalization;
- multiline and code-indentation preservation;
- case-insensitive whole-token vocabulary replacements;
- whole-utterance text expansions; and
- bounded trailing-phrase corrections introduced by “actually” or “I mean.”

Vocabulary replacement treats replacement text literally, including `$` and
other regular-expression replacement characters. Correction rules operate per
line and intentionally fall back to removing only the correction marker when a
safe trailing phrase cannot be identified.

## Automated verification

The standard coverage run is:

```powershell
.\scripts\Test.ps1 -Clean -Coverage
```

`Test.ps1` creates an isolated result directory for each run, merges duplicate
Core source lines emitted relative to different test-project roots, and fails
when `DictaClone.Core` line coverage is below 90%.

Milestone 1 verification on the target Windows 11 x64 laptop:

| Check | Result |
| --- | ---: |
| Restore and Release build | Pass, 0 warnings and 0 errors |
| All automated tests | 69 passed, 0 failed |
| Core tests | 38 passed |
| Text tests | 21 passed |
| Existing audio/speech/integration/end-to-end/Windows tests | 10 passed |
| `DictaClone.Core` merged line coverage | 94.95% (432/455) |
| Required Core line coverage | 90% |
| PowerShell syntax validation | Pass |
| C# formatting and repository whitespace checks | Pass |

The Core regression suite covers successful state transitions, repeat-key and
concurrent starts, cancellation while recording and transcribing, silence and
empty results, target changes, dependency failures, recovery to idle,
idempotent shutdown, invalid settings, hotkey normalization, and conflicts.

## Milestone 1 exit gate

| Exit criterion | Status |
| --- | --- |
| Core has no Windows/UI package dependency | Pass |
| State, repeat-key, cancellation, silence, failure, and concurrency tests pass | Pass |
| Core line coverage is at least 90% | Pass (94.95%) |

Milestone 2 remains intentionally unstarted pending review.
