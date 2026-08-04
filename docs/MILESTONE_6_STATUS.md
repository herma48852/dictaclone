# Milestone 6 status — Smart Edit and selected-text editing

Status: Complete and accepted.

Last updated: 2026-08-04

## Delivered

- Smart Edit is off by default. Its `Alt+Shift+Space` hold-to-talk shortcut is
  also disabled until provider consent and a credential are saved. Schema v1
  and v2 settings migrate to schema v3 with this safe default.
- The **Smart Edit** settings tab names the exact data sent: the locally
  transcribed spoken instruction and explicitly selected text. Microphone audio
  remains local.
- Provider endpoint, model, timeout, retry count, and optional custom
  instructions are ordinary settings. The API key is stored separately as a
  per-user generic credential in Windows Credential Manager and is never
  exported, logged, or placed in `settings.json`.
- The initial provider adapter uses the OpenAI Responses API over HTTPS. The
  default model is `gpt-5.6-sol`, selected from the current official model
  guidance; endpoint and model remain configurable. See the official
  [GPT-5.6 Sol model page](https://developers.openai.com/api/docs/models/gpt-5.6-sol)
  and [latest-model guide](https://developers.openai.com/api/docs/guides/latest-model).
- Requests separate system instructions from user input, mark selected text
  with explicit boundaries, treat selection contents as untrusted data, and
  include the configured vocabulary, work domain, and custom instructions.
- A selected-text transaction saves and restores the clipboard using sequence
  ownership checks, supports standard `Ctrl+C` and native Emacs `Alt+W`, and
  fingerprints the exact captured text.
- A provider result is inserted only after both the original foreground target
  and exact selected text are revalidated. If either changed, no insertion
  occurs and the generated result remains available through **Copy last
  result**.
- Provider handling has a 30-second default timeout, one bounded retry for HTTP
  429/5xx or disconnects, cancellation, `Retry-After` support capped at two
  seconds, and actionable authentication, rate-limit, timeout, offline,
  malformed-response, focus-change, and selection-change messages.
- Normal tests cannot call a paid service. The live contract test requires both
  `DICTACLONE_RUN_LIVE_SMART_EDIT=1` and
  `DICTACLONE_OPENAI_API_KEY`; the dedicated script additionally requires
  `-AllowPaidProviderCall`.

## Automated evidence

- `scripts/Invoke-Milestone6Regression.ps1`: 258 offline,
  non-desktop Release cases pass. This includes provider transport contracts,
  prompt boundaries, settings migration/validation, Windows credential
  round-trip/delete, selection capture/revalidation, UI consent, controller
  recovery, and all earlier non-desktop regressions.
- The complete Release suite contains 261 offline cases: the 258 cases above
  plus three isolated desktop E2E cases. All 259 have passed during this
  milestone. Desktop E2E is intentionally isolated from coverage because live
  clipboard/focus instrumentation can interfere with its real Windows desktop
  transaction.
- The final coverage run passed all 258 non-desktop cases.
  `DictaClone.Core` line coverage is **94.61%** (614/649), above the required
  90% gate. The three live-desktop E2E cases are run separately because
  coverage instrumentation can contend with their real clipboard/focus work.
- The live-provider test was not run because no user secret or authorization
  was supplied. No implementation or regression command contacted a provider.

## Manual review

Accepted by the user on 2026-08-04. The manual Smart Edit review passed.

Run this from PowerShell:

```powershell
.\scripts\Invoke-Milestone6ManualTest.ps1 -NoBuild
```

The script launches the Release app and Notepad, then prints ten steps using
the complete settings-tab, checkbox, field, button, tray-command, and shortcut
names. A real provider call can consume billable tokens. The review covers:

1. explicit provider disclosure, key storage, and enablement;
2. selected-text replacement with `Alt+Shift+Space`;
3. foreground-target and selection-change rejection;
4. **Copy last result** recovery;
5. offline/timeout behavior; and
6. credential removal and shortcut disablement.

Milestone 6 is ready to commit when requested. Milestone 7 packaging and release
qualification is next.

## Verification incident note

An early solution build left orphaned reusable MSBuild nodes after its parent
process exited. Those exact orphan groups were identified by parent process ID
and stopped without touching the separately running DictaClone test instance or
other .NET work. The older DictaClone Release instance was then identified as
the assembly-lock owner and stopped before the successful Release build. This
was a build-environment cleanup issue, not a Smart Edit runtime failure.
