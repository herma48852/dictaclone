# Milestone 4 Status

Milestone 4 is **accepted** as of 2026-08-01. The implementation and automated
exit gates are complete, and the user confirmed the manual insertion test
succeeded. Broader compatibility checks in additional installed applications
remain useful follow-up coverage but do not block progression.

## Implemented scope

- Captures the foreground window handle and process when recording starts and
  revalidates that exact target immediately before insertion.
- Identifies targets running above DictaClone's process integrity level and
  reports the Windows UIPI boundary instead of silently failing or elevating the
  entire application.
- Paste Mode snapshots all practical clipboard formats on an STA thread,
  retries transient clipboard contention with bounded backoff, inserts Unicode
  text with Ctrl+V, and restores only when the clipboard sequence shows that
  DictaClone still owns the transaction.
- Paste Mode uses a 250 ms target-settle window before restoration. This closes
  a real race found under parallel test-host load, where the target could read
  the restored clipboard after a shorter 75 ms window.
- Typing Mode never accesses the clipboard. It preserves Unicode surrogate
  pairs, maps CRLF/CR/LF to Enter, maps tabs to Tab, and uses configurable
  0-100 ms character delay.
- Local targets receive Unicode `SendInput`; recognized RDP, Citrix, and VMware
  targets prefer mapped virtual-key/scancode input with Unicode fallback.
- The x64 native `INPUT` union has the correct 40-byte layout. The first real
  insertion run caught and permanently regressed an undersized interop union
  that Windows rejected with error 87.
- Live dictation inserts only after transcription and text processing succeed.
  The dedicated Typing Mode shortcut forces delayed typing; normal dictation
  uses the selected default insertion mode.
- The settings window exposes Paste or Delayed Typing as the default and a
  0-100 ms typing delay. Settings apply for the current run; disk persistence is
  intentionally part of Milestone 5.
- Lost focus, elevated targets, clipboard contention, blocked input, and
  cancellation have distinct status messages and cleanup paths.

## Automated evidence

The final clean Release gate was run with:

```powershell
.\scripts\Test.ps1 -Configuration Release -Clean -Coverage
```

Result:

- Restore and clean build passed with zero warnings and zero errors.
- All 200 tests passed.
- `DictaClone.Core` line coverage: 97.14% (476/490), above the 90% gate.
- Focused Milestone 4 suites: 40 Core, 59 Windows, 38 App, and 4 end-to-end
  tests passed.
- The real insertion test was also repeated three consecutive times after the
  clipboard timing regression was fixed.

The automated WPF target corpus covers:

- Single-line and multiline text.
- Punctuation.
- German, Greek, and Chinese text.
- Emoji and UTF-16 surrogate pairs.
- A 4,096-character transcript.
- Exact Paste Mode clipboard restoration.
- Exact Typing Mode text with line endings and tabs.
- Proof that Typing Mode does not change the clipboard sequence or contents.

Unit and component regressions additionally cover target mismatch, missing
foreground target, elevated target, transient and persistent clipboard
contention, concurrent clipboard replacement, input failure, cancellation,
retry timing, remote-target mapping, delay validation, native input layout, and
live workflow status/error behavior.

## Manual review result

On 2026-08-01, the user ran the Milestone 4 manual test and reported success.
This accepted the live Paste Mode, Typing Mode, clipboard-preservation, and
focus-safe insertion workflow for progression to the next milestone.

The review command was:

Run the safe dedicated target first:

```powershell
.\scripts\Invoke-Milestone4ManualTest.ps1 -Target TestTarget -NoBuild
```

The script prints the exact checklist for Paste Mode, Typing Mode, clipboard
preservation, focus-change rejection, and insertion settings. Repeat by changing
`-Target` to any installed application:

- `Notepad`
- `Edge`
- `VSCode`
- `Terminal`
- `Word`
- `Outlook`

An available RDP or Citrix session must still be checked manually because no
such session is created by the test suite. Running DictaClone non-elevated
against an elevated editor should show the actionable elevated-target error and
insert no text.

## Follow-up compatibility coverage

The wider Notepad, browser, VS Code, Windows Terminal, Office, and remote-session
matrix remains available through the same script. Any application-specific
failure should receive a regression test or numbered manual case. These changes
remain uncommitted until the user requests a commit.
