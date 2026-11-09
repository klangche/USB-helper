# Development

The build, the release process and the source layout.

C# on .NET 10, Avalonia for the GUI, YamlDotNet for the database, xUnit for
tests. Builds and releases run on GitHub Actions: every pull request is built
and tested on Windows; a separate macOS workflow also builds and tests it on macOS. Pull requests go to the `dev` branch. When `dev` is
ready, one pull request from `dev` to `main` is merged with a merge commit
(not squash or rebase, so every pull request since the last release stays
visible; `dev` is kept), and that merge is released automatically by
`.github/workflows/release.yml`: `main` only holds released versions.
`release/next-version.sh` picks the version from the titles of all pull
requests merged since the last release (`Fixed:` and `Security:` bump the patch
version, `Added:`, `Changed:` and `Removed:` the
minor version, a `!` such as `Changed!:` the major version; titles with none of
these prefixes make no release). Versions start at 0.1.0. While the version is
0.x, a `!` bumps only the minor version; a pull request titled
`Release 1.0.0: ...` makes 1.0.0. The workflow then builds the portable zip
and the `.msi` (the starter and the published app's loose files, see [Install](install.md)),
creates the stable tag `vX.Y.Z`, and publishes a GitHub release with notes from
the pull request titles since the last release. Every merge into `dev`
publishes a pre-release (beta) the same way: `vX.Y.Z-beta.N`, where `X.Y.Z` is
the version the next release would get and `N` counts up from 1 for each beta
of it (`release/beta-version.sh`).
Each release and each beta also gets the macOS dmg as an extra asset. A separate job in
`.github/workflows/macos.yml` waits for the release (on `main`) or beta (on `dev`) of the same commit,
builds the dmg with that version and attaches it, so a slow macOS runner never
delays or fails the Windows release; if that job fails, re-run it. To add the dmg
to an existing release, run that workflow by hand with its tag (for example `v0.7.0`).
Beta tags never count as a release (`release/last-release.sh`).

- `src/UsbHelper.Core`: database, detection interface (`IUsbDetector`), and
  later topology and diagnostics. No GUI or operating-system code.
- `src/UsbHelper.Detection.Windows`: Windows USB detection (SetupAPI and the USB
  hub IOCTLs).
- `src/UsbHelper.App`: the GUI. Run it from source on Windows with
  `dotnet run --project src/UsbHelper.App`. It reads the database built into
  it from `database/` (rebuild after a database change), picks the computer
  profile only when the manufacturer and model reported by Windows match a
  profile exactly, and otherwise lets the user choose. All texts it shows are in
  `src/UsbHelper.App/Resources/Strings.resx`. The update check (`Update/UpdateCheck`,
  `Update/ReleaseVersion`), the verified `.msi` download (`Update/VerifiedDownload`)
  and the popup's model (`ViewModels/UpdateViewModel`) are tested in `tests/UsbHelper.App.Tests`
  against a fake release feed. `release.yml` publishes `<zip>.sha256` with every
  release and beta for the update check.
- `installer/Package.wxs`: the per-user `.msi`, built in `release.yml` with
  WiX Toolset (the `wix` .NET tool, pinned in `.config/dotnet-tools.json`, and
  its UI and Util extensions, pinned in `release/build-msi.ps1`; Windows only).
  It uses the standard minimal setup UI with the "Launch USB-helper" box, and
  starts the app after an install with `LAUNCHAPPONEXIT=1`, which the app's own
  update passes (`Update/MsiInstall.cs`). It packs the
  same files as the zip. Its `UpgradeCode` must never change: it is how a newer
  `.msi` finds the installed one. `release.yml` installs, upgrades and
  uninstalls every `.msi` it builds as a check. `release/msi-version.sh` maps
  release versions to MSI versions, tested by `release/msi-version.test.sh`.
- `src/UsbHelper.App/Assets`: the app icon. `usb-helper.svg` is the source;
  `usb-helper.ico` (16 to 256 pixels) is generated from it and is the exe icon
  (`ApplicationIcon`), which Avalonia also uses for every window. After changing
  the SVG, run `src/UsbHelper.App/Assets/make-icon.sh` (needs `rsvg-convert` from
  librsvg and ImageMagick's `convert`; on Windows in WSL or Git Bash) and commit
  both files.
- `src/UsbHelper.Starter`: `USB-helper.exe` at the top of the zip and the
  install folder (#130), compiled with NativeAOT (its compiler packages are
  pinned in the project; update both pins together). It starts
  `app\UsbHelper.exe` and shows a message from `Strings.resx` when that is
  missing; tested in `tests/UsbHelper.Starter.Tests`. The app finds the top
  folder as the parent of its `app` folder (`UpdateKinds.TopFolder`).
  `release.yml` starts both the unzipped zip and the installed `.msi` through
  `USB-helper.exe` and waits for the window (`release/start-test.ps1`).
- `src/UsbHelper.Cli`: prints the detected USB tree, to compare with USBView or
  Device Manager ("Devices by connection"). Windows only:
  `dotnet run --project src/UsbHelper.Cli`. Add `-- --watch` to keep running
  and print each change while devices are plugged in or out.

Live updates on Windows: the app listens for USB device events (no polling, no
administrator rights), waits half a second for related events to settle, then
reads again only the hub the device is connected to, including everything
below it. When an event cannot be placed in the known tree (for example a dock
that brings its own USB controller), the whole tree is read again once and
only the differences are reported. A hub or device that Windows removes and
adds again within that wait leaves the tree unchanged; it is reported as
reconnected. Connection errors are reported only when Windows reports them on
the next read; a port error or device problem code that Windows sets without
any device event (for example more than half a second after the device
started) is seen at the next event on that hub. If a read fails, the whole tree
is read again two seconds later, with the events of the failed read.
