[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [switch] $AllowPaidProviderCall,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

if (!$AllowPaidProviderCall) {
    throw 'Pass -AllowPaidProviderCall to explicitly authorize one live provider test.'
}

if ([string]::IsNullOrWhiteSpace($env:DICTACLONE_OPENAI_API_KEY)) {
    throw 'Set DICTACLONE_OPENAI_API_KEY in this PowerShell process first.'
}

$env:DICTACLONE_RUN_LIVE_SMART_EDIT = '1'
$dotNet = Get-RepositoryDotNet
$project = Join-Path `
    $script:RepositoryRoot `
    'tests\DictaClone.Text.Tests\DictaClone.Text.Tests.csproj'
$arguments = @(
    'test',
    $project,
    '--configuration',
    'Release',
    '--no-restore',
    '--filter',
    'Category=LiveProvider',
    '-maxcpucount:1'
)
if ($NoBuild) {
    $arguments += '--no-build'
}

try {
    Invoke-CheckedCommand $dotNet @arguments
}
finally {
    Remove-Item Env:DICTACLONE_RUN_LIVE_SMART_EDIT -ErrorAction SilentlyContinue
}
