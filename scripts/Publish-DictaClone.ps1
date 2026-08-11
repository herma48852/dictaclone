[CmdletBinding()]
param(
    [string] $Version,

    [string] $OutputDirectory
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

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\staging' "$Version\win-x64\publish")
}

$publishDirectory = Assert-RepositoryChildPath $OutputDirectory
$appProject = Join-Path `
    $script:RepositoryRoot `
    'src\DictaClone.App\DictaClone.App.csproj'
$dotNet = Get-RepositoryDotNet

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $script:RepositoryRoot
try {
    Invoke-CheckedCommand `
        $dotNet `
        restore `
        $appProject `
        --locked-mode `
        --disable-parallel `
        --verbosity `
        minimal `
        '-maxcpucount:1'

    Invoke-CheckedCommand `
        $dotNet `
        publish `
        $appProject `
        --configuration `
        Release `
        --runtime `
        win-x64 `
        --self-contained `
        true `
        --no-restore `
        --output `
        $publishDirectory `
        "-p:Version=$Version" `
        '-p:ContinuousIntegrationBuild=true' `
        '-p:DebugSymbols=false' `
        '-p:DebugType=None' `
        '-p:PublishSingleFile=false' `
        '-p:PublishTrimmed=false' `
        '-p:PublishReadyToRun=false' `
        '--verbosity' `
        'minimal' `
        '-maxcpucount:1'
}
finally {
    Pop-Location
}

$runtimeDirectory = Join-Path $publishDirectory 'runtimes'
if (Test-Path -LiteralPath $runtimeDirectory -PathType Container) {
    Get-ChildItem -LiteralPath $runtimeDirectory -Directory |
        Where-Object { $_.Name -ne 'win-x64' } |
        ForEach-Object {
            $runtimePath = Assert-RepositoryChildPath $_.FullName
            Remove-Item -LiteralPath $runtimePath -Recurse -Force
        }
}

$requiredFiles = @(
    'DictaClone.App.exe',
    'DictaClone.App.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'THIRD-PARTY-NOTICES.md',
    'MODEL-LICENSES.md',
    'RELEASE_NOTES.md',
    'ROLLBACK.md',
    'CLEAN_ROOM_INSTALLATION.md'
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $publishDirectory $file
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Self-contained publish is missing required file: $file"
    }
}

Write-Host "Published DictaClone $Version to $publishDirectory"
return $publishDirectory
