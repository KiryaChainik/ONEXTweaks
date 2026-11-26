param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$source=Join-Path $project 'src\ONEXTweaks.LedCalibrator'
if(!$OutputDirectory){$OutputDirectory=Join-Path $project 'artifacts\LedCalibrator'}
$null=New-Item -ItemType Directory $OutputDirectory -Force
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path $compiler)){throw '.NET Framework compiler is required'}
$files=@('CalibrationCore.cs','BridgeClient.cs','CalibrationSession.cs','CalibratorText.cs','MainForm.cs')|ForEach-Object{(Resolve-Path (Join-Path $source $_)).Path}
$exe=Join-Path $OutputDirectory 'ONEXTweaksLedCalibrator.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /langversion:5 /codepage:65001 /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ('/out:'+$exe) $files
if($LASTEXITCODE){throw 'Calibration app compilation failed'}
Copy-Item (Join-Path $source 'ONEXTweaksLedCalibrator.exe.config') ($exe+'.config') -Force
$testExe=Join-Path $OutputDirectory 'LedCalibrationTests.exe'
$testSource=(Resolve-Path (Join-Path $project 'tests\LedCalibration\Program.cs')).Path
& $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /langversion:5 /codepage:65001 ('/out:'+$testExe) $files[0] $testSource
if($LASTEXITCODE){throw 'Calibration model compilation failed'}
& $testExe
if($LASTEXITCODE){throw 'Calibration model checks failed'}
Write-Output $exe
