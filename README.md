# ShellyHwinfoBridge

> **Status: beta (0.1.0-beta.2).** The tray app, main window with its settings page and the setup wizard are in
> place.

A small Windows tray application that reads live wall power from a Shelly smart plug and publishes it into
[HWiNFO](https://www.hwinfo.com/) as custom sensors:

| Sensor | Meaning |
|---|---|
| **AC Power (Wall)** | The raw Shelly reading |
| **DC Power (Est.)** | AC converted through your PSU's efficiency curve |
| **PSU Loss** | AC − DC |
| **Other Components** | DC minus the component sensors you select (CPU, GPU, RAM, …), computed by HWiNFO with a formula sensor |
| **Shelly Online** | 1 while the plug is reachable, 0 otherwise |
| **Energy Today** / **Energy This Month** | Wall energy in kWh from the plug's own energy counter |

The sensors are written to `HKCU\Software\HWiNFO64\Sensors\Custom\Shelly Power`. HWiNFO picks them up after a restart.

## Using the app

### First run: the setup wizard

The first start opens a six-step setup wizard. Nothing is saved or written to HWiNFO until you click Apply in step 5.

1. **HWiNFO:** checks that HWiNFO64 is installed and running, and whether its Shared Memory Support is on (see below
   for how to turn it on).
2. **Shelly:** lists the Shelly plugs found on your network. Pick yours or type its IP address, enter the password if
   you set one in the Shelly app, and click "Test connection". A fixed IP or a DHCP reservation in your router keeps
   the plug at the same address. If the plug is on another network or VLAN, allow TCP port 80 from the PC to the plug.
3. **PSU:** pick your power supply from the database (filter by brand, wattage and rating, or search). If it is
   missing, use a generic curve for its 80 Plus rating (approximate, least accurate at low load) or enter your own
   curve as efficiency figures or AC/DC points (CSV import and export). The input voltage curve (115 V or 230 V) is
   picked from the voltage the plug measures; "Change" overrides it.
4. **Sensors:** tick the HWiNFO power sensors to subtract from DC power to get **Other Components** (CPU, GPU, RAM and
   so on). Likely candidates are ticked for you, and the wizard warns when a sensor would be counted twice or when two
   sensors share a name. "Advanced: edit formula" lets you write the HWiNFO formula yourself.
5. **Apply:** review the summary, choose whether to start with Windows, then click Apply. The app saves the settings,
   writes the sensors and starts polling, and the wizard moves on to the last step.
6. **Verify:** HWiNFO must be restarted to show new sensors. "Restart HWiNFO" does that for you, and the wizard then
   checks that the four Shelly Power sensors appear (give HWiNFO a few seconds to come back). If HWiNFO runs as
   administrator the app cannot close it; exit HWiNFO from its tray icon, start it again and click Verify. "Finish"
   closes the wizard.

Cancelling the wizard changes nothing. On first run the app then stays in the tray, not set up; run the wizard again
from the tray icon or the main window.

### HWiNFO Shared Memory Support

Shared Memory Support is an HWiNFO setting that lets other programs read the sensor list. To turn it on: in HWiNFO's
sensor window, open Settings (the gear icon), click "Main Settings", tick "Shared Memory Support" on the General tab,
click OK and restart HWiNFO. The wizard uses it in step 4 to list your power sensors by name and in step 6 to verify
the new sensors. The bridge itself does not need it: polling the plug and writing the sensors works without it. When it
is off, step 4 lets you type the sensor names instead.

The free HWiNFO version turns Shared Memory Support off after 12 hours. That is fine for setup, and you can turn it off
again afterwards. To keep seeing the live Other Components value in the main window and the tray tooltip without it,
let HWiNFO export that value for its gadget: in HWiNFO's sensor Settings, open the "HWiNFO Gadget" tab and tick
"Enable reporting to Gadget", select "Other Components" in the list, tick "Report value in Gadget" below the list (its
Sidebar column shows Yes) and click OK. The app reads that export from the registry when shared memory is off. With
neither available, the main window shows "Only available with Shared Memory enabled in HWiNFO" instead.

### Main window

Double-click the tray icon (or choose "Open…") to open the main window. It shows the connection status (plug name and
measured voltage), the live AC, DC, PSU loss and Other Components readings, the "Energy history" chart for the last
48 hours, 60 days or 24 months (hover a bar for its energy) with today's and this month's totals, and a summary of the
PSU, sensors and HWiNFO status. The buttons open Settings and pause or resume polling. Closing the window (or pressing
Esc) only hides it; the app keeps running in the tray.

### Settings

"Settings" in the main window or the tray menu shows the settings page inside the main window; "Done" (or Esc) returns
to the dashboard. Every change applies immediately, without a restart:

- **Shelly device:** host and password ("unchanged" means the stored password is kept; "Clear" removes it) and a
  connection test.
- **Power supply:** "Change…" opens the PSU step of the wizard; the input voltage can be fixed to 115 V or 230 V.
- **Sensors and formula:** "Edit…" opens the sensor step of the wizard. The tip next to the formula explains the
  gadget export described above.
- **Polling:** the poll interval (500 to 5000 ms) and smoothing (moving average over 1 to 30 readings).
- **Energy:** energy logging on or off, an optional price per kWh and currency for the cost shown in the main window,
  and "Open energy folder".
- **Startup and tray:** start with Windows, show the tray icon.
- **PSU database:** "Check for update" downloads a newer `psu-database.json` from this project, validates it and keeps
  the current one if the download is invalid.
- **Maintenance:** "Open log folder"; "Re-run setup wizard" leaves the settings page and opens the wizard with your
  current settings; "Remove HWiNFO sensors" deletes the Shelly Power sensors from HWiNFO (for uninstalling) and pauses
  polling, because the next poll would create them again; "Exit ShellyHwinfoBridge".

The settings page and the wizard are never open at the same time: while the wizard is open, Settings is disabled.
The settings page also shows the app version at the bottom right.

### Tray icon

The icon is orange while the plug is online, grey when it is offline and grey with a pause mark while polling is
paused. Its tooltip shows the live readings. Right-click it for Open…, Settings…, Setup wizard…, Pause/Resume polling,
Open log folder and Exit. Exiting sets Shelly Online to 0 and the readings to 0 in HWiNFO.

### Hiding the tray icon

Turn off "Show tray icon" in Settings to run without an icon. The app keeps running and polling. To get back in, start
ShellyHwinfoBridge again (Start menu, shortcut or the exe): instead of a second copy, the running app opens its main
window. From there, Settings can show the icon again, and Settings › Maintenance › "Exit ShellyHwinfoBridge" exits the
app while the icon is hidden.

### Energy logging

The plug counts energy itself, also while the app is not running. The app turns that counter into hourly rows in
`%APPDATA%\ShellyHwinfoBridge\energy\YYYY-MM.csv` and shows them in the main window's chart; hours when the app was
not running are filled in from the counter the next time it starts.
The chart's hover tooltip also shows the app's estimate of the energy the PC drew on the DC side; that estimate only
covers the time the app was running. Settings, logs and energy files live in `%APPDATA%\ShellyHwinfoBridge`.

Development hardware: Shelly Plug M Gen3.

## Requirements

- Windows 10 or 11, x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (the app is framework-dependent)
- HWiNFO64
- A Gen2 or Gen3 Shelly device with a `Switch` component (e.g. Plug S Gen3, Plug M Gen3, Plus Plug S) or a
  `PM1` component (e.g. PM Mini). Gen1 devices are not supported.

## Building

Requires the .NET 8 SDK.

```powershell
dotnet build -c Release
dotnet test -c Release
```

Publish a single-file, framework-dependent x64 executable:

```powershell
dotnet publish src/ShellyHwinfoBridge/ShellyHwinfoBridge.csproj -c Release -o publish
```

`publish/ShellyHwinfoBridge.exe` and `publish/psu-database.json` are the files to distribute.

## Project layout

| Path | Contents |
|---|---|
| `src/ShellyHwinfoBridge.Core` | PSU curves and DC estimation, PSU database and updater, HWiNFO registry writer, shared-memory reader and locator, Shelly client and discovery, poller, settings, autostart, logging |
| `src/ShellyHwinfoBridge` | The WinForms tray application |
| `tests/ShellyHwinfoBridge.Core.Tests` | xUnit tests |
| `psu-database.json` | The PSU efficiency curve database (also embedded in the app as a fallback) |
| `tools/cybenetics-import` | Maintainer tool that turns a Cybenetics report PDF into a database entry (see its README) |

## PSU data attribution

The PSU efficiency curves in `psu-database.json` come from [Cybenetics](https://www.cybenetics.com/) evaluation
reports. Each database entry names its source and links to the original report, as Cybenetics requires for reuse
of its data. PSUs not in the database can use a generic curve derived from the 80 Plus minimum requirements
(approximate, least accurate at low load) or a manually entered curve.

## License

[MIT](LICENSE). Copyright (c) 2026 Zapador.
