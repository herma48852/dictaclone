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
    -PassThru

Write-Output "DictaClone started with process ID $($appProcess.Id)."
Write-Output @'
Milestone 3 manual check:
1. Hold Ctrl+Shift+Space and speak a short sentence. The red Listening pill and
   live level meter should respond only while the chord is held.
2. Release Space. The pill should show local transcription progress and then
   display the recognized text. Text insertion is Milestone 4 and is not
   expected yet.
3. Switch virtual desktops with Ctrl+Win+Left/Right. This must not start a
   recording or show the red pill.
4. Double-click the tray icon, choose System default or a specific microphone,
   select base.en/small.en and en/auto, adjust silence sensitivity, and apply.
5. With the model already installed, disconnect the network and repeat step 1.
   Transcription should still work.
6. Exit DictaClone from its notification-area menu when finished. Closing a
   target window or this PowerShell session does not stop the tray process.
'@
