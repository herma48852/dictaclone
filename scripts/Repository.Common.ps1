Set-StrictMode -Version Latest

$script:RepositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))

$env:DOTNET_CLI_HOME = Join-Path $script:RepositoryRoot '.cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $script:RepositoryRoot '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $script:RepositoryRoot '.nuget\http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $script:RepositoryRoot '.nuget\plugins-cache'

function Get-RepositoryDotNet {
    $localDotNet = Join-Path $script:RepositoryRoot '.dotnet\dotnet.exe'

    if (Test-Path -LiteralPath $localDotNet) {
        return $localDotNet
    }

    $installedDotNet = Get-Command dotnet -ErrorAction SilentlyContinue

    if ($null -eq $installedDotNet) {
        throw 'The .NET SDK was not found. Install the SDK version pinned in global.json.'
    }

    return $installedDotNet.Source
}

function Get-RepositoryVersion {
    $buildProperties = Join-Path $script:RepositoryRoot 'Directory.Build.props'
    [xml] $document = Get-Content -LiteralPath $buildProperties -Raw
    $versionNode = $document.SelectSingleNode(
        '/Project/PropertyGroup/VersionPrefix')
    $version = if ($null -eq $versionNode) {
        $null
    }
    else {
        $versionNode.InnerText
    }

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'Directory.Build.props does not define VersionPrefix.'
    }

    return [string] $version
}

function Assert-RepositoryChildPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $repositoryPath = [IO.Path]::GetFullPath($script:RepositoryRoot)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $prefix = $repositoryPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar

    if (!$resolvedPath.StartsWith(
            $prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must remain inside the repository: $resolvedPath"
    }

    return $resolvedPath
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}
