[CmdletBinding()]
param(
    [ValidateSet(
        'None',
        'TestTarget',
        'Notepad',
        'Edge',
        'VSCode',
        'Terminal',
        'Word',
        'Outlook')]
    [string] $Target = 'TestTarget',

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
$targetProject = Join-Path `
    $script:RepositoryRoot `
    'tests\DictaClone.TestTarget\DictaClone.TestTarget.csproj'
$targetAssembly = Join-Path `
    $script:RepositoryRoot `
    'tests\DictaClone.TestTarget\bin\Release\net10.0-windows10.0.22000.0\DictaClone.TestTarget.dll'

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

    if ($Target -eq 'TestTarget') {
        Invoke-CheckedCommand `
            $dotNet `
            build `
            $targetProject `
            --configuration `
            $configuration `
            --no-restore `
            --verbosity `
            minimal `
            '-maxcpucount:1'
    }
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

switch ($Target) {
    'TestTarget' {
        if (!(Test-Path -LiteralPath $targetAssembly)) {
            throw "The test target was not found at $targetAssembly."
        }

        $null = Start-Process `
            -FilePath $dotNet `
            -ArgumentList $targetAssembly `
            -PassThru
    }
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
    'Terminal' {
        $terminal = Get-Command 'wt.exe' -ErrorAction SilentlyContinue
        if (!$terminal) {
            throw 'Windows Terminal was not found.'
        }

        $null = Start-Process -FilePath $terminal.Source -PassThru
    }
    'Word' {
        $null = Start-Process -FilePath 'winword.exe' -PassThru
    }
    'Outlook' {
        $null = Start-Process -FilePath 'outlook.exe' -PassThru
    }
}

Write-Output @'
Milestone 4 manual compatibility check:
1. In any editor, copy the text "M4 clipboard sentinel" so it is on the
   clipboard. Focus a blank insertion point in the selected target.
2. Hold Ctrl+Shift+Space, say "testing one two three", and release. Paste Mode
   should insert the recognized text at the original cursor. Pasting manually
   afterward should still produce the clipboard sentinel.
3. Reset the sentinel, focus a blank insertion point, hold Ctrl+Alt+Space,
   speak, and release. Typing Mode should insert the text character by
   character, and manually pasting afterward should still produce the sentinel.
4. Start dictation in one window, move focus to a different window before
   release, and release. DictaClone should report that focus changed and insert
   into neither window.
5. Double-click the tray icon. Change Default insertion and the 0-100 ms Typing
   delay, choose Apply settings, and repeat. These settings apply for this run.
6. Repeat with -Target Notepad, Edge, VSCode, Terminal, Word, or Outlook as
   installed. In a terminal, use a harmless blank prompt and avoid dictating a
   command. Test RDP/Citrix manually if a session is available.
7. Exit DictaClone from its notification-area menu when finished. Closing the
   target or this PowerShell session does not stop the tray process.
'@
