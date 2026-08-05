[CmdletBinding()]
param(
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$project = Join-Path `
    $script:RepositoryRoot `
    'tests\DictaClone.EndToEndTests\DictaClone.EndToEndTests.csproj'

Write-Host @"
DictaClone 50-cycle desktop stress test

This test opens the window "DictaClone Test Target" and repeatedly changes the
real Windows clipboard and foreground focus. Until the command finishes:

  - do not use the keyboard or mouse;
  - do not change windows or virtual desktops;
  - do not copy or paste in another application; and
  - leave "DictaClone Test Target" in the foreground.

Expected duration after build: approximately 30 seconds.
"@

if (!$NoBuild) {
    Invoke-CheckedCommand `
        $dotNet `
        build `
        $project `
        --configuration `
        Release `
        --no-restore `
        --verbosity `
        minimal `
        '-maxcpucount:1'
}

Invoke-CheckedCommand `
    $dotNet `
    test `
    $project `
    --configuration `
    Release `
    --no-build `
    --no-restore `
    --filter `
    'Category=ManualDesktopStress' `
    '-maxcpucount:1'
