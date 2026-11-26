$ErrorActionPreference='Stop'
$dev=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
$local=Join-Path $env:LOCALAPPDATA 'Packages\ONEXTweaks.Widget_3a23eh8tyg7na\LocalState'
try{
 $results=@()
 for($i=1;$i -le 3;$i++){
  Get-Process XboxGamingBar -ErrorAction SilentlyContinue|Where-Object Path -like '*ONEXTweaks.Widget*'|ForEach-Object{Stop-Process -Id $_.Id; $_.WaitForExit(5000)|Out-Null}
  Start-Sleep -Milliseconds 500
  Remove-Item (Join-Path $local 'onex-widget-verification.json') -ErrorAction SilentlyContinue
  Remove-Item (Join-Path $local 'onex-widget-verification-error.txt') -ErrorAction SilentlyContinue
  & (Join-Path $dev 'tools\launch-gamebar-widget.ps1')
  for($attempt=0;$attempt -lt 30;$attempt++){if(Test-Path (Join-Path $local 'onex-widget-verification.json')){break};Start-Sleep -Milliseconds 500}
  if(Test-Path (Join-Path $local 'onex-widget-verification-error.txt')){throw (Get-Content (Join-Path $local 'onex-widget-verification-error.txt') -Raw)}
  $r=Get-Content (Join-Path $local 'onex-widget-verification.json') -Raw|ConvertFrom-Json
  if(!$r.releaseHomeLoaded -or $r.navigationCount -ne 8 -or !$r.combinedHome -or !$r.duplicateCardsHidden -or !$r.connected -or $r.connectionBannerVisible -or $r.releasePl1 -lt 8){throw 'Repeated open failed'}
  $results+=@{attempt=$i;capture=$r}
 }
 @{passed=$true;attempts=$results}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $dev 'Reports\combined-home-reopen-0.15.0.json')
}catch{@{passed=$false;error=$_.ToString();stack=$_.ScriptStackTrace}|ConvertTo-Json|Set-Content (Join-Path $dev 'Reports\combined-home-reopen-0.15.0.json');exit 1}
