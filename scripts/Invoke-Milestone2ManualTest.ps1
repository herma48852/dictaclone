[CmdletBinding()]
param(
    [ValidateSet('None', 'Notepad', 'Edge', 'VSCode', 'TestTarget')]
    [string] $Target = 'None',

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
    -PassThru
Write-Output "DictaClone started with process ID $($appProcess.Id)."

switch ($Target) {
    'Notepad' {
        $null = Start-Process -FilePath 'notepad.exe' -PassThru
    }
    'Edge' {
        $edgeCandidates = @(
            (Join-Path `
                ${env:ProgramFiles(x86)} `
                'Microsoft\Edge\Application\msedge.exe'),
            (Join-Path `
                $env:ProgramFiles `
                'Microsoft\Edge\Application\msedge.exe')
        )
        $edge = $edgeCandidates |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if (!$edge) {
            throw 'Microsoft Edge was not found.'
        }

        $null = Start-Process -FilePath $edge -PassThru
    }
    'VSCode' {
        $code = Get-Command 'code.cmd' -ErrorAction SilentlyContinue
        if (!$code) {
            throw 'Visual Studio Code was not found on PATH.'
        }

        $null = Start-Process -FilePath $code.Source -PassThru
    }
    'TestTarget' {
        $targetAssembly = Join-Path `
            $script:RepositoryRoot `
            'tests\DictaClone.TestTarget\bin\Release\net10.0-windows10.0.22000.0\DictaClone.TestTarget.dll'
        if (!(Test-Path -LiteralPath $targetAssembly)) {
            Invoke-CheckedCommand `
                $dotNet `
                build `
                (Join-Path `
                    $script:RepositoryRoot `
                    'tests\DictaClone.TestTarget\DictaClone.TestTarget.csproj') `
                --configuration `
                $configuration `
                --no-restore `
                --verbosity `
                minimal `
                '-maxcpucount:1'
        }

        $null = Start-Process `
            -FilePath $dotNet `
            -ArgumentList $targetAssembly `
            -PassThru
    }
}

Write-Output @'
Manual check:
1. Focus the target application.
2. Hold Ctrl+Shift+Space. The red Listening pill should appear without moving focus.
3. Release the keys. The pill should show Working, then Shortcut detected.
4. Double-click the DictaClone notification-area icon to test shortcut recording.
5. Exit from the notification-area menu and confirm the shortcut no longer reacts.
Closing the target window does not stop DictaClone; use Exit in the tray menu.
'@
