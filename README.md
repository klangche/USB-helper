# USB-helper

A portable diagnostic tool that shows the USB topology of a computer as a live
tree and checks a BYOD/BYOM setup (bring your own device / monitor) against
that computer's hardware limits.

Status: in planning. Nothing is built yet.

## Features (planned)

- Live USB tree: controllers, hubs, ports and devices, updated on connect and
  disconnect without rebuilding the whole tree.
- Hub count, tier depth and hop depth, each checked against the computer
  model's own limits.
- Display capabilities per port: DisplayPort Alt Mode, MST, Thunderbolt,
  USB4, HDMI and DisplayLink.
- Disconnected devices and connection errors reported by the operating system.
- Connection status colours:
  - Green: connected, no issues, limits satisfied.
  - Yellow: a topology limit is exceeded or a documented limitation applies.
  - Purple: a connection error on a connection that is still present.
  - Red: an expected device or connection is missing.
  - Grey: unknown, unverified or not yet evaluated.
- Results separate detected hardware facts, verified database facts,
  predicted risks and unknown information. A setup is never reported as fully
  compatible while required data is missing.
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

### Adding or updating a computer profile

1. Copy `database/computers/.template.yaml` to
   `database/computers/<manufacturer>/<model>.yaml`.
2. Set `id` (unique), `inherits` (a platform id from `database/platforms/`),
   `manufacturer` and `model`. Keep `limits.max_hubs`, `limits.max_tiers` and
   `limits.max_hops` in the file, `null` if unknown.
3. Fill in only values you can cite. List every source in `sources`, set
   `verification.status` and `verification.last_checked` (`YYYY-MM-DD`).
   Remove template fields you do not need; they then inherit.
4. Run `dotnet test`: it loads the whole database and reports any problem.
5. Open a pull request. Changes to verified values need review; the git history
   keeps every correction.

## Platforms

- Windows x64: first target. Portable, no installation, no administrator
  rights, no network connection needed.
- macOS and Linux: planned, with the same GUI and diagnostics.

## Install

Not released yet. Releases will be a zip
(`usb-helper-<version>-windows-x64.zip`): unzip and run.

## Usage

Not released yet.

## Development

C# on .NET 10, Avalonia for the GUI, YamlDotNet for the database, xUnit for
tests. Builds and releases run on GitHub Actions.

- `src/UsbHelper.Core`: database, detection interface (`IUsbDetector`), and
  later topology and diagnostics. No GUI or operating-system code.
- `src/UsbHelper.Detection.Windows`: Windows USB detection (SetupAPI and the USB
  hub IOCTLs).
- `src/UsbHelper.App`: the GUI.
- `src/UsbHelper.Cli`: prints the detected USB tree, to compare with USBView or
  Device Manager ("Devices by connection"). Windows only:
  `dotnet run --project src/UsbHelper.Cli`. Add `-- --watch` to keep running
  and print each change while devices are plugged in or out.

Live updates on Windows: the app listens for USB device events (no polling, no
administrator rights), waits half a second for related events to settle, then
reads again only the hub the device is connected to, including everything
below it. When an event cannot be placed in the known tree (for example a dock
that brings its own USB controller), the whole tree is read again once and
only the differences are reported. Connection errors are reported only when
Windows reports them on the next read; a port error that Windows raises without
any device event is seen at the next event on that hub.

Contributors and AI agents follow AGENTS.md.

## License

Not chosen yet.
