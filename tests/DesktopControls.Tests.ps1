#Requires -Version 5.1
param([ValidateSet('taskManager','explorer','closeWindow','keyboard')] [string]$Action)
$ErrorActionPreference = 'Stop'
$dev=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
$appState=Join-Path $env:LOCALAPPDATA 'Packages\ONEXTweaks.Widget_3a23eh8tyg7na\LocalState'
$report=Join-Path $dev ('Reports\desktop-'+$Action+'.json')
$result=@{passed=$false;action=$Action}
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class OnexDesktopTestLaunch {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr ShellExecute(IntPtr hwnd,string op,string file,string args,string directory,int show);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
}
'@
$shell=New-Object -ComObject Shell.Application
$beforeTask=@(Get-Process Taskmgr -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$beforeExplorer=@($shell.Windows() | ForEach-Object { [long]$_.HWND })
$beforeOsk=@(Get-Process osk -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Id)
$probe=$null
try {
    Get-Process XboxGamingBar -ErrorAction SilentlyContinue | Where-Object {$_.Path -like '*ONEXTweaks.Widget*'} | Stop-Process
    Remove-Item (Join-Path $appState 'onex-desktop-tile-invoked.json') -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $dev 'Reports\desktop-action.json') -ErrorAction SilentlyContinue
    if($Action -eq 'keyboard') {
        Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TouchKeyboard.Native.cs')))
        $result.beforeTouch=[OnexNativeTouchKeyboard]::Read()
        [OnexNativeTouchKeyboard]::HideForTest()
        if([OnexNativeTouchKeyboard]::Read().visible){throw 'Keyboard must be hidden before invoking the tile'}
    }
    $arm=@{action=$Action}
    if ($Action -eq 'closeWindow') {
        $probeExe=Join-Path $dev 'tools\ONEXTweaks.CloseProbe.exe'
        & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (('/out:')+$probeExe) (Join-Path $PSScriptRoot 'CloseProbe.cs')
        if ($LASTEXITCODE) { throw 'Close probe compilation failed' }
        $closed=Join-Path $dev 'Reports\close-probe.json'
        Remove-Item $closed -ErrorAction SilentlyContinue
        $probe=Start-Process $probeExe -ArgumentList ('"'+$closed+'"') -PassThru
        $arm.expectedTargetApp='ONEXTweaks.CloseProbe'
        Start-Sleep -Seconds 1
    }
    $arm | ConvertTo-Json | Set-Content (Join-Path $appState 'onex-desktop-verification-armed.json')
    $package=Get-AppxPackage ONEXTweaks.Widget
    $uri='ms-gamebar://launch/activate/'+$package.PackageFamilyName+'_App_GamingWidget?onexVerify=1&onexDesktopTest='+$Action
    [OnexDesktopTestLaunch]::ShellExecute([IntPtr]::Zero,'open',$uri,$null,$null,1) | Out-Null
    $backend=Join-Path $dev 'Reports\desktop-action.json'
    for($i=0;$i -lt 150 -and !(Test-Path $backend);$i++){Start-Sleep -Milliseconds 100}
    if (!(Test-Path $backend)) { throw 'No desktop action result arrived from the widget' }
    $reply=Get-Content $backend -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$reply.result.ok -or $reply.result.action -ne $Action) { throw ('Backend action failed: '+$reply.result.error) }
    if($Action -in @('taskManager','explorer')){
        if(!$reply.result.foregroundVerified){throw 'Launch did not verify foreground focus'}
        Start-Sleep -Milliseconds 500
        $front=[OnexDesktopTestLaunch]::GetForegroundWindow();$frontPid=[uint32]0;$null=[OnexDesktopTestLaunch]::GetWindowThreadProcessId($front,[ref]$frontPid)
        $frontName=(Get-Process -Id $frontPid).ProcessName
        if($frontName -ne $(if($Action -eq 'taskManager'){'Taskmgr'}else{'explorer'})){throw ('Focus did not remain on launched window: '+$frontName)}
        $result.foregroundVerified=$true;$result.foregroundProcess=$frontName;$result.targetWindow=$reply.result.targetWindow
    }
    $tile=Get-Content (Join-Path $appState 'onex-desktop-tile-invoked.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$tile.invoked -or !$tile.isGameBar) { throw 'Original Game Bar tile was not invoked' }
    if($Action -eq 'taskManager') {
        for($i=0;$i -lt 80 -and !(Get-Process Taskmgr -ErrorAction SilentlyContinue);$i++){Start-Sleep -Milliseconds 100}
        if(!(Get-Process Taskmgr -ErrorAction SilentlyContinue)){throw 'Task Manager did not open'}
        $result.processObserved=$true
    } elseif($Action -eq 'explorer') {
        $opened=@()
        for($i=0;$i -lt 80 -and !$opened.Count;$i++) { $opened=@($shell.Windows() | Where-Object {$beforeExplorer -notcontains [long]$_.HWND}); if(!$opened.Count){Start-Sleep -Milliseconds 100} }
        if(!$opened.Count){throw 'No new File Explorer window appeared'}
        $result.newExplorerWindows=$opened.Count
    } elseif($Action -eq 'keyboard') {
        if(!$reply.result.keyboard.visible -or !$reply.result.keyboard.docked){throw 'Windows touch keyboard did not dock at the bottom'}
        if($reply.result.targetApp -ne 'Windows touch keyboard'){throw 'The keyboard action used a classic OSK fallback'}
        $result.keyboardWindowObserved=$true
        $result.touchKeyboard=$reply.result.keyboard
        $result.nativeTouch=[OnexNativeTouchKeyboard]::Read()
        if(!$result.nativeTouch.visible -or !$result.nativeTouch.docked){throw 'Independent native read did not confirm a bottom-docked touch keyboard'}
        $result.classicOskUsed=$false
    } else {
        for($i=0;$i -lt 80 -and !(Test-Path $closed);$i++){Start-Sleep -Milliseconds 100}
        if(!(Test-Path $closed)){throw 'The dedicated target window did not close'}
        $closure=Get-Content $closed -Raw | ConvertFrom-Json
        if(!$closure.closed -or !$closure.wmClose -or $reply.result.targetApp -ne 'ONEXTweaks.CloseProbe'){throw 'Close action did not target the dedicated window'}
        $result.wmCloseObserved=$true
        $result.gameBarPreserved=[bool](Get-Process GameBar -ErrorAction SilentlyContinue)
    }
    if($Action -eq 'closeWindow'){
        if(!$tile.cancelledFirst -or !$tile.confirmationShown){throw 'Close confirmation and cancellation were not verified'}
        $result.cancelledFirst=$tile.cancelledFirst;$result.confirmationShown=$tile.confirmationShown
    }
    $result.packageVersion=$package.Version.ToString()
    $result.tile=$tile.tile
    $result.passed=$true
} catch { $result.error=$_.ToString() }
finally {
    Remove-Item (Join-Path $appState 'onex-desktop-verification-armed.json') -ErrorAction SilentlyContinue
    if($Action -eq 'taskManager') { Get-Process Taskmgr -ErrorAction SilentlyContinue | Where-Object {$beforeTask -notcontains $_.Id} | ForEach-Object {$_.CloseMainWindow() | Out-Null} }
    if($Action -eq 'explorer') { $shell.Windows() | Where-Object {$beforeExplorer -notcontains [long]$_.HWND} | ForEach-Object {$_.Quit()} }
    if($Action -eq 'keyboard'){Get-Process osk -ErrorAction SilentlyContinue|Where-Object{$beforeOsk -notcontains $_.Id}|ForEach-Object{$_.CloseMainWindow()|Out-Null}}
    if($probe -and !$probe.HasExited){$probe.CloseMainWindow() | Out-Null}
    $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $report
}
