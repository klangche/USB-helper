# Hardware database, counting and diagnostic rules

How the YAML database is built, how hubs and tiers are counted, and how each connection gets its status.

## Hardware database

Human-readable YAML on the [`database` branch](https://github.com/klangche/USB-helper/tree/database) (#288),
version-controlled and reviewed. Facts and advice are kept apart: platform and computer files hold facts, guide files hold
every limit and recommendation.

The branch is the only source. Each format has its own folder, `v3/` today (`schema_version: 3` in `base.yaml`), so an
app always gets files in the format it reads; a new format adds `v4/` next to it. Every build takes the head of the
branch: `release/database.sh` copies every format this code reads that the branch has, `v<format>/`, into
`database/v<format>/` (ignored by git) before the build, in CI and locally. An older format may be missing; the newest
must be there, or the script fails. Run it once after cloning, and again to get the newest files. It also writes
`database/.source`, the commit's full sha and date, which the build puts into the app with the files.

### Formats (#334)

- The formats the app reads are the readers in `src/UsbHelper.Core/Database/DatabaseFormats.cs`, one per format. Every
  reader gives the same in-memory database, so the newest engine also judges older data.
- At start the app uses the newest built-in format that loads. When it does not load, the next lower one is used. The
  log says which format is in use, and why when it is not the newest ("Database format v3 in use, because v4 does not
  load").
- A folder of a format the app does not know, such as `v5/` seen by an app that reads up to `v4/`, is ignored, so
  publishing a new format never breaks installed apps.
- There is no setting for the format (#389; betas had one, #334). A saved `databaseFormat` in settings.json is ignored.
- The window shows the database in use at its bottom right, for example "Database v4 · 2026-11-24 (abc1234)". Its hover,
  and the About window's database line, give the format, whether it is built in or downloaded, the full commit and the
  other formats built in ("Also built in, not used: v3"). The log ("Database in use: v4, built in, …"), the PDF report
  and the exported setup (`database`) name it too.
- Adding `v5`: add its reader to `DatabaseFormats`, and `v5/` to the `database` branch before the code that reads it is
  built. Nothing else changes: the build script, the update check and the cache follow the list of readers.

### Format v4 (#327)

v4 judges every guide exactly as its source counts. Its folder is `v4/` on the `database` branch, with
`schema_version: 4`.

#### Signed-off, dated files (#389)

- A v4 philosophy is `guides/<id>-<YYYY-MM-DD>.yaml`, for example `guides/ochno-2026-11-24.yaml`. The id is the part
  before the date, so a remembered choice ("ochno") keeps working.
- A dated file has `signed off:` with `by` (who approves this version), optionally `role` and `company`, and `date`,
  which must equal the date in the file name:

  ```yaml
  signed off:  # who approves this version
    by: Johan Niininen
    role: Product manager
    company: Ochno
    date: 2026-11-24
  ```

- Several versions of one id may sit side by side. The newest is used. Older ones are read to check them, then kept as
  history and never judged. The log says, for example, "Ochno: using guides/ochno-2026-11-24.yaml (signed off 2026-11-24
  by Johan Niininen (Product manager, Ochno)); 1 older version kept: guides/ochno-2026-11-19.yaml".
- Ids are written in lower case. One id spelled two ways (`Ochno.yaml` next to `ochno.yaml`, dated or not) is an
  error, so two versions never share an id and a date. A dated file without a sign-off, or one whose sign-off date is
  not the file name's date, is an error with the file and line.
- An undated file (`guides/ochno.yaml`) still loads, without a sign-off, and counts as older than any dated one. This
  is for the move to dated files only.
- A philosophy's hover in the menu ends with its file and sign-off ("Ochno · ochno-2026-11-24.yaml · signed off …"); the
  PDF report and the exported setup (`philosophy`) carry the same line.

#### The readable layout (#386)

A philosophy is written in the readable layout: block style only (no `{ }` or `[ ]`), long text in `>` blocks with no
quotes or escapes, and a short comment on every key. It is a shorter way to write the full form below: the app expands
it into `measures` and `rules` when it loads the file, so both judge the same. A file in the full form still loads.

```yaml
name: Ochno  # the name in the menu
selectable: true  # true: it can be picked in the menu
proav: 1  # 1: ProAV, marked in the menu · 0: general purpose

counts: chain, room  # chain = hubs in front of a device · tiers = USB tiers · room = only the room's hubs

computer_hubs:  # the hubs inside the computer
  use: measured, computer, chip  # measured = this computer, computer = its file, chip = its chip's file
  if unknown: at least  # at least = the count is a minimum · a number = assume that many

limits:  # the numbers, for every device unless a scope below says otherwise
  chain:  # what 'chain' may reach
    max: 5  # a fault above this
    source: ochno  # which source above
    quote: having max 5 usb hubs daisy-chained  # its words, copied exactly
  room:  # what 'room' may reach
    max: 4  # a fault above this
    source: ochno  # which source above
    quote: >  # its words, copied exactly
      to ensure that the video conference room works with 100% of all laptops, the max available
      hubs are 4
```

- `counts`: the standard counts the philosophy uses, defined once in `base.yaml` under `counts`, each with `explain`
  (in words) and `adds` (its path elements): `chain` (computer_hub, hub, marked_hub), `tiers` (root_hub, computer_hub,
  hub, marked_hub, cable_length, device) and `room` (hub, marked_hub). A count named under `limits` is used too.
- `computer_hubs`: `use` is the evidence for the computer's own hubs, in order (`measured` is what this computer
  reports, the full form's `detected`); `if unknown` is `at least` (the count is a minimum) or a number of hubs to
  assume. It applies to every count that adds `computer_hub`.
- `active cables`: `usb2` and `usb3`, the metres per extra tier of a marked active cable, for every count that adds
  `cable_length`.
- `limits`: per count, `recommended` and/or `max` (empty = not stated, a number = the limit, `none` = deliberately no
  limit), `source` and `quote`. Inside a count, a scope gives the limits for part of the paths: `usb2:`, `usb3:`,
  `cameras:`, `audio:`, `on <os>:` or `on <os>/<chip>:`, each with its own `recommended`, `max`, `source` and `quote`.
  A scope narrows the one it sits in, so `cameras:` inside `on windows/arm:` is cameras on Windows on ARM; a scope
  inside one of the same kind (a bus in a bus, a device in a device, a platform in a platform) is an error. A limit's
  own numbers become its first rule, then each scope in the order written. A limit or scope with neither
  `recommended` nor `max` and no scopes is an error, and so is a `quote` or `source` on a limit that has only
  scopes: give it in each scope.
- A philosophy that counts another way writes that count out under `measures` (with `weights`), in the full form,
  and gives its limits under `limits` as above.
- `basis`, `sources`, `cables`, `requires` and `notes` are as in the full form, written in block style.
- `requires` is read before the layout is expanded: a newer file that needs a newer app is listed as such, not an
  error.
- A mistake names the readable key and its line, for example `guides/ochno.yaml:14: Limit 'chain': 'max' must be a
  whole number or none.`

#### The full form (advanced)

- `base.yaml` lists the **path elements** once, each with `found_by` (how the app finds it): `root_hub`,
  `computer_hub`, `hub`, `marked_hub`, `cable_length`, `usb4_router` (read on macOS, #332) and `device`. It also lists the
  **device kinds** by USB class: `camera` (14, 0x0E), `audio` (1) and `hid` (3).
- Platform chips and computer files have `computer_hubs` instead of `built_in_hub_levels`: the computer's own hubs
  that the operating system does not show. It is a number, `null` (unknown) or one per bus, `{ usb2: 1, usb3: 1 }`.
- A guide has:
  - `basis`: the source id and the exact quote it works from;
  - `sources`: id → `title`, `author`, `version`, `read` (how it was read), `files` and `quotes` (#333). `files` lists
    any number of entries, each exactly one of `pdf` or `link`; a `pdf` may give `sha256` and `size`, and a PDF
    without a public download gives only those. One entry may leave out `name` (its URL's decoded file name, or the
    host, is shown); with several, every entry needs its own unique `name`. A quote gives `text`, the entry's name as
    `file` (optional with a single PDF) and `page`. A plain `link` still works as one link entry;
  - `measures`: each a sum of path elements (`counts`), with optional `weights`, and which evidence it accepts for
    `computer_hub` (`from: [detected, computer, chip]`) and what to do when that is unknown (`unknown: open`, or a
    number). `detected` hubs (#330) take the place of the computer or chip value on that path, so none is counted
    twice: on Windows a hub behind a port that is not user-connectable, or one Windows puts in the computer's own
    container (`InLocalMachineContainer`), with an external port behind it, and a detected 0 when the device is
    outside the computer on the computer's own port; on macOS the hubs a controller hides, 7 less its
    `UsbHostControllerTierLimit` (6 on an M1 Max, so 1). `cable_length` may give `usb2_metres_per_tier` and
    `usb3_metres_per_tier`: an active cable marked with its length (#331) then adds a tier per that many metres
    after the first, so at 5 m per tier a 10 m cable adds 1;
  - `rules`: each names a `measure`, optionally a `bus` (`usb2`, `usb3`), `devices` (device kinds) and `on`
    (`windows` or `windows/amd`), then `recommended` and/or `max` (a number or `none`), `source` and `quote`;
  - `cables` (#372): notes on cable lengths and the cable models the philosophy approves. They are only shown, on a
    marked cable's hover and in the PDF report, and never change a status. `notes` entries have `type` (`passive`,
    `active_copper`, `active_optical`, `usb4`), optionally `bus` (`usb2`, `usb3`), `text` (the note as shown after the
    guide's name), `source` and `quote`. `models` entries have `name`, `type`, `length` (rated, in metres), optionally
    `bus` (the mark's bus when the user marks a cable as that model), `speed`, `hubs` and `tiers` (what the model adds,
    when its maker states it: the mark's hubs and tiers; a philosophy that converts an active cable's length into tiers,
    such as Crestron, still does), `host: false` (it does not work as the host cable), `note` and `source`. An error in
    this section, as anywhere in a v4 guide, stops the whole v4 database from loading. A
    philosophy's approved models are listed in the cable menu when it is selected;
  - `requires`: elements or device kinds the guide needs. A guide that needs one this app does not know is listed
    as "needs a newer USB-Helper" and judges nothing; the rest of the database still loads;
  - nothing about computers (#367). MST is a fact about the computer, in the OS files: `displays.mst` for the
    operating system and `chips.<chip>.displays.mst` where a chip differs (true or false); a computer file's wins.
    A philosophy in the full form that states `platforms`, `mst`, `computer_hubs`, `displays`, `ports`, `usb`, `usb4`
    or `chips` does not load: the error names the file and line. (The readable layout's own `computer_hubs` key is the
    evidence for the computer's hubs, not a value.)
- There is **no fallback** between guides. A hub or device is judged by every rule that matches it, and the strictest
  result wins. A count with an unknown part (an `open` computer_hub, `usb4_router`) is "at least": above a limit it is
  exceeded, within it unknown.
- In a manufacturer guide (`proav: 1`), `basis` and every rule need `source` and `quote`. `own: true` marks a guide
  with the owner's own numbers (USB-Helper, Unisynk), which have no source to quote. Unknown measures, path elements and
  device kinds, and two rules with the same measure, bus, devices and platforms, are errors with the file and line.
- The line shown when hovering a guide in the menu is generated from its rules, with the words from the strings file, for example
  "hub chain max 5; USB 2: hub chain max 2; cameras and audio: tiers recommended 3, max 4".

```yaml
name: Ochno
selectable: true
proav: 1
basis: { source: ochno, quote: "having max 5 usb hubs daisy-chained" }
sources:
  ochno: { link: "https://…", title: "Guide: USB hub chains in video conference room", version: "Updated Oct 28" }
measures:
  chain:
    counts: [computer_hub, hub, marked_hub]
    computer_hub: { from: [computer, chip], unknown: open }
rules:
  - { measure: chain, max: 5, source: ochno, quote: "…" }
```

### USB4 routers (#332)

`usb4_router` is a path element. No current guide counts it, so no result depends on it yet.

- **macOS:** read from `system_profiler SPThunderboltDataType` with the USB tree. Each USB4 or Thunderbolt device on a
  bus (`thunderboltusb4_bus_N`) is a router, one level deeper than the one it hangs on, and a path's count is the
  deepest router on its bus. The owner's M1 Max with a Parade PS9010 dock shows one router at depth 1 on bus 0, and the
  dock's USB hubs stay on the Mac's own controller (#267, 2026-11-23). Bus N is linked to the controller whose
  locationID is `0xN000000`; that is seen on one port so far, and a controller without a bus of its number stays
  unknown.
- **Windows:** read with the USB tree from Microsoft's USB4 connection manager. Each host router has a root device
  router, and a dock is a device router below it (depth 1). A host router is linked to the USB controller on the same
  PCI device, which carries its USB 3 tunnel: on the owner's Asus with the PS9010 dock (#354), the host routers and the
  Type-C controller are PCI device 13. A USB 2 controller with no host router on its PCI device (the dock's USB 2 half
  runs on the PCH, not through a router) stays unknown, as does every controller without Microsoft's connection manager.
  Two ports on one controller cannot be told apart, so the count is the deepest router of either: for a device in the
  shallower port it can be too high, never too low. Seen on one PC so far.
- Unknown is never guessed as 0: a measure that counts `usb4_router` without a reading gets an unknown part ("not
  measured yet"), so its count is "at least": over a limit it is exceeded, within one it is unknown.

- Routers are not hubs: PureLink V6 says to "distinguish USB hub tiers from USB4 routers and PCIe/DisplayPort tunnels".
  Hubs behind a USB4 dock stay hubs on the USB 3 bus.
- The depth limit, `usb4.max_router_depth 5` in `base.yaml`, comes from:
  - the Linux Thunderbolt/USB4 driver, `drivers/thunderbolt/tb.h` lines 75–76 at commit 4843f6873f7c (read on
    2026-11-22,
    https://github.com/torvalds/linux/blob/4843f6873f7c4835e62f2c73ba0fd538e57a2f22/drivers/thunderbolt/tb.h#L75-L76):
    `#define TB_SWITCH_MAX_DEPTH 6` (Thunderbolt) and `#define USB4_SWITCH_MAX_DEPTH 5` (USB4).
  Still to be checked, so not cited as a source: the USB-IF USB4 System Overview (#267 saw it only in search snippets,
  which say "up to 6 routers" and "up to 5 hops", and no primary text was read), the USB4 specification and Microsoft's
  USB4 documentation, each with its exact section.
- Detection (macOS through `MacThunderbolt.cs`, Windows through the USB4 host router) moves to #267's follow-up. It is
  enabled only after a test with a real dock.

A running app gets newer files without a new app version (#290, see [usage.md](usage.md#database-updates)): at
start it asks the GitHub API for the newest commit that changed `v<format>/` on the branch
(`commits?sha=database&path=v<format>&per_page=1`), for each format it reads. When that commit's date is later than the database in use, it lists the
commit's files (`git/trees/<sha>?recursive=1`), downloads every `.yaml` file of the folder from
`raw.githubusercontent.com/klangche/USB-helper/<sha>/v<format>/...`, and uses them only when they load without a problem.
No manifest or extra workflow is needed: merging to the `database` branch is publishing. Two API requests per format per start
(GitHub allows 60 an hour without a token), and the files only when they are newer.

To change a file, open a pull request to the `database` branch. Its checks (`.github/workflows/database.yml`) load the
files with the code of `dev`, and of the last release when that reads the same format, and run every test, so a file the
app cannot read never lands.

```
v3/ on the database branch, database/v3/ after release/database.sh
├── base.yaml          facts from the USB specification
├── platforms/         one file per operating system, with its chips
├── guides/            one file per guide: usb-helper.yaml (the default), desktop.yaml, purelink.yaml, ochno.yaml,
│                      unisynk.yaml, extron.yaml, crestron.yaml
└── computers/         one file per problem machine, by manufacturer
```

The build puts these files into the app, so the download has no database folder. The database is only read by the
application; a newer one it downloads is kept in `database-cache/<sha>/v<format>/` next to `settings.json`, with a
`current-v<format>` file per format, so each format is cached on its own.

### base.yaml

```yaml
schema_version: 2
usb:
  max_tiers: 7                     # seven tiers on a path (USB 2.0 specification)
  max_devices_per_controller: 127  # a 7-bit device address; above it is unstable
usb4:
  max_router_depth: 5              # a reference: a guide judges routers with its own rule (#332)
```

### Platform files

One file per operating system family. The file name is the platform (`windows`, `macos`, `ubuntu`, `debian`,
`apple-mobile`, `android`), the same keys as under `platforms:` in the guide files. Each chip is one platform profile, named after the file and the chip: `windows-intel`, `macos-a-series`.
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
| `ubuntu`, `debian` | `intel` 0, `amd` 0, `arm` 0 (Ubuntu and Debian are separate systems, owner decision 2026-11-17) |
| `apple-mobile` (iPhone and iPad) | `a-series` 2, `m-series` null (not measured yet) |
| `android` | `phone-qualcomm` 0, `tablet-exynos` 1 (from the usb-script config) |

### Guide files

Each guide is one file, `guides/<guide>.yaml`, with the same platforms and chips in every guide, so a guide can be sent
to a manufacturer, filled in and sent back. A returned file with a missing or extra platform, chip or key is rejected,
with file, line and key. A new file in `guides/` is a new guide; no code change is needed.

Every guide file has the same keys in the same order; only the header's values and the limits differ (#310).

```yaml
name: PureLink
selectable: true
proav: 1                # 1: a ProAV guide, 0: general purpose
count: [tiers, hubs]    # what this guide judges: any of chain, tiers, hubs
link:                   # the guide's page (http or https); optional

limits:                 # global: every system
  hubs:                 # external hubs
    recommended: 3
    max: 5
  tiers:                # USB tiers as the specification counts them (host = tier 1), built-in hub levels included
    recommended: 4
    max: 7
  chain:                # hubs daisy-chained in front of a device, built-in hub levels included
    recommended:
    max:
  buses:                # hubs per bus; empty = the hubs above
    usb2: { hubs: { recommended: , max: } }
    usb3: { hubs: { recommended: , max: } }
    usb4: { hubs: { recommended: , max: } }
  camera_audio:         # stricter tiers and chain for cameras and audio devices
    tiers: { recommended: 3, max: 4 }
    chain: { recommended: , max: }
  exceeded_note: "Too many hubs/tiers – video or audio may drop frames."   # at most 160 characters

platforms:
  windows:
    mst: 1              # 1: extends the desktop over DisplayPort MST, 0: does not
    limits: {}          # only what differs from the global limits
    chips:
      amd:
        mst: 1
        limits:
          tiers: { recommended: 3, max: 6 }
  # macos, ubuntu, debian, apple-mobile, android: the same, with every chip of platforms/<platform>.yaml

sources:
  - "..."
```

Guides state their limits in different terms (#310): some count USB tiers, some count the hubs daisy-chained in
front of a device ("max 5 hub hops", "five cascading hubs"), some count all hubs on a bus. So every guide says what it
counts:

- `count` (required): a list of any of `chain`, `tiers` and `hubs`, at least one, each once. Only these are judged
  for the guide; its numbers for the others are ignored and never fall back into judging. `count` is read from the
  picked guide itself, never from USB-Helper's.
- `chain`: the hubs daisy-chained in front of a device on its path from the computer: the external hubs in front of
  it (marked hubs included) plus the chip's `built_in_hub_levels`. Root hubs do not count; a laptop's non-root
  internal hub does. With unknown built-in levels the chain is "at least" what is counted, like tiers. It is judged on
  each bus, like tiers, since a product can have more USB 2 than USB 3 hubs. Its findings name `limits.max_chain`,
  `limits.recommended_chain`, `limits.camera_audio_max_chain` and `limits.camera_audio_recommended_chain`; the report
  calls it "Hubs in a chain".
- `link` (optional): the guide's page, an absolute http or https address. The app links it; without it, the first
  web address in `sources`.

Example, Ochno's guide ("having max 5 usb hubs daisy-chained", the same for USB 2.0 and USB 3.2, a laptop's internal
hub included):

```yaml
count: [chain]
link: https://ochno.atlassian.net/wiki/spaces/~ocs/pages/1114996744/Guide+USB+hub+chains+in+video+conference+room
limits:
  chain:
    recommended: none   # one hard limit
    max: 5
```

A device behind 5 external hubs is within on Windows on Intel (no built-in level) and unstable behind 6; on an
M-series Mac (one built-in level) 4 are within and 5 unstable, which is Ochno's "4 for all laptops".

How to read a value:

- A number is the limit. An empty value (or a key left out of a chip's or platform's `limits`) means: use the value
  above it: the chip's, then the platform's, then the global one, then USB-Helper's (`guides/usb-helper.yaml`) the same
  way. This is different from platform and computer files, where `null` means unknown.
- `none` means: deliberately no limit, and nothing above it is used either.
- Hubs on one bus: `buses.<bus>.hubs`, else `hubs`, at each level. Tiers and chain are the same on every bus.
- USB-Helper's guide is the default (ticked at start) and the fallback, so its global limits (`buses` included) and
  every `mst` are filled in. The per-bus hubs are filled in only there, until a manufacturer gives numbers.
- `selectable: false` lists a guide that is not offered yet.
- Each limit is checked on its own; nothing is worked out between hubs, tiers and chain. Above any `max` is unstable
  (purple), above any `recommended` potentially unstable (orange). Only what the guide's `count` lists is judged.
- Each bus's hubs apply to that bus's tree. A hub on a port whose bus the operating system does not report is judged by
  the lower of the USB 2 and USB 3 values, and the log says so. `usb4` is loaded and checked as a reference; routers are
  judged only by a guide's own rule on a measure that counts `usb4_router` (#332).
- A device's tier is the counted tier plus the chip's `built_in_hub_levels`: on an A-series chip (two levels) a device in
  the computer's own port is on tier 4.
- Findings and the log name the guide, the key and the file, for example
  `limits.max_tiers = 7 (USB-Helper: limits.tiers.max 7 in guides/usb-helper.yaml)`, so a
  value that fell back reads as USB-Helper's.

| Guide | Values |
|---|---|
| `usb-helper` | The owner's file of 2026-11-17 (the former usb-script platform values): counts tiers and hubs; hubs 4 / 6, tiers 5 / 7, hubs per bus 4 / 6 (to be revised by the owner), cameras and audio tiers 3 / 5; chain 3 / 5 and cameras and audio 1 / 3 (its tiers minus 2), filled in as the fallback, not judged; iPhone and iPad stricter; Android direct products only (any hub is potentially unstable); `mst` 1 on Windows except ARM, 0 elsewhere (macOS has no MST). No sources or link: the owner keeps them in a separate document. |
| `desktop` | General purpose and the freest: exactly the USB specification, tiers at most 7, no recommendation, no hub count, no camera rule. |
| `purelink` | PureLink's USB-C whitepaper v3 (Robin Cullen, 8 Oct 2025, not published yet, so no link): `count: [tiers]`, tiers 4 recommended / 7 max, cameras and audio 3 / 4. No per-platform numbers. |
| `extron` | Extron's "USB-C for Pro AV" design guide, linked to its article page: `count: [chain]`, at most five cascading hubs, no recommendation, no camera rule. |
| `crestron` | Crestron's knowledge article 3082 "USB Tiers" (linked; needs a Crestron login): `count: [tiers]`, never more than 7, no recommendation, no camera rule. |
| `unisynk` | The owner's file of 2026-11-17; counts tiers and hubs for now, as its text is not verified yet. |
| `ochno` | Follows Ochno's own guide (#310): counts the chain only, at most 5 hubs daisy-chained, no recommendation and no separate limit for cameras and audio devices; no tier or hub numbers and no platform or chip overrides. Links its guide page. |

### Custom guide

An unlocked user's own guide (#300) is not part of the database: it is `custom.yaml` in the folder of the app's
settings file (`%APPDATA%\USB-helper\custom.yaml` on Windows, next to `settings.json`), written by the app's Custom
editor (see [usage.md](usage.md#custom-philosophy)). It has the shape of every guide file and is checked the same way,
against the platforms and chips of the loaded database, with USB-Helper's guide as the fallback for every empty value.
In addition, `name` must be `Custom` and `selectable` must be `true`. Errors name `custom.yaml` and the line. The blank
the editor offers has every platform and chip with every value empty, `proav: 0`, `count: [tiers, hubs]` (as
USB-Helper's, since `count` is required and never falls back), an empty `link` and the unlock email as a note.

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
whose platforms, chips or keys differ, a guide without a valid `count` or with a `link` that is not an http or https
address, an empty global value or `mst` in USB-Helper's guide, an `mst` other than 0 or 1, an `exceeded_note` over 160
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
DisplayPort output) comes from a computer file's `displays.mst`, else, in v4,
the OS file's (`displays.mst`, or the chip's own, #367). A v3 guide's `mst` for
the platform and chip (1 or 0) is still read for v3 files. When a
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

1. Start from a file exported by the app ("Report this machine"), or copy
   `v3/computers/.template.yaml` on the `database` branch. Save it there as
   `v3/computers/<manufacturer>/<model>.yaml`.
2. Set `platform` (a file in `v3/platforms/`), `chip_family` (one of its
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
5. To check it locally, copy it into `database/computers/` of a `dev` checkout and run `dotnet test`: it loads the
   whole database and reports any problem. The pull request runs the same check.
6. Open a pull request to the `database` branch. Changes to verified values need review; the git history
   keeps every correction.
7. Once merged, running apps of the same format download the change at their next start (unless "Download database
   updates" is off), and every build after it has it built in.

A computer profile's id is never written in the file. The app computes it from
`manufacturer` and `model` (ignoring case and extra spaces): 6 lowercase letters
and digits, for example `y98inr` for `Dell Inc.` / `Latitude 7450`. The same
names always give the same id, so nobody can set or change it, and a second
file for the same model is rejected. A platform profile's id is its file and
chip, for example `macos-a-series`.

## Topology counting

The three kinds of limits in a guide are checked independently (owner decisions
2026-11-17, #297, and 2026-11-18, #310), each only when the guide's `count` lists it:

- **Tier depth** (`tiers`): as the USB specification counts it: the host with
  its root hub is tier 1, each hub adds one, the device's own tier counts, and
  so do the chip's `built_in_hub_levels`.
- **Hub count** (`hubs`): all external hubs connected to the computer, root
  hubs and built-in hubs excluded, counted per bus (see below).
- **Hub chain** (`chain`): the hubs daisy-chained in front of a device: the
  external hubs between the computer's own port and the device, marked hubs
  included, plus the chip's `built_in_hub_levels`. Example: laptop port, dock
  hub, monitor hub, keyboard on an M-series Mac: a chain of 3.

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
the computer and does not count.
If Windows does not say for a port (older versions, some drivers), a hub on it
is counted as before, as an external hub, and the log says so.

Tiers add the built-in hub levels of the chip, or of the computer file when it
sets its own.

Hubs inside docks and monitors count like any other hub, because the operating
system reports them as hubs. Example: laptop port, dock hub, monitor hub,
keyboard. The keyboard is at tier 4 (on a chip without built-in hub levels),
and there are 2 hubs.

Active cables and extenders that Windows shows as a hub count automatically.
Ones it does not show are marked by the user on the port they are plugged into:
"+N hubs" (+1 to +3) adds N hubs to the hub count of that port's bus and N
tiers to everything behind that port, since every hub is a tier; "+N tiers"
(+1 to +3) adds only N tiers, for an extender that is not a hub. Both count in
every checked profile (#307). On a port
whose bus is not reported, the added hubs count toward the total only, like
any other hub on that port. A mark on a port with nothing on it adds nothing.

## Diagnostic rules

Each connection line (from a hub port to the hub or device on it) gets one
status per checked profile; when several apply, the first in this list wins:

1. Red: a hub or device seen since the app started is no longer connected. It
   stays red until it returns or is cleared.
2. Purple (unstable): the operating system reports an error on the port or the
   device; or the hub or device disconnected and came back since the app
   started, however long or short the drop, until it leaves again or is
   cleared; or its tier depth or hub chain is above a `max` of the selected
   guide (`camera_audio` too for a camera or audio device); or it is a hub on a bus
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
with the lowest `tiers.max`; on a tie the lowest `tiers.recommended`, then
`hubs.max`, then `chain.max` and `chain.recommended`. An unknown limit, or one
the guide does not count, counts as higher than any known one; when still tied, the first in the menus wins. Detected facts (operating
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
