<#
.SYNOPSIS
    Checks the winget manifests written by New-WingetManifests.ps1 before they are published.

.DESCRIPTION
    A quick structural check, not a full schema validation (`winget validate` is not available on the GitHub
    Windows runners): the three files exist, are UTF-8 without tabs, every line is a comment, a "key: value" pair or
    a list item, the required fields have the expected values, and the identifier, version and manifest version
    agree across the files. When the powershell-yaml module (ConvertFrom-Yaml) is available the files are also fully
    parsed as YAML. Works in Windows PowerShell 5.1 and PowerShell 7. Throws on the first set of problems.

.PARAMETER ManifestDir
    The folder that holds the three manifests (...\manifests\z\Zapador\ShellyHwinfoBridge\<version>).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestDir
)

$ErrorActionPreference = 'Stop'
$packageId = 'Zapador.ShellyHwinfoBridge'
$problems = New-Object System.Collections.Generic.List[string]
$expected = @{
    version       = "$packageId.yaml"
    installer     = "$packageId.installer.yaml"
    defaultLocale = "$packageId.locale.en-US.yaml"
}
$canParse = [bool](Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue)
$parsed = @{}

foreach ($type in $expected.Keys) {
    $path = Join-Path $ManifestDir $expected[$type]
    if (-not (Test-Path $path)) {
        $problems.Add("Missing $($expected[$type]).")
        continue
    }

    $text = [System.IO.File]::ReadAllText($path, (New-Object System.Text.UTF8Encoding($false, $true)))
    $text = $text.Replace("`r`n", "`n")
    if ($text.Contains("`t")) { $problems.Add("$($expected[$type]): contains a tab.") }

    $top = @{}
    $lineNo = 0
    foreach ($line in ($text -split "`r?`n")) {
        $lineNo++
        if ($line -match '^\s*(#.*)?$') { continue }
        if ($line -match '^([A-Za-z][A-Za-z0-9]*):(?: (.+))?$') {
            $top[$Matches[1]] = if ($Matches[2]) { $Matches[2].Trim().Trim("'") } else { '' }
        }
        elseif ($line -notmatch '^(  )*(- )?[A-Za-z][A-Za-z0-9]*:( .+)?$' -and $line -notmatch '^(  )*- [^:]+$') {
            $problems.Add("$($expected[$type]) line ${lineNo}: not a key/value or list item: $line")
        }
    }

    $schema = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$type.$($top['ManifestVersion']).schema.json"
    if (-not $text.Contains($schema)) { $problems.Add("$($expected[$type]): schema header does not match ManifestVersion.") }
    if ($top['ManifestType'] -ne $type) { $problems.Add("$($expected[$type]): ManifestType is '$($top['ManifestType'])', expected '$type'.") }
    if ($top['PackageIdentifier'] -ne $packageId) { $problems.Add("$($expected[$type]): wrong PackageIdentifier.") }
    if ($top['ManifestVersion'] -notmatch '^\d+\.\d+\.\d+$' -or [version]$top['ManifestVersion'] -lt [version]'1.6.0') {
        $problems.Add("$($expected[$type]): ManifestVersion must be 1.6.0 or newer.")
    }

    if ($canParse) {
        try {
            $yaml = ConvertFrom-Yaml $text
            if ($yaml['PackageIdentifier'] -ne $packageId) { $problems.Add("$($expected[$type]): YAML parse gives a different PackageIdentifier.") }
        }
        catch {
            $problems.Add("$($expected[$type]): YAML parse failed: $($_.Exception.Message)")
        }
    }

    $parsed[$type] = @{ Top = $top; Text = $text }
}

if ($parsed.Count -eq 3) {
    $v = $parsed['version'].Top
    $i = $parsed['installer'].Top
    $l = $parsed['defaultLocale'].Top
    foreach ($key in 'PackageVersion', 'ManifestVersion') {
        if ($v[$key] -ne $i[$key] -or $v[$key] -ne $l[$key]) { $problems.Add("$key differs between the manifests.") }
    }
    if ($v['DefaultLocale'] -ne $l['PackageLocale']) { $problems.Add('DefaultLocale does not match PackageLocale.') }

    if ($i['InstallerType'] -ne 'inno') { $problems.Add('InstallerType must be inno.') }
    if ($i['Scope'] -ne 'machine') { $problems.Add('Scope must be machine.') }
    if ($i['ProductCode'] -notmatch '^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}_is1$') { $problems.Add('ProductCode must be {GUID}_is1.') }
    $installerText = $parsed['installer'].Text
    if ($installerText -notmatch '(?m)^- Architecture: x64$') { $problems.Add('Installers[0].Architecture must be x64.') }
    if ($installerText -notmatch '(?m)^  InstallerUrl: https://\S+-setup\.exe$') { $problems.Add('InstallerUrl must be an https URL of the setup.exe.') }
    if ($installerText -notmatch '(?m)^  InstallerSha256: [0-9A-F]{64}$') { $problems.Add('InstallerSha256 must be 64 upper-case hex digits.') }

    foreach ($key in 'Publisher', 'PackageName', 'License', 'ShortDescription', 'PublisherSupportUrl', 'PackageUrl') {
        if (-not $l[$key]) { $problems.Add("defaultLocale: $key is missing.") }
    }
    if ($l['Publisher'] -ne 'Zapador') { $problems.Add('Publisher must be Zapador.') }
    if ($l['PackageName'] -ne 'Shelly HWiNFO Bridge') { $problems.Add("PackageName must be 'Shelly HWiNFO Bridge'.") }
    if ($l['License'] -ne 'MIT') { $problems.Add('License must be MIT.') }
    if ($parsed['defaultLocale'].Text -notmatch '(?m)^Tags:\r?\n- ') { $problems.Add('defaultLocale: Tags are missing.') }
}

if ($problems.Count -gt 0) {
    throw ("winget manifest check failed:`n - " + ($problems -join "`n - "))
}

$mode = if ($canParse) { 'structure and YAML parse' } else { 'structure (powershell-yaml not available, no full parse)' }
Write-Output "winget manifests OK ($mode): $ManifestDir"
