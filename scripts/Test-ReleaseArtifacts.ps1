[CmdletBinding()]
param(
    [string] $Version,

    [string] $ReleaseDirectory,

    [ValidateRange(5, 120)]
    [int] $SmokeTimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-RepositoryVersion
}

if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\release' $Version)
}

$releaseDirectoryPath = Assert-RepositoryChildPath $ReleaseDirectory
$portableName = "DictaClone-$Version-win-x64-portable.zip"
$installerName = "DictaClone-$Version-win-x64-setup.exe"
$requiredArtifacts = @(
    $portableName,
    $installerName,
    'release-manifest.json',
    'SHA256SUMS.txt'
)

foreach ($name in $requiredArtifacts) {
    if (!(Test-Path -LiteralPath `
            (Join-Path $releaseDirectoryPath $name) `
            -PathType Leaf)) {
        throw "Release artifact is missing: $name"
    }
}

$checksumPath = Join-Path $releaseDirectoryPath 'SHA256SUMS.txt'
$checksumEntries = @{}
foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        throw "Invalid SHA256SUMS.txt line: $line"
    }

    $checksumEntries[$Matches[2]] = $Matches[1]
}

$expectedChecksumNames = @(
    $portableName,
    $installerName,
    'release-manifest.json'
)
foreach ($name in $expectedChecksumNames) {
    if (!$checksumEntries.ContainsKey($name)) {
        throw "SHA256SUMS.txt does not cover $name."
    }

    $actual = (Get-FileHash `
        -LiteralPath (Join-Path $releaseDirectoryPath $name) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $checksumEntries[$name]) {
        throw "SHA-256 mismatch for $name."
    }
}

if ($checksumEntries.Count -ne $expectedChecksumNames.Count) {
    throw 'SHA256SUMS.txt contains unexpected or duplicate entries.'
}

$manifest = Get-Content `
    -LiteralPath (Join-Path $releaseDirectoryPath 'release-manifest.json') `
    -Raw |
    ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or
    $manifest.product -ne 'DictaClone' -or
    $manifest.version -ne $Version -or
    $manifest.runtimeIdentifier -ne 'win-x64' -or
    !$manifest.selfContained -or
    $manifest.singleFile) {
    throw 'Release manifest metadata is inconsistent with this release.'
}

$validationDirectory = Assert-RepositoryChildPath (
    Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\validation' ([guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $validationDirectory -Force | Out-Null

try {
    Expand-Archive `
        -LiteralPath (Join-Path $releaseDirectoryPath $portableName) `
        -DestinationPath $validationDirectory

    $requiredPayloadFiles = @(
        'DictaClone.App.exe',
        'DictaClone.App.dll',
        'coreclr.dll',
        'hostfxr.dll',
        'THIRD-PARTY-NOTICES.md',
        'MODEL-LICENSES.md',
        'RELEASE_NOTES.md',
        'ROLLBACK.md'
    )
    foreach ($name in $requiredPayloadFiles) {
        if (!(Test-Path -LiteralPath `
                (Join-Path $validationDirectory $name) `
                -PathType Leaf)) {
            throw "Portable archive is missing required payload file: $name"
        }
    }

    $forbiddenFiles = @(
        Get-ChildItem -LiteralPath $validationDirectory -Recurse -File |
            Where-Object {
                $_.Name -in @(
                    'settings.json',
                    'history.json',
                    'diagnostics.jsonl') -or
                $_.Extension -in @('.pdb', '.bin')
            }
    )
    if ($forbiddenFiles.Count -gt 0) {
        throw ('Portable archive contains generated, private, model, or debug ' +
            "files: $($forbiddenFiles.FullName -join ', ')")
    }

    function Get-PeMachine {
        param([Parameter(Mandatory)][string] $Path)

        $stream = [IO.File]::OpenRead($Path)
        $reader = [IO.BinaryReader]::new($stream)
        try {
            $stream.Position = 0x3c
            $peOffset = $reader.ReadInt32()
            $stream.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) {
                throw "File is not a valid PE image: $Path"
            }

            return $reader.ReadUInt16()
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }
    }

    $appPath = Join-Path $validationDirectory 'DictaClone.App.exe'
    $installerPath = Join-Path $releaseDirectoryPath $installerName
    if ((Get-PeMachine $appPath) -ne 0x8664) {
        throw 'Portable application host is not x64.'
    }
    if ((Get-PeMachine $installerPath) -notin @(0x014c, 0x8664)) {
        throw 'Installer host is not a supported Windows PE architecture.'
    }

    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        $appPath).FileVersion
    if (!$fileVersion.StartsWith("$Version.", [StringComparison]::Ordinal)) {
        throw "Application file version '$fileVersion' does not match $Version."
    }

    $process = Start-Process `
        -FilePath $appPath `
        -ArgumentList '--smoke-test' `
        -WorkingDirectory $validationDirectory `
        -WindowStyle Hidden `
        -PassThru
    if (!$process.WaitForExit($SmokeTimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        throw "Portable smoke test exceeded $SmokeTimeoutSeconds seconds."
    }

    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        throw "Portable smoke test exited with code $($process.ExitCode)."
    }
}
finally {
    if (Test-Path -LiteralPath $validationDirectory) {
        $validatedDirectory = Assert-RepositoryChildPath $validationDirectory
        Remove-Item -LiteralPath $validatedDirectory -Recurse -Force
    }
}

Write-Host 'Release artifact validation passed:'
Write-Host "  version: $Version"
Write-Host '  runtime: win-x64 self-contained'
Write-Host '  portable smoke test: passed'
Write-Host '  checksums: passed'
