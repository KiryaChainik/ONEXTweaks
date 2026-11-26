#Requires -Version 5.1
param([switch]$Watchdog)
$ErrorActionPreference = 'Stop'
if ((Get-Process -Id $PID).SessionId -eq 0) { throw 'Run this test in the interactive user session.' }
Add-Type -Path (Join-Path $PSScriptRoot 'MediaControls.Native.cs')
$dev = Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
$reports = Join-Path $dev 'Reports'
$appState = Join-Path $env:LOCALAPPDATA 'Packages\ONEXTweaks.Widget_3a23eh8tyg7na\LocalState'
$baselineFile = Join-Path $reports 'media-test-baseline.json'
$doneFile = Join-Path $reports 'media-test-restored.json'
function Set-Brightness([int]$value) {
    $methods = @(Get-CimInstance -Namespace root/wmi -Class WmiMonitorBrightnessMethods | Where-Object Active)
    if ($methods.Count -ne 1) { throw 'Expected one active internal brightness provider' }
    $reply = Invoke-CimMethod -InputObject $methods[0] -MethodName WmiSetBrightness -Arguments @{Timeout=[uint32]0;Brightness=[byte]$value}
    if ($null -ne $reply.ReturnValue -and $reply.ReturnValue -ne 0) { throw 'Independent WMI brightness write failed' }
}
function Get-Brightness { [int](Get-CimInstance -Namespace root/wmi -Class WmiMonitorBrightness | Where-Object Active | Select-Object -ExpandProperty CurrentBrightness) }
if ($Watchdog) {
    Start-Sleep -Seconds 60
    if (!(Test-Path $doneFile) -and (Test-Path $baselineFile)) {
        $saved = Get-Content $baselineFile -Raw | ConvertFrom-Json
        $audio = New-Object MediaControlProbe
        try { Set-Brightness $saved.brightness; $audio.SetVolume([float]$saved.volumeScalar); $audio.SetMuted([bool]$saved.muted) }
        finally { $audio.Dispose() }
        @{watchdogRestored=$true} | ConvertTo-Json | Set-Content (Join-Path $reports 'media-watchdog.json')
    }
    return
}
$audio = New-Object MediaControlProbe
$before = @{brightness=(Get-Brightness); volumeScalar=$audio.Volume(); muted=$audio.Muted()}
$before | ConvertTo-Json | Set-Content $baselineFile
Remove-Item $doneFile -ErrorAction SilentlyContinue
foreach ($name in @('onex-media-external-ready.json','onex-media-external-applied.json','onex-media-verification.json')) { Remove-Item (Join-Path $appState $name) -ErrorAction SilentlyContinue }
$action = New-ScheduledTaskAction -Execute powershell.exe -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Watchdog')
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
try {
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 3)
    Register-ScheduledTask ONEXTweaks-Media-Restore -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    Start-ScheduledTask ONEXTweaks-Media-Restore
} catch {
    @{passed=$false;error=$_.ToString();restored=$true} | ConvertTo-Json | Set-Content (Join-Path $reports 'media-os-verification.json')
    $audio.Dispose()
    return
}
@{armed=$true} | ConvertTo-Json | Set-Content (Join-Path $appState 'onex-media-verification-armed.json')
$result = @{passed=$false;before=$before;sessionId=(Get-Process -Id $PID).SessionId;isAdmin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}
try {
    $ready = Join-Path $appState 'onex-media-external-ready.json'
    for ($i=0; $i -lt 400 -and !(Test-Path $ready); $i++) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path $ready)) { throw 'Widget did not reach external synchronization verification' }
    $externalBrightness = if ($before.brightness -gt 3) {$before.brightness-2} else {$before.brightness+1}
    $percent = [int][Math]::Round($before.volumeScalar*100)
    $externalVolume = if ($percent -gt 3) {$percent-3} else {$percent+1}
    Set-Brightness $externalBrightness
    $audio.SetVolume([float]($externalVolume/100.0))
    $audio.SetMuted($true)
    $actualBrightness = Get-Brightness
    if ($actualBrightness -ne $externalBrightness -or [Math]::Abs($audio.Volume()*100-$externalVolume) -gt .02) { throw 'Independent OS readback did not confirm the external changes' }
    @{brightness=$externalBrightness;volume=$externalVolume;muted=$true} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $appState 'onex-media-external-applied.json')
    $verification = Join-Path $appState 'onex-media-verification.json'
    for ($i=0; $i -lt 200 -and !(Test-Path $verification); $i++) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path $verification)) { throw 'Widget media verification did not finish' }
    $ui = Get-Content $verification -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$ui.passed -or !$ui.noWriteEcho -or !$ui.externalSyncPassed) { throw ('Widget verification failed: ' + $ui.error) }
    $result.externalBrightnessReadback = $actualBrightness
    $result.externalVolumeReadback = $audio.Volume()*100
    $result.uiVerificationPassed = $true
    $result.passed = $true
} catch { $result.error = $_.ToString() }
finally {
    try {
        Set-Brightness $before.brightness
        $audio.SetVolume([float]$before.volumeScalar)
        $audio.SetMuted([bool]$before.muted)
        Start-Sleep -Milliseconds 300
        $result.after = @{brightness=(Get-Brightness);volumeScalar=$audio.Volume();muted=$audio.Muted()}
        $result.restored = $result.after.brightness -eq $before.brightness -and [Math]::Abs($result.after.volumeScalar-$before.volumeScalar) -lt .000001 -and $result.after.muted -eq $before.muted
        $result.passed = $result.passed -and $result.restored
        if ($result.restored) {
            @{restored=$true} | ConvertTo-Json | Set-Content $doneFile
            Stop-ScheduledTask ONEXTweaks-Media-Restore
            Unregister-ScheduledTask ONEXTweaks-Media-Restore -Confirm:$false
        }
    } catch { $result.restoreError=$_.ToString(); $result.passed=$false }
    $audio.Dispose()
    Remove-Item (Join-Path $appState 'onex-media-verification-armed.json') -ErrorAction SilentlyContinue
    $result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $reports 'media-os-verification.json')
}
