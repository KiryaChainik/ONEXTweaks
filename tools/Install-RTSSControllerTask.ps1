# ONEXTweaks: RTSS requires full administrative rights for its application hooks.
$ErrorActionPreference='Stop'
$exe=Join-Path ${env:ProgramFiles(x86)} 'RivaTuner Statistics Server\RTSS.exe'
if(!(Test-Path $exe)){throw 'Install RTSS from its official Guru3D distribution first.'}
$user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$action=New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
$principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName 'ONEXTweaks-RTSS' -Action $action -Principal $principal -Settings $settings -Force|Out-Null
