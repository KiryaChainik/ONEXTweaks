param([string]$ExpectedVersion='0.15.1.0')
$ErrorActionPreference='Stop'
$local=Join-Path $env:LOCALAPPDATA 'Packages\ONEXTweaks.Widget_3a23eh8tyg7na\LocalState'
$dev=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
try {
 $package=Get-AppxPackage ONEXTweaks.Widget
 if($package.Version.ToString() -ne $ExpectedVersion){throw 'New frontend not installed'}
 Get-Process XboxGamingBar -ErrorAction SilentlyContinue|Where-Object Path -like '*ONEXTweaks.Widget*'|Stop-Process
 Remove-Item (Join-Path $local 'onex-widget-verification-error.txt') -ErrorAction SilentlyContinue
 Remove-Item (Join-Path $local 'onex-widget-power-test.json') -ErrorAction SilentlyContinue
 '{}'|Set-Content (Join-Path $local 'onex-layout-verification-armed.json')
 Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReleaseOpen {
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr ShellExecute(IntPtr w,string op,string f,string a,string d,int s);
}
'@
 $uri='ms-gamebar://launch/activate/'+$package.PackageFamilyName+'_App_GamingWidget?onexVerify=1&onexLayoutTest=1'
 [ReleaseOpen]::ShellExecute([IntPtr]::Zero,'open',$uri,$null,$null,1)|Out-Null
 for($i=0;$i -lt 60;$i++){
  Start-Sleep -Milliseconds 500
  if(Test-Path (Join-Path $local 'onex-widget-verification-error.txt')){throw (Get-Content (Join-Path $local 'onex-widget-verification-error.txt') -Raw)}
  if(Test-Path (Join-Path $local 'onex-widget-power-test.json')){break}
 }
 $power=Get-Content (Join-Path $local 'onex-widget-power-test.json') -Raw|ConvertFrom-Json
 if(!$power.passed -or !$power.rawRestored){throw ($power|ConvertTo-Json -Depth 6)}
 Start-Sleep -Seconds 4
 $layout=Get-Content (Join-Path $local 'onex-widget-verification.json') -Raw|ConvertFrom-Json
 if(!$layout.releaseHomeLoaded -or $layout.navigationCount -ne 8 -or !$layout.horizontalMedia -or $layout.releasePl1 -ne $power.before.pl1Watts){throw 'New layout verification failed'}
 @{passed=$true;power=$power;layout=$layout;sessionId=(Get-Process -Id $PID).SessionId}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $dev 'Reports\release-layout-verification.json')
}catch{@{passed=$false;error=$_.ToString();stack=$_.ScriptStackTrace}|ConvertTo-Json|Set-Content (Join-Path $dev 'Reports\release-layout-verification.json');exit 1}
