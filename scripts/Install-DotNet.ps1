[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$sdkVersion = (Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json).sdk.version
$installDirectory = Join-Path $repositoryRoot '.dotnet'
$installScript = Join-Path ([IO.Path]::GetTempPath()) 'dictaclone-dotnet-install.ps1'

Write-Host "Installing .NET SDK $sdkVersion into $installDirectory..."

try {
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri 'https://dot.net/v1/dotnet-install.ps1' `
        -OutFile $installScript

    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $installScript `
        -Version $sdkVersion `
        -Architecture x64 `
        -InstallDir $installDirectory `
        -NoPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install.ps1 failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $installScript) {
        Remove-Item -LiteralPath $installScript -Force
    }
}

Write-Host "Installed $(& (Join-Path $installDirectory 'dotnet.exe') --version)."
