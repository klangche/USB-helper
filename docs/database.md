# Hardware database, counting and diagnostic rules

How the YAML database is built, how hubs, tiers and hops are counted, and how each connection gets its status.

## Hardware database

Human-readable YAML in `database/`, version-controlled and reviewed. Facts and advice are kept apart: platform and
computer files hold facts, guide files hold every limit and recommendation.

```
database/
├── base.yaml          facts from the USB specification
├── platforms/         one file per operating system, with its chips
├── guides/            one file per guide: usb-helper.yaml (the default), purelink.yaml, ochno.yaml, unisynk.yaml
└── computers/         one file per problem machine, by manufacturer
```

The build puts these files into the app, so the download has no database folder. The database is only read, never
written, by the application.

### base.yaml

```yaml
schema_version: 2
usb:
  max_tiers: 7                     # seven tiers on a path (USB 2.0 specification)
  max_devices_per_controller: 127  # a 7-bit device address; above it is unstable
usb4:
  max_router_depth: 5              # loaded, not judged yet: USB4 links are not detected yet (#267)
```

### Platform files

One file per operating system family. The file name is the platform (`windows`, `macos`, `linux`, `apple-mobile`,
`android`). Each chip is one platform profile, named after the file and the chip: `windows-intel`, `macos-a-series`.
Adding or renaming a chip is only a change to the file.

```yaml
name: macOS
chips:
  apple-silicon:
    name: M-series
    architecture: arm64
    built_in_hub_levels: 1   # hub levels inside the computer, in front of its own port; null when unknown
```

