[CmdletBinding()]
param(
    [string] $Version,

    [string] $ReleaseDirectory,

    [string] $InnoCompilerPath,

    [ValidateRange(30, 300)]
    [int] $ProcessTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-RepositoryVersion
}

if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\release' $Version)
}

$releaseDirectoryPath = Assert-RepositoryChildPath $ReleaseDirectory
$currentInstaller = Join-Path `
    $releaseDirectoryPath `
    "DictaClone-$Version-win-x64-setup.exe"
if (!(Test-Path -LiteralPath $currentInstaller -PathType Leaf)) {
    throw "Current installer was not found: $currentInstaller"
}

$runningApps = Get-Process -Name 'DictaClone.App' -ErrorAction SilentlyContinue
if ($null -ne $runningApps) {
    throw 'Exit every running DictaClone app before the installer lifecycle test.'
}

$uninstallRegistryRoot =
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$existingInstall = Get-ChildItem `
    -LiteralPath $uninstallRegistryRoot `
    -ErrorAction SilentlyContinue |
    Get-ItemProperty |
    Where-Object {
        $displayName = $_.PSObject.Properties['DisplayName']
        $null -ne $displayName -and
            $displayName.Value -like 'DictaClone*'
    } |
    Select-Object -First 1
if ($null -ne $existingInstall) {
    throw ('A per-user DictaClone installation already exists. Uninstall it ' +
        'before running this isolated lifecycle test.')
}

