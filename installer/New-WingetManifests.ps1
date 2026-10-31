<#
.SYNOPSIS
    Writes the three winget manifests (version, installer, default locale) for a release.

.DESCRIPTION
    Output: <OutputRoot>\manifests\z\Zapador\ShellyHwinfoBridge\<Version>\, the layout of microsoft/winget-pkgs.
    The ProductCode is read from the AppId in installer\ShellyHwinfoBridge.iss ("{GUID}_is1", the uninstall key
    Inno Setup registers), so the script and the manifests cannot drift apart.
    Works in Windows PowerShell 5.1 and PowerShell 7. Prints the manifest folder as the last line of output.

.PARAMETER Version
    Package version without a leading "v". Pre-release versions are refused unless -AllowPrerelease is given
    (betas are not published to winget; the switch exists for dry runs).

.PARAMETER InstallerUrl
    Public download URL of the setup.exe (the GitHub release asset).

.PARAMETER InstallerPath
    Local path of the same setup.exe, used to compute InstallerSha256.

.PARAMETER ReleaseNotesUrl
    Optional URL of the release page.

.PARAMETER OutputRoot
    Root folder for the manifests. Default: <repo>\winget.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string] $InstallerUrl,

    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [string] $ReleaseNotesUrl,

    [string] $OutputRoot,

    [switch] $AllowPrerelease
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'winget' }

$manifestVersion = '1.9.0'
$packageId = 'Zapador.ShellyHwinfoBridge'
$repoUrl = 'https://github.com/eksdeexD/ShellyHwinfoBridge'

if ($Version.Contains('-') -and -not $AllowPrerelease) {
    throw "Version $Version is a pre-release; betas are not published to winget."
}
if (-not (Test-Path $InstallerPath)) { throw "Installer not found: $InstallerPath" }

$iss = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'ShellyHwinfoBridge.iss')
$match = [regex]::Match($iss, '(?m)^AppId=\{\{([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}\s*$')
if (-not $match.Success) { throw 'AppId={{GUID} not found in ShellyHwinfoBridge.iss.' }
$productCode = '{' + $match.Groups[1].Value.ToUpperInvariant() + '}_is1'

$sha256 = (Get-FileHash -Algorithm SHA256 -Path $InstallerPath).Hash.ToUpperInvariant()
$releaseDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')

# Single-quoted YAML scalar (doubles embedded single quotes).
function Quote([string] $value) { "'" + $value.Replace("'", "''") + "'" }

function Header([string] $type) {
    @(
        '# Created by installer/New-WingetManifests.ps1 (ShellyHwinfoBridge release workflow).',
        "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$type.$manifestVersion.schema.json",
        ''
    )
}

$versionManifest = (Header 'version') + @(
    "PackageIdentifier: $packageId",
    "PackageVersion: $(Quote $Version)",
    'DefaultLocale: en-US',
    'ManifestType: version',
    "ManifestVersion: $manifestVersion"
)

$installerManifest = (Header 'installer') + @(
    "PackageIdentifier: $packageId",
    "PackageVersion: $(Quote $Version)",
    'MinimumOSVersion: 10.0.17763.0',
    'InstallerType: inno',
    'Scope: machine',
    'InstallModes:',
    '- interactive',
    '- silent',
    '- silentWithProgress',
    'UpgradeBehavior: install',
    'ElevationRequirement: elevatesSelf',
    "ProductCode: $(Quote $productCode)",
    "ReleaseDate: $releaseDate",
    'Dependencies:',
    '  PackageDependencies:',
    '  - PackageIdentifier: Microsoft.DotNet.DesktopRuntime.8',
    'Installers:',
    '- Architecture: x64',
    "  InstallerUrl: $InstallerUrl",
    "  InstallerSha256: $sha256",
    'ManifestType: installer',
    "ManifestVersion: $manifestVersion"
)

$localeManifest = (Header 'defaultLocale') + @(
    "PackageIdentifier: $packageId",
    "PackageVersion: $(Quote $Version)",
    'PackageLocale: en-US',
    'Publisher: Zapador',
    "PublisherSupportUrl: $repoUrl/issues",
    'Author: Zapador',
    "PackageName: $(Quote 'Shelly HWiNFO Bridge')",
    "PackageUrl: $repoUrl",
    'License: MIT',
    "LicenseUrl: $repoUrl/blob/main/LICENSE",
    "Copyright: $(Quote 'Copyright (c) 2026 Zapador')",
    "ShortDescription: $(Quote 'Publishes live wall power from a Shelly smart plug into HWiNFO as custom sensors.')",
    "Description: $(Quote ('A small Windows tray app that reads the wall (AC) power of your PC from a Shelly smart plug ' +
        'and writes it into HWiNFO as custom sensors, together with the estimated DC power from your PSU''s ' +
        'efficiency curve, the PSU loss, the power of the components HWiNFO does not measure and the energy used ' +
        'today and this month.'))",
    'Tags:',
    '- energy',
    '- hwinfo',
    '- power-meter',
    '- psu',
    '- sensors',
    '- shelly',
    '- tray'
)
if ($ReleaseNotesUrl) { $localeManifest += "ReleaseNotesUrl: $ReleaseNotesUrl" }
$localeManifest += @(
    'ManifestType: defaultLocale',
    "ManifestVersion: $manifestVersion"
)

$dir = Join-Path $OutputRoot "manifests\z\Zapador\ShellyHwinfoBridge\$Version"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$files = @{
    "$packageId.yaml"                  = $versionManifest
    "$packageId.installer.yaml"        = $installerManifest
    "$packageId.locale.en-US.yaml"     = $localeManifest
}
foreach ($name in $files.Keys) {
    [System.IO.File]::WriteAllText((Join-Path $dir $name), (($files[$name] -join "`n") + "`n"), $utf8)
}

Write-Output $dir