| Platform | Chips (`built_in_hub_levels`) |
|---|---|
| `windows` | `intel` 0, `amd` 0, `arm` 0 |
| `macos` | `intel` 0, `apple-silicon` (M-series) 1, `a-series` 2 |
| `linux` | `x64` 0, `arm` 0 |
| `apple-mobile` | `a-series` 2 |
| `android` | `phone-qualcomm` 0, `tablet-exynos` 1 (from the usb-script config's 4 hops) |

### Guide files

Every guide file has exactly the same shape: the header (`name`, `selectable`, `notes`, `sources`, `verification`), a
`limits` block for all platforms, and under `platforms` a section for every platform and chip of `platforms/`, each with
`mst` and a `limits` block. A manufacturer can get a copy, fill in numbers and send it back; a returned file with a
missing or extra key is rejected, with file, line and key.

```yaml
name: PureLink
selectable: true
limits:              # all platforms
  hubs:              # external hubs; per bus (USB 2, USB 3), or all together when the bus is not reported
    recommended:
    max:
  tiers:             # USB tiers as the specification counts them, built-in hub levels included
    recommended: 4
    max: 7
  hops:              # external hubs in front of a device
    recommended:
    max:
  camera_audio:      # the same for cameras and audio devices, on top of the general limits
    tiers:
      recommended: 3
      max: 4
    hops:
      recommended:
      max:
  exceeded_note:     # shown with the finding when a limit here is exceeded; at most 160 characters
platforms:
  macos:
    mst:             # 1: extends the desktop over DisplayPort MST, 0: does not
    limits: ...      # the same block
    chips:
      a-series:
        mst:
        limits: ...
```

How to read a value:

- An empty value means: use USB-Helper's value (`guides/usb-helper.yaml`). This is different from platform and computer
  files, where `null` means unknown.
- `none` means: this guide states no limit here.
- A chip's value wins over its platform's, which wins over the one under `limits`. The picked guide is read this way
  first, then USB-Helper's.
- USB-Helper's guide is the default (ticked at start) and the fallback, so every number and `mst` in it is filled in.
- `selectable: false` lists a guide that is not filled in yet; it is not offered.
- Each limit is checked on its own; nothing is worked out between hubs, tiers and hops. Above any `max` is unstable
  (purple), above any `recommended` potentially unstable (orange).
- A device's tier is the counted tier plus the chip's `built_in_hub_levels`: on an A-series chip (two levels) a device in
  the computer's own port is on tier 4.
- Findings and the log name the guide, the value and the file, for example
  `limits.recommended_tiers = 4 (PureLink: tiers.recommended 4 in guides/purelink.yaml)`, so a value that fell back reads
  as USB-Helper's.

| Guide | Values |
|---|---|
| `usb-helper` | To be confirmed by the owner: today's merged values as hops per chip (5 / 2 without built-in levels, 4 / 1 on M-series and Android tablets, 3 / 1 on A-series), `tiers.max` 7, cameras and audio 1 hop recommended, `mst` 1 on Windows and 0 elsewhere; the rest `none`. |
| `purelink` | `tiers` 4 / 7, `camera_audio.tiers` 3 / 4; everything else empty. |
| `ochno`, `unisynk` | Not filled in, not selectable. |

### Computer files

```yaml
platform: macos          # a platform file
chip_family: a-series    # one of its chips; "chip" is the processor name the computer reports
manufacturer: Apple Inc.
model: Mac17,5
name: MacBook Neo
built_in_hub_levels: 2   # optional, when this model differs from its chip
displays:
  max_external: 1
  mst: false             # optional: a hardware fact that wins over the guide's mst
```

A computer file never holds limits. Fields left out inherit the chip's facts; an explicit `null` means unknown and is
never replaced by a guessed default. Lists add the computer's items after the platform's; an empty list or mapping adds
nothing.

Loading stops with a list of every problem found, each with file and line: invalid YAML, duplicate fields, unknown
fields, wrong value types, invalid verification status or date, duplicate ids, a missing platform or chip, a guide file
whose shape differs, an empty value in USB-Helper's guide, an `mst` other than 0 or 1, an `exceeded_note` over 160
characters, a tier above `usb.max_tiers`, or a file that is not read (only `base.yaml`, `platforms/*.yaml`,
`guides/*.yaml` and the `.yaml` files below `computers/` are; a file whose name starts with a dot, such as
`.template.yaml`, is left out on purpose).

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

MST (DisplayPort Multi-Stream Transport, several independent displays on one
DisplayPort output) comes from the selected guide's `mst` for the platform and
chip (1 or 0), or from a computer file's `displays.mst`, which wins. When a
checked profile has no MST and more than one external DisplayPort
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

A computer gets a profile only when it is a problem machine: one that differs
from its platform profile through built-in hubs, unusual ports, display limits
or other quirks. Every other computer uses its platform profile, so the list
stays short.

1. Start from a file exported by the app ("Export computer profile"), or copy
   `database/computers/.template.yaml`. Save it as
   `database/computers/<manufacturer>/<model>.yaml`.
2. Set `platform` (a file in `database/platforms/`), `chip_family` (one of its
   chips), `manufacturer`
   and `model` exactly as the computer reports them. Do not add an `id`: it is
   computed (see below). If the model is a technical string (for example
   `Mac17,5`), add `name` (for example `MacBook Neo`): the app shows it, but
   only `manufacturer` and `model` are matched.
3. A computer file holds no limits: they are in the guides. Set
   `built_in_hub_levels` or `displays` only where this model differs from its
   chip, or `null` when the chip's value does not apply and the right one is
   unknown.
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
file for the same model is rejected. A platform profile's id is its file and
chip, for example `macos-a-series`.

## Topology counting

The three kinds of limits in a guide are checked independently:

- **Tier depth** (`tiers`): as the USB specification counts it: the host with
  its root hub is tier 1, each hub adds one, the device's own tier counts, and
  so do the chip's `built_in_hub_levels`.
- **Hop depth** (`hops`): the number of hubs between the computer's own
  port and the device.
- **Hub count** (`hubs`): all external hubs connected to the computer, root
  hubs and built-in hubs excluded, counted per bus (see below).

A USB 3 hub is two hubs in one box: a USB 2 hub on the USB 2 bus and a
SuperSpeed hub on the USB 3 bus, and each bus has its own tree. So all three
limits apply per bus, unless a profile says otherwise: a hub or device belongs
to the bus of the port it is plugged into, and `hubs` is checked against
the bus with the most hubs, not the sum of both halves. The bus comes from the
operating system only (macOS: the port's class, Windows: the protocols the port
reports), never from a name or a speed. A hub or device on a port whose bus is
not reported is in neither bus's count, so the counts are then "at least". All
hubs together are an upper bound for each bus: while they are within
the limit, the limit is within; above it, the limit is unknown and the log
says why. When no bus is reported at all, both buses are counted together, as
before.

Each kind has a `recommended` and a `max` value: above the recommendation still
works up to the maximum, but less reliably. Cameras and audio devices are also
judged by the guide's `camera_audio` limits. More devices on one controller
than `usb.max_devices_per_controller` (127) is unstable.

Counting starts at the computer's own port, the one the user plugs into. Many
laptops have a hub built in between the root hub and that port (for the webcam,
Bluetooth or a card reader). On Windows the app asks for each port whether it is
user-connectable; a hub reached only through ports that are not is built into
the computer and does not count. A device in the laptop's own port has 0 hops.
If Windows does not say for a port (older versions, some drivers), a hub on it
is counted as before, as an external hub, and the log says so.

Hops in the guides are counted the same way, from the computer's own port.
Tiers add the built-in hub levels of the chip, or of the computer file when it
sets its own.

Hubs inside docks and monitors count like any other hub, because the operating
system reports them as hubs. Example: laptop port, dock hub, monitor hub,
keyboard. The keyboard is at tier 4 with 2 hops (on a chip without built-in hub
levels), and there are 2 hubs.

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
   cleared; or its tier or hop depth is above a `max` of the selected guide
   (`camera_audio` too for a camera or audio device); or it is a hub on a bus
   whose hub count is above `hubs.max` (a hub whose bus is not reported counts
   as on every bus over the limit).
3. Orange (potentially unstable): above a `recommended` value, within every
   maximum. The finding adds the guide's `exceeded_note`, if any.
4. Grey (unknown): no profile or no guide is selected, or the guide states none
   of these limits.
5. Green (stable): none of the above.

Stability is judged on the limits the guide states: a limit it does not state (`none`)
does not turn anything grey and is reported in the log, not in the findings
list. A limit from an `unverified` file counts too; the log says it is
unverified, the window does not. With several
profiles checked, each line shows the worst result of all of them, and each
profile gets its own overall result: the highest of its lines, its limits and
its display check (purple above `displays.max_external`, orange for a
DisplayLink adapter on a computer without DisplayLink support or a further
DisplayPort display on a computer without MST). The findings
list then shows only the findings of the strictest checked profile: the one
with the lowest `hops.max`; on a tie the lowest `hops.recommended`, then
`tiers.max`, `tiers.recommended` and `hubs.max`. An unknown limit counts as higher than any known
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
is itself `verified` or `partial`: for a limit, the guide file it comes from.

If a hub, port or controller cannot be read (a hub without ports, a port with an
error and nothing on it, a controller without a root hub), what is behind it
cannot be counted: limits that are not already exceeded are reported as unknown,
and devices seen behind it earlier are grey (unknown), not red. Errors on a
controller or root hub make the overall result purple and are listed as
findings; they have no connection line of their own.
