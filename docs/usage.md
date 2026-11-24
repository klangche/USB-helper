# Using USB-helper

The window, the tree, the log, the exports and the settings in detail.

Start USB-helper from the Start menu (installer) or `USB-helper.exe` in the
`USB-helper` folder (portable zip; see [Install](install.md)). The window opens at once and
shows what it is reading
("Loading database...", "Reading USB devices...", "Reading displays..."); the
profiles and the tree fill in as they arrive. The window shows the USB tree.
Its root is this computer, named after its computer profile when one matches,
else by the manufacturer and model Windows reports, else by its machine name;
its children are the computer's ports with something on them (#370). Both halves
of a USB-C connector, its USB 2 and USB 3 halves, which the system shows on two
buses and often on two controllers, are one row, "USB-C port 1"; another port is
named as the system names it, "Port 3". After the ports, "Built into the
computer" holds the devices inside it (a webcam, a fingerprint reader,
Bluetooth: in the computer's own container on Windows, or on a port the system
calls internal), and, only when something cannot be placed on a port, "Other USB
ports (not identified)", whose hover says why; it stays open while the tree follows what is plugged in. Controllers and root hubs are no
rows: the port's hover names the controller and root hub of each half. A USB4 dock is its own row below
its port, "USB4 dock: PS9010 (router, depth 1)", with the dock's hubs below it.
The two halves of a USB 3 hub are one row, "USB3.2 Hub + USB2.0 Hub (1DA0:901A,
USB 2 + USB 3)", with the ports of both below it; it has the worse status of its
two halves, and a mark on its line goes on both halves' lines. A dropped USB 3 hub
stays one row too, with "(disconnected)". Every other hub
and device names the bus of its port: "WPP20 (6993:B022, USB 2)". The Mac and the
PC show the same tree for the same setup.

