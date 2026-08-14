# macOS clean-room installation and use

This guide installs and exercises DictaClone on a Mac or macOS user profile
that has not run it before. DictaClone supports macOS 14 or newer on Apple
Silicon and Intel. Choose either the Apple Silicon source-build path or the
prebuilt-release path below. A prebuilt release is self-contained; source
builders install the .NET SDK, Git, and full Xcode.

An internet connection is required for the first verified speech-model
download. Ordinary dictation works offline after the selected model is present.

The current development-signed macOS qualification archives are attached to
the [DictaClone 0.1.3 prerelease](https://github.com/herma48852/dictaclone/releases/tag/v0.1.3).
They are intended for local compatibility testing and are not notarized for
general direct distribution.

## Build from source on Apple Silicon

This is the supported way for another Mac owner to build DictaClone for
personal use while public notarized macOS release assets are deferred. It does
not require paid Apple Developer Program membership.

### Install the source-build prerequisites

Use an Apple Silicon Mac running macOS 14 or newer with enough free space for
full Xcode and the build output. Install:

- Git;
- full Xcode from the Mac App Store, with the built-in macOS platform support;
  and
- the macOS Arm64 installer for .NET SDK 10.0.302 from the official
  [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).

Homebrew is optional. The iOS, watchOS, tvOS, and visionOS Xcode components are
not required. Select Xcode and finish its first-launch setup:

```zsh
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
sudo xcodebuild -runFirstLaunch
uname -m
xcodebuild -version
git --version
```

`uname -m` must report `arm64`.

### Clone and test

Clone the current accepted source and enter the checkout:

```zsh
git clone https://github.com/herma48852/dictaclone.git
cd dictaclone
dotnet --version
git status --short --branch
./scripts/macos/test.sh
```

From inside the checkout, `dotnet --version` must resolve SDK 10.0.302 under
the repository's `global.json`. The first test run restores locked NuGet
dependencies and therefore requires internet access. It does not request
Microphone or Accessibility permission.

### Sign and build

The simplest local build is ad-hoc signed:

```zsh
./scripts/macos/build-app.sh osx-arm64
```

An ad-hoc build is usable on the Mac that built it, but its signing requirement
changes when the application changes. macOS may therefore require privacy
permissions to be granted again after a rebuild.

For a stable local signing identity, sign in with an Apple Account under
**Xcode > Settings > Apple Accounts**. Select the account, open
**Manage Certificates**, and add an **Apple Development** certificate. A free
[Personal Team](https://developer.apple.com/help/account/basics/about-your-developer-account)
is sufficient for this local development use. Confirm that the certificate and
its private key form a valid identity:

```zsh
security find-identity -v -p codesigning
```

Copy the complete identity name shown by that command and build with it:

```zsh
export DICTACLONE_CODESIGN_IDENTITY='Apple Development: Your Name (TEAMID)'
./scripts/macos/build-app.sh osx-arm64
```

The script performs a locked restore, creates a self-contained application,
signs it, verifies the signature, and writes the app, ZIP, and checksum under
`artifacts/macos/<version>/osx-arm64`. It prints the exact output paths when it
finishes. Developer ID signing, notarization, and paid program membership are
not required for a personal source build; they are required before the owner
distributes a prebuilt app to other people.

### Install the source build

Open the generated directory, using the version printed by the build. For the
current version:

```zsh
open artifacts/macos/0.1.3/osx-arm64
```

Drag `DictaClone.app` into **Applications** before launching it. Then continue
with [Grant permissions](#grant-permissions):

```zsh
open /Applications/DictaClone.app
```

Always launch the installed bundle through Finder, Spotlight, or `open`.
Launching the executable inside `Contents/MacOS` directly can prevent macOS
from associating permission requests with the application bundle.

## Install a prebuilt release

### Before installation

Download these files from the same DictaClone release into one new folder:

- `DictaClone-<version>-osx-arm64.zip` on an Apple Silicon Mac, or
  `DictaClone-<version>-osx-x64.zip` on an Intel Mac;
- `MACOS_CLEAN_ROOM_INSTALLATION.md`; and
- `SHA256SUMS.txt`.

Choose **Apple menu > About This Mac** if the processor architecture is not
known. Apple M-series computers use `osx-arm64`; Intel computers use `osx-x64`.
Do not use GitHub's automatically generated **Source code** archives, which do
not contain the runnable app.

Open Terminal, change to the download folder, and verify the selected archive.
For Apple Silicon:

```zsh
shasum -a 256 -c SHA256SUMS.txt --ignore-missing
```

The command must report the selected ZIP as `OK`. Do not open an archive whose
hash fails or whose checksum came from a different source than the release.

The first macOS qualification archives may carry an ad-hoc development
signature and will not pass Gatekeeper on another Mac. Such an archive is for
source-tree testing only. A distributable build must have a valid Developer ID
signature and a stapled Apple notarization ticket; do not bypass Gatekeeper to
test an untrusted download.

### Install and launch

1. Double-click the specifically named DictaClone ZIP to extract it.
2. Drag `DictaClone.app` into **Applications**. Do not run it from the archive
   or Downloads folder.
3. Control-click DictaClone in Applications, choose **Open**, and confirm the
   first launch if macOS asks. A properly notarized release should identify its
   developer and must not report that the app is damaged.
4. DictaClone starts as a menu-bar utility. It normally has no Dock icon and
   opens its first-run settings window. Finder, Spotlight, and Launcher show
   the full DictaClone application icon; the smaller menu-bar image remains
   visible while the utility is running.
5. On **General**, select **Follow system default microphone** or a specific
   microphone, leave the local model as `base.en` for the initial check, and
   choose **Apply settings**.
6. Keep the network connected for the first dictation while DictaClone
   downloads and verifies the selected local model.

Always launch the `.app` through Finder, Spotlight, or `open
/Applications/DictaClone.app`. Do not execute
`DictaClone.app/Contents/MacOS/DictaClone.Mac.App` directly: bypassing
LaunchServices can prevent macOS from associating a permission request with the
installed bundle.

## Grant permissions

DictaClone requires Microphone and Accessibility permission. It also reports
Input Monitoring separately for diagnostics, but Accessibility already grants
the event listening and posting access used by DictaClone's active shortcut
tap. Open **Privacy & recovery** in DictaClone Settings to see each current
state. Use the permission buttons there; the Microphone button first makes the
macOS consent request so DictaClone is registered in the system list.

1. Choose **Microphone** in DictaClone Settings and approve the macOS prompt. If
   access was previously denied, enable DictaClone in **System Settings >
   Privacy & Security > Microphone** when that pane opens.
2. Under **Privacy & Security > Accessibility**, enable DictaClone so it can
   retain and revalidate the focused field and insert the result.
3. Input Monitoring can remain denied when Accessibility is authorized. It is
   not an additional acceptance requirement for the active shortcut tap.
4. Quit DictaClone from its menu-bar menu and reopen it after changing a
   permission if macOS requests a restart.

If a locally rebuilt replacement does not appear in Accessibility, first quit
every DictaClone instance, register and launch the installed bundle, then make
the Accessibility request again:

```zsh
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
  -f /Applications/DictaClone.app
open /Applications/DictaClone.app
```

If necessary, use the `+` control in **Privacy & Security > Accessibility** and
select `/Applications/DictaClone.app`. This recovery is intended for local
development replacements; a normal clean installation should register during
its first Finder launch.

Test the denial path as part of clean-room acceptance: leave each permission
off initially, attempt its associated action, and confirm DictaClone reports an
actionable error without repeatedly prompting. Then grant it and retry. Revoking
a permission later must fail safely rather than inserting into an unverified
target.

## Dictate

1. Open TextEdit, choose **Format > Make Plain Text**, and place the insertion
   point in the document.
2. Hold `Control+Shift+Space`. The **Listening** status appears without taking
   focus from TextEdit.
3. Speak, then release the shortcut. DictaClone transcribes locally and inserts
   the result at the original insertion point.
4. Use `Control+Option+Escape` to cancel an active dictation. Use
   `Control+Option+Space` for clipboard-free Typing Mode.

To use the single dedicated Volume Down key instead, open DictaClone Settings,
replace the **Dictation** shortcut with `VolumeDown`, and apply the settings.
Hold the speaker-volume-down key to dictate and release it to transcribe.
DictaClone consumes that media-key event while it is bound, so the shortcut
does not also lower the system volume. `F11` remains a distinct standard
function-key binding and may require Fn under the current macOS keyboard mode.

The recognized shortcut's primary key is consumed, so it should not type a
space or invoke a command in the foreground application. Paste Mode temporarily
uses the system pasteboard, then restores every captured format only if no other
process changed it. Typing Mode does not read or modify the pasteboard.

Use the DictaClone menu-bar icon to open settings or history, copy the last
result, or exit. Closing a window does not exit the menu-bar app. Shortcut text
uses the macOS names Command, Control, Option, and Shift.

After the first model download and successful dictation:

1. Exit DictaClone.
2. Disconnect Wi-Fi and any other network connection.
3. Reopen DictaClone from Applications and repeat the TextEdit test.
4. Confirm ordinary dictation succeeds without a network request.

Smart Edit is separate from ordinary dictation. It is disabled by default,
requires explicit endpoint/model/API-key configuration, and uses the network
when invoked. Its key is stored in the login Keychain.

## Acceptance checks

Before accepting a release, exercise both insertion modes in TextEdit, a browser
field, Terminal, and a rich-text editor. Include punctuation, emoji, combining
characters, multiple lines, tabs, and a long paragraph. Confirm:

- switching to a different window before transcription completes prevents
  insertion into the wrong target;
- selected-text Smart Edit rejects a changed selection;
- Paste Mode restores rich pasteboard formats and never overwrites a clipboard
  change made during insertion;
- Typing Mode leaves the pasteboard untouched;
- cancelling and releasing shortcuts never leave recording active;
- changing the microphone or unplugging it fails cleanly;
- enabling **Start DictaClone when I sign in to this Mac** starts one copy at
  the next login, and disabling it removes that behavior; and
- a second manual launch does not create a second active instance.

For the release-security check, run this from Terminal:

```zsh
codesign --verify --strict --verbose=2 /Applications/DictaClone.app
spctl --assess --type execute --verbose=2 /Applications/DictaClone.app
xcrun stapler validate /Applications/DictaClone.app
```

All three commands must succeed for a public direct-distribution release.

## Data locations and removal

Exit DictaClone from its menu-bar menu before removal. Move
`/Applications/DictaClone.app` to the Trash. That removes the application but
retains settings, downloaded models, optional history, and diagnostics under:

```text
~/Library/Application Support/DictaClone
```

If start at login was enabled, disable it in DictaClone before removing the app.
If the app is already gone, remove only this DictaClone-owned file:

```text
~/Library/LaunchAgents/com.dictaclone.desktop.plist
```

To remove all retained DictaClone data, deliberately move the DictaClone folder
shown above to the Trash. To remove the optional Smart Edit secret, open
**Keychain Access**, search for service `com.dictaclone.desktop`, verify that the
item belongs to DictaClone, and delete it. These data-removal steps cannot be
undone after the Trash or Keychain item is emptied.
