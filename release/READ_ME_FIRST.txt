USB-helper {version} for Windows (x64)

No installation and no administrator rights are needed. No network
connection is needed.

1. Unzip USB-helper.exe, for example to your Desktop. It is the whole
   app; you can move it anywhere.

2. This build is not signed, so Windows blocks it after a download.
   Before the first start, open PowerShell in this folder and run:

   Unblock-File -Path .\USB-helper.exe

   Or right-click USB-helper.exe, choose Properties, tick "Unblock",
   then OK.

3. Start USB-helper.exe. If Windows SmartScreen still shows a warning,
   click "More info", then "Run anyway".

The first start of each version takes a few seconds longer: USB-helper.exe
unpacks the app to %LOCALAPPDATA%\USB-helper\{version} (a folder in your
user profile) and starts it from there. Later starts use that folder, and
older versions there are removed. To remove USB-helper completely, delete
USB-helper.exe and the folder %LOCALAPPDATA%\USB-helper.

The hardware database is built into USB-helper.exe; a new version brings
the latest database. How to add or correct a computer profile:
https://github.com/klangche/USB-helper#hardware-database

The app icon is the "usb" icon from Font Awesome Free by Fonticons, Inc.
(https://fontawesome.com), licensed under CC BY 4.0
(https://creativecommons.org/licenses/by/4.0/), drawn white.
