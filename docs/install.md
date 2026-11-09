# Install and update

Every download, what it contains, and how the app updates itself.

## Install

Windows x64: each [release](https://github.com/klangche/USB-helper/releases)
has two downloads with the same app: an installer,
`usb-helper-<version>-windows-x64.msi`, and a portable zip,
`usb-helper-<version>-windows-x64.zip`. Neither needs administrator rights.
Releases marked pre-release are test builds of the next version.

### Installer (.msi)

Download the `.msi` and open it. It installs for the current user only, with
no administrator rights and no UAC prompt, to
`%LOCALAPPDATA%\Programs\USB-helper`, and adds USB-helper to the Start menu
and to Apps & features (Settings > Apps > Installed apps). As the `.msi` is
not signed, Windows SmartScreen may warn first: click "More info", then "Run
anyway". The setup shows the license, the progress and a last page with
"Launch USB-helper", ticked by default: Finish then starts USB-helper; untick
it to finish without starting. A newer `.msi` (stable or beta) upgrades the
installed one in place; an older one does not install over a newer one.
Settings live in `%APPDATA%\USB-helper` and stay through upgrades. To
uninstall, use Apps & features, or `msiexec /x
usb-helper-<version>-windows-x64.msi`.

Windows Installer versions have no pre-release part, so Apps & features shows
an MSI version (`release/msi-version.sh`): `X.Y.Z` is `X.Y.(Z*1000+999)` and
`X.Y.Z-beta.N` is `X.Y.(Z*1000+N)`. For example 0.6.0-beta.2 is 0.6.2 and
0.6.0 is 0.6.999, so every beta sorts before its release and after the
release before it. Pull request builds are 0.0.0. The real version is in the
entry's comment and in Help > About.

### Portable zip

Download the zip and follow `READ_ME_FIRST.txt` in it. In short: unblock the
zip once (`Unblock-File -Path .\usb-helper-<version>-windows-x64.zip` in
PowerShell, as it is not signed), unzip it, and start `USB-helper.exe` in the
`USB-helper` folder. No installation and no administrator rights are needed,
and no network connection either (it is used only for the update check below).

The zip holds one folder:

```
USB-helper/
  USB-helper.exe         starts the app
  READ_ME_FIRST.txt
  LICENSE.txt
  app/
    UsbHelper.exe        the app
    *.dll, *.json        its libraries and the .NET runtime, as loose files
```

`USB-helper.exe` is a small native starter (NativeAOT, no .NET runtime of its
own): it starts `app\UsbHelper.exe` with the same arguments and exits, in a few
milliseconds. Without the `app` folder next to it, it says so and links to the
releases. Keep the files together; the folder can be moved anywhere. Nothing is
unpacked elsewhere. To remove USB-helper, delete its folder, and
`%APPDATA%\USB-helper` if you want its settings gone too (see [Settings](usage.md#settings)).

### macOS universal disk image

Every release and every beta (pre-release `vX.Y.Z-beta.N`, see Releases) also carries
`usb-helper-<version>-macos-universal.dmg` and its `.sha256`, built from the
same commit and version as the Windows files. Download the dmg from the release's
page (not a zip), open it, and drag `USB-helper.app` to the Applications
folder. These are unsigned builds, and the app's update check ignores them. The dmg is added
a little after the Windows files; if the macOS build failed, the release has none. If Gatekeeper blocks the app and you trust this
download, run this command in Terminal after installing; it clears quarantine
only from USB-helper.app and does not change Gatekeeper settings or any other
files:

```
xattr -dr com.apple.quarantine /Applications/USB-helper.app
```

The disk image also includes `READ_ME_FIRST.txt` and the license.

### From 0.5.0

That version was one `USB-helper.exe` that unpacked the app to
`%LOCALAPPDATA%\USB-helper\<version>`. This version does not use or touch
that folder; delete it and the old `USB-helper.exe` by hand. The 0.5.0
update cannot install this version: download it once by hand.

The portable 0.6.0 betas before this layout had `UsbHelper.exe` at the top of
the zip (no `app` folder). Their in-app update looks for that file and so
cannot install this layout; download the zip once by hand. An installed
(`.msi`) beta updates as normal.

### Updates

After the window shows (unless "Check for updates at start" is off in the
settings), the app asks GitHub Releases (no account or token) for the newest
stable release and the newest beta, in the background: the window works as
normal meanwhile. Only releases with a published SHA-256 file
(`usb-helper-<version>-windows-x64.zip.sha256`, or `.msi.sha256` for an
installed app) count, so that every download can be verified. If either is
newer than the running version, a small window opens over the main window with
the running version, "Update to stable X", "Update to beta Y" (whichever are
newer; the beta only when betas are on in the settings) and "Skip"; the main
window keeps reading devices behind it. Closing the window is the same as
"Skip". Otherwise nothing is shown. With no network, no answer within 2
seconds, a GitHub error or its rate limit, the check is skipped silently.

Installed by the `.msi` (the app runs from
`%LOCALAPPDATA%\Programs\USB-helper`): on a choice, the app downloads that
release's `.msi` and its SHA-256 file to `%LOCALAPPDATA%\USB-helper-installer`,
verifies it, runs it (`msiexec /i ... /passive /norestart LAUNCHAPPONEXIT=1`:
a progress bar, no questions, never a restart of the computer) and closes, so
the installer can replace its files. When the install is done, the installer
starts the new version (`LAUNCHAPPONEXIT=1`). Releases from before the `.msi`
(0.5.0 and older) are not offered to an installed app.

Windows Installer may need the installed version's `.msi` again to remove that
version, at the next update or an uninstall from Apps & features. So that
folder keeps the installed version's `.msi` (about 80 MB) and the newest one,
and each update deletes only older ones. Delete the folder by hand after
uninstalling if you want the space back.

Installed by the in-app update of 0.8.0 or older? That covers every version
those updates installed, including 0.8.1 when 0.8.0 installed it. Their `.msi`
is in `%TEMP%\USB-helper-update`, which those updates emptied before the next
download and which Windows' own temp cleanup can empty too (#222). Removing that
version, by an update or from Apps, then asks for "a network resource that is
unavailable" and names the `.msi`. Leave the prompt open, put that `.msi` back
and click OK, for example for 0.8.0:

```
New-Item -ItemType Directory -Force "$env:TEMP\USB-helper-update" | Out-Null; Invoke-WebRequest "https://github.com/klangche/USB-helper/releases/download/v0.8.0/usb-helper-0.8.0-windows-x64.msi" -OutFile "$env:TEMP\USB-helper-update\usb-helper-0.8.0-windows-x64.msi"
```

If the prompt was cancelled, USB-helper may be missing from Apps while parts of
it stay registered. Running the current `.msi` again shows the same prompt for
the old `.msi`; put that one back as above and click OK. Other ways out have not
been tested.

A `.msi` download that fails verification is never run; the window shows why
and the current version keeps running. A download that stalls is given up
after 10 minutes.

Portable: on a choice, the app opens that release's page on GitHub in the
browser and keeps running; it does not download, unzip or replace itself.
Download the new zip there and unzip it as under Install. The log
(#87) lists the check's result, for example "Update check: beta
0.7.0-beta.2 is newer than 0.6.0", "Opened the release page of
0.7.0-beta.2",
"Update skipped" or "Update check skipped: no answer within 2 s".
