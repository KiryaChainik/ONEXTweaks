$ErrorActionPreference='Stop'
$p=Get-AppxPackage ONEXTweaks.Widget
$local=Join-Path $env:LOCALAPPDATA ('Packages\'+$p.PackageFamilyName+'\LocalState')
New-Item $local -ItemType Directory -Force|Out-Null
Get-Process XboxGamingBar -ErrorAction SilentlyContinue|Where-Object{$_.Path -like '*ONEXTweaks.Widget*'}|ForEach-Object{Stop-Process -Id $_.Id;$_.WaitForExit(5000)|Out-Null}
Start-Sleep -Milliseconds 500
Remove-Item (Join-Path $local 'onex-focus-verification.json') -ErrorAction SilentlyContinue
'{}'|Set-Content (Join-Path $local 'onex-focus-verification-armed.json')
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class OpenFocusTest{[DllImport("shell32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr ShellExecute(IntPtr a,string b,string c,string d,string e,int f);}'
$uri='ms-gamebar://launch/activate/'+$p.PackageFamilyName+'_App_GamingWidget?onexVerify=1&onexFocusTest=1'
$x=[OpenFocusTest]::ShellExecute([IntPtr]::Zero,'open',$uri,$null,$null,1)
if($x.ToInt64() -le 32){throw 'Launch failed'}
for($i=0;$i -lt 50;$i++){if(Test-Path (Join-Path $local 'onex-focus-verification.json')){break};Start-Sleep -Seconds 1}
Copy-Item (Join-Path $local 'onex-focus-verification.json') (Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev\Reports\stats-compact-verification-0.15.1.json') -Force
