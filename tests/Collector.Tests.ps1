#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\Collect-DeviceInfo.ps1')

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw ('FAIL: ' + $Message) }
}

# Model IDs survive, unique suffixes and Bluetooth addresses do not.
$ids = @(Get-ModelIdentifiers @(
    'USB\VID_045E&PID_028E\SECRET-SERIAL',
    'HID\VID_045E&PID_028E&MI_01\PRIVATE-INSTANCE',
    'PCI\VEN_8086&DEV_1234&SUBSYS_00000000&REV_01\PRIVATE-INSTANCE',
    'ACPI\PNP0C09\PRIVATE-INSTANCE',
    'BTHENUM\DEV_001122334455\PRIVATE-INSTANCE'
))
Assert-True ($ids -contains 'USB\VID_045E&PID_028E') 'USB model ID missing'
Assert-True ($ids -contains 'HID\VID_045E&PID_028E&MI_01') 'HID interface missing'
Assert-True ($ids -contains 'ACPI\PNP0C09') 'EC class ID missing'
Assert-True (($ids -join '|') -notmatch 'SECRET|PRIVATE|001122334455') 'Unique identifier leaked'
Assert-True (@(Get-ModelIdentifiers @($null, '', 'malformed')).Count -eq 0) 'Malformed IDs not rejected'

# Verify that raw CIM data is projected onto an explicit allowlist.
function Get-CimInstance {
    param($Namespace, $ClassName, $Property, $Filter, $OperationTimeoutSec)
    if ($ClassName -eq 'Win32_BIOS') {
        return [pscustomobject]@{
            Manufacturer = 'Fixture AMI'
            SMBIOSBIOSVersion = '5.01'
            SerialNumber = 'PRIVATE-BIOS-SERIAL'
            PSComputerName = 'PRIVATE-COMPUTER'
        }
    }
    if ($ClassName -eq 'Win32_PnPEntity') {
        return [pscustomobject]@{
            PNPClass = 'HIDClass'
            PNPDeviceID = 'HID\VID_045E&PID_028E\PRIVATE-INSTANCE'
            HardwareID = @('HID\VID_045E&PID_028E')
            Name = 'Fixture controller'
            Manufacturer = 'Fixture'
            ConfigManagerErrorCode = 0
        }
    }
    throw 'Unexpected test query'
}
$bios = @(Read-CimFields Win32_BIOS @('Manufacturer', 'SMBIOSBIOSVersion'))
$projected = $bios | ConvertTo-Json
Assert-True ($projected -notmatch 'PRIVATE|SerialNumber|PSComputerName') 'CIM metadata leaked'
$pnp = @(Read-InputAndSystemDevices)
$projected = $pnp | ConvertTo-Json -Depth 5
Assert-True ($projected -notmatch 'PRIVATE|PNPDeviceID|HardwareID') 'PnP raw instance leaked'
Assert-True ($pnp[0].ModelIdentifiers -contains 'HID\VID_045E&PID_028E') 'PnP model was lost'

# Replace all system data sources. No Windows APIs are used by these tests.
function Read-CimFields {
    param($ClassName, $Fields, $Namespace)
    if ($ClassName -eq 'Win32_VideoController') { throw 'PRIVATE-ERROR-MESSAGE' }
    if ($ClassName -eq 'Win32_Battery') { return }
    [pscustomobject]@{ Source = $ClassName; Value = 'fixture' }
}
function Read-InputAndSystemDevices { [pscustomobject]@{ Name = 'fixture controller' } }
function Read-RelevantDrivers { return }
function Read-RelevantServices { return }
function Read-RelevantSoftware { return }
function Get-AppxPackage { param($Name) return }

$report = New-DeviceReport
Assert-True ($report.SchemaVersion -eq 1) 'Wrong schema version'
Assert-True ($report.Sections.GPU.Status -eq 'unavailable') 'Failed source shown as available'
Assert-True ($report.Sections.Battery.Status -eq 'empty') 'Empty source shown as available'
Assert-True ($report.Sections.Devices.Status -eq 'ok') 'Working source was lost'
Assert-True ($report.Warnings.Count -eq 1) 'Missing failure details'
Assert-True ($report.DiscoveryStatus -eq 'partial') 'Partial discovery shown as complete'
Assert-True ($report.MissingRequiredSections -contains 'GPU') 'Failed required source not reported'
$json = $report | ConvertTo-Json -Depth 12
Assert-True ($json -notmatch 'PRIVATE-ERROR-MESSAGE') 'Exception message leaked'
$decoded = $json | ConvertFrom-Json
Assert-True ($decoded.Sections.GPU.Data.Count -eq 0) 'Unavailable source must contain an empty array'
Assert-True ($decoded.Sections.Devices.Data.Count -eq 1) 'Singleton must remain an array'

Write-Host 'PASS: model identifiers, privacy filtering, source failure isolation, partial discovery, JSON arrays.'
