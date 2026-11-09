# Hardware database, counting and diagnostic rules

How the YAML database is built, how hubs, tiers and hops are counted, and how each connection gets its status.

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
  hubs and built-in hubs excluded, counted per bus (see below).

A USB 3 hub is two hubs in one box: a USB 2 hub on the USB 2 bus and a
SuperSpeed hub on the USB 3 bus, and each bus has its own tree. So all three
limits apply per bus, unless a profile says otherwise: a hub or device belongs
to the bus of the port it is plugged into, and `max_hubs` is checked against
the bus with the most hubs, not the sum of both halves. The bus comes from the
operating system only (macOS: the port's class, Windows: the protocols the port
reports), never from a name or a speed. A hub or device on a port whose bus is
not reported is in neither bus's count, so the counts are then "at least" and
the log says why. When no bus is reported at all, both buses are counted
together, as before.

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
   `max_hops`; or it is a hub on a bus whose hub count is above `max_hubs`
   (a hub whose bus is not reported counts as on every bus over the limit).
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
