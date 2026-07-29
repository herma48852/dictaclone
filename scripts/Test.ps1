[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $Clean,

    [switch] $Coverage
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$solution = Join-Path $script:RepositoryRoot 'DictaClone.slnx'

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
        '-maxcpucount:1'
    )

    if ($Coverage) {
        $testArguments += '--collect:XPlat Code Coverage'
        $testArguments += '--results-directory'
        $testArguments += (Join-Path $script:RepositoryRoot 'TestResults')
    }

    Invoke-CheckedCommand $dotNet @testArguments
}
finally {
    Pop-Location
}
