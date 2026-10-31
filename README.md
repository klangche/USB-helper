# USB-helper

A portable diagnostic tool that shows the USB topology of a computer as a live
tree and checks a BYOD/BYOM setup (bring your own device / monitor) against
that computer's hardware limits.

Status: first version for Windows; computer profiles still to be added.

## Features

- Live USB tree with drawn tree lines: controllers, hubs, occupied ports and
  devices, every branch open, updated on connect and disconnect without
  rebuilding the whole tree. Hubs and devices show the product name they
  report, falling back to the Windows name.
- Per controller: its hub count, deepest tier and deepest hop.
- Hub count, tier depth and hop depth, counted from the computer's own port
  and each checked against the computer model's own limits.
- Active cables and extenders that the operating system does not show can be
  marked per port ("+N hops"); the marks count in every tier and hop check.
- Display capabilities per port: DisplayPort Alt Mode, MST, Thunderbolt,
  USB4, HDMI and DisplayLink.
- Disconnected devices and connection errors reported by the operating system.
- Connection status colours, per checked profile:
  - Green: stable, within every limit the profile knows.
  - Orange: potentially unstable, above a recommendation or a documented
    limitation applies.
  - Purple: unstable, a limit is exceeded, the operating system reports an
    error on a connection that is still present, or the device disconnected
    and came back.
  - Red: an expected device or connection is missing.
  - Grey: unknown, no profile checked or the profile has no limits.
- Results separate detected hardware facts, verified database facts,
  predicted risks and unknown information. Unknown limits are listed as
  findings.
- No bandwidth, throughput or speed calculations.

## Hardware database

Human-readable YAML in `database/`, version-controlled and reviewed:

```
database/
├── base.yaml          shared structural defaults
├── platforms/         operating system and architecture profiles
└── computers/         one file per computer model, by manufacturer
```

A computer profile inherits from a platform profile, which inherits from
`base.yaml`. Every profile records its sources, a verification status
(`verified`, `partial`, `unverified`) and the date it was last checked.

Inheritance rules:

- An omitted field inherits the parent's value.
- An explicit value overrides the parent's value.
- An explicit `null` overrides the parent's value and means unknown. Unknown
  is never replaced by a guessed default. `verification.status` cannot be
  `null`; use `unverified`.
- Mappings merge field by field. Lists add the child's items after the
  parent's. An empty list (`[]`) or mapping (`{}`) adds nothing and never
  erases inherited data; use `null` to mark inherited data as unknown.

The database is only read, never written, by the application. Loading stops
with a list of every problem found, each with file and line: invalid YAML,
duplicate fields, unknown fields, wrong value types, invalid verification
status or date, duplicate ids, a missing parent, or a parent of the wrong kind
(platforms inherit from `base`, computers from a platform, so inheritance
cannot loop).

### Platform profiles

Each platform profile sets the hop limits from the `platformStability` values in
the [usb-script config](https://github.com/klangche/usb-script/blob/main/usb-tree-config.json)
(`max` as `max_hops`, `rec` as `recommended_hops`), with status `partial`:
tested in practice, not a manufacturer specification. Hub count and tier depth
stay unknown.

| Platform profile | `max_hops` | `recommended_hops` |
|---|---|---|
| `windows-intel`, `windows-amd` | 7 | 5 |
| `windows-arm` | 5 | 3 |
| `macos-intel` | 7 | 5 |
| `macos-apple-silicon` | 5 | 3 |
| `apple-mobile-a-series` (iPhone, iPad) | 4 | 2 |
| `linux-x64` | 6 | 4 |
| `linux-arm` | 5 | 3 |
| `android-phone-qualcomm` | 5 | 3 |
| `android-tablet-exynos` | 4 | 2 |

### Ports and displays

Port capabilities are recorded per physical port under `ports:` in a computer
profile, and only where documented. A field that is left out is not claimed
either way; `null` means unknown.

```yaml
ports:
  left_front:
    location: left side, front
    usb: usb-c
    thunderbolt: 4            # generation
    usb4: true
    displayport_alt_mode: true
    displayport_mst: null     # unknown
    native_displayport: false
    hdmi: false
    displaylink: true
    limitations:
      - "Charging only below 15 W."
displays:
  max_external: 2
  restrictions:
    - "HDMI and the left front port share one display output."
```

Display checks are separate from the topology limits. The app counts the active
external displays that use the computer's own outputs (HDMI, DisplayPort,
DisplayPort Alt Mode, Thunderbolt, USB4) and compares the count with
`displays.max_external`; DisplayLink and wireless displays are not counted.
DisplayLink adapters are found in the USB tree and checked against the ports'
`displaylink` values. Which physical port a monitor uses cannot be detected, so
the port capabilities and restrictions are listed, not matched to monitors. The
GUI reads the display count again at every USB change and profile change; a
monitor plugged in without any USB change shows up at the next one. A
display count is never derived from the number of ports, a USB-C port is never
assumed to carry video, and no bandwidth is calculated.

