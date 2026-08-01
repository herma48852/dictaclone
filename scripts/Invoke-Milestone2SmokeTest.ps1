[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateRange(1, 60)]
    [int] $TimeoutSeconds = 10,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$dotNet = Get-RepositoryDotNet
$appProject = Join-Path `
    $script:RepositoryRoot `
    'src\DictaClone.App\DictaClone.App.csproj'
$appAssembly = Join-Path `
    $script:RepositoryRoot `
    "src\DictaClone.App\bin\$Configuration\net10.0-windows10.0.22000.0\DictaClone.App.dll"

if (!$NoBuild) {
    Invoke-CheckedCommand `
        $dotNet `
        build `
        $appProject `
        --configuration `
        $Configuration `
        --no-restore `
        --verbosity `
        minimal `
        '-maxcpucount:1'
}

if (!(Test-Path -LiteralPath $appAssembly)) {
    throw "DictaClone was not found at $appAssembly."
}

$process = Start-Process `
    -FilePath $dotNet `
    -ArgumentList @($appAssembly, '--smoke-test') `
    -WindowStyle Hidden `
    -PassThru

try {
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "DictaClone did not exit within $TimeoutSeconds seconds."
    }

    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        throw "DictaClone smoke test exited with code $($process.ExitCode)."
    }

    Write-Output "DictaClone process smoke test: PASS (PID $($process.Id))."
}
finally {
    if (!$process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }

    $process.Dispose()
}
