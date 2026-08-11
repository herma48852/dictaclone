[CmdletBinding()]
param(
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$appProject = Join-Path `
    $script:RepositoryRoot `
    'src\DictaClone.App\DictaClone.App.csproj'
$appAssembly = Join-Path `
    $script:RepositoryRoot `
    'src\DictaClone.App\bin\Release\net10.0-windows10.0.22000.0\DictaClone.App.dll'

if (!$NoBuild) {
    Invoke-CheckedCommand `
        $dotNet `
        build `
        $appProject `
        --configuration `
        Release `
        --no-restore `
        --verbosity `
        minimal `
        '-maxcpucount:1'
}

if (!(Test-Path -LiteralPath $appAssembly)) {
    throw "DictaClone was not found at $appAssembly."
}

$appProcess = Start-Process `
    -FilePath $dotNet `
    -ArgumentList $appAssembly `
    -WindowStyle Hidden `
    -PassThru

Write-Output "DictaClone started with process ID $($appProcess.Id)."
Write-Output @'
Milestone 5 manual review (uses %LocalAppData%\DictaClone):
1. If this is the first persisted run, the first-run settings window should
   open. Choose the microphone/model, then use Privacy & recovery > Complete
   setup. If setup was already completed, open settings from the tray instead.
2. In the "DictaClone first-run setup" or "DictaClone settings" window, select
   the "Knowledge" tab. In the "Work domain" combo box, select "Software
   development". Under the "Vocabulary" heading, select its "Add row" button;
   enter "jay son" in the "Spoken form" column and "JSON" in the "Written
   form" column. Under the "Expansions" heading, select its separate "Add row"
   button; enter "test signature" in the "Trigger" column and "Kind regards"
   in the "Replacement" column. Select the "Apply knowledge" button and expect
   "Knowledge settings submitted for saving." Close the settings window using
   its X button. Right-click the "DictaClone" notification-area icon, select
   "Open settings", select the "Knowledge" tab, and verify all three values.
   For the Paste Mode baseline, open Windows Notepad, click its main editing
   area, hold Ctrl+Shift+Space, say "open the jay son file", and release; the
   inserted text should contain "JSON". For native GNU Emacs, place point in an
   editable buffer, hold the default Dictation shortcut Ctrl+Shift+Space, say the
   same phrase, and release. DictaClone should use Emacs's Ctrl+Y yank command,
   insert text containing "JSON", and restore the previous Windows clipboard.
   Repeat in a new blank line with the DictaClone Typing Mode shortcut
   Ctrl+Alt+Space; the same Emacs compatibility path should insert the text.
   In another new blank line, use either DictaClone shortcut, say only "test
   signature", and release; the inserted text should be "Kind regards"
   (terminal punctuation is also acceptable).
3. Dictate a result, then use tray > Copy last result and paste it manually.
   Repeat while deliberately changing focus before release: insertion should be
   rejected, but Copy last result should still recover the final text.
4. In Privacy & recovery, enable local history with a limit of 2. Complete three
   dictations. Tray > Transcript history should show only the newest two; Copy
   selected should copy the exact text. Clear history and confirm the list is
   empty. Disable history again if you do not want it retained.
5. Export settings, then import that exported JSON. Create a support bundle.
   These actions should report success without changing dictation behavior.
   The bundle should contain system.json, settings-summary.json, and optional
   diagnostics.jsonl, but no settings.json, history.json, transcript text,
   vocabulary text, microphone ID, or clipboard content.
6. Select Microphone privacy settings and confirm Windows opens Privacy &
   security > Microphone. Return without changing permission unless intended.
7. Toggle Start with Windows on, apply, reopen settings to confirm it persisted,
   then toggle it off and apply unless startup is desired.
8. With multiple monitors or mixed display scaling, dictate once with a target
   on each display. The status pill should use that display's work area, remain
   crisp, and never take focus. Keyboard Tab/Shift+Tab and arrow navigation
   should reach every settings tab, field, grid, and button.
9. Exit from the tray, rerun this script with -NoBuild, and verify settings
   survive restart and first-run setup does not reappear. Ctrl+Win+Left/Right
   must still switch virtual desktops without starting a recording.
10. Exit DictaClone from the tray when finished. Closing settings or this
    PowerShell session does not stop the tray process.
'@