### Adding or updating a computer profile

1. Start from a file exported by the app ("Export computer profile"), or copy
   `database/computers/.template.yaml`. Save it as
   `database/computers/<manufacturer>/<model>.yaml`.
2. Set `inherits` (a platform id from `database/platforms/`), `manufacturer`
   and `model` exactly as the computer reports them. Do not add an `id`: it is
   computed (see below). If the model is a technical string (for example
   `Mac17,5`), add `name` (for example `MacBook Neo`): the app shows it, but
   only `manufacturer` and `model` are matched.
3. Limits (`limits.max_hubs`, `max_tiers`, `max_hops`, `recommended_hops`)
   left out inherit the platform profile's values. Set a value only where this
   model differs (for example built-in hubs), or `null` when the platform's
   value does not apply and the right one is unknown.
4. Fill in only values you can cite. List every source in `sources`, set
   `verification.status` and `verification.last_checked` (`YYYY-MM-DD`).
   Remove template fields you do not need; they then inherit.
5. Run `dotnet test`: it loads the whole database and reports any problem.
6. Open a pull request. Changes to verified values need review; the git history
   keeps every correction.

A computer profile's id is never written in the file. The app computes it from
`manufacturer` and `model` (ignoring case and extra spaces): 6 lowercase letters
and digits, for example `y98inr` for `Dell Inc.` / `Latitude 7450`. The same
names always give the same id, so nobody can set or change it, and a second
file for the same model is rejected. Platform profiles keep their written `id`,
which computer profiles name in `inherits`.

## Topology counting

The three limits in a computer profile are checked independently:

- **Tier depth** (`max_tiers`): the computer's own port is on tier 1, each hub
  below it adds one, and the device's own tier counts.
- **Hop depth** (`max_hops`): the number of hubs between the computer's own
  port and the device.
- **Hub count** (`max_hubs`): all external hubs connected to the computer, root
  hubs and built-in hubs excluded.

A profile may also set `recommended_hops`: hop depths above it still work up to
`max_hops`, but less reliably.

Counting starts at the computer's own port, the one the user plugs into. Many
laptops have a hub built in between the root hub and that port (for the webcam,
Bluetooth or a card reader). On Windows the app asks for each port whether it is
user-connectable; a hub reached only through ports that are not is built into
the computer and does not count. A device in the laptop's own port has 0 hops.
If Windows does not say for a port (older versions, some drivers), a hub on it
is counted as before, as an external hub, and a finding says so.

Hop and tier limits in the database (platform and computer profiles) are
counted the same way, from the computer's own port. A computer with built-in
hubs or other quirks sets its own `max_hops` in its computer profile.

Hubs inside docks and monitors count like any other hub, because the operating
system reports them as hubs. Example: laptop port, dock hub, monitor hub,
keyboard. The keyboard is at tier 4 with 2 hops, and there are 2 hubs.

Active cables and extenders that Windows shows as a hub count automatically.
Ones it does not show are marked by the user on the port they are plugged into:
a mark of +N hops adds N hops and N tiers to everything behind that port, in
every checked profile. It does not change the hub count.

## Diagnostic rules

Each connection line (from a hub port to the hub or device on it) gets one
status per checked profile; when several apply, the first in this list wins:

1. Red: a hub or device seen since the app started is no longer connected. It
   stays red until it returns or is cleared.
2. Purple (unstable): the operating system reports an error on the port or the
   device; or the hub or device disconnected and came back since the app
   started, however long or short the drop, until it leaves again or is
   cleared; or its tier or hop depth is above the profile's `max_tiers` or
   `max_hops`; or it is a hub and the hub count is above `max_hubs`.
3. Orange (potentially unstable): its hop depth is above `recommended_hops`,
   within `max_hops`.
4. Grey (unknown): no profile is checked, or the profile has none of these
   limits.
5. Green (stable): none of the above.

Stability is judged on the limits the profile knows: an unknown limit (`null`)
is listed as a finding and does not turn anything grey. A limit from an
`unverified` file counts too; its findings say it is unverified. With several
profiles checked, each line shows the worst result of all of them, and each
profile gets its own overall result: the highest of its lines, its limits and
its display check (purple above `displays.max_external`, orange for a
DisplayLink adapter on a computer without DisplayLink support). Every finding
names its device, port or
computer and the reason, and is marked as a detected fact, a verified database
fact, a predicted risk or unknown. A limit or model exception is a verified
database fact only when the file that sets it is itself `verified` or
`partial`. A limit a computer profile inherits keeps its platform file's
status.

