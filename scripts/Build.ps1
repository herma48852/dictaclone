[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $Clean
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
}
finally {
    Pop-Location
}