$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
function Get-DictaCloneStartupValue {
    $values = Get-ItemProperty -LiteralPath $runKeyPath
    $property = $values.PSObject.Properties['DictaClone']
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

$existingStartup = Get-DictaCloneStartupValue
if ($null -ne $existingStartup) {
    throw ('A DictaClone Start-with-Windows value already exists. Disable it ' +
        'in DictaClone settings before running this isolated lifecycle test.')
}

$testRoot = Assert-RepositoryChildPath (
    Join-Path $script:RepositoryRoot 'artifacts\installer-lifecycle-test')
$testInstallDirectory = Assert-RepositoryChildPath (
    Join-Path $testRoot 'installed-app')
$baselineOutputDirectory = Assert-RepositoryChildPath (
    Join-Path $testRoot 'baseline-installer')
$publishDirectory = Assert-RepositoryChildPath (
    Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\staging' "$Version\win-x64\publish"))
$baselineInstaller = Join-Path `
    $baselineOutputDirectory `
    'DictaClone-0.0.1-win-x64-setup.exe'
$setupLog = Join-Path $testRoot 'setup.log'
$upgradeLog = Join-Path $testRoot 'upgrade.log'
$repairLog = Join-Path $testRoot 'repair.log'
$uninstallLog = Join-Path $testRoot 'uninstall.log'
$startMenuGroup = 'DictaClone-Milestone7-Test'
$sentinelName = 'milestone7-installer-test-{0}.tmp' -f `
    [guid]::NewGuid().ToString('N')
$userDataDirectory = Join-Path $env:LOCALAPPDATA 'DictaClone'
$sentinelPath = Join-Path $userDataDirectory $sentinelName
$testStartupCommand = '"{0}"' -f (
    Join-Path $testInstallDirectory 'DictaClone.App.exe')

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        if ($argument -match '\s') {
            throw "Automated installer arguments must not contain whitespace: $argument"
        }
    }
    $startInfo.Arguments = $Arguments -join ' '

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (!$process.Start()) {
            throw "Process did not start: $FilePath"
        }

        if (!$process.WaitForExit($ProcessTimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Process exceeded $ProcessTimeoutSeconds seconds: $FilePath"
        }

        if ($process.ExitCode -ne 0) {
            throw "Process exited with code $($process.ExitCode): $FilePath"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-DictaCloneUninstallEntry {
    return Get-ChildItem `
        -LiteralPath $uninstallRegistryRoot `
        -ErrorAction SilentlyContinue |
        Get-ItemProperty |
        Where-Object {
            $displayName = $_.PSObject.Properties['DisplayName']
            $installLocation = $_.PSObject.Properties['InstallLocation']
            $null -ne $displayName -and
                $null -ne $installLocation -and
                $displayName.Value -like 'DictaClone*' -and
                $installLocation.Value -eq "$testInstallDirectory\"
        } |
        Select-Object -First 1
}

if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
New-Item -ItemType Directory -Path $userDataDirectory -Force | Out-Null
Set-Content `
    -LiteralPath $sentinelPath `
    -Value 'Installer lifecycle user-data preservation sentinel.' `
    -Encoding ascii

$lifecycleCompleted = $false
try {
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
        -Version '0.0.1' `
        -PublishDirectory $publishDirectory `
        -OutputDirectory $baselineOutputDirectory `
        -OutputBaseFilename 'DictaClone-0.0.1-win-x64-setup' `
        -CompilerPath $InnoCompilerPath |
        Out-Host

    Invoke-BoundedProcess `
        -FilePath $baselineInstaller `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            "/DIR=$testInstallDirectory",
            "/GROUP=$startMenuGroup",
            "/LOG=$setupLog")

    $installedExe = Join-Path $testInstallDirectory 'DictaClone.App.exe'
    $uninstaller = Join-Path $testInstallDirectory 'unins000.exe'
    if (!(Test-Path -LiteralPath $installedExe -PathType Leaf) -or
        !(Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw 'Baseline install did not create the application and uninstaller.'
    }

    $entry = Get-DictaCloneUninstallEntry
    if ($null -eq $entry -or $entry.DisplayVersion -ne '0.0.1') {
        throw 'Baseline install did not register version 0.0.1 for this user.'
    }
    if ($null -ne (Get-DictaCloneStartupValue)) {
        throw 'Installer enabled Start with Windows without user consent.'
    }

    New-ItemProperty `
        -LiteralPath $runKeyPath `
        -Name 'DictaClone' `
        -Value $testStartupCommand `
        -PropertyType String `
        -Force |
        Out-Null

    Invoke-BoundedProcess `
        -FilePath $currentInstaller `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            "/DIR=$testInstallDirectory",
            "/GROUP=$startMenuGroup",
            "/LOG=$upgradeLog")

    $entry = Get-DictaCloneUninstallEntry
    if ($null -eq $entry -or $entry.DisplayVersion -ne $Version) {
        throw "Upgrade did not register version $Version for this user."
    }
    if (!(Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'Upgrade removed deliberately retained user data.'
    }
    if ((Get-DictaCloneStartupValue) -ne $testStartupCommand) {
        throw 'Upgrade changed the user-approved startup registration.'
    }

    $repairProbe = Join-Path $testInstallDirectory 'ROLLBACK.md'
    Remove-Item -LiteralPath $repairProbe -Force
    Invoke-BoundedProcess `
        -FilePath $currentInstaller `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            "/DIR=$testInstallDirectory",
            "/GROUP=$startMenuGroup",
            "/LOG=$repairLog")
    if (!(Test-Path -LiteralPath $repairProbe -PathType Leaf)) {
        throw 'Repair did not restore a missing installed file.'
    }

    Invoke-BoundedProcess `
        -FilePath $uninstaller `
        -Arguments @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            "/LOG=$uninstallLog")

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path -LiteralPath $installedExe) -and
        [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }

    if (Test-Path -LiteralPath $installedExe) {
        throw 'Uninstall left the application binary behind.'
    }
    if ($null -ne (Get-DictaCloneUninstallEntry)) {
        throw 'Uninstall left its Installed Apps registration behind.'
    }
    if ($null -ne (Get-DictaCloneStartupValue)) {
        throw 'Uninstall left the Start-with-Windows registration behind.'
    }
    if (!(Test-Path -LiteralPath $sentinelPath -PathType Leaf)) {
        throw 'Silent uninstall removed user data instead of retaining it.'
    }

    $lifecycleCompleted = $true
    Write-Host 'Installer lifecycle validation passed:'
    Write-Host '  per-user, non-admin install: passed'
    Write-Host '  startup registration requires consent: passed'
    Write-Host '  upgrade preserves user data and startup choice: passed'
    Write-Host '  repair restores missing files: passed'
    Write-Host '  uninstall removes binaries and startup registration: passed'
    Write-Host '  silent uninstall retains user data: passed'
}
finally {
    $remainingUninstaller = Join-Path $testInstallDirectory 'unins000.exe'
    if (!$lifecycleCompleted -and
        (Test-Path -LiteralPath $remainingUninstaller -PathType Leaf)) {
        try {
            Invoke-BoundedProcess `
                -FilePath $remainingUninstaller `
                -Arguments @(
                    '/VERYSILENT',
                    '/SUPPRESSMSGBOXES',
                    '/NORESTART')
        }
        catch {
            Write-Warning "Cleanup uninstaller failed: $($_.Exception.Message)"
        }
    }

    $selfDeleteDeadline = [DateTime]::UtcNow.AddSeconds(30)
    while ((Test-Path -LiteralPath $remainingUninstaller) -and
        [DateTime]::UtcNow -lt $selfDeleteDeadline) {
        Start-Sleep -Milliseconds 200
    }

    $currentStartup = Get-DictaCloneStartupValue
    if ($currentStartup -eq $testStartupCommand) {
        Remove-ItemProperty `
            -LiteralPath $runKeyPath `
            -Name 'DictaClone' `
            -Force
    }

    if (Test-Path -LiteralPath $sentinelPath -PathType Leaf) {
        Remove-Item -LiteralPath $sentinelPath -Force
    }

    if (Test-Path -LiteralPath $testRoot) {
        $validatedTestRoot = Assert-RepositoryChildPath $testRoot
        $cleanupDeadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            try {
                Remove-Item -LiteralPath $validatedTestRoot -Recurse -Force
                break
            }
            catch [IO.IOException] {
                if ([DateTime]::UtcNow -ge $cleanupDeadline) {
                    throw
                }

                Start-Sleep -Milliseconds 200
            }
        }
        while (Test-Path -LiteralPath $validatedTestRoot)
    }
}
