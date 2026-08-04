[CmdletBinding()]
param(
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$configuration = 'Release'
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
        $configuration `
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
$null = Start-Process -FilePath 'notepad.exe' -PassThru

Write-Output @'
Milestone 6 Smart Edit manual review:
1. Double-click the "DictaClone" notification-area icon to open "DictaClone
   settings". Select the "Smart Edit" tab.
2. In "Provider HTTPS endpoint", keep
   https://api.openai.com/v1/responses unless your provider specifies another
   HTTPS Responses endpoint. In "Provider model", keep gpt-5.6-sol or enter a
   model available to your account.
3. In "API key (leave blank to keep the stored key)", enter the provider API
   key. Select the "Enable cloud Smart Edit and allow selected text to be sent"
   checkbox. Read the disclosure above it, then choose "Apply Smart Edit
   settings". This action stores the key in Windows Credential Manager; it does
   not put the key in settings.json.
4. In Notepad, type: This sentence is unnecessarily and excessively wordy.
   Select that exact sentence with the mouse or Shift+Arrow keys.
5. Hold Alt+Shift+Space, say "make this concise", and release. Expect the blue
   processing pill, then the selected sentence alone should be replaced by a
   concise result. Unselected surrounding text must not change.
6. Select another sentence. Hold Alt+Shift+Space, say "rewrite in active
   voice", move focus to the settings window before the request completes, and
   release if needed. Expect "Focus changed" and no replacement. Use the
   notification-area icon's "Copy last result" command if a result was already
   returned.
7. Repeat step 5 but change or clear the selection while Smart Edit is
   processing. Expect "Selection changed" and no replacement; "Copy last
   result" should recover the generated result.
8. Disconnect the network and repeat. Expect an unavailable or timeout message
   and unchanged selected text. Reconnect afterward.
9. Reopen the "Smart Edit" tab and choose "Remove stored API key". Smart Edit
   and its Alt+Shift+Space shortcut should become disabled. Pressing that
   shortcut must not start a recording.
10. Exit DictaClone from its notification-area menu when finished. A provider
   call can consume billable tokens; the normal regression script never calls
   a live provider.
'@
