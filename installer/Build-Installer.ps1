<#
.SYNOPSIS
    Compiles installer\ShellyHwinfoBridge.iss with Inno Setup's ISCC.exe.

.DESCRIPTION
    Used by the release and CI workflows and for local builds (Windows PowerShell 5.1 or PowerShell 7).
    Run `dotnet publish src/ShellyHwinfoBridge/ShellyHwinfoBridge.csproj -c Release -o publish` first.
    Prints the path of the produced setup.exe as the last line of output.

.PARAMETER Version
    Product version without a leading "v", e.g. 1.0.0 or 1.1.0-beta.1.

.PARAMETER PublishDir
    Folder with the published ShellyHwinfoBridge.exe and psu-database.json. Default: <repo>\publish.

.PARAMETER OutputDir
    Folder for the setup.exe. Default: <repo>\artifacts\installer.

.PARAMETER InstallInnoSetup
    Install Inno Setup with Chocolatey when ISCC.exe is not found (used on GitHub runners).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $PublishDir,

    [string] $OutputDir,

    [switch] $InstallInnoSetup
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishDir) { $PublishDir = Join-Path $repoRoot 'publish' }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts\installer' }
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

foreach ($file in 'ShellyHwinfoBridge.exe', 'psu-database.json') {
    if (-not (Test-Path (Join-Path $PublishDir $file))) {
        throw "Missing $file in $PublishDir. Run dotnet publish first."
    }
}

function Find-Iscc {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    return $null
}

$iscc = Find-Iscc
if (-not $iscc -and $InstallInnoSetup) {
    Write-Host 'ISCC.exe not found; installing Inno Setup with Chocolatey.'
    choco install innosetup -y --no-progress | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "choco install innosetup failed with exit code $LASTEXITCODE." }
    $iscc = Find-Iscc
}
if (-not $iscc) {
    throw 'ISCC.exe (Inno Setup 6) not found. Install it, e.g. winget install --id JRSoftware.InnoSetup -e'
}

Write-Host "Using $iscc"
$script = Join-Path $PSScriptRoot 'ShellyHwinfoBridge.iss'
& $iscc "/DAppVersion=$Version" "/DPublishDir=$PublishDir" "/DOutputDir=$OutputDir" $script | Out-Host
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$setup = Join-Path $OutputDir "ShellyHwinfoBridge-$Version-setup.exe"
if (-not (Test-Path $setup)) { throw "ISCC succeeded but $setup was not produced." }
Write-Output $setup
