# ShellyHwinfoBridge

> **Status: in development.** The core library is in place (see below); the tray UI and setup wizard are not yet implemented.

A small Windows tray application that reads live wall power from a Shelly smart plug and publishes it into
[HWiNFO](https://www.hwinfo.com/) as custom sensors:

| Sensor | Meaning |
|---|---|
| **AC Power (Wall)** | The raw Shelly reading |
| **DC Power (Est.)** | AC converted through your PSU's efficiency curve |
| **PSU Loss** | AC − DC |
| **Other Components** | DC minus the component sensors you select (CPU, GPU, RAM, …), computed by HWiNFO with a formula sensor |
| **Shelly Online** | 1 while the plug is reachable, 0 otherwise |

The sensors are written to `HKCU\Software\HWiNFO64\Sensors\Custom\Shelly Power`. HWiNFO picks them up after a restart.

## What works so far

The pieces the tray app and setup wizard will be built on are implemented and tested:

- **Plug discovery:** finds Shelly devices on the local network through mDNS (`_shelly._tcp`) and shows their name,
  model and IP address. Manual IP or host name entry remains possible for networks where mDNS does not pass.
- **HWiNFO detection:** finds HWiNFO64 through its installer registry entry or the running process, and can restart
  it (close, then relaunch from the detected path) so it picks up the new sensors.
- **HWiNFO sensor list:** reads all power (W) sensors from HWiNFO's shared memory, grouped by device, so the wizard
  can pre-select CPU, GPU and RAM power. This needs **Shared Memory Support** enabled in HWiNFO's settings, but only
  during setup; the bridge itself never depends on it.
- **Start with Windows:** an optional per-user autostart entry.
- **PSU database updates:** "Check for database update" downloads the latest `psu-database.json` from this
  project's GitHub repository, validates it, and only replaces the local copy when the download is valid and newer.
- **Single instance:** starting the app again while it runs (for example with the tray icon hidden) asks the running
  instance to show its settings instead of starting a second copy.

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
