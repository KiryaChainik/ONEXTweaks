#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root=Join-Path $env:LOCALAPPDATA 'ONEXTweaks'
$launcher=Join-Path $root 'CompanionLauncher.exe'
if(!(Test-Path -LiteralPath $launcher)){throw 'Install the companion launcher first (Install-Startup.ps1).'}
$process=Start-Process -FilePath $launcher -PassThru
try{if(!$process.WaitForExit(20000) -or $process.ExitCode){throw 'ONEXTweaks companions did not start.'}}finally{$process.Dispose()}
$signal=$null
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do{
        try{
            $ready=Get-Content (Join-Path $root 'dev\Reports\widget-ready.json') -Raw|ConvertFrom-Json
            $bridge=Get-Process ONEXTweaks.WidgetBridge -ErrorAction Stop
            if($ready.ready -and @($bridge|Where-Object Id -eq $ready.pid).Count){$signal=[Threading.EventWaitHandle]::OpenExisting('Local\ONEXTweaks.ToggleOverlay.v1');break}
        }catch{}
        Start-Sleep -Milliseconds 100
    }while([DateTime]::UtcNow -lt $deadline)
    if(!$signal){throw 'The widget did not finish background startup in this session.'}
    # One activation queue, including its open/close state. No competing URI.
    $null=$signal.Set()
}finally{if($signal){$signal.Dispose()}}
