[CmdletBinding()]
param(
    [ValidateSet('base.en', 'small.en')]
    [string] $Model = 'base.en',

    [ValidateRange(0, 1)]
    [double] $MaximumWordErrorRate = 0.15,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$toolProject = Join-Path `
    $script:RepositoryRoot `
    'tools\DictaClone.DevTools\DictaClone.DevTools.csproj'
$toolAssembly = Join-Path `
    $script:RepositoryRoot `
    'tools\DictaClone.DevTools\bin\Release\net10.0-windows10.0.22000.0\DictaClone.DevTools.dll'
$wave = Join-Path `
    $script:RepositoryRoot `
    'tests\Fixtures\audio\jfk.wav'
$expected = Join-Path `
    $script:RepositoryRoot `
    'tests\Fixtures\transcripts\jfk.txt'
$modelDirectory = Join-Path $script:RepositoryRoot 'models'
$modelFile = Join-Path `
    $modelDirectory `
    $(if ($Model -eq 'base.en') {
        'ggml-base.en.bin'
    }
    else {
        'ggml-small.en.bin'
    })

if (!(Test-Path -LiteralPath $modelFile)) {
    throw "The $Model model is not installed. Run .\scripts\Download-Models.ps1 -Model $Model first."
}

if (!$NoBuild) {
    Invoke-CheckedCommand `
        $dotNet `
        build `
        $toolProject `
        --configuration `
        Release `
        --no-restore `
        --verbosity `
        minimal `
        '-maxcpucount:1'
}

& $dotNet `
    $toolAssembly `
    speech-regression `
    $wave `
    $expected `
    $Model `
    $MaximumWordErrorRate.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture) `
    $modelDirectory
exit $LASTEXITCODE
