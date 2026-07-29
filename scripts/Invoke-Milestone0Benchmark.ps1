[CmdletBinding()]
param(
    [switch] $DownloadModels
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

if ($DownloadModels) {
    & (Join-Path $PSScriptRoot 'Download-Models.ps1')
}

$dotNet = Get-RepositoryDotNet
$project = Join-Path $script:RepositoryRoot 'tools\DictaClone.DevTools\DictaClone.DevTools.csproj'
$fixture = Join-Path $script:RepositoryRoot 'tests\Fixtures\audio\jfk.wav'
$expected = Join-Path $script:RepositoryRoot 'tests\Fixtures\transcripts\jfk.txt'
$output = Join-Path $script:RepositoryRoot 'artifacts\benchmarks\milestone-0.json'
$baseModel = Join-Path $script:RepositoryRoot 'models\ggml-base.en.bin'
$smallModel = Join-Path $script:RepositoryRoot 'models\ggml-small.en.bin'

foreach ($requiredPath in @($fixture, $expected, $baseModel, $smallModel)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required benchmark input is missing: $requiredPath"
    }
}

Push-Location $script:RepositoryRoot

try {
    Invoke-CheckedCommand $dotNet run `
        --project $project `
        --configuration Release `
        --no-build `
        -- `
        benchmark `
        $fixture `
        $expected `
        $output `
        base.en `
        $baseModel `
        small.en `
        $smallModel
}
finally {
    Pop-Location
}
