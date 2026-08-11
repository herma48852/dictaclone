[CmdletBinding()]
param(
    [string] $ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Repository.Common.ps1')

$version = Get-RepositoryVersion
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path `
        $script:RepositoryRoot `
        (Join-Path 'artifacts\release' $version)
}

$releaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$installer = Join-Path `
    $releaseDirectory `
    "DictaClone-$version-win-x64-setup.exe"
$portable = Join-Path `
    $releaseDirectory `
    "DictaClone-$version-win-x64-portable.zip"
$installationGuide = Join-Path `
    $releaseDirectory `
    'CLEAN_ROOM_INSTALLATION.md'

foreach ($path in @($installer, $portable, $installationGuide)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw ('Release artifact is missing. Supply the complete release ' +
            'directory with -ReleaseDirectory, or install Inno Setup 6.7.3 ' +
            'and run .\scripts\Invoke-Milestone7Regression.ps1 first: ' +
            $path)
    }
}

Write-Host @"
DictaClone Milestone 7 manual release review

Artifacts:
  Installer: $installer
  Portable:  $portable
  Guide:     $installationGuide

Use a clean Windows 11 x64 test user if one is available. Do not use an account
with another DictaClone installation.

1. Checksum and portable launch
   - Before using the desktop for other work, run this PowerShell command and
     leave the keyboard, mouse, clipboard, and foreground window untouched
     until it finishes:
       .\scripts\Invoke-Milestone7DesktopStress.ps1 -NoBuild
   - Expect one 50-cycle desktop stress test to pass.
   - In PowerShell, run:
       Get-FileHash '$installer' -Algorithm SHA256
   - Compare it with the installer line in:
       $releaseDirectory\SHA256SUMS.txt
   - Extract the portable ZIP to a new folder and run DictaClone.App.exe.
   - Confirm Windows does not ask for a .NET runtime installation.
   - In the first-run window titled "DictaClone first-run setup", select the
     "Microphone" and "Local model" = base.en controls, then select "Apply
     settings". Under "Privacy & recovery", select "Complete setup".
   - Open Notepad. Hold Ctrl+Win+Space, say "portable dictation works", release,
     allow the verified model download to finish, and confirm the text is
     inserted.
   - Exit using "Exit DictaClone" on the notification-area icon.

2. Per-user installer
   - Run the setup EXE. Confirm there is no administrator/UAC credential prompt.
   - On "Select Destination Location", confirm the destination is under the
     current user's AppData\Local\Programs\DictaClone directory.
   - Complete setup and launch DictaClone.
   - Open "DictaClone settings" from the notification-area icon. Under
     "Privacy & recovery", confirm the complete checkbox "Start DictaClone
     when I sign in to Windows" is initially clear.

3. Installed and offline restart
   - Select the same microphone and "Local model" = base.en, then select
     "Apply settings".
   - Disconnect Wi-Fi/Ethernet.
   - Exit DictaClone with "Exit DictaClone", then launch "DictaClone" from the
     Start menu.
   - Open Notepad. Hold Ctrl+Win+Space, say "offline restart works", release,
     and confirm the text is inserted without network access.
   - Reconnect the network when finished.

4. Startup consent and repair/upgrade behavior
   - In "DictaClone settings" > "Privacy & recovery", select the complete
     checkbox "Start DictaClone when I sign in to Windows" and then select
     "Apply settings".
   - Run the same installer again as a repair. Confirm settings and the startup
     choice remain.
   - If an earlier signed-off installer is available, install that earlier
     version first and then this installer; confirm settings remain after the
     upgrade. The automated gate already exercises a synthetic earlier version.

5. Uninstall
   - Exit DictaClone.
   - Open Windows Settings > Apps > Installed apps, locate the complete app name
     "DictaClone 0.1.0", choose its three-dot menu, and choose "Uninstall".
   - At "Delete DictaClone settings, downloaded speech models, transcript
     history, and diagnostics for this Windows user?", choose "No" for the
     retention test.
   - Confirm DictaClone is absent from Installed apps and the Start menu, its
     binaries are gone from AppData\Local\Programs\DictaClone, and its
     Start-with-Windows entry is gone.
   - Confirm intentionally retained user data remains under
     AppData\Local\DictaClone. Reinstall should reuse it.

6. Data-purge uninstall (optional final cleanup)
   - Reinstall, then uninstall again.
   - At the same data-deletion question choose "Yes".
   - Confirm AppData\Local\DictaClone is removed.

Report success or identify the numbered step and exact displayed message.
"@
