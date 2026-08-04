[CmdletBinding()]
param(
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$configuration = 'Release'
$solution = Join-Path $script:RepositoryRoot 'DictaClone.slnx'

if (!$NoBuild) {
    Invoke-CheckedCommand `
        $dotNet `
        build `
        $solution `
        --configuration `
        $configuration `
        --no-restore `
        --verbosity `
        minimal `
        '-maxcpucount:1'
}

Invoke-CheckedCommand `
    $dotNet `
    test `
    $solution `
    --configuration `
    $configuration `
    --no-build `
    --no-restore `
    --nologo `
    --filter `
    'Category!=LiveProvider&Category!=DesktopE2E' `
    '-maxcpucount:1'
