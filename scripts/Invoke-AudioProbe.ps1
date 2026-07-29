[CmdletBinding()]
param(
    [ValidateRange(0.1, 10)]
    [double] $CaptureSeconds = 1
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$project = Join-Path $script:RepositoryRoot 'tools\DictaClone.DevTools\DictaClone.DevTools.csproj'

Push-Location $script:RepositoryRoot

try {
    Invoke-CheckedCommand $dotNet run --project $project --configuration Release --no-build -- devices
    Invoke-CheckedCommand $dotNet run --project $project --configuration Release --no-build -- capture $CaptureSeconds
}
finally {
    Pop-Location
}
