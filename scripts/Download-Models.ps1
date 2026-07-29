[CmdletBinding()]
param(
    [ValidateSet('base.en', 'small.en')]
    [string[]] $Model = @('base.en', 'small.en'),

    [switch] $Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$manifest = @{
    'base.en' = @{
        FileName = 'ggml-base.en.bin'
        Length = 147964211
        Sha256 = 'a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002'
        Uri = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin?download=true'
    }
    'small.en' = @{
        FileName = 'ggml-small.en.bin'
        Length = 487614201
        Sha256 = 'c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d'
        Uri = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin?download=true'
    }
}

function Save-ModelFile {
    param(
        [Parameter(Mandatory)]
        [string] $Uri,

        [Parameter(Mandatory)]
        [string] $Destination
    )

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue

    if ($null -ne $curl) {
        & $curl.Source `
            --location `
            --fail `
            --retry 5 `
            --retry-delay 2 `
            --show-error `
            --output $Destination `
            $Uri

        if ($LASTEXITCODE -ne 0) {
            throw "curl failed with exit code $LASTEXITCODE while downloading $Uri."
        }

        return
    }

    Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Destination
}

$modelDirectory = Join-Path $script:RepositoryRoot 'models'
New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null

foreach ($modelName in $Model) {
    $modelEntry = $manifest[$modelName]
    $destination = Join-Path $modelDirectory $modelEntry.FileName

    if (Test-Path -LiteralPath $destination) {
        $existingHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash

        if ($existingHash -eq $modelEntry.Sha256) {
            Write-Host "$modelName is already present and verified."
            continue
        }

        if (-not $Force) {
            throw "$destination exists but its SHA-256 does not match. Use -Force to replace it."
        }
    }

    $stagingPath = "$destination.partial-$([guid]::NewGuid().ToString('N'))"
    Write-Host "Downloading $modelName to a staging file..."

    try {
        Save-ModelFile -Uri $modelEntry.Uri -Destination $stagingPath
        $downloadLength = (Get-Item -LiteralPath $stagingPath).Length

        if ($downloadLength -ne $modelEntry.Length) {
            throw "Length verification failed for $modelName. Expected $($modelEntry.Length), received $downloadLength."
        }

        $downloadHash = (Get-FileHash -LiteralPath $stagingPath -Algorithm SHA256).Hash

        if ($downloadHash -ne $modelEntry.Sha256) {
            throw "SHA-256 verification failed for $modelName."
        }

        Move-Item -LiteralPath $stagingPath -Destination $destination -Force
        $sizeMiB = (Get-Item -LiteralPath $destination).Length / 1MB
        Write-Host ("Verified {0} ({1:N1} MiB)." -f $modelName, $sizeMiB)
    }
    finally {
        if (Test-Path -LiteralPath $stagingPath) {
            Remove-Item -LiteralPath $stagingPath -Force
        }
    }
}