If a hub, port or controller cannot be read (a hub without ports, a port with an
error and nothing on it, a controller without a root hub), what is behind it
cannot be counted: limits that are not already exceeded are reported as unknown,
and devices seen behind it earlier are grey (unknown), not red. Errors on a
controller or root hub make the overall result purple and are listed as
findings; they have no connection line of their own.

## Platforms

- Windows x64: first target. Portable, no installation, no administrator
  rights, no network connection needed.
- macOS and Linux: planned, with the same GUI and diagnostics.

## Install

Windows x64: download `usb-helper-<version>-windows-x64.zip` from the
[releases](https://github.com/klangche/USB-helper/releases), unzip it, and
follow `READ_ME_FIRST.txt` in the zip. In short: keep the `database` folder next
to `USB-helper.exe`, unblock the unsigned exe once (`Unblock-File -Path
.\USB-helper.exe` in PowerShell), and start it. No installation, no
administrator rights and no network connection are needed. Releases marked
pre-release (`-beta.N`) are test builds of the next version.

## Usage

Start `USB-helper.exe`. The window shows the USB tree: thin grey lines join
the items, and the line to each hub and device is drawn in its status colour,
with the result word after its name. Empty ports are left out. Each
controller shows its hub count, deepest tier and deepest hop. Next to the tree
is a summary with the overall result, the status field, disconnected and
reconnected devices and all findings. At start, the platform profile for the
detected operating system and processor (for example `windows-amd` on Windows
with an AMD processor) is checked; uncheck it to judge by others. When no
platform profile matches, nothing is checked and every line is grey
(unknown). Computer profiles are never checked automatically. In the top bar,
check the systems to judge the setup by in the two drop-down lists,
"Operating systems" (generic platform profiles such as `windows-intel`) and
"Computers" (computer profiles); any mix can be checked. Each line then shows
the worst result over the checked profiles. The
status field has one row per checked profile with its result (stable,
potentially unstable, unstable or unknown, in its colour) and its hub, tier,
hop and display checks. A finding that only some checked profiles report names
its profile. Plug and unplug devices: only the affected part of the tree
changes. A device that is unplugged stays red at its last port; one that comes
back is purple (unstable). Click "Clear" next to it to reset it.

Is an active cable or extender plugged in that the tree does not show? Select
the port it is plugged into and pick its extra hops (+1 to +5) under the tree.
The port shows the mark, and every tier and hop check counts it. Marks last
until the app closes.

No profile for your computer yet? Click "Export computer profile" and save the
file (`<manufacturer>-<model>.yaml`). It holds what the app detected: the
manufacturer and model exactly as Windows reports them, the processor, the
matching platform profile, the USB controllers with their root hub ports, and
the number of active external displays. It sets no limits, so it uses the
platform profile's, and the status is `unverified`. Send the file to the
developers; they add model-specific limits from cited sources.

## Development

C# on .NET 10, Avalonia for the GUI, YamlDotNet for the database, xUnit for
tests. Builds and releases run on GitHub Actions: every pull request is built
and tested on Windows. Pull requests go to the `dev` branch. When `dev` is
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
`Release 1.0.0: ...` makes 1.0.0. The workflow then builds the portable zip,
creates the tag `vX.Y.Z` and publishes a GitHub release with notes from the pull
request titles since the last release. Every merge into `dev` publishes a
pre-release (beta) the same way: `vX.Y.Z-beta.N`, where `X.Y.Z` is the version
the next release would get and `N` counts up from 1 for each beta of it
(`release/beta-version.sh`). Beta tags never count as a release
(`release/last-release.sh`).

- `src/UsbHelper.Core`: database, detection interface (`IUsbDetector`), and
  later topology and diagnostics. No GUI or operating-system code.
- `src/UsbHelper.Detection.Windows`: Windows USB detection (SetupAPI and the USB
  hub IOCTLs).
- `src/UsbHelper.App`: the GUI. Run it from source on Windows with
  `dotnet run --project src/UsbHelper.App`. It reads `database/` next to the
  executable (copied there by the build), picks the computer profile only when
  the manufacturer and model reported by Windows match a profile exactly, and
  otherwise lets the user choose. All texts it shows are in
  `src/UsbHelper.App/Resources/Strings.resx`.
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

Contributors and AI agents follow AGENTS.md.

## License

MIT, see [LICENSE](LICENSE).
