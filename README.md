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
`base.yaml`. An omitted field inherits; an explicit value overrides; an
explicit `null` means unknown. Every profile records its sources, a
verification status (`verified`, `partial`, `unverified`) and the date it was
last checked. To add a computer, copy `database/computers/.template.yaml`.

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

Contributors and AI agents follow AGENTS.md.

## License

Not chosen yet.
