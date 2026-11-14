# Using USB-helper

The window, the tree, the log, the exports and the settings in detail.

Start USB-helper from the Start menu (installer) or `USB-helper.exe` in the
`USB-helper` folder (portable zip; see [Install](install.md)). The window opens at once and
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
status colour, with the result word after its name. Hubs and devices show
the product name they report, falling back to the Windows name. Empty ports are left
out. Only the USB controller rows can be collapsed: click a controller's
chevron, double-click it, or use the left and right arrow keys to hide or show
its tree; while it is hidden the controller's name is bold. Only the
selected controller (see below) is open, and only when it has devices,
connected or disconnected. The computer's row, the Displays group and
everything below a controller, hubs built into the computer included, are
always open. Until you select an item or open or close a controller, the
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
hop, for example "Hubs: 4 (3+1), deepest tier: 4, deepest hop: 2". The
numbers in brackets are the hubs per bus, always in the same order: USB2 first
(the USB 2.0 tree), then USB3 (the SuperSpeed tree, USB 3.0 to 3.2), and
hovering the counts shows them in full, as in the tree as text. A USB 3 hub has a half on each bus, and
each bus has its own chain. The deepest tier and hop are those of any bus. The
computer's row shows the same over all controllers, and a controller with
nothing connected shows no counts. Copy tree as text, the log and the export
name the buses instead: "Hubs: 4 (USB2: 3, USB3: 1), deepest tier: 4, deepest
hop: 2". When the operating system reports no bus at all, there are no
brackets. The counts say "at least" when a hub, port or bus could not be read;
a hub on a port whose bus is not reported is in the total but on neither bus.
Hovering a hub or device
shows its own depth, for example "Tier 4, hubs in front: 2".
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
automatically. In the top bar, check the systems to judge the setup by in
the two drop-down lists,
"Operating systems" (generic platform profiles such as `windows-intel`) and
"Computers" (computer profiles); any mix can be checked. Each line then shows
the worst result over the checked profiles. The
status field has one row per checked profile with its result (stable,
potentially unstable, unstable or unknown, in its colour) and its hub, tier,
hop and display checks. With several profiles checked, the findings come from
the strictest one only and name it (see the [diagnostic rules](database.md#diagnostic-rules)). Plug and unplug
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
Export full setup (or "Export" in the top bar). Type a name in the "Room or
system name" box in the top bar first if you like; it is not saved between
runs. The export saves one zip, `usb-helper_<room>_<date>_<time>.zip`
(without the room when none is entered), with these files:

- `setup.json`: everything the app detected and found, with a `format_version`:
  the app version, the computer (manufacturer, model, processor, platform
  profile), the checked profiles, every controller, hub, port
  and device (name, vendor and product id, USB-C and user-connectable flags),
  your active-cable marks, the monitors and their output type, and each
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
  hub, tier, hop and display counts.
- **USB tree**: the tree as in the window, with its status colours.
- **Results**: each checked profile with its result, then each check that is
  not stable with its measured value, limit, result and the profiles it
  applies to. Limits that are not in the database are listed in grey.
- **Displays**: each monitor, its output type and the USB device it is
  connected to.
- **Findings**: one short line each, grouped as detected facts, verified
  database facts, predicted risks and unknowns.

The top of the first page has the company logo, or the company name when there
is no logo, then the title "USB setup report", the room ("Room: not entered"
when none, for example in a setup zip), the date and app version, and
"Licensed to <email>" when set. The footer of every page has the company name
(only when there is no logo) and the room, "Generated by USB-helper
<version>", "Licensed to <email>" when set, and "Page X of Y".

No profile for your computer yet? Click "Export computer profile" (or File >
Export computer profile) and save the file (`<manufacturer>-<model>.yaml`). It
holds what the app detected: the manufacturer and model exactly as Windows
reports them, the processor, the matching platform profile, the USB
controllers with their root hub ports, and the number of active external
displays. It sets no limits, so it uses the platform profile's, and the status
is `unverified`. It never holds the company name, room or licensed email. Send
the file to the developers; they add model-specific limits from cited sources.

The menu bar at the top has File (Refresh, Export computer profile, Export
log, Export full setup, Export PDF, Settings, Exit), Edit (Copy tree as text)
and Help (Check for updates, Unlock, About USB-helper). Copy tree as text puts
the tree on the clipboard as plain text, the same text the log gets, for
pasting into an email:

```
Dell Inc. Latitude 7450 (Hubs: 1, deepest tier: 5, deepest hop: 3)
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
(see [Updates](install.md#updates)) and also says when the running version is up to date or the
check failed; it is greyed out where the app has no update check (outside
Windows). About shows the running version, the license, the icon's attribution,
a note on known quirks and a link to this repository. One quirk: the tree
shows the names devices give themselves, and many USB 3 hubs call their USB 2
half "USB2.1 Hub". There is no USB 2.1; it is a USB 2.0 hub with extra power
saving.

## Unlock

An unlock code adds company branding, the PDF report and beta updates. Codes
are given out by the project owner, each for one email address.

Click "Unlock..." in the top bar, or Help > Unlock..., enter the email and the
code and click "Unlock". A code for another email is refused ("This code does
not match this email"). Once unlocked, the "Unlock..." button is gone and the
status line at the bottom of the window shows "Unlocked to <email>"; click it
to open the popup again, where "Remove code" locks the app again (not in a
free-unlock build, below). The email and code are kept in the settings file
(see below) and checked at every start. A build can also be unlocked for
everyone, betas and releases alike; the status line of that build then shows
"Unlocked (free beta)". A build without codes (for example one built from
source) says "Unlock codes are not available in this build".

## Settings

File > Settings opens the settings. Each change is saved at once:

- **Check for updates at start** (on by default): off, the app does not ask
  GitHub after it starts; Help > Check for updates still works.
- **Offer beta versions** (on by default, only when unlocked): off, only
  stable releases are offered, and a newer beta alone counts as up to date.
  While locked the box is greyed out and unticked and only stable releases are
  offered; "Enter a code to unlock beta updates" opens the unlock popup. The
  choice made while unlocked is kept and comes back on unlock.
- **Remember the checked profiles between starts** (off by default): on, the
  operating systems and computers checked in the two lists are checked again
  at the next start, instead of only the detected platform profile. If
  nothing was checked, the detected platform profile is checked, as without
  the setting. Turning it off forgets them.
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

The settings are stored in `%APPDATA%\USB-helper\settings.json` (the user's
roaming app data folder), the same for every copy of the app, so they survive
updates and moving the app's folder. With a roaming Windows profile they
also follow the user to other computers, remembered profiles included.
The logo is kept in the `branding` folder next to that file: the original
image, the crop and the cropped images. Without the file the defaults apply. A
file that cannot be read is named in the log with the reason, and the
defaults apply; it is only replaced when a setting is changed.
