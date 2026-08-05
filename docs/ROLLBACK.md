# DictaClone rollback guide

Settings and downloaded models are stored separately from application binaries
under `%LocalAppData%\DictaClone`, so installing, repairing, upgrading, or
rolling back does not normally replace user data.

## Roll back an installed copy

1. Exit DictaClone from its notification-area icon.
2. Export settings from **DictaClone settings > Privacy & recovery > Export
   settings** if an additional backup is desired.
3. Uninstall DictaClone from **Settings > Apps > Installed apps**. Choose to
   keep user data when prompted.
4. Verify the SHA-256 checksum of the earlier installer against that release's
   `SHA256SUMS.txt`.
5. Install the earlier version for the same Windows user.
6. Launch DictaClone and confirm the settings schema is supported.

Do not roll back across a settings-schema change unless the older release notes
explicitly say it can read the newer schema. If an older release rejects the
settings, exit the app, move `%LocalAppData%\DictaClone\settings.json` to a safe
backup name, start with defaults, and import a settings export created by that
release.

## Portable rollback

Extract the earlier portable archive to a new directory. Do not overwrite a
running copy in place. Exit the current copy, launch the earlier
`DictaClone.App.exe`, verify operation, and then remove the newer application
directory. Both copies use the same per-user settings and model directory.

## Restore the newer build

Run the newer installer again. Its stable application identity upgrades or
repairs the same per-user installation without deleting user data.
