[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $Clean,

    [switch] $Coverage,

    [ValidateRange(0, 100)]
    [double] $MinimumCoreLineCoverage = 90
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$solution = Join-Path $script:RepositoryRoot 'DictaClone.slnx'
$coverageResultsDirectory = $null

Push-Location $script:RepositoryRoot

try {
    if ($Clean) {
        Invoke-CheckedCommand $dotNet clean $solution --configuration $Configuration --verbosity minimal '-maxcpucount:1'
    }

    Invoke-CheckedCommand $dotNet restore $solution --locked-mode --disable-parallel --verbosity minimal '-maxcpucount:1'
    Invoke-CheckedCommand $dotNet build $solution --configuration $Configuration --no-restore --verbosity minimal '-maxcpucount:1'

    $testArguments = @(
        'test',
        $solution,
        '--configuration',
        $Configuration,
        '--no-build',
        '--no-restore',
        '--filter',
        'Category!=LiveProvider&Category!=DesktopE2E',
        '-maxcpucount:1'
    )

    if ($Coverage) {
        $coverageRunName = 'coverage-{0:yyyyMMdd-HHmmss}-{1}' -f `
            (Get-Date), `
            [guid]::NewGuid().ToString('N')
        $coverageResultsDirectory = Join-Path `
            $script:RepositoryRoot `
            (Join-Path 'TestResults' $coverageRunName)
        $testArguments += '--collect:XPlat Code Coverage'
        $testArguments += '--results-directory'
        $testArguments += $coverageResultsDirectory
    }

    Invoke-CheckedCommand $dotNet @testArguments

    if ($Coverage) {
        & (Join-Path $PSScriptRoot 'Assert-Coverage.ps1') `
            -ResultsDirectory $coverageResultsDirectory `
            -AssemblyName 'DictaClone.Core' `
            -MinimumLinePercent $MinimumCoreLineCoverage

    }

    $endToEndProject = Join-Path `
        $script:RepositoryRoot `
        'tests\DictaClone.EndToEndTests\DictaClone.EndToEndTests.csproj'
    Invoke-CheckedCommand `
        $dotNet `
        test `
        $endToEndProject `
        --configuration `
        $Configuration `
        --no-build `
        --no-restore `
        --filter `
        'Category=DesktopE2E&Category!=ManualDesktopStress' `
        '-maxcpucount:1'
}
finally {
    Pop-Location
}
