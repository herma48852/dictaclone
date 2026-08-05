[CmdletBinding()]
param(
    [switch] $SkipAutomatedTests,

    [switch] $SkipInstallerLifecycle,

    [string] $InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$version = Get-RepositoryVersion

if (!$SkipAutomatedTests) {
    & (Join-Path $PSScriptRoot 'Test.ps1') `
        -Configuration Release `
        -Clean `
        -Coverage
    if ($LASTEXITCODE -ne 0) {
        throw "Automated tests failed with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'New-DictaCloneRelease.ps1') `
    -Version $version `
    -SkipTests `
    -AllowDirty `
    -InnoCompilerPath $InnoCompilerPath
if ($LASTEXITCODE -ne 0) {
    throw "Release creation failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot 'Test-ReleaseArtifacts.ps1') `
    -Version $version
if ($LASTEXITCODE -ne 0) {
    throw "Artifact validation failed with exit code $LASTEXITCODE."
}

if (!$SkipInstallerLifecycle) {
    & (Join-Path $PSScriptRoot 'Test-InstallerLifecycle.ps1') `
        -Version $version `
        -InnoCompilerPath $InnoCompilerPath
    if ($LASTEXITCODE -ne 0) {
        throw "Installer lifecycle validation failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Milestone 7 automated regression passed for DictaClone $version."