The selected philosophy's limits are marked in the tree (#371), in its own measure:
on the row where its recommended or max is reached ("Ochno: chain max 5 hubs ·
reached here") or passed ("PureLink: tiers recommended 4 tiers · passed here (5)"), and on the
deepest hub of each path still within it, with how many more hubs fit behind it
("Ochno: chain max 5 hubs · 2 more fit behind"); each label names the
philosophy's measure, here Ochno's chain. A recommendation is orange, a maximum
purple. A device rule, such as a camera rule, is marked on the rows of that kind only, with
how many more hubs fit in front of the device. When the two halves of a hub shown as
one have different markings, each names its bus.
Only what the philosophy states is marked: without a limit there is no marking.
When part of a count is unknown ("at least"), the marking's outline is dashed and
hovering it says which part and why. The counts are this computer's as measured
(#368); without a profile for this computer, the first ticked system's. Copy tree as
text and the PDF report add the markings after the row.

Thin lines join the items, starting at the
computer. Each line is drawn in the worst status of everything it leads to: a
stable tree is green all the way, and one unstable (purple) or disconnected
(red) device colours its path up to the computer while its stable neighbours
stay green. The short line into each hub and device keeps that item's own
status colour, and a dot in that colour is in front of its name. The result
word ("unknown", "potentially unstable", "unstable", "disconnected") follows
the name only when the item is not stable, and such a row has a light
background in its status colour; a stable row shows no word. Hubs and devices show
the product name they report, falling back to the Windows name. A port on a hub is not
a row of its own: the hub or device on it shows the port number in a small box
in front of its name (the computer's port rows have the box themselves), and a disconnected
device keeps the number of its last port. Hovering the box shows the port
(#266): its name and bus, a small connector drawing (rounded for USB-C, with
the USB 2 pair and the USB 3 lanes green when in use, grey when not, an empty
outline when not detected), whether it is USB-C, the other half of the
connector on the other bus when it is known (the companion port, or the halves
paired while testing ports), and whether it is one of the computer's own ports,
inside the computer or on a hub. USB-A versus USB-B, the DisplayPort lanes,
power and the cable's e-marker are never read, so the hover says they are not
detected. While hovering, the rows from the computer to that port are
highlighted in light blue. Empty ports are left
out.

A camera or an audio device has a symbol in front of its name (#276): 📷 for a
camera, 🎙 for a microphone or speaker, both for a webcam with a microphone.
Hovering the symbol says what the device is, how the app knows (reported by the
operating system, or selected by you), why it is judged more strictly (cameras
and audio devices send a steady stream, so they get a lower depth), and the
limits of the guide in use for that kind with their source. When the system
does not say, select the kind by hand: right-click a device row (or press
Shift+F10 or the menu key) and choose Camera, Audio, Camera and audio, or
Neither; "Use what the system reports" removes it. A kind selected by hand has
"✎" after its symbol, is judged by the same stricter rules, is noted in the log
("Device kind of … selected by hand: camera") and is carried in the setup export.
Like the cable marks, it lasts until the app closes. A port with a problem keeps its own row, "Port 2: ...", with what is on
it below. Only the port rows (and the built-in and other groups) can be collapsed: click
a row's chevron, double-click it, or use the left and right arrow keys to hide or
show what is on it; while it is hidden the row's name is bold. Only the ports of
the selected controller (see below) are open. The computer's row, the Displays
group and everything below a port row are always open. Until you select an item
or open or close a port row, the
tree follows what is plugged in: a controller that gains hubs or devices and
then has more than the selected one is selected and opened, and the others
are collapsed. Here disconnected devices count too, so a dropped dock keeps
its place until a bigger tree arrives or its disconnected devices are
cleared. A selected controller that loses devices stays selected, so a brief
drop does not jump away and its disconnected devices stay in view; a tie
never switches. After your own choice, it is kept while the app runs;
Refresh follows again, starting from the busiest controller by connected
hubs and devices. A
DisplayLink device is shown as a display: a "Display" badge and "Extended
display: DisplayLink driver required" before its name;
every hub above it says it "has a display". After the ports, a
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
lists the monitors at start and each one connected or removed. A hub or
device on a port that Windows reports as a USB-C connector (from its port connector
properties) has a "USB-C" badge after its name; this changes no result. Empty
ports are not in the tree; the log lists the flag of every port Windows
reports it for. The computer's row shows its hub count, device count and deepest
tier, for example "Hubs: 3 (2+1), devices: 7, deepest tier: 4". The devices are
the connected USB devices, each counted once whatever its bus; hubs (both halves
of a USB 3 hub), root hubs, controllers and ports are not devices. The
numbers in brackets are the hubs per bus, always in the same order: USB 2
(the USB 2.0 tree), then USB 3 (the SuperSpeed tree, USB 3.0 to 3.2). USB4 has
no hubs: a USB4 dock is a USB4 router, and its USB hubs count on USB 2 and USB 3
(#359). When a controller reports a USB4 router, the counts add it, for example
"Hubs: 4 (2+2) · USB4 routers: 1", and the dock has its own row below its port
(see above). When several ports of that controller are in use, the system does
not say which one has the router: it is then named under "Other USB ports". The
log says "USB4 routers of …: 1"
with what reports it. Without a router there is no USB4 part. The first number is
the sum of the hubs on each bus. A USB 3 hub has a half on each bus and the
operating system reports each half as a hub, so one USB 3 hub counts once on USB 2
and once on USB 3: "Hubs: 2 (1+1)". A USB 2 hub counts on USB 2 only. Each bus
has its own chain and its own limits: the hub limits are judged per bus, not on
the sum. The deepest tier is that of any bus. Hovering the counts shows which bus
is which. The counts are the whole computer's, over all controllers. Copy tree as text and the PDF report show
the counts the same way. When the operating system reports no bus at all, there
are no brackets, just the total. The counts say "at least" when a hub, port or bus
could not be read; a hub on a port whose bus is not reported is in the total but
on no bus.
The side panel has a small table, "Hubs, devices and tiers", for what the status
field covers (the selected controller, or all controllers when the computer's
row is selected): first the hubs as in the row counts, "Hubs: 3 (2+1)", and the
devices, "Devices: 7", then one line each for USB 2 and USB 3, each with that bus's
own hub count and deepest tier, the marked active cables counted.
A bus with nothing on it shows zeros; when nothing is connected there is no
table. The numbers say "at least" in the same cases as the row counts, and
when the operating system reports no bus at all the table has one line, "All
buses together".
Each hub and device row
shows its own depth at the right, for example "Tier 4, 2 hubs in front" ("1 hub
in front"; just "Tier 2" with none); hovering the row shows it too. The hubs in
front are the external hubs between the computer's own port and the row, and
the hubs marked on that path. They are information only: no limit is judged by
them. Controllers, hubs built into the computer and
monitors show none. Copy tree as text, the log, the export and the PDF report
keep one line per port ("Port 1 [USB-C]") and the result word on every hub and
device.
Next to the tree is a
summary with the overall result, the status field, disconnected and
reconnected devices and the findings. The overall result, the status field
and the findings cover one USB
controller's tree, named under "Status". At start, and when that controller
disappears, the controller with the most connected hubs and devices (built-in
hubs not counted) is selected; on a tie the first in the tree. Until you
choose, a controller that grows past it is selected (see above). Click another
controller, or anything under it, to switch; click the computer at the top to
cover all controllers. The hub count is always the whole computer's. The tree
and the disconnected list always show everything. At start, the platform
profile for the detected operating system and processor (for example
`windows-amd` on Windows with an AMD processor) is checked; uncheck it to
judge by others. When no platform profile matches, nothing is checked and
every line is grey (unknown). Computer profiles are never checked
automatically. In the top bar, "USB philosophy" holds the philosophies (guides) on top (USB-Helper,
then PureLink, Ochno, Unisynk and any other guide file, one at a time;
USB-Helper is picked at start and the picked one is kept through restarts and updates, and a guide that is not offered yet is greyed
out) and, below a line, the operating systems with a tick per chip (Windows
Intel / AMD / ARM, macOS Intel / M-series / A-series, Ubuntu, Debian, iPhone
and iPad, Android). "Known issues" holds the computer profiles: machines with known problems or lower limits, never the default. Any mix of chips
and computers can be checked. Each line then shows the worst result over the
checked profiles, judged by the picked philosophy; the status field says
"Judged by the PureLink philosophy". The
status field groups the results by outcome (#369): every system, this
computer and each checked profile, is in exactly one group, by its whole set of
findings. "Same issue on:" names the systems with the same issues, then each
issue once ("Cam Link 4K: tiers 5 exceeds the recommended 3") with what was
counted below it, part by part with its origin (#326). Clicking an issue
selects its device in the tree, as a click in the tree does, so the status field
then covers that device's controller. "Unknown on:" holds the systems whose counts are
only "at least" or whose limits are unknown, and "Good on:" those with no
finding. The worst groups come first. The exported log ends with the same
groups under "Results by outcome", and the PDF report lists them too. This
computer is always one of the systems, for example "MacBook Pro (16-inch, 2021), measured" (#368):
its own computer file if there is one, else its operating system and chip,
judged with what it measures, such as the hub an M1 Max hides inside itself
(its tier limit) or Windows saying a hub is part of the computer, wherever the
philosophy accepts `detected`. Each checked profile is judged with the
database's values only, never with what this computer measures, so a Mac
with "Windows Intel" checked judges Windows as Windows is in the database. Below it are only
the checks that are exceeded: hubs, tiers and external displays, each with its count and
limit. The hubs line shows the hubs as in the row counts and the bus that is over
its limit, for example "External hubs: 3 (2+1), limit 1 on USB 2 (exceeded)". A check that is within its limit, or whose limit or count is unknown,
is not shown there; it is in the log. A stable profile shows only "stable".
With several profiles checked, the findings come from
the strictest one only and name it (see the [diagnostic rules](database.md#diagnostic-rules)). Plug and unplug
devices: only the affected part of the tree changes. A device that is
unplugged stays red at its last port; one that comes back is purple
(unstable). Click "Clear" next to it to reset it.

Is an active cable or extender plugged in that the tree does not show? Click
the small "+" in the middle of the line into the hub or device
behind the cable (or select that item and press Enter or the context menu
key) and pick what it adds from a short menu: None, +1 Hub, +2 Hub (active
extender), +3 Hub, +1 Tier or +2 Tier. "+N Hub" adds N hubs to the hub count of
that port's bus and, by itself, N tiers to everything behind that line, because
every hub is a tier. "+N Tier" adds only N tiers, for an extender or repeater
that is not a hub. The marked line is drawn dashed with a badge saying what it
adds, for example "+2 Hub" or "+1 Tier", so it is clear that you added it and
Windows did not detect it.
Below the list, "Active cable" takes the cable's length in metres and its bus
(USB 2 or USB 3), then Set. Each guide works out what that length adds: Crestron
gives the first 5 m (USB 2) or 3 m (USB 3) free, then one tier per further 5 m
or 3 m, so a 10 m USB 2 cable adds one tier; a guide that does not count cable
length, such as Ochno, ignores it. The badge then says, for example, "10 m USB 2
active copper cable".
Under it, pick the cable's type (passive, active copper, active optical,
USB4/Thunderbolt), one of the selected philosophy's approved models or "Other
cable", and whether it is the host cable (#372). It is ticked at first on a
computer port. Tick it for the cable between two daisy-chained docks too. A
passive cable adds no tiers, whatever its length. A model brings its rated length,
type and bus, and the hubs and tiers its maker says it adds: an Ochno O-EC extension
cable adds none. Hovering the marked row shows the selected philosophy's notes on
that cable, which the PDF report also lists, for example "PureLink: passive USB 3.2
Gen 1 about 2 m (indicative); PureInstall PI6600 (21002) is rated 5 m", or "Ochno:
O-EC-10 is not allowed as the host cable." These notes never change a status.
Click the "+" again to change the mark, or pick "None" to remove it. Marks last until the app closes.

Click "Log" in the top bar to open the log: every event of this session with
its time, newest at the bottom. It lists the start and loading stages, the
database load result and which database is in use ("Database in use: built in,
2026-11-18 (9c92cfe)"), the database update check, per controller its hubs per bus ("Hubs of ...: 3 (2+1)
(USB 2+USB 3)", and "USB4 routers of ...: 1" when a USB4 dock is connected, with how many are on ports whose bus is not reported), per
hub which ports Windows reports as USB-C or not
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
Export full setup (or "Export" in the top bar). Type a name in the "Room or
system name" box in the top bar first if you like; it is not saved between
runs. The export saves one zip, `usb-helper_<room>_<date>_<time>.zip`
(without the room when none is entered), with these files:

- `setup.json`: everything the app detected and found, with a `format_version`:
  the app version, the computer (manufacturer, model, processor, platform
  profile), the checked profiles, every controller, hub, port
  and device (name, vendor and product id, USB-C and user-connectable flags),
  your active-cable marks (each as its hubs and tiers, `{"hubs": 2, "tiers": 2}`;
  a file from an older version has one number N per mark, which is read as
  +N hubs), the monitors and their output type, and each
  checked profile's result with every finding, the unknown ones included.
- `tree.txt`: the tree as text, as Edit > Copy tree as text gives it.
- `screenshot.png`: the main window as shown.
- `log.txt`: the log.
- `report.pdf`: the PDF report (see below), only when unlocked (see
  [Unlock](#unlock)). If the PDF cannot be made, the zip still holds the other
  files and the log says why.

When they are set, the company name (only when unlocked), the room and the
licensed email head `tree.txt` and Edit > Copy tree as text ("Company: ...",
"Room: ...", "Licensed to: ...") and are in `setup.json`. The log lists the
company name and the licensed email at the top and each time they change.

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

File > Export PDF saves the "USB setup report" as one PDF,
`usb-helper_<room>_<date>_<time>.pdf`. It needs the unlock, a room name and a
read USB tree; until then the menu item is greyed out and its tooltip says why
("Enter a code to unlock PDF reports", "Enter a room name to export a PDF",
"Wait until the USB tree is read"). The report covers all controllers, in this
order:

- **Summary**: the computer, the checked profiles, the overall result and the
  hub, USB device, tier and display counts; below the hub count, the hubs per bus, for
  example "(2+1): USB 2, USB 3", then "· USB4 routers: 1" when there are any.
- **USB tree**: the tree as in the window, with its status colours.
- **Results**: each checked profile with its result, then each check that is
  not stable with its measured value, limit, result and the profiles it
  applies to. Limits that are not in the database are listed in grey.
- **Displays**: each monitor, its output type and the USB device it is
  connected to.
- **Findings**: one short line each, grouped as detected facts, verified
  database facts, predicted risks and unknowns.

The top of the first page has the company logo, or the company name when there
is no logo (or the logo is too hard to see on the light theme and there is no
light-theme logo), then the title "USB setup report", the room ("Room: not entered"
when none, for example in a setup zip), the date and app version, and
"Licensed to <email>" when set. The footer of every page has the company name
(only when there is no logo) and the room, "Generated by USB-helper
<version>", "Licensed to <email>" when set, and "Page X of Y".

No profile for your computer yet? Click "Report this machine" (or File >
Report this machine) and save the file (`<manufacturer>-<model>.yaml`). It
holds what the app detected: the manufacturer and model exactly as Windows
reports them, the processor, the matching platform profile, the USB
controllers with their root hub ports, and the number of active external
displays. It sets no limits, so it uses the platform profile's, and the status
is `unverified`. It never holds the company name, room or licensed email. Send
the file to the developers; they add model-specific limits from cited sources.

The menu bar at the top has File (Refresh, Report this machine, Export
log, Export full setup, Export PDF, Settings, Exit), Edit (Copy tree as text)
and Help (Check for updates, Unlock, About USB-helper). Copy tree as text puts
the tree on the clipboard as plain text, the same text the log gets, for
pasting into an email:

```
Dell Inc. Latitude 7450 (Hubs: 3, devices: 1, deepest tier: 5)
|-- USB-C port 1 [USB-C]
|   `-- [+2 hubs] Dock (USB 2) - stable
|       `-- Port 1
|           `-- Keyboard (046D:C31C, USB 2) - stable
`-- Displays
    `-- DELL U2723QE (HDMI)
```

Each row has its badges in brackets (what a marked cable adds, such as "+2 hubs" or "+1 tier", "USB-C",
"Display"), the result word after " - " and its notes in parentheses.

Press Alt, or Alt with the underlined letter (for example Alt+F for File), to
use the menu from the keyboard. Check for updates runs the update check again
(see [Updates](install.md#updates)) and also says when the running version is up to date or the
check failed; it is greyed out where the app has no update check (outside
Windows). About shows the running version, the database in use ("Database: built
in, <date> (<commit>)" or "Database: updated <date> (<commit>)", see
[Database updates](#database-updates)), the license, the icon's attribution,
a note on known quirks and a link to this repository. One quirk: the tree
shows the names devices give themselves, and many USB 3 hubs call their USB 2
half "USB2.1 Hub". There is no USB 2.1; it is a USB 2.0 hub with extra power
saving.

## Custom philosophy

While unlocked, "USB philosophy" lists **Custom** last, after the ProAV
guides: your own guide. Picking it before you have saved one, or clicking its
"Edit..." button, opens the Custom editor: the guide as YAML text, in the same
shape as the other guides (see [database.md](database.md#custom-guide)), with
every operating system and chip. It starts with your saved guide, else with a
blank one: every value empty, so everything falls back to USB-Helper's guide
until you fill it in. Leave a value empty to use the value above it, write a
number for a limit, or `none` for no limit. The blank records the unlock email
as a note ("Made by <email>").

- **Download blank YAML...** saves the blank to a file you pick, for example to
  fill in elsewhere or send to someone.
- **Open file...** loads a `.yaml` file into the editor (nothing is saved yet).
- **Save** checks the text as every guide is checked. With problems, they are
  listed with their line (`custom.yaml:12: ...`) and nothing is saved; the
  previous Custom guide stays. Without problems it is saved as `custom.yaml`
  next to the settings file and used at once: the status field says "Judged by
  the Custom philosophy".
- **Close** closes the editor. Custom picked and closed without saving leaves
  the previous philosophy picked.

Custom is kept as the picked philosophy through restarts. Locking the app
hides Custom and USB-Helper's guide applies; so it does at start when the app
is locked or `custom.yaml` is missing or invalid (the log says why). The file
is kept, and Custom is back once unlocked.

## Guide sources

Under the guide's name, "Sources:" lists every PDF and web link of the guide's
sources, each by its name (#333). A PDF has a small PDF mark and opens in the
app's own viewer, at the quoted page when the guide gives one; a link opens in
the browser. A PDF is downloaded only when you click it, into `sources-cache/`
next to `settings.json`, and checked: it must be a PDF, and its size and
fingerprint must match the guide's. When the download fails or gives no PDF,
the address opens in the browser and the app says why. When the fingerprint
differs, the viewer says "This source has changed since the guide was written".
A PDF without a public download (for example behind a login) asks for a copy
you have, checks it against the fingerprint, and keeps it. The viewer scrolls,
zooms, shows the page number and saves a copy; when it cannot start, the
system's PDF app opens the file.

## Testing several ports

To test a setup on more than one of the computer's ports, unplug it and plug it
into another port (#342). When the same hubs and devices, by vendor and product
id, come back behind another of the computer's own ports, the app sees a move,
not a disconnect, also when the unplug and the plug-in arrive as two changes:
the tree follows the setup, nothing turns purple for "reconnected", and the
status field says, for example, "The setup moved from Port 1 (USB-C, USB 3) to
Port 2 (USB 2)." What did not come back is listed as missing, as usual. Two of
the computer's ports with the same name are told apart by their hub, for
example "Port 1 on Root hub 2". On Windows the USB 2 and USB 3 halves of one
connector count as one port, named after the USB 3 half, from the first time
they are seen together: when the devices on them have the same physical
location (the connector's ACPI `_PLD`, which a USB-C port shares even when its
halves are on two controllers), or when the hub reports them as companions.
Until then, an empty USB-C port whose halves are on two controllers shows as
two ports. On a Mac, the USB 2 and USB 3 root ports of one dual-role (USB-C)
controller are one connector from the start (#358). A connector with both halves
on USB-C is named "USB-C port 1", "USB-C port 2" and so on. A move takes both
halves along, also when the USB 2 hubs arrive in one change and the USB 3 hubs
in the next. Plugging the whole setup back into the port it left is a replug,
not a reconnect: the status says "The setup was plugged back into USB-C port
1." A single device that drops and comes back while the rest of the port stays
connected is still marked "reconnected".

Each port tried keeps its last result. Only ports that had the same devices
are compared, so a device that was not plugged in on one port is not a
difference, and each difference is logged once. How the ports differ is listed with the
findings, in the log and in the PDF report: a device that has an error on one
port only, that runs on USB 2 on one ("On Port 2 the Camera runs on
USB 2 (USB 3 on Port 1).") or a setup within the limits on one port and over
one on another. The exported log ends with, and the PDF report has, "Ports
tested": "Tested on 2 of 3 ports: Port 1 stable, Port 2 potentially unstable
(Camera on USB 2). Port 3 not tested." The number of ports comes from the checked
computer's file, one per USB entry under `ports` (MacBook Neo: 2). When the
system shows more ports than the file has entries, one entry stands for several
ports (the MacBook Pro has one `thunderbolt4` entry for three), so the ports
seen this session count instead, as they do without a file; on a Mac whose
controllers do not say which ports are its own (Intel Macs), the summary then
says so. On Apple silicon each USB controller drives the Mac's own USB-C
receptacles, so its root ports are the Mac's own (#359). Nothing is
saved; "Refresh and clear log" starts over.

## Sessions

A session lasts from the start of USB-helper, or from "New session", until it
closes. Its log, its disconnected and reconnected devices and its findings are
kept in memory only. The log's first line, and the exported log's header, name
the instance: "USB-helper 0.11.0 started 2026-11-22 09:30", or after a new
session "New session 2026-11-22 21:30 (USB-helper 0.11.0)".

An app left open through sleep or for days would otherwise keep an old session
(#341). So when the computer slept (the clock jumped more than 5 minutes
between two checks, made every 30 seconds), or when the window is activated on
a later day than the session started, USB-helper asks once: "The computer
slept. Start a new session?" or "USB-helper has been open since Sunday 22
November. Start a new session?"

- "New session" clears the log, the disconnected and reconnected devices and
  the findings, and reads the USB tree again, as "Refresh and clear log" does.
  "Save the log first" opens Save log before; when that is cancelled or fails,
  the session is kept and nothing is cleared. Cable marks and settings stay.
- "Keep this session" clears nothing; the log says "Kept the session after
  sleep (12 h)". It is not asked again until the next sleep, or the next day.

When a newer version has been installed while this window stayed open, it
asks instead, for example "USB-helper 0.12.0 is installed; this window is
0.11.0. Restart now?": "Restart" starts the installed version and closes this
one, "Later" keeps this window.

## Unlock

An unlock code adds company branding, the PDF report and beta updates. Codes
are given out by the project owner, each for one email address.

Click "Unlock..." in the top bar, or Help > Unlock..., enter the email and the
code and click "Unlock". A code for another email is refused ("This code does
not match this email"). Once unlocked, the "Unlock..." button is gone and the
status line at the bottom of the window shows "Unlocked to <email>"; click it
to open the popup again, where "Remove code" locks the app again (not in a
free-unlock build, below). The email and code are kept in the settings file
(see below) and checked at every start. A beta can also be unlocked for
everyone (never a stable release); the status line of that beta then shows
"Unlocked (free beta)". A build without codes (for example one built from
source) says "Unlock codes are not available in this build".

## Settings

File > Settings opens the settings. Each change is saved at once:

- **Check for updates at start** (on by default): off, the app does not ask
  GitHub after it starts; Help > Check for updates still works.
- **Download database updates** (on by default): off, the app does not ask
  GitHub for newer database files and keeps the database it has; see
  [Database updates](#database-updates).
- **Offer beta versions** (on by default, only when unlocked): off, only
  stable releases are offered, and a newer beta alone counts as up to date.
  While locked the box is greyed out and unticked and only stable releases are
  offered; "Enter a code to unlock beta updates" opens the unlock popup. The
  choice made while unlocked is kept and comes back on unlock.
- **Database format** (betas only, Newest by default): the database format
  the beta uses from the next start, for example "Newest (v4)" or "v3", to
  compare an older format's results. Saved in `settings.json`. Release builds
  do not show it and always use the newest format.
- **Remember the checked profiles between starts** (off by default): on, the
  chips ticked and the philosophy picked in "USB philosophy" are ticked and picked again
  at the next start, instead of only the detected platform profile and
  USB-Helper's guide. Computers are never checked at start. If no chip was
  ticked, the detected platform profile is checked, as without the setting.
  Turning it off forgets them.
- **Company name** and **Company logo** (only when unlocked): shown in the top
  bar and on the PDF report; the name is also in the exports. While locked
  they are greyed out ("Enter a code to unlock" opens the unlock popup), and
  what was set before is kept. The name is saved when you leave the box.
  "Choose image..." takes an SVG, PNG or WebP image; transparency is kept, and
  an SVG stays vector on the PDF. The crop tool then opens with a fixed frame,
  3 times wider than high: drag the image to move it, zoom with the "Zoom"
  slider or the mouse wheel, or click "Fit whole image". Only what is inside
  the frame is kept; parts of the frame outside the image stay transparent.
  "Save" keeps the crop, "Cancel" keeps the previous logo. "Adjust crop..."
  opens the crop tool again for the same image, and "Remove" removes the logo.
  Where an image fits (the top bar, the PDF) the logo is shown, and the company
  name only when there is no logo; never both.
- **A logo that is hard to see on a theme.** The app follows the system's
  light or dark theme. When the logo's contrast with the top bar is below 3:1
  on a theme (for example a black logo on the dark theme), Settings shows "This
  logo is hard to see on the dark theme" (or light) under the logo, with two
  choices: "Invert it" shows an inverted copy (colours inverted, transparency
  kept), which "Keep" saves for that theme and "Cancel" drops; closing Settings
  while the copy shows keeps it too (#360); "Upload a logo
  for the dark theme" (or light) uses the same file picker and crop tool. Either
  can be removed again with "Remove". The logo is never put on another
  background. On each theme the top bar shows that theme's own logo, else the
  logo when its contrast there is 3:1 or more, else the company name; a theme
  change while the app runs updates it at once. The PDF has a white page, so it
  uses the light theme's choice. A new, recropped or removed logo also removes
  the theme logos made from the old one.

## Database updates

The hardware database is built into the app, and newer files reach a running
copy without a new app version (#290). Each start, in the background and next
to the update check, the app asks GitHub for the newest change to the files of
its database format on the `database` branch. When that change is newer than
the database in use, the app downloads the files, loads and checks them as at
every start, stores them in the `database-cache` folder next to
`settings.json`, and uses them at once: the menus fill in again with the same
ticks and the same philosophy. Custom is checked again against the new files and
falls back to USB-Helper's guide when it no longer fits, as at start.

At the next start the app uses the newer of its built-in database and the
stored one. A stored database that no longer loads (for example after an app
update that reads another format) is ignored, and the log says why. When there
is no network, GitHub does not answer in time or limits the requests, or a file
does not load, the database in use stays, the log gets one line, and nothing
pops up. The setting **Download database updates** turns this off.

The settings are stored in `%APPDATA%\USB-helper\settings.json` (the user's
roaming app data folder), the same for every copy of the app, so they survive
updates and moving the app's folder. With a roaming Windows profile they
also follow the user to other computers, remembered profiles included.
The logo is kept in the `branding` folder next to that file: the original
image, the crop and the cropped images; a theme's own logo is kept the same way
in `branding-light` or `branding-dark`. Without the file the defaults apply. A
file that cannot be read is named in the log with the reason, and the
defaults apply; it is only replaced when a setting is changed.
