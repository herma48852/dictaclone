[CmdletBinding()]
param(
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$configuration = 'Release'
$solution = Join-Path $script:RepositoryRoot 'DictaClone.slnx'
$testProjects = @(
    'tests\DictaClone.Core.Tests\DictaClone.Core.Tests.csproj',
    'tests\DictaClone.Windows.Tests\DictaClone.Windows.Tests.csproj',
    'tests\DictaClone.App.Tests\DictaClone.App.Tests.csproj',
    'tests\DictaClone.EndToEndTests\DictaClone.EndToEndTests.csproj'
)

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

foreach ($relativeProject in $testProjects) {
    $project = Join-Path $script:RepositoryRoot $relativeProject
    Invoke-CheckedCommand `
        $dotNet `
        test `
        $project `
        --configuration `
        $configuration `
        --no-build `
        --no-restore `
        --nologo `
        '-maxcpucount:1'
}
