#Requires -Version 5.1
param([string] $VerifyDirectory, [switch] $ExercisePower)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'ONEXTweaks'
$dotnet = Join-Path $base 'toolchain\dotnet-10.0.403\dotnet.exe'
$app = Join-Path $base 'dev\project\src\ONEXTweaks.Panel\bin\Release\net10.0-windows\ONEXTweaks.Panel.exe'
if (-not (Test-Path -LiteralPath $dotnet) -or -not (Test-Path -LiteralPath $app)) {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('ONEXTweaks development build is not installed in this profile.', 'ONEXTweaks') | Out-Null
    exit 1
}
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $app
if ($VerifyDirectory) { $start.Arguments = '--verify "{0}"' -f $VerifyDirectory }
if ($ExercisePower) { $start.Arguments += ' --exercise-power' }
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.EnvironmentVariables['DOTNET_ROOT'] = Split-Path -Parent $dotnet
$start.EnvironmentVariables['DOTNET_ROOT_X64'] = Split-Path -Parent $dotnet
$process = [Diagnostics.Process]::Start($start)
$process.WaitForExit()
exit $process.ExitCode
