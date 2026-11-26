#Requires -Version 5.1
<#
ONEXTweaks hardware discovery. Uses Windows read APIs only.
Run with Windows PowerShell 5.1. Administrator rights are not required.
Dot-source this file to load functions without collecting or writing anything.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'Reports')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ModelIdentifiers {
    param([AllowNull()][string[]] $Identifiers)

    # Preserve model/interface identifiers, never the instance suffix, USB
    # serial number, Bluetooth address or user-specific device instance path.
    $result = @()
    foreach ($identifier in $Identifiers) {
        if ([string]::IsNullOrWhiteSpace($identifier)) { continue }
        $parts = $identifier.ToUpperInvariant().Split([char]92)
        if ($parts.Length -lt 2) { continue }
        $bus = $parts[0]
        if ($bus -notin @('USB', 'HID', 'PCI', 'ACPI')) { continue }
        $tokens = @([regex]::Matches($parts[1], '(?:VID|PID|VEN|DEV|SUBSYS|REV|MI|IG|COL)_[0-9A-F]{2,8}(?=&|$)') |
            ForEach-Object { $_.Value })
        if ($tokens.Count -gt 0) {
            $result += $bus + '\' + ($tokens -join '&')
        }
        elseif ($bus -eq 'ACPI' -and $parts[1] -match '^(?:PNP[0-9A-F]{4}|ACPI[0-9A-F]{4}|INT[0-9A-F]{4}|INTC[0-9A-F]{4})$') {
            $result += 'ACPI\' + $parts[1]
        }
    }
    return @($result | Sort-Object -Unique)
}

function Read-CimFields {
    param(
        [Parameter(Mandatory = $true)][string] $ClassName,
        [Parameter(Mandatory = $true)][string[]] $Fields,
        [string] $Namespace = 'root/cimv2'
    )

    # Explicit field allowlist: no computer name, serial, UUID or user account.
    Get-CimInstance -Namespace $Namespace -ClassName $ClassName -Property $Fields -OperationTimeoutSec 15 |
        ForEach-Object {
            $row = [ordered]@{}
            foreach ($field in $Fields) {
                $value = $_.$field
                if ($value -is [datetime]) { $value = $value.ToUniversalTime().ToString('o') }
                $row[$field] = $value
            }
            [pscustomobject]$row
        }
}

function Read-InputAndSystemDevices {
    $classes = @('HIDClass', 'USB', 'System', 'Display', 'Sensor', 'Battery', 'XboxComposite', 'XnaComposite')
    Get-CimInstance -ClassName Win32_PnPEntity -Filter 'Present=True' -OperationTimeoutSec 20 |
        Where-Object { $_.PNPClass -in $classes -and $_.PNPDeviceID -match '^(USB|HID|PCI|ACPI)\\' } |
        ForEach-Object {
            $ids = @(Get-ModelIdentifiers -Identifiers (@($_.HardwareID) + @($_.PNPDeviceID)))
            # Do not serialize the CIM object: it includes raw instance IDs.
            [pscustomobject][ordered]@{
                Class = $_.PNPClass
                Name = $_.Name
                Manufacturer = $_.Manufacturer
                ModelIdentifiers = $ids
                ErrorCode = $_.ConfigManagerErrorCode
            }
        } | Sort-Object Class, Name
}

function Read-RelevantDrivers {
    Get-CimInstance -ClassName Win32_PnPSignedDriver -OperationTimeoutSec 20 |
        Where-Object {
            $_.DeviceClass -in @('HIDCLASS', 'DISPLAY', 'SENSOR', 'BATTERY', 'USB', 'SYSTEM') -and
            $_.DeviceID -match '^(USB|HID|PCI|ACPI)\\'
        } |
        ForEach-Object {
            [pscustomobject][ordered]@{
                Name = $_.DeviceName
                Class = $_.DeviceClass
                ModelIdentifiers = @(Get-ModelIdentifiers -Identifiers @($_.DeviceID))
                Provider = $_.DriverProviderName
                Version = $_.DriverVersion
                InfName = $_.InfName
                Signed = $_.IsSigned
            }
        } | Sort-Object Class, Name
}

function Read-RelevantServices {
    Get-CimInstance -ClassName Win32_Service -OperationTimeoutSec 15 |
        Where-Object { ($_.Name + ' ' + $_.DisplayName) -match '(?i)onex|one.?netbook|handheld.?companion|hidhide|vigem|viiper|usbip|rtss|rivatuner|motion.?assistant' } |
        Select-Object Name, DisplayName, State, StartMode
}

function Read-RelevantSoftware {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    foreach ($root in $roots) {
        # Missing registry branches are normal, access/other errors are recorded.
        $base = $root.Substring(0, $root.Length - 2)
        if (-not (Test-Path -LiteralPath $base)) { continue }
        Get-ItemProperty -Path $root |
            Where-Object { $_.PSObject.Properties.Name -contains 'DisplayName' } |
            Where-Object { $_.DisplayName -match '(?i)onex|one.?netbook|handheld.?companion|hidhide|vigem|viiper|usbip|rivatuner|RTSS|motion.?assistant|hwinfo|clawtweaks|^steam$|epic games launcher|playnite|lossless scaling' } |
            ForEach-Object {
                $version = $null
                $publisher = $null
                if ($_.PSObject.Properties.Name -contains 'DisplayVersion') { $version = $_.DisplayVersion }
                if ($_.PSObject.Properties.Name -contains 'Publisher') { $publisher = $_.Publisher }
                [pscustomobject][ordered]@{
                    Name = $_.DisplayName
                    Version = $version
                    Publisher = $publisher
                }
            }
    }
}

