[CmdletBinding()]
param(
    [string] $Version,

    [string] $PublishDirectory,

    [string] $OutputDirectory,

    [string] $OutputBaseFilename,

    [string] $CompilerPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-RepositoryVersion
}

$parsedVersion = $null
if (![Version]::TryParse($Version, [ref] $parsedVersion) -or
    $parsedVersion.Revision -ge 0) {
    throw 'Version must contain exactly three numeric components, such as 0.1.0.'
}

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\staging' "$Version\win-x64\publish")
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\release' $Version)
}

if ([string]::IsNullOrWhiteSpace($OutputBaseFilename)) {
    $OutputBaseFilename = "DictaClone-$Version-win-x64-setup"
}

$publishDirectoryPath = Assert-RepositoryChildPath $PublishDirectory
$outputDirectoryPath = Assert-RepositoryChildPath $OutputDirectory

if (!(Test-Path -LiteralPath `
        (Join-Path $publishDirectoryPath 'DictaClone.App.exe') `
        -PathType Leaf)) {
    throw "Publish output was not found: $publishDirectoryPath"
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $CompilerPath = $command.Source
    }
}

if ([string]::IsNullOrWhiteSpace($CompilerPath) -and
    ![string]::IsNullOrWhiteSpace($env:DICTACLONE_ISCC)) {
    $CompilerPath = $env:DICTACLONE_ISCC
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $candidatePaths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    )
    $CompilerPath = $candidatePaths |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($CompilerPath) -or
    !(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw ('Inno Setup ISCC.exe was not found. Install pinned version 6.7.3 ' +
        'for the current user or set DICTACLONE_ISCC to its full path.')
}

New-Item -ItemType Directory -Path $outputDirectoryPath -Force | Out-Null
$installerScript = Join-Path `
    $script:RepositoryRoot `
    'installer\DictaClone.iss'
$versionInfoVersion = '{0}.{1}.{2}.0' -f `
    $parsedVersion.Major, `
    $parsedVersion.Minor, `
    $parsedVersion.Build

Invoke-CheckedCommand `
    $CompilerPath `
    '/Qp' `
    "/DMyAppVersion=$Version" `
    "/DMyVersionInfoVersion=$versionInfoVersion" `
    "/DMySourceDir=$publishDirectoryPath" `
    "/DMyOutputDir=$outputDirectoryPath" `
    "/DMyOutputBaseFilename=$OutputBaseFilename" `
    $installerScript

$installerPath = Join-Path $outputDirectoryPath "$OutputBaseFilename.exe"
if (!(Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Inno Setup did not produce the expected installer: $installerPath"
}

Write-Host "Built installer $installerPath"
return $installerPath
