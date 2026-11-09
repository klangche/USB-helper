# USB-helper

A portable diagnostic tool that shows a computer's USB topology as a live tree
and checks a BYOD/BYOM setup (bring your own device / monitor) against that
computer's hardware limits: how many hubs, how deep the tiers and how many hops.

It recognises the computer model by itself (Windows: manufacturer and model;
macOS: the model identifier such as `MacBookPro18,2`) and judges the setup by
that model's profile from a reviewed, cited hardware database.

## Features

- **Live USB tree** from the computer down: controllers, hubs, occupied ports
  and devices, updated on every plug and unplug without rebuilding the tree.
- **Follows what you plug in.** The busiest controller is opened and selected,
  and a dock or hub plugged in later takes over until you pick something
  yourself. Refresh starts following again.
- **Counts per USB bus.** Each controller and the computer's row show hubs,
  deepest tier and deepest hop for USB 2 and USB 3, the two halves of a USB 3
  hub kept apart. Every hub and device shows its own tier and hops on hover.
- **Checks against limits** of the computer model and its platform: hub count,
  tier depth, hop depth and recommended hops, each line coloured green
  (stable), orange (potentially unstable), purple (unstable), red (missing) or
  grey (unknown). One unstable device colours its path up to the computer.
- **Displays**: each active monitor and its output type, monitors inside USB-C
  hubs placed under their hub, DisplayLink adapters, and display-count and MST
  checks.
- **Active cables** the operating system does not show can be marked
  ("+N hops") and count in every check.
- **Honest results**: detected facts, verified database facts, predicted risks
  and unknowns are kept apart. Unknown is never replaced by a guess. No
  bandwidth or speed analysis.
- **Log and exports**: a timed log of every event, the tree as text, and a
  full setup export (zip with JSON, tree, screenshot and log) without serial
  numbers.

## Platforms

- **Windows x64** (also runs on Windows 11 on ARM through emulation): per-user
  `.msi` installer or portable zip, no administrator rights.
- **macOS** (Apple silicon and Intel): universal disk image with every release
  and beta, unsigned.
- Linux: planned.

## Install

Download from the [releases](https://github.com/klangche/USB-helper/releases):

- Windows: `usb-helper-<version>-windows-x64.msi` (installs for the current
  user) or `usb-helper-<version>-windows-x64.zip` (portable; follow
  `READ_ME_FIRST.txt` in it).
- macOS: `usb-helper-<version>-macos-universal.dmg`; drag the app to
  Applications. If Gatekeeper blocks it, see [Install](docs/install.md).

Releases marked pre-release are betas of the next version. The app checks for
updates at start and can update an installed `.msi` by itself.

## Usage

Start USB-helper. The tree fills in as devices are read, and the platform
profile for your operating system and processor is checked. Pick your computer
model under "Computers" if it is not matched already, plug in the setup, and
read the colours and the findings next to the tree. Click "Log" for every
event, and use File > Export full setup to send a setup to someone.

## Documentation

- [Using USB-helper](docs/usage.md): the window, the tree, the log, the exports
  and the settings.
- [Install and update](docs/install.md): every download, what it contains, and
  how updates work.
- [Hardware database, counting and diagnostic rules](docs/database.md): the
  YAML profiles, how to add a computer, how hubs, tiers and hops are counted,
  and how each status is decided.
- [Development](docs/development.md): build, tests, releases and source layout.

## Development

C# on .NET 10 with Avalonia, YamlDotNet and xUnit. Build and test with
`dotnet test`; pull requests go to `dev`, and GitHub Actions builds every
release. Details in [Development](docs/development.md).

Contributors and AI agents follow AGENTS.md.

## License

MIT, see [LICENSE](LICENSE).

The app icon is the "usb" icon from [Font Awesome Free](https://fontawesome.com)
by Fonticons, Inc., licensed under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
([Font Awesome Free license](https://fontawesome.com/license/free)). Changed:
square view box and drawn white. USB is a trademark of its owner; the icon
stands for USB connections, and no endorsement is implied.
