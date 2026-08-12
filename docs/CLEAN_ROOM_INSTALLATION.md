# Windows clean-room installation and use

This document covers Windows. For macOS, use the
[macOS clean-room installation and use guide](MACOS_CLEAN_ROOM_INSTALLATION.md).

This guide installs and exercises DictaClone on a Windows 11 x64 computer or
Windows user profile that has not previously run DictaClone. A standard,
non-administrator account is sufficient. The installer and portable package
are self-contained; neither the .NET SDK nor Inno Setup is required.

An internet connection is required for the first verified speech-model
download. Normal dictation works offline after that model has been downloaded.

## Before installation

For the current qualification build, open the
[DictaClone 0.1.2 GitHub prerelease](https://github.com/herma48852/dictaclone/releases/tag/v0.1.2),
expand **Assets**, and download these five files into one new folder:

- `DictaClone-0.1.2-win-x64-setup.exe`;
- `DictaClone-0.1.2-win-x64-portable.zip`;
- `CLEAN_ROOM_INSTALLATION.md`;
- `release-manifest.json`; and
- `SHA256SUMS.txt`.

Do not download GitHub's automatically generated **Source code (zip)** or
**Source code (tar.gz)** archives in place of the named portable ZIP. Those
archives contain repository source, not the runnable self-contained
application.

Keep the downloaded filenames unchanged so checksum verification can find
them. If the clean machine has no internet access, copy the complete five-file
release folder from a trusted machine instead.

Confirm `SHA256SUMS.txt` against the copy supplied by the release owner, then
verify every listed file before running the installer or portable application.
In PowerShell, change to the folder containing the five downloads and run:

```powershell
foreach ($taskLine in Get-Content .\SHA256SUMS.txt) {
    if ($taskLine -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
        throw "Invalid checksum line: $taskLine"
    }

    $taskExpectedHash = $Matches[1]
    $taskFileName = $Matches[2]
    $taskActualHash = (Get-FileHash `
        -LiteralPath (Join-Path $PWD $taskFileName) `
        -Algorithm SHA256).Hash

    if ($taskActualHash -ne $taskExpectedHash) {
        throw "SHA-256 mismatch: $taskFileName"
    }
}

'All release checksums match.'
```

This pre-release build is not Authenticode-signed, so Windows SmartScreen may
display a warning. Continue only when the files came from the expected release
and all checksums match. If necessary, select **More info** and **Run anyway**.

## Install

1. Run `DictaClone-<version>-win-x64-setup.exe` as the standard user. Do not
   choose **Run as administrator**.
2. Confirm that setup does not request administrator credentials. The default
   destination is `%LocalAppData%\Programs\DictaClone`.
3. Complete setup with **Launch DictaClone** selected. DictaClone starts in the
   Windows notification area and opens **DictaClone first-run setup**.
4. On **General**, choose the microphone, leave **Local model** set to
   `base.en` for the initial check, and select **Apply settings**.
5. On **Privacy & recovery**, leave **Start DictaClone when I sign in to
   Windows** clear unless startup is wanted, then select **Complete setup**.
6. Keep the network connected for the first dictation while DictaClone
   downloads and verifies the selected local speech model.

The installer does not enable Start with Windows by itself. That setting is
created only after it is selected in DictaClone settings and applied.

## Dictate

1. Open Notepad or another non-elevated application and place the text cursor
   in an editable field.
2. Hold `Ctrl+Shift+Space`. The red **Listening** status appears without taking
   focus from the target application.
3. Speak while holding the shortcut. Wait until the green level bar moves,
   then release it. DictaClone transcribes locally and inserts the result at
   the original cursor position.
4. Use `Ctrl+Alt+Escape` to cancel an active dictation. If an application does
   not accept normal Paste Mode, use `Ctrl+Alt+Space` for Typing Mode.

DictaClone consumes the completed dictation shortcut so it does not type a
space or invoke a foreground command. In Windows Terminal, including Codex
CLI, Paste Mode automatically uses the terminal's `Ctrl+Shift+V` text-paste
path. In native GNU Emacs, both Dictation and Typing Mode automatically use
Emacs's `Ctrl+Y` yank path and restore the previous clipboard afterward. No
Emacs key-map change is required.

Right-click the **DictaClone** notification-area icon to open settings, copy the
last result, view enabled transcript history, or exit. Closing the settings
window does not exit the tray application. Shortcuts can be changed under
**General** in settings.

After the first model download completes, exit DictaClone, disconnect the
network, start **DictaClone** from the Start menu, and repeat the Notepad check
to confirm offline use.

DictaClone runs non-elevated by design. Windows prevents it from inserting text
into an application that is running as administrator. Ordinary dictation and
microphone audio remain local; the optional cloud Smart Edit feature is off by
default and requires separate configuration.

## Portable alternative

To use the portable build instead of installing, extract the entire portable
ZIP into a new folder and run `DictaClone.App.exe`. Do not run the executable
from inside the ZIP. The first-run and dictation steps are the same, and no .NET
runtime installation should be requested.

Installed and portable copies share settings and downloaded models under
`%LocalAppData%\DictaClone` and cannot run simultaneously. If Start with
Windows is enabled for a portable copy, keep its extracted folder at the same
location.

## Uninstall or remove

Exit DictaClone from its notification-area icon before removal.

For an installed copy, open **Windows Settings > Apps > Installed apps**, find
the complete DictaClone version, and select **Uninstall**. Choose **No** at the
user-data prompt to retain settings, downloaded models, history, and diagnostics
for a reinstall. Choose **Yes** to remove that data as well. Uninstall always
removes the application binaries, shortcuts, and Start-with-Windows entry.

For a portable copy, delete its extracted application folder after exit. Its
per-user data remains under `%LocalAppData%\DictaClone` unless deliberately
removed.
