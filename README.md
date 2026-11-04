# USB-helper

A portable diagnostic tool that shows the USB topology of a computer as a live
tree and checks a BYOD/BYOM setup (bring your own device / monitor) against
that computer's hardware limits.

Status: first version for Windows; computer profiles still to be added.

## Features

- Live USB tree with drawn tree lines from one root for the computer (the
  host): controllers, hubs, occupied ports and devices. Each controller has a
  chevron that hides or shows its tree; at start only the busiest controller is
  open, and a collapsed controller's name is bold. Updated on connect and
  disconnect without rebuilding the whole tree. Hubs and devices show the
  product name they report, falling back to the Windows name. Every line, from
  the computer down, takes the worst status of everything below it, so one
  unstable device colours its whole path up to the computer. DisplayLink
  devices are shown as displays ("Extended display: DisplayLink driver
  required"), and the hubs above them say they have a display. A port in the
  tree (one with something on it) that Windows reports as USB-C shows a
  "USB-C" badge; other ports, and ports Windows says nothing about, show none.
  The log lists the flag of every port Windows reports it for.
- Displays group under the computer: each active monitor with its name and
  output type (built-in, HDMI, DisplayPort, DisplayPort over USB4/Thunderbolt,
  DVI, VGA, indirect such as DisplayLink, wireless). A monitor that Windows
  puts in the same device container as a USB hub or device (for example a
  USB-C monitor with a built-in hub) is shown under that hub or device instead.
  Updated when a monitor is connected or removed; each change is logged.
- The tree as plain text, in the log after the first read and every change,
  and on the clipboard with Edit > Copy tree as text.
- Export full setup: one zip with everything detected (`setup.json`), the
  tree as text, a screenshot and the log, without serial numbers.
- Per controller: its hub count, deepest tier and deepest hop.
- Hub count, tier depth and hop depth, counted from the computer's own port
  and each checked against the computer model's own limits.
- Active cables and extenders that the operating system does not show can be
  marked with the "+" on their connection line ("+N hops"); the marks count in
  every tier and hop check.
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
  predicted risks and unknown information. The findings list shows only real
  findings (a limit or recommendation exceeded, display problems, errors,
  disconnects and reconnects); unknown limits and database facts are in the
  log.
- A log of every event in the session (start, database, connects,
  disconnects, reconnects, errors, profile checks, cable marks and every
  finding), with time, exportable as a text file.
- No bandwidth, throughput or speed calculations.

## Hardware database

Human-readable YAML in `database/`, version-controlled and reviewed:

```
database/
├── base.yaml          shared structural defaults
├── platforms/         operating system and architecture profiles
└── computers/         one file per computer model, by manufacturer
```

The build puts these files into the app, so the download has no database
folder.

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
  mst: false     # DisplayPort Multi-Stream Transport; null = unknown
  restrictions:
    - "HDMI and the left front port share one display output."
```

Display checks are separate from the topology limits. The app counts the active
external displays that use the computer's own outputs (HDMI, DisplayPort,
DisplayPort Alt Mode, Thunderbolt, USB4) and compares the count with
`displays.max_external`; DisplayLink and wireless displays are not counted.
DisplayLink adapters are found in the USB tree (by DisplayLink's USB vendor id,
0x17E9) and checked against the ports' `displaylink` values. Which physical
port a monitor uses cannot be detected, so the port capabilities and
restrictions are listed, not matched to monitors.

`displays.mst` says whether the computer supports DisplayPort Multi-Stream
Transport, several independent displays on one DisplayPort output. When a
checked profile says `mst: false` and more than one external DisplayPort
display is active (DisplayPort, DisplayPort Alt Mode, or DisplayPort over
USB4/Thunderbolt; DisplayLink, HDMI and built-in displays never count), the
first one is judged as usual and every further one is potentially unstable
(orange) with "No MST on <computer>: this display is duplicated; use a second
USB-C port or DisplayLink." The computer named is the profile's, which need not
be the one running the app, so a Mac can be planned for on Windows. Above
`displays.max_external` the result stays unstable (purple). When `mst` is
unknown, the log says so and nothing is judged. The GUI reads the monitors
again when Windows reports a display change, and at every USB change and
profile change. A
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
7. The database is built into the app, so users get the change with the next
   release or beta build; an existing download keeps its own database.

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
is counted as before, as an external hub, and the log says so.

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
does not turn anything grey and is reported in the log, not in the findings
list. A limit from an `unverified` file counts too; the log says it is
unverified, the window does not. With several
profiles checked, each line shows the worst result of all of them, and each
profile gets its own overall result: the highest of its lines, its limits and
its display check (purple above `displays.max_external`, orange for a
DisplayLink adapter on a computer without DisplayLink support or a further
DisplayPort display on a computer without MST). The findings
list then shows only the findings of the strictest checked profile: the one
with the lowest `max_hops`; on a tie the lowest `recommended_hops`, then
`max_tiers`, then `max_hubs`. An unknown limit counts as higher than any known
one; when still tied, the first in the menus wins. Detected facts (operating
system errors, disconnected and reconnected devices) are always listed. Every
finding names its device, port or computer and the reason, and is marked as a
detected fact, a verified database fact, a predicted risk or unknown.

The findings list shows only real findings: a limit or recommendation
exceeded, a display problem (more external displays than
`displays.max_external`, a DisplayLink adapter on a computer without
DisplayLink support, a further DisplayPort display without MST), an operating
system error, a disconnected device and a
reconnected one. Everything else is a note and goes to the log only (see
Usage): unknown limits and values, unverified limits, limits and port
capabilities from the database, documented limitations and restrictions, the
active display count, a connected DisplayLink adapter, hubs that cannot be
read and hubs that may be built in. So a profile with unknown limits shows no
findings while everything is within the limits it knows. A limit
or model exception is a verified database fact only when the file that sets it
is itself `verified` or `partial`. A limit a computer profile inherits keeps
its platform file's status.

If a hub, port or controller cannot be read (a hub without ports, a port with an
error and nothing on it, a controller without a root hub), what is behind it
cannot be counted: limits that are not already exceeded are reported as unknown,
and devices seen behind it earlier are grey (unknown), not red. Errors on a
controller or root hub make the overall result purple and are listed as
findings; they have no connection line of their own.

## Platforms

- Windows x64: first target. A per-user installer (`.msi`) or a portable zip,
  both without administrator rights. The network is used only for the optional
  update check at start; without it, the app works as normal.
- macOS: experimental universal builds for Apple silicon and Intel are
  attached to the beta releases; live diagnostics are still in development.
  Linux: planned.

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
`%APPDATA%\USB-helper` if you want its settings gone too (see Settings).

### macOS universal disk image

Every beta (pre-release `vX.Y.Z-beta.N`, see Releases) also carries
`usb-helper-<version>-macos-universal.dmg` and its `.sha256`, built from the
same commit and version as the Windows files. Download the dmg from the beta's
page (not a zip), open it, and drag `USB-helper.app` to the Applications
folder. Stable releases have no macOS file yet. These are unsigned test
builds, and the app's update check ignores them. The dmg is added
a little after the Windows files; if the macOS build failed, the beta has none. If Gatekeeper blocks the app and you trust this
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
release's `.msi` and its SHA-256 file to `%TEMP%\USB-helper-update`, verifies
it, runs it (`msiexec /i ... /passive /norestart LAUNCHAPPONEXIT=1`: a
progress bar, no questions, never a restart of the computer) and closes, so
the installer can replace its files. When the install is done, the installer
starts the new version (`LAUNCHAPPONEXIT=1`). Releases from before the `.msi`
(0.5.0 and older) are not offered to an installed app.

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

## Usage

Start USB-helper from the Start menu (installer) or `USB-helper.exe` in the
`USB-helper` folder (portable zip; see Install). The window opens at once and
shows what it is reading
("Loading database...", "Reading USB devices...", "Reading displays..."); the
profiles and the tree fill in as they arrive. The window shows the USB tree.
Its root is this computer, named after its computer profile when one matches,
else by the manufacturer and model Windows reports, else by its machine name;
the USB controllers are its children. Thin lines join the items, starting at the
computer. Each line is drawn in the worst status of everything it leads to: a
stable tree is green all the way, and one unstable (purple) or disconnected
(red) device colours its path up to the computer while its stable neighbours
stay green. The short line into each hub and device keeps that item's own
status colour, with the result word after its name. Empty ports are left
out. Only the USB controller rows can be collapsed: click a controller's
chevron, double-click it, or use the left and right arrow keys to hide or show
its tree; while it is hidden the controller's name is bold. At start only the
selected controller (see below), the one with the most connected hubs and
devices, is open, and only when it has any. The computer's row, the Displays
group and everything below a controller, hubs built into the computer
included, are always open. The choice is kept while the app runs. A
DisplayLink device is shown as a display: a "Display" badge and "Extended
display: DisplayLink driver required" before its name;
every hub above it says it "has a display". After the controllers, a
"Displays" group lists each active monitor with its name and output type, for
example "DELL U2723QE (HDMI)"; a monitor without a name, often the built-in
panel, is listed as "Monitor". A monitor is shown under a USB hub or device
instead when Windows says both belong to one physical device: they have the
same container id (for example a USB-C monitor and the USB hub built into it;
the monitor is shown under the highest USB node of that device). Nothing else
places a monitor: the container shared by everything built into the computer
links nothing, and a monitor without a match stays in the group. The log says
when a monitor is shown under a USB node and when that ends. While a
DisplayLink device is connected, indirect monitors
are not listed in the group: they stay under their DisplayLink device.
Selecting the group or a monitor in it does not change what the status field
covers; a monitor shown under a USB node selects that node's controller, like
anything else under it.
The group follows monitors as they are connected and removed, and the log
lists the monitors at start and each one connected or removed. A port in the
tree that Windows reports as a USB-C connector (from its port connector
properties) has a "USB-C" badge after its name; this changes no result. Empty
ports are not in the tree; the log lists the flag of every port Windows
reports it for. Each controller shows its hub count, deepest tier and deepest
hop. Next to the tree is a
summary with the overall result, the status field, disconnected and
reconnected devices and the findings. The overall result, the status field
and the findings cover one USB
controller's tree, named under "Status". At start, and when that controller
disappears, the controller with the most connected hubs and devices (built-in
hubs not counted) is selected; on a tie the first in the tree. Click another
controller, or anything under it, to switch; click the computer at the top to
cover all controllers. The hub count is always the whole computer's. The tree
and the disconnected list always show everything. At start, the platform
profile for the detected operating system and processor (for example
`windows-amd` on Windows with an AMD processor) is checked; uncheck it to
judge by others. When no platform profile matches, nothing is checked and
every line is grey (unknown). Computer profiles are never checked
automatically. In the top bar, check the systems to judge the setup by in
the two drop-down lists,
"Operating systems" (generic platform profiles such as `windows-intel`) and
"Computers" (computer profiles); any mix can be checked. Each line then shows
the worst result over the checked profiles. The
status field has one row per checked profile with its result (stable,
potentially unstable, unstable or unknown, in its colour) and its hub, tier,
hop and display checks. With several profiles checked, the findings come from
the strictest one only and name it (see the diagnostic rules). Plug and unplug
devices: only the affected part of the tree changes. A device that is
unplugged stays red at its last port; one that comes back is purple
(unstable). Click "Clear" next to it to reset it.

Is an active cable or extender plugged in that the tree does not show? Click
the small "+" in the middle of the line from the port to the hub or device
behind the cable (or select that item and press Enter or the context menu
key) and pick its extra hops (+1 to +5). The marked line is
drawn dashed with a "+N hops" badge, so it is clear that you added it and
Windows did not detect it. Every tier and hop check counts the extra hops for
everything behind that line. Click the "+" again to change the mark, or pick
"None" to remove it. Marks last until the app closes.

Click "Log" in the top bar to open the log: every event of this session with
its time, newest at the bottom. It lists the start and loading stages, the
database load result, per hub which ports Windows reports as USB-C or not
(ports it says nothing about are left out), every connect, disconnect,
reconnect and error the operating system reports, every profile you check or
uncheck, every cable mark, and every finding when it appears, including the
ones about unknown limits. After the first read and after every USB change
(and every display change that shows), it also lists the whole tree as text,
so the log shows how the tree looked at each moment. Click "Export" in the log
window to save it as a text file (`usb-helper-log-<date>-<time>.txt`), for
example to send with a bug report; File > Export log does the same from the
main window. The log is kept in memory only and starts empty each time the app
starts.

To explain a setup to someone, or to report one to the developers, use File >
Export full setup. It saves one zip with four files,
`usb-helper-setup-<manufacturer>-<model>-<date>.zip`:

- `setup.json`: everything the app detected and found, with a `format_version`:
  the app version, the computer (manufacturer, model, processor, platform
  profile), the checked profiles, every controller, hub, port
  and device (name, vendor and product id, USB-C and user-connectable flags),
  your active-cable marks, the monitors and their output type, and each
  checked profile's result with every finding, the unknown ones included.
- `tree.txt`: the tree as text, as Edit > Copy tree as text gives it.
- `screenshot.png`: the main window as shown.
- `log.txt`: the log.

The export holds device and monitor names and vendor and product ids, but no
serial numbers: the Windows device paths, which can hold them, are replaced by
positions in the tree ("1/1/2"), the Windows container ids are replaced by
numbered ones that still show which devices belong together, and `setup.json`
leaves out the computer's
network name, which is often an asset or service tag. When Windows reports no
manufacturer and model, the window, and so `screenshot.png` and `tree.txt`,
name the computer by its network name instead. The developers can load
`setup.json` in the tests as if the app had detected it, so a reported setup
becomes a test case.

No profile for your computer yet? Click "Export computer profile" (or File >
Export computer profile) and save the file (`<manufacturer>-<model>.yaml`). It
holds what the app detected: the manufacturer and model exactly as Windows
reports them, the processor, the matching platform profile, the USB
controllers with their root hub ports, and the number of active external
displays. It sets no limits, so it uses the platform profile's, and the status
is `unverified`. Send the file to the developers; they add model-specific
limits from cited sources.

The menu bar at the top has File (Export computer profile, Export log,
Export full setup, Settings, Exit), Edit (Copy tree as text) and Help (Check
for updates, About USB-helper). Copy tree as text puts the tree on the clipboard
as plain text, the same text the log gets, for pasting into an email:

```
Dell Inc. Latitude 7450
|-- Controller (Hubs: 1, deepest tier: 5, deepest hop: 3)
|   `-- ROOT
|       `-- Port 1 [USB-C]
|           `-- [+2 hops] Dock - stable
|               `-- Port 1
|                   `-- Keyboard (046D:C31C) - stable
`-- Displays
    `-- DELL U2723QE (HDMI)
```

Each row has its badges in brackets ("+N hops" for a marked cable, "USB-C",
"Display"), the result word after " - " and its notes in parentheses.

Press Alt, or Alt with the underlined letter (for example Alt+F for File), to
use the menu from the keyboard. Check for updates runs the update check again
(see Install) and also says when the running version is up to date or the
check failed; it is greyed out where the app has no update check (outside
Windows). About shows the running version, the license, the icon's attribution
and a link to this repository.

### Settings

File > Settings opens the settings. Each change is saved at once:

- **Check for updates at start** (on by default): off, the app does not ask
  GitHub after it starts; Help > Check for updates still works.
- **Offer beta versions** (on by default): off, only stable releases are
  offered, and a newer beta alone counts as up to date.
- **Remember the checked profiles between starts** (off by default): on, the
  operating systems and computers checked in the two lists are checked again
  at the next start, instead of only the detected platform profile. If
  nothing was checked, the detected platform profile is checked, as without
  the setting. Turning it off forgets them.

The settings are stored in `%APPDATA%\USB-helper\settings.json` (the user's
roaming app data folder), the same for every copy of the app, so they survive
updates and moving the app's folder. With a roaming Windows profile they
also follow the user to other computers, remembered profiles included.
Without the file the defaults apply. A
file that cannot be read is named in the log with the reason, and the
defaults apply; it is only replaced when a setting is changed.

## Development

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
and the `.msi` (the starter and the published app's loose files, see Install),
creates the stable tag `vX.Y.Z`, and publishes a GitHub release with notes from
the pull request titles since the last release. Every merge into `dev`
publishes a pre-release (beta) the same way: `vX.Y.Z-beta.N`, where `X.Y.Z` is
the version the next release would get and `N` counts up from 1 for each beta
of it (`release/beta-version.sh`).
Each beta also gets the macOS dmg as an extra asset. A separate job in
`.github/workflows/macos.yml` waits for the beta release of the same commit,
builds the dmg with that version and attaches it, so a slow macOS runner never
delays or fails the Windows release; if that job fails, re-run it. Stable releases from `main` have no macOS asset yet.
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

Contributors and AI agents follow AGENTS.md.

## License

MIT, see [LICENSE](LICENSE).

The app icon is the "usb" icon from [Font Awesome Free](https://fontawesome.com)
by Fonticons, Inc., licensed under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
([Font Awesome Free license](https://fontawesome.com/license/free)). Changed:
square view box and drawn white. USB is a trademark of its owner; the icon
stands for USB connections, and no endorsement is implied.
