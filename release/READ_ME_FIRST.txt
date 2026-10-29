USB-helper {version} for Windows (x64)

No installation and no administrator rights are needed. No network
connection is needed.

1. Unzip the whole folder, for example to your Desktop. Keep the
   "database" folder next to USB-helper.exe.

2. This build is not signed, so Windows blocks it after a download.
   Before the first start, open PowerShell in this folder and run:

   Unblock-File -Path .\USB-helper.exe

   Or right-click USB-helper.exe, choose Properties, tick "Unblock",
   then OK.

3. Start USB-helper.exe. If Windows SmartScreen still shows a warning,
   click "More info", then "Run anyway".

The hardware database is the "database" folder. How to add or correct a
computer profile: https://github.com/klangche/USB-helper#hardware-database
