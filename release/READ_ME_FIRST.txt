USB-helper {version} for Windows (x64)

This is the portable zip: no installation and no administrator rights
are needed. (Installed with the .msi instead? Then start USB-helper from
the Start menu; updates come as .msi files and the steps below do not
apply.) A network
connection is used only to check for updates after USB-helper starts;
without one, it works as normal.

1. This build is not signed, so Windows marks the download as blocked.
   Before unzipping, open PowerShell in the download folder and run:

   Unblock-File -Path .\usb-helper-{version}-windows-x64.zip

   Or right-click the zip, choose Properties, tick "Unblock", then OK.

2. Unzip it, for example to your Desktop. You get one folder,
   USB-helper, with USB-helper.exe, these two text files and the
   folder "app" with the app's files. Keep them together; you can move
   the whole USB-helper folder anywhere.

3. Start USB-helper.exe in that folder. If Windows SmartScreen shows a
   warning, click "More info", then "Run anyway".

Updates: when a newer version exists, USB-helper shows it in a small
window after it starts. If you pick it, USB-helper opens that release's
page in your browser; download the new zip there and unzip it as above.
To remove USB-helper completely, delete its folder.

From 0.5.0: that version unpacked itself to %LOCALAPPDATA%\USB-helper.
This version does not use or touch that folder; you can delete it, and
the old USB-helper.exe. A portable 0.6.0 beta cannot update itself to
this version: this download replaces it; delete the old folder.

The hardware database is built into the app; a new version brings
the latest database. How to add or correct a computer profile:
https://github.com/klangche/USB-helper#hardware-database

The app icon is the "usb" icon from Font Awesome Free by Fonticons, Inc.
(https://fontawesome.com), licensed under CC BY 4.0
(https://creativecommons.org/licenses/by/4.0/), drawn white.
