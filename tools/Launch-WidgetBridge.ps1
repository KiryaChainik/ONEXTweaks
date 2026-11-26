#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'ONEXTweaks'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $root 'dev\project\src\ONEXTweaks.WidgetBridge\bin\Release\net10.0-windows\ONEXTweaks.WidgetBridge.exe'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $root 'toolchain\dotnet-10.0.403'
$start.EnvironmentVariables['DOTNET_ROOT_X64'] = $start.EnvironmentVariables['DOTNET_ROOT']
$process = [Diagnostics.Process]::Start($start)
$process.WaitForExit()
exit $process.ExitCode
