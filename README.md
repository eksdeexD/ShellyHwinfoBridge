# ShellyHwinfoBridge

> **Status: in development.** The core library is in place; the tray UI and setup wizard are not yet implemented.

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
| `src/ShellyHwinfoBridge.Core` | PSU curves and DC estimation, PSU database, HWiNFO registry writer, Shelly client, poller, settings, logging |
| `src/ShellyHwinfoBridge` | The WinForms tray application |
| `tests/ShellyHwinfoBridge.Core.Tests` | xUnit tests |
| `psu-database.json` | The PSU efficiency curve database (also embedded in the app as a fallback) |
| `tools/cybenetics-import` | Planned import tool for new database entries |

## PSU data attribution

The PSU efficiency curves in `psu-database.json` come from [Cybenetics](https://www.cybenetics.com/) evaluation
reports. Each database entry names its source and links to the original report, as Cybenetics requires for reuse
of its data. PSUs not in the database can use a generic curve derived from the 80 Plus minimum requirements
(approximate, least accurate at low load) or a manually entered curve.

## License

[MIT](LICENSE). Copyright (c) 2026 Zapador.