function New-DeviceReport {
    $sections = [ordered]@{}
    $warnings = New-Object 'System.Collections.Generic.List[object]'
    $readers = [ordered]@{
        System = { Read-CimFields Win32_ComputerSystem @('Manufacturer', 'Model', 'SystemFamily', 'SystemSKUNumber', 'TotalPhysicalMemory') }
        Board = { Read-CimFields Win32_BaseBoard @('Manufacturer', 'Product', 'Version') }
        Bios = { Read-CimFields Win32_BIOS @('Manufacturer', 'SMBIOSBIOSVersion', 'ReleaseDate', 'SMBIOSMajorVersion', 'SMBIOSMinorVersion') }
        OS = { Read-CimFields Win32_OperatingSystem @('Caption', 'Version', 'BuildNumber', 'OSArchitecture') }
        CPU = { Read-CimFields Win32_Processor @('Name', 'Manufacturer', 'NumberOfCores', 'NumberOfLogicalProcessors') }
        GPU = { Read-CimFields Win32_VideoController @('Name', 'DriverVersion', 'CurrentHorizontalResolution', 'CurrentVerticalResolution', 'CurrentRefreshRate') }
        Battery = { Read-CimFields Win32_Battery @('Name', 'BatteryStatus', 'EstimatedChargeRemaining', 'DesignCapacity', 'FullChargeCapacity') }
        Devices = { Read-InputAndSystemDevices }
        Drivers = { Read-RelevantDrivers }
        Services = { Read-RelevantServices }
        Software = { Read-RelevantSoftware | Sort-Object Name, Version -Unique }
        GameBar = { Get-AppxPackage -Name Microsoft.XboxGamingOverlay | Select-Object Name, Version }
    }

    foreach ($name in $readers.Keys) {
        Write-Host ('Reading {0}...' -f $name)
        try {
            $reader = $readers[$name]
            $data = @(& $reader)
            $status = 'ok'
            if ($data.Count -eq 0) { $status = 'empty' }
            $sections[$name] = [ordered]@{ Status = $status; Data = $data }
        }
        catch {
            # Exception messages may include user paths or device instance IDs.
            # Record only the section and exception type, not the message.
            $sections[$name] = [ordered]@{ Status = 'unavailable'; Data = @() }
            $warnings.Add([pscustomobject]@{ Section = $name; ErrorType = $_.Exception.GetType().Name })
        }
    }

    $required = @('System', 'Board', 'Bios', 'OS', 'CPU', 'GPU', 'Devices')
    $missing = @($required | Where-Object { $sections[$_].Status -ne 'ok' })

    return [pscustomobject][ordered]@{
        SchemaVersion = 1
        CollectorVersion = '0.1.0'
        CollectedAtUtc = [datetime]::UtcNow.ToString('o')
        DiscoveryStatus = $(if ($missing.Count -eq 0) { 'complete' } else { 'partial' })
        MissingRequiredSections = $missing
        Sections = [pscustomobject]$sections
        Warnings = @($warnings.ToArray())
    }
}

function Invoke-DeviceCollection {
    param([Parameter(Mandatory = $true)][string] $Directory)

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'This collector requires Windows. No report was created.'
    }
    Write-Host 'ONEXTweaks - hardware discovery 0.1'
    Write-Host 'Read-only queries. No administrator rights required.'
    Write-Host 'Please wait while Windows enumerates devices.'
    $report = New-DeviceReport
    $missing = $report.MissingRequiredSections

    $null = New-Item -Path $Directory -ItemType Directory -Force
    $stamp = [datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $path = Join-Path $Directory ('onextweaks-device-{0}-{1}.json' -f $stamp, $suffix)
    $json = $report | ConvertTo-Json -Depth 12
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($path, $json, $encoding)

    Write-Host ''
    Write-Host ('Report saved: {0}' -f $path)
    if ($report.Sections.Bios.Status -eq 'ok') {
        foreach ($bios in $report.Sections.Bios.Data) {
            Write-Host ('BIOS: {0} {1} ({2})' -f $bios.Manufacturer, $bios.SMBIOSBIOSVersion, $bios.ReleaseDate)
        }
    }
    if ($missing.Count -gt 0) { Write-Warning ('Discovery is partial. Missing: {0}' -f ($missing -join ', ')) }
    if ($report.Warnings.Count -gt 0) {
        Write-Warning ('Unavailable sections: {0}. Details are in the JSON.' -f $report.Warnings.Count)
    }
    return $path
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $null = Invoke-DeviceCollection -Directory $OutputDirectory
    }
    catch {
        Write-Host ('Collection failed: {0}' -f $_.Exception.Message) -ForegroundColor Red
        exit 1
    }
}
