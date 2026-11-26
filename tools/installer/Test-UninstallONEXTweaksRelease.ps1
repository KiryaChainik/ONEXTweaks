$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
function Check($Condition,$Message) { if (!$Condition) { throw $Message } }
function Reject([scriptblock]$Block,[string]$Expected) {
    try { & $Block | Out-Null } catch { if ($_.Exception.Message -like "*$Expected*") { return }; throw }
    throw "Expected failure containing '$Expected'."
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
$state = Join-Path $temp 'ONEXTweaks/Installation'
$root = Join-Path $temp 'Programs/ONEXTweaks'
$local = Join-Path $temp 'User'
$owned = Join-Path $root 'WidgetBridge/owned.dll'
$shared = Join-Path $root 'WidgetBridge/other.dll'
$receiptPath = Join-Path $state 'owned-release.json'
$lease = Join-Path $local 'ONEXTweaks/dev/privateReports/controller-capture-lease.json'
$sid = 'S-1-5-21-100-200-300-1001'
$family = 'ONEXTweaks.Widget_3a23eh8tyg7na'
$full = 'ONEXTweaks.Widget_0.29.100.0_x64__3a23eh8tyg7na'
$f = @{ Running=$true; Enabled=$true; Task=$true; Package=$true; Marker=''; TaskReads=0; Stops=0; Restores=0; Unregisters=0; Author='installer';
    MaintenanceActive=$false; MaintenanceStarts=0; MaintenanceStops=0; ParentKey=$false; ReceiptKey=$false; TransientReads=0; TransientOmission=$false;
    RuntimeDeleteBlocks=0; RuntimeDeleteAttempts=0; StopCountdown=0 }
$comTask = [pscustomobject]@{Fixture=$f}
Add-Member -InputObject $comTask -MemberType ScriptProperty -Name Enabled -Value { $this.Fixture.Enabled } -SecondValue {
    $this.Fixture.Enabled = $args[0]
}
Add-Member -InputObject $comTask -MemberType ScriptMethod -Name GetInstances -Value {
    param([int]$Flags)
    Check ($Flags -eq 0) 'Unexpected task instance flags.'
    if ($this.Fixture.StopCountdown -gt 0) {
        $this.Fixture.StopCountdown--
        if (!$this.Fixture.StopCountdown) { $this.Fixture.Running = $false }
    }
    [pscustomobject]@{Count=[int]$this.Fixture.Running}
}
Add-Member -InputObject $comTask -MemberType ScriptMethod -Name Stop -Value {
    param([int]$Flags)
    Check ($Flags -eq 0 -and !$this.Fixture.Enabled) 'Stop was requested before disabling the verified task.'
    $this.Fixture.Stops++
    $this.Fixture.StopCountdown = 2
}
function Get-ONEXTweaksTaskFolder { 'Owned test folder' }
function Get-ONEXTweaksComOwnedTask($Plan,$Folder,$Name) {
    Check ($Folder -ceq 'Owned test folder' -and $Name -ceq 'ONEXTweaks-WidgetBridge' -and
        (Get-ONEXTweaksReleaseTaskDigest $Name) -ceq $Plan.Tasks[$Name].DefinitionSha256) 'Unverified COM task.'
    if ($f.Task) { $comTask }
}
function Assert-ONEXTweaksProtectedItem($Path) { Check ($Path.StartsWith($temp) -and (Test-Path -LiteralPath $Path)) 'Unexpected fixture protection check.' }
function Export-ScheduledTask($TaskPath,$TaskName) {
    Check ($TaskPath -eq '\' -and $TaskName -ceq 'ONEXTweaks-WidgetBridge') 'Unexpected task export.'
    $flag = ([string]$f.Enabled).ToLowerInvariant()
    $author = $f.Author
    if ($f.TransientReads -gt 0) {
        $f.TransientReads--
        if ($f.TransientReads -eq 1) { throw [IO.FileNotFoundException]::new('Task export briefly unavailable.') }
        $author = 'transient observation'
    }
    "<Task xmlns='http://schemas.microsoft.com/windows/2004/02/mit/task'><RegistrationInfo><Author>$author</Author></RegistrationInfo><Settings><Enabled>$flag</Enabled></Settings></Task>"
}
function Get-ScheduledTask($TaskPath) {
    Check ($TaskPath -eq '\') 'Unexpected folder.'
    $f.TaskReads++
    if ($f.TransientOmission) { $f.TransientOmission=$false; return }
    if ($f.Task) {
        [pscustomobject]@{ TaskName='ONEXTweaks-WidgetBridge'; TaskPath='\'; State=$(if(!$f.Enabled){'Disabled'}elseif($f.Running){'Running'}else{'Ready'});
            Settings=[pscustomobject]@{ Enabled=$f.Enabled } }
    }
}
function Test-ONEXTweaksRegisteredTask($Name) { Check ($Name -ceq 'ONEXTweaks-WidgetBridge') 'Unexpected COM path.'; return $f.Task }
function Stop-ScheduledTask($InputObject) { Check ($InputObject.TaskName -ceq 'ONEXTweaks-WidgetBridge') 'Unexpected stop.'; $f.Stops++; $f.Running=$false }
function Disable-ScheduledTask($InputObject) { Check ($InputObject.TaskName -ceq 'ONEXTweaks-WidgetBridge') 'Unexpected disable.'; $f.Enabled=$false }
function Unregister-ScheduledTask($InputObject,[switch]$Confirm) {
    Check (!$Confirm -and $InputObject.TaskName -ceq 'ONEXTweaks-WidgetBridge' -and $f.Restores -eq 1 -and !$f.Running) 'Unverified unregister.'
    $f.Unregisters++; $f.Task=$false
}
function Get-AppxPackage($Name,$User) {
    Check ($Name -ceq 'ONEXTweaks.Widget' -and (!$User -or $User -ceq $sid)) 'Unexpected AppX query.'
    if ($f.Package) { [pscustomobject]@{PackageFullName=$f.PackageName;PackageFamilyName=$family} }
}
function Remove-AppxPackage($Package,[switch]$Confirm) {
    Check (!$Confirm -and $Package -ceq $full -and !$f.MaintenanceActive -and $f.ReceiptKey) 'Unexpected package removal.'
    $f.Package = $false
}
function Open-ONEXTweaksPackageMaintenance($PackageFullName) {
    Check ($PackageFullName -ceq $full -and !$f.MaintenanceActive) 'Package maintenance targeted an unrelated app.'
    $f.MaintenanceActive = $true
    $f.MaintenanceStarts++
    $session = [pscustomobject]@{ Fixture=$f }
    Add-Member -InputObject $session -MemberType ScriptMethod -Name Dispose -Value {
        $this.Fixture.MaintenanceActive = $false
        $this.Fixture.MaintenanceStops++
    }
    return $session
}
function New-Item($Path,[switch]$Force) {
    if ($Path -ceq 'HKCU:\Software\ONEXTweaks') { $f.ParentKey = $true; return }
    Check ($Path -ceq 'HKCU:\Software\ONEXTweaks\Installation' -and $f.ParentKey) 'Unexpected registry write or missing parent.'
    $f.ReceiptKey = $true
}
function New-ItemProperty($LiteralPath,$Name,$PropertyType,$Value,[switch]$Force) {
    Check ($LiteralPath -ceq 'HKCU:\Software\ONEXTweaks\Installation' -and $Name -ceq 'OwnerRemovalReceipt' -and $f.ReceiptKey) 'Wrong owner registry hive.'
    $f.Marker = $Value
}
function Get-ItemProperty($LiteralPath,$Name) {
    Check ($LiteralPath -in @("Registry::HKEY_USERS\$sid\Software\ONEXTweaks\Installation",'HKCU:\Software\ONEXTweaks\Installation')) 'Registry read for unrelated owner.'
    [pscustomobject]@{ OwnerRemovalReceipt=$f.Marker }
}
function Invoke-ONEXTweaksOneXConsoleHandoff($Operation,$StateDirectory,$UserSid) {
    Check ($Operation -eq 'Restore' -and $UserSid -eq $sid -and $StateDirectory -eq $state -and (Test-Path -LiteralPath $receiptPath)) 'Invalid OEM restoration.'
    if ($f.FailRestore) { throw 'Changed OEM task' }
    $f.Restores++
    $f.TransientReads = 2
    $f.TransientOmission = $true
    return 'Restored'
}
function Test-Path($LiteralPath,$PathType='Any') {
    if ($LiteralPath -ceq 'HKCU:\Software\ONEXTweaks') { return $f.ParentKey }
    if ($LiteralPath -ceq 'HKCU:\Software\ONEXTweaks\Installation') { return $f.ReceiptKey }
    Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType -ErrorAction Stop
}
function Remove-Item($LiteralPath,[switch]$Force) {
    Check ($LiteralPath -ceq $owned -and $Force) 'Deletion attempted outside the owned runtime file.'
    $f.RuntimeDeleteAttempts++
    if ($f.RuntimeDeleteBlocks -gt 0) {
        $f.RuntimeDeleteBlocks--
        throw [UnauthorizedAccessException]::new('The fixture runtime image is still mapped.')
    }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
}
try {
    foreach ($folder in @($state,(Split-Path $owned),(Split-Path $lease))) { [void][IO.Directory]::CreateDirectory($folder) }
    [IO.File]::WriteAllText($owned,'owned')
    [IO.File]::WriteAllText($shared,'foreign')
    $data = Join-Path $local "Packages/$family/LocalState/profile.json"
    [void][IO.Directory]::CreateDirectory((Split-Path $data))
    [IO.File]::WriteAllText($data,'keep the profile')
    $f.PackageName = $full
    $manifest = [ordered]@{ Schema=1; InstallId=[guid]::NewGuid().ToString(); OwnerSid=$sid; PackageOwned=$true;
        GuideOwned=$false; PackageFullName=$full; PackageFamilyName=$family;
        Files=@(@{Path='WidgetBridge\owned.dll';Sha256=(Get-FileHash -LiteralPath $owned).Hash});
        Tasks=@(@{Name='ONEXTweaks-WidgetBridge';DefinitionSha256=(Get-ONEXTweaksReleaseTaskDigest 'ONEXTweaks-WidgetBridge')}) }
    [IO.File]::WriteAllText($receiptPath,($manifest | ConvertTo-Json -Depth 5))
    $plan = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly
    Check ($f.TaskReads -eq 0) 'Owner phase accessed privileged scheduler.'
    Reject { Get-ONEXTweaksOwnedFile $root @{Path='..\outside';Sha256=('a'*64)} } 'Unsafe release file'
    Reject { Get-ONEXTweaksOwnedFile $root @{Path='WidgetBridge\owned.dll:stream';Sha256=('a'*64)} } 'Unsafe release file'
    $f.Author = 'changed'
    Reject { Get-ONEXTweaksUninstallPlan $state $root } 'task was replaced'
    $f.Author = 'installer'
    [IO.File]::WriteAllText($owned,'changed')
    Reject { Get-ONEXTweaksUninstallPlan $state $root } 'file was replaced'
    [IO.File]::WriteAllText($owned,'owned')
    $plan = Get-ONEXTweaksUninstallPlan $state $root
    Reject { Invoke-ONEXTweaksMachineRemoval $plan $lease } 'owner removal phase'
    $f.PackageName = $full.Replace('100.0','101.0')
    Reject { Invoke-ONEXTweaksOwnerRemoval $plan $local $false } 'registration has changed'
    $f.PackageName = $full
    [IO.File]::WriteAllText($lease,'active')
    Reject { Invoke-ONEXTweaksOwnerRemoval $plan $local $false } 'Controller capture is active'
    Check ($f.Package -and !$f.Marker) 'Widget recovery UI was removed during active capture.'
    [IO.File]::Delete($lease)
    $backup = Invoke-ONEXTweaksOwnerRemoval $plan $local $false
    Check (!$f.Package -and $f.Marker -ceq $plan.Digest -and ([IO.File]::ReadAllText((Join-Path $backup 'LocalState/profile.json')) -ceq 'keep the profile')) 'Owner package/data phase failed.'
    Check ($null -eq (Invoke-ONEXTweaksOwnerRemoval $plan $local $false)) 'Owner retry disturbed the backup.'
    Check ($f.MaintenanceStarts -eq 1 -and $f.MaintenanceStops -eq 1) 'Widget backup did not release its package maintenance session.'
    $f.Package = $true
    Reject { Invoke-ONEXTweaksMachineRemoval $plan $lease } 'still has this widget'
    $f.Package = $false
    [IO.File]::WriteAllText($lease,'active')
    Reject { Invoke-ONEXTweaksMachineRemoval $plan $lease } 'Controller capture is active'
    Check ($f.Stops -eq 0 -and $f.Restores -eq 0 -and $f.Task -and $f.Enabled -and (Test-Path -LiteralPath $owned)) 'Capture guard allowed mutation.'
    & {
        $recoveryPlan = [pscustomobject]@{ Root=$root; Receipt=$plan.Receipt; Tasks=@{'ONEXTweaks-ControllerInputRecovery'=@{}} }
        $recovery = [pscustomobject]@{ Principal=[pscustomobject]@{RunLevel='Limited';UserId=$sid};
            Actions=@([pscustomobject]@{ Execute=(Join-Path $root 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'); Arguments='--controller-login-recovery' }) }
        function Get-ONEXTweaksRemainingTask($Plan,$Name) { Check ($Name -ceq 'ONEXTweaks-ControllerInputRecovery') 'Wrong recovery task.'; $recovery }
        function Test-ONEXTweaksUserPrincipal($Task,$UserSid) { $Task.Principal.UserId -ceq $UserSid }
        function Get-ONEXTweaksVerifiedTool($Plan,$Relative) { Check ($Relative -ceq 'WidgetBridge\ONEXTweaks.WidgetBridge.exe') 'Unexpected recovery file.' }
        function Start-ScheduledTask($InputObject) { Check ([object]::ReferenceEquals($recovery,$InputObject)) 'Unexpected recovery action.'; [IO.File]::Delete($lease) }
        $recovery.Principal.RunLevel='Highest'
        Reject { Assert-ONEXTweaksReleasedController $recoveryPlan $lease } 'Controller capture is active'
        $recovery.Principal.RunLevel='Limited'
        Assert-ONEXTweaksReleasedController $recoveryPlan $lease
        Check (!(Test-Path -LiteralPath $lease)) 'Receipt-owned low-privilege recovery did not release its capture.'
    }
    # Model an earlier uninstall stopping after Disable while the instance still runs.
    $f.Enabled = $false
    $f.FailRestore = $true
    Reject { Invoke-ONEXTweaksMachineRemoval $plan $lease } 'Changed OEM task'
    Check ($f.Stops -eq 1 -and !$f.Running -and $f.Unregisters -eq 0 -and
        (Test-Path -LiteralPath $owned) -and (Test-Path -LiteralPath $receiptPath)) 'Disabled task kept running or an OEM conflict lost recovery state.'
    $f.FailRestore = $false
    & {
        $read = @{Attempts=0}
        function Get-FileHash($LiteralPath,$Algorithm) {
            $read.Attempts++
            if ($read.Attempts -eq 1) { throw [IO.IOException]::new('File verification is temporarily blocked.', -2147024864) }
            Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
        }
        function Start-Sleep([int]$Milliseconds) {
            Check ($Milliseconds -eq 250) 'Unexpected file verification retry interval.'
            [IO.File]::WriteAllText($owned,'replaced during retry')
        }
        Reject { Remove-ONEXTweaksOwnedReleaseFile $plan $plan.Files[$owned] } 'file was replaced'
        Check ($read.Attempts -eq 2 -and $f.RuntimeDeleteAttempts -eq 0) 'File verification was skipped during retry.'
        [IO.File]::WriteAllText($owned,'owned')
    }
    [IO.File]::SetAttributes($owned, [IO.FileAttributes]::ReadOnly)
    & {
        function Start-Sleep([int]$Milliseconds) { Check ($Milliseconds -eq 250) 'Unexpected retry interval.' }
        $f.RuntimeDeleteBlocks = 22
        Reject { Remove-ONEXTweaksOwnedReleaseFile $plan $plan.Files[$owned] } 'runtime image is still mapped'
        Check ($f.RuntimeDeleteAttempts -eq 21 -and (Test-Path -LiteralPath $owned) -and
            (Test-Path -LiteralPath $receiptPath)) 'Persistent runtime lock did not stop at the retry limit.'
    }
    $f.RuntimeDeleteAttempts = 0
    $f.RuntimeDeleteBlocks = 2
    $result = Invoke-ONEXTweaksMachineRemoval (Get-ONEXTweaksUninstallPlan $state $root) $lease
    Check ($result.State -eq 'Removed' -and !$f.Task -and !(Test-Path -LiteralPath $owned) -and (Test-Path -LiteralPath $shared)) 'Unexpected machine cleanup result.'
    Check ($f.TransientReads -eq 0 -and $f.Unregisters -eq 1) 'Transient scheduler reads prevented one verified removal.'
    Check ($f.RuntimeDeleteAttempts -eq 3) 'The temporary runtime image mapping prevented first-attempt uninstall.'
    Check (!(Test-Path -LiteralPath $receiptPath) -and (Test-Path -LiteralPath $data) -and $null -eq (Get-ONEXTweaksUninstallPlan $state $root)) 'Repeated uninstall or user data changed.'
    $actors = @()
    try {
        $relative = 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'
        $bridgeImage = Join-Path $root $relative
        $otherImage = Join-Path $local 'ONEXTweaks.WidgetBridge.exe'
        foreach ($image in @($bridgeImage,$otherImage)) {
            [IO.File]::Copy((Join-Path ([Environment]::SystemDirectory) 'ping.exe'),$image)
            $actors += Start-Process -FilePath $image -ArgumentList '-t','127.0.0.1' -WindowStyle Hidden -PassThru
        }
        Check (!$actors[0].HasExited -and !$actors[1].HasExited) 'The runtime process fixtures did not start.'
        $fileRows = @{}
        $fileRows[$bridgeImage] = @{Path=$relative;Sha256=(Get-FileHash -LiteralPath $bridgeImage).Hash}
        $actualSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        Stop-ONEXTweaksOwnedBridgeProcesses ([pscustomobject]@{Root=$root;Files=$fileRows;Receipt=@{OwnerSid=$sid}})
        Check (!$actors[0].HasExited) 'A runtime process was stopped for a different owner SID.'
        Stop-ONEXTweaksOwnedBridgeProcesses ([pscustomobject]@{Root=$root;Files=$fileRows;Receipt=@{OwnerSid=$actualSid}})
        Check ($actors[0].HasExited -and !$actors[1].HasExited) 'Process cleanup missed the detached runtime or stopped an unrelated image.'
    } finally {
        foreach ($actor in $actors) {
            if (!$actor.HasExited) { $actor.Kill(); $null = $actor.WaitForExit(5000) }
            $actor.Dispose()
        }
    }
    'Two-phase uninstall, capture blocking, preserved AppX data, exact task/file ownership and retry fixtures passed.'
} finally { if ([IO.Directory]::Exists($temp)) { [IO.Directory]::Delete($temp,$true) } }
