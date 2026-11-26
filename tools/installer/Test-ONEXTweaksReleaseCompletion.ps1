#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Complete-ONEXTweaksRelease.ps1')

$package = [pscustomobject]@{ PackageFullName='ONEXTweaks.Widget_0.29.1110.0_x64__0123456789abc';
    PackageFamilyName='ONEXTweaks.Widget_0123456789abc'; Architecture='X64'; Status='Ok' }
$plan = [pscustomobject]@{ Receipt=$package.PSObject.Copy(); Tasks=@{
    'ONEXTweaks-Startup'=$true; 'ONEXTweaks-WidgetBridge'=$true; 'ONEXTweaks-ControllerInputRecovery'=$true;
    'ONEXTweaks-RTSS'=$true; 'ONEXTweaks-PowerAgent'=$true } }
$runtime = [pscustomobject]@{ Backend='Healthy'; RtssTask='Ready'; RtssSession='Running' }
$components = @([pscustomobject]@{ Name='usbip'; Status='Installed' }, [pscustomobject]@{ Name='OEM'; Status='Not applicable' })

function Assert-Readiness($Expected, [bool]$Hardware = $true, [bool]$Restart = $false) {
    $actual = Get-ONEXTweaksReleaseReadiness $plan $package $runtime $components $Hardware $Restart
    if ($actual -cne $Expected) { throw "Expected '$Expected', got '$actual'" }
}
Assert-Readiness 'Ready'
$runtime.Backend = 'PowerAgent hardware readback unavailable'
Assert-Readiness $runtime.Backend
$runtime.Backend = 'Healthy'
$runtime.RtssTask = 'Missing'
Assert-Readiness 'RTSS startup task unconfirmed'
$runtime.RtssTask = 'Ready'
$runtime.RtssSession = 'Unavailable'
Assert-Readiness 'RTSS is not running in the widget owner session'
$runtime.RtssSession = 'Running'
Assert-Readiness 'Windows restart required' $true $true
$components[0].Status = 'Driver service reported error 577'
Assert-Readiness 'Prerequisite verification incomplete'
$components[0].Status = 'Installed'
$plan.Tasks.Remove('ONEXTweaks-PowerAgent')
Assert-Readiness 'Release task is not owned: ONEXTweaks-PowerAgent'
Assert-Readiness 'Ready' $false
$package.Status = 'Modified'
Assert-Readiness 'Widget registration unconfirmed' $false
'PASS: release completion requires the owner widget, backend, RTSS, prerequisite files and restart clearance'
