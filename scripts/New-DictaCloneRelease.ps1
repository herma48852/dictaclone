[CmdletBinding()]
param(
    [string] $Version,

    [switch] $SkipTests,

    [switch] $AllowDirty,

    [string] $InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-RepositoryVersion
}

$gitStatus = @(& git -C $script:RepositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the Git worktree.'
}

if (!$AllowDirty -and $gitStatus.Count -gt 0) {
    throw 'Release creation requires a clean worktree. Commit changes or use -AllowDirty for a local qualification build.'
}

if (!$SkipTests) {
    & (Join-Path $PSScriptRoot 'Test.ps1') `
        -Configuration Release `
        -Clean `
        -Coverage
    if ($LASTEXITCODE -ne 0) {
        throw "Release tests failed with exit code $LASTEXITCODE."
    }
}

$stagingDirectory = Assert-RepositoryChildPath (
    Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\staging' "$Version\win-x64\publish"))
$releaseDirectory = Assert-RepositoryChildPath (
    Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\release' $Version))

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

$installationGuidePath = Join-Path `
    $releaseDirectory `
    'CLEAN_ROOM_INSTALLATION.md'
Copy-Item `
    -LiteralPath (Join-Path `
        $script:RepositoryRoot `
        'docs\CLEAN_ROOM_INSTALLATION.md') `
    -Destination $installationGuidePath

& (Join-Path $PSScriptRoot 'Publish-DictaClone.ps1') `
    -Version $Version `
    -OutputDirectory $stagingDirectory |
    Out-Host

$portableName = "DictaClone-$Version-win-x64-portable.zip"
$portablePath = Join-Path $releaseDirectory $portableName
Compress-Archive `
    -Path (Join-Path $stagingDirectory '*') `
    -DestinationPath $portablePath `
    -CompressionLevel Optimal

$installerName = "DictaClone-$Version-win-x64-setup.exe"
$installerPath = Join-Path $releaseDirectory $installerName
& (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
    -Version $Version `
    -PublishDirectory $stagingDirectory `
    -OutputDirectory $releaseDirectory `
    -OutputBaseFilename ([IO.Path]::GetFileNameWithoutExtension($installerName)) `
    -CompilerPath $InnoCompilerPath |
    Out-Host

if (!(Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer output is missing: $installerPath"
}

$gitCommit = (& git -C $script:RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve the Git commit for the release manifest.'
}

$artifactEntries = @()
foreach ($path in @($portablePath, $installerPath, $installationGuidePath)) {
    $file = Get-Item -LiteralPath $path
    $artifactEntries += [ordered]@{
        file = $file.Name
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
$manifest = [ordered]@{
    schemaVersion = 1
    product = 'DictaClone'
    version = $Version
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    singleFile = $false
    gitCommit = $gitCommit
    sourceDirty = $gitStatus.Count -gt 0
    createdUtc = (Get-Date).ToUniversalTime().ToString('O')
    installer = [ordered]@{
        format = 'Inno Setup 6.7.3 per-user EXE'
        privileges = 'lowest'
        authenticodeStatus = $signature.Status.ToString()
    }
    artifacts = $artifactEntries
}

$manifestPath = Join-Path $releaseDirectory 'release-manifest.json'
$manifest |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

$checksumPaths = @(
    $portablePath,
    $installerPath,
    $installationGuidePath,
    $manifestPath) |
    Sort-Object { [IO.Path]::GetFileName($_) }
$checksumLines = foreach ($path in $checksumPaths) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $name = [IO.Path]::GetFileName($path)
    "$hash  $name"
}
$checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Created DictaClone $Version release artifacts in $releaseDirectory"
Get-ChildItem -LiteralPath $releaseDirectory -File |
    Sort-Object Name |
    Select-Object Name, Length
