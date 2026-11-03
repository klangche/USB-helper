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
   USB-helper, with the app and its files. Keep the files together;
   you can move the whole folder anywhere.

3. Start UsbHelper.exe in that folder. If Windows SmartScreen shows a
   warning, click "More info", then "Run anyway".

Updates: when a newer version exists, USB-helper shows it at the top of
its window. If you pick it, USB-helper downloads and verifies it,
unzips it into a new folder next to this one (USB-helper-<version>),
starts it and closes. This folder stays; delete it when you no longer
need it. To remove USB-helper completely, delete its folders.

From 0.5.0: that version unpacked itself to %LOCALAPPDATA%\USB-helper.
This version does not use or touch that folder; you can delete it, and
the old USB-helper.exe.

The hardware database is built into UsbHelper.exe; a new version brings
the latest database. How to add or correct a computer profile:
https://github.com/klangche/USB-helper#hardware-database

The app icon is the "usb" icon from Font Awesome Free by Fonticons, Inc.
(https://fontawesome.com), licensed under CC BY 4.0
(https://creativecommons.org/licenses/by/4.0/), drawn white.
