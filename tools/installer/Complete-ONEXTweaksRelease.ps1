#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName='Report')]
param(
    [Parameter(Mandatory, ParameterSetName='Commit')][switch]$Complete,
    [Parameter(ParameterSetName='Commit')][switch]$SameBootOnly,
    [Parameter(Mandatory, ParameterSetName='Resume')][switch]$ResumeOneXConsoleHandoff,
    [Parameter(Mandatory, ParameterSetName='Restore')][switch]$RestoreOneXConsoleStartup
)

function Get-ONEXTweaksReleaseReadiness($Plan, $Package, $Runtime, $Components, [bool]$HardwareRequired, [bool]$RestartPending) {
    if ($Package.PackageFullName -cne $Plan.Receipt.PackageFullName -or $Package.PackageFamilyName -cne $Plan.Receipt.PackageFamilyName -or
        $Package.Status -ne 'Ok' -or $Package.Architecture -ne 'x64') { return 'Widget registration unconfirmed' }
    if ($RestartPending) { return 'Windows restart required' }
    if (@($Components | Where-Object Status -notin @('Installed','Not applicable')).Count) { return 'Prerequisite verification incomplete' }
    $names = @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-RTSS')
    if ($HardwareRequired) { $names += 'ONEXTweaks-PowerAgent' }
    foreach ($name in $names) {
        if (!$Plan.Tasks.ContainsKey($name)) { return "Release task is not owned: $name" }
    }
    if ($Runtime.Backend -ne 'Healthy') { return $Runtime.Backend }
    if ($Runtime.RtssTask -ne 'Ready') { return 'RTSS startup task unconfirmed' }
    if ($Runtime.RtssSession -ne 'Running') { return 'RTSS is not running in the widget owner session' }
    return 'Ready'
}

function Get-ONEXTweaksRtssSessionState([string]$Sid, [int]$Session) {
    $task = Get-ScheduledTask -TaskPath '\' -TaskName 'ONEXTweaks-RTSS'
    $path = $task.Actions[0].Execute
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='RTSS.exe' AND SessionId=$Session" | Where-Object ExecutablePath -EQ $path)
    foreach ($process in $processes) {
        if ((Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid).Sid -eq $Sid) { return 'Running' }
    }
    return 'Unavailable'
}

function Test-ONEXTweaksMachineRestartPending([string]$State, [string]$LocalAppData) {
    foreach ($key in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    )) {
        if (Test-Path -LiteralPath $key) { return $true }
    }
    $os = Get-CimInstance Win32_OperatingSystem
    $bootId = $os.LastBootUpTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
    foreach ($path in @((Join-Path $State 'pending-restart.json'),
        (Join-Path $LocalAppData 'ONEXTweaks\Setup\pending-restart.json'))) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        if ($path.StartsWith($State + '\', [StringComparison]::OrdinalIgnoreCase)) { Assert-ONEXTweaksProtectedItem $path }
        $marker = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($marker.BootId -notmatch '^\d+$' -or !$marker.Component) { throw 'Invalid ONEXTweaks restart receipt.' }
        if ($marker.BootId -ceq $bootId) { return $true }
    }
    return $false
}

function Get-ONEXTweaksPendingOemRestart([string]$State) {
    $path = Join-Path $State 'onexconsole-handoff.json'
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return }
    Assert-ONEXTweaksProtectedStateDirectory $State
    Assert-ONEXTweaksProtectedItem $path
    $record = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($record.Phase -in @('RestartPrepared','RestartPending','RestartRestoring')) { return $record }
}

function Open-ONEXTweaksCompletionLock([string]$Path, $Access, [bool]$Retry) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        try { return [IO.File]::Open($Path, 'Open', $Access, 'None') }
        catch [IO.IOException] {
            if (!$Retry -or ($_.Exception.HResult -band 65535) -notin @(32,33) -or $watch.Elapsed.TotalSeconds -ge 25) { throw }
            Start-Sleep -Milliseconds 300
        }
    } while ($true)
}

function Test-ONEXTweaksOemGuiForOwner([string]$Sid, [int]$Session) {
    $path = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'OneXConsole\OneXConsole.exe'
    if ($Session -le 0 -or !(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='OneXConsole.exe' AND SessionId=$Session" | Where-Object {
        $_.ExecutablePath -ieq $path -and $_.CommandLine -match '(?:^|\s)--startupHidden(?:\s|$)' -and $_.CommandLine -notmatch '\s--type='
    })
    if ($processes.Count -ne 1 -or (Invoke-CimMethod -InputObject $processes[0] -MethodName GetOwnerSid).Sid -cne $Sid) { return $false }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    return $signature.Status -eq 'Valid' -and
        $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ceq $oneXConsolePrerequisite.Publisher
}

function Test-ONEXTweaksOemHardwareProbe($Plan, [datetime]$Boot) {
    $relative = 'PowerAgent\ONEXTweaks.PowerAgent.exe'
    $path = Join-Path $Plan.Root 'PowerAgent\installer-hardware-ready.json'
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $path -Force
    if ($item.Length -gt 4096 -or $item.LastWriteTimeUtc -lt $Boot) { return $false }
    Assert-ONEXTweaksProtectedItem $path
    $proof = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    $requiresFan = Test-ONEXTweaksQualifiedOemHardware
    if ($proof.schema -ne 3 -or $proof.samples -ne 3 -or
        ($requiresFan -and ($proof.fan -isnot [bool] -or !$proof.fan)) -or
        (!$requiresFan -and ($proof.tdpWritable -isnot [bool] -or !$proof.tdpWritable)) -or
        $proof.tdp -isnot [bool] -or !$proof.tdp -or $proof.processId -notmatch '^[0-9]+$' -or
        $proof.ownerSession -notmatch '^[1-9][0-9]*$') { return $false }
    $stamp = [DateTimeOffset]::Parse($proof.utc, [Globalization.CultureInfo]::InvariantCulture)
    $age = [DateTimeOffset]::UtcNow - $stamp
    if ($stamp.UtcDateTime -lt $Boot -or $age.TotalSeconds -lt 0 -or $age.TotalSeconds -gt 15) { return $false }
    $exe = Join-Path $Plan.Root $relative
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($proof.processId)"
    if (!$process -or $process.SessionId -ne 0 -or $process.ExecutablePath -ine $exe -or
        $process.CreationDate.ToUniversalTime() -gt $stamp.UtcDateTime -or
        (Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid).Sid -cne 'S-1-5-18') { return $false }
    $owners = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe' AND SessionId=$($proof.ownerSession)" |
        ForEach-Object { (Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid).Sid } | Sort-Object -Unique)
    return $owners.Count -eq 1 -and $owners[0] -ceq $Plan.Receipt.OwnerSid
}

function Restore-ONEXTweaksOemRestart($Plan, $SchedulerFolder) {
    $sid = $Plan.Receipt.OwnerSid
    $isolated = $false
    try {
        foreach ($name in 'ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-PowerAgent') {
            if ($name -eq 'ONEXTweaks-PowerAgent') {
                $folders = Get-ItemProperty -LiteralPath "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" -Name 'Local AppData'
                $lease = Join-Path $folders.'Local AppData' 'ONEXTweaks\dev\privateReports\controller-capture-lease.json'
                Assert-ONEXTweaksReleasedController $Plan $lease $SchedulerFolder
                Wait-ONEXTweaksHardwareRestore $Plan
            }
            $task = Get-ONEXTweaksComOwnedTask $Plan $SchedulerFolder $name
            if (!$task) { continue }
            if ($task.Enabled) { $task.Enabled = $false }
            if ($task.State -in @(2,4)) {
                $task.Stop(0)
                for ($i=0; $i -lt 24; $i++) {
                    $task = Get-ONEXTweaksComOwnedTask $Plan $SchedulerFolder $name
                    if (!$task -or $task.State -notin @(2,4)) { break }
                    Start-Sleep -Milliseconds 250
                }
                if ($task -and $task.State -in @(2,4)) { throw "Release task did not stop: $name" }
            }
        }
        $oemDirectory = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'OneXConsole\resources\resources'
        $stockHelper = Join-Path $oemDirectory 'CompatLayerCT.exe'
        $ownedHelper = Join-Path $Plan.Root 'PowerAgent\OneXConsole-fix3\CompatLayerCT.exe'
        foreach ($process in Get-CimInstance Win32_Process -Filter "Name='CompatLayerCT.exe' AND SessionId=0") {
            if ($process.ExecutablePath -notin @($stockHelper,$ownedHelper) -or
                $process.CommandLine -notmatch '\s1013\s+ONEXTweaks\.PowerAgent\s+2\s+pioforce\s*$' -or
                (Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid).Sid -cne 'S-1-5-18') { continue }
            $actor = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
            if (!$actor -or $actor.Path -ine $process.ExecutablePath -or
                [Math]::Abs(($actor.StartTime - $process.CreationDate).TotalSeconds) -gt 0.01) { continue }
            if ((Get-FileHash -LiteralPath $actor.Path).Hash -notin @(
                '505DB9A04EB5F23D8D0DE31307ADB9D57D5DA1900908D9D995EDFC49EA5C7328',
                'B82263110012916610A5FC3C1EA69E79D1AF1CA631727CFD08278098172EC845')) { throw 'An unverified helper still owns the channel.' }
            $actor.Kill()
            if (!$actor.WaitForExit(4000)) { throw 'The owned OEM helper did not exit.' }
        }
        $isolated = $true
    } finally {
        $restored = Invoke-ONEXTweaksOneXConsoleHandoff -Operation RestoreRestart -StateDirectory $Plan.StateDirectory -UserSid $sid -SchedulerFolder $SchedulerFolder
    }
    if (!$isolated -or $restored -ne 'OEM startup restored; launch pending') { return $restored }
    $oem = Get-ONEXTweaksOemTaskSnapshot $sid $SchedulerFolder
    $sessions = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe' AND SessionId>0" |
        Where-Object {(Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid).Sid -ceq $sid} |
        Select-Object -ExpandProperty SessionId -Unique)
    if ($oem.State -ne 'Recognized' -or !$oem.Enabled -or $sessions.Count -ne 1) {
        throw 'OEM startup restored; waiting for the installation owner desktop.'
    }
    if (!(Test-ONEXTweaksOemGuiForOwner $sid $sessions[0])) { $null = $oem.Task.Run($null) }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $helpers = @(Get-CimInstance Win32_Process -Filter "Name='CompatLayerCT.exe' AND SessionId=$($sessions[0])" |
            Where-Object { $_.ExecutablePath -ieq $stockHelper -and
                (Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid).Sid -ceq $sid })
        if ($helpers -and (Test-ONEXTweaksOemGuiForOwner $sid $sessions[0])) {
            return Invoke-ONEXTweaksOneXConsoleHandoff -Operation ConfirmRestartRestore -StateDirectory $Plan.StateDirectory -UserSid $sid -SchedulerFolder $SchedulerFolder
        }
        Start-Sleep -Milliseconds 750
    } while ($watch.Elapsed.TotalSeconds -lt 20)
    throw 'OEM startup restored; OEM hardware helper has not reported back.'
}

function Invoke-ONEXTweaksOemRestartResume($Plan, $Pending) {
    $state = $Plan.StateDirectory
    $sid = $Plan.Receipt.OwnerSid
    $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime()
    $bootId = $boot.Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = $service.GetFolder('\')
    if ($Pending.Phase -ne 'RestartPending' -or $Pending.BootId -ceq $bootId) {
        return Restore-ONEXTweaksOemRestart $Plan $folder
    }
    try {
        $null = Get-ONEXTweaksVerifiedTool $Plan 'PowerAgent\ONEXTweaks.PowerAgent.exe'
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $last = 'Waiting for ONEXTweaks hardware.'
        do {
            try {
                $guard = Get-ONEXTweaksComOwnedTask $Plan $folder 'ONEXTweaks-OneXConsoleHandoff'
                $oem = Get-ONEXTweaksOemTaskSnapshot $sid $folder
                if (!$guard -or !$guard.Enabled -or $oem.State -ne 'Recognized' -or $oem.Enabled -or
                    $oem.Fingerprint -cne $Pending.Fingerprint -or (Get-OneXConsolePrerequisiteState) -ne 'Installed') {
                    throw 'OneXConsole or its protected restart task is not yet verified.'
                }
                $packages = @(Get-AppxPackage -User $sid -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main)
                if ($packages.Count -ne 1 -or $packages[0].PackageFullName -cne $Plan.Receipt.PackageFullName -or
                    $packages[0].Status -ne 'Ok') { throw 'The release AppX registration is not yet verified.' }
                foreach ($name in 'ONEXTweaks-PowerAgent','ONEXTweaks-WidgetBridge') {
                    $task = Get-ONEXTweaksComOwnedTask $Plan $folder $name
                    if (!$task -or !$task.Enabled) { throw "Release task unavailable: $name" }
                    if ($task.State -notin @(2,4)) { $null = $task.Run($null) }
                }
                if (Test-ONEXTweaksOemHardwareProbe $Plan $boot) {
                    $result = Invoke-ONEXTweaksOneXConsoleHandoff -Operation CommitRestart -StateDirectory $state -UserSid $sid `
                        -BootId $bootId -OemStatus Installed -BackendState Healthy -SchedulerFolder $folder
                    if ($result -ceq 'Disabled by ONEXTweaks') { return $result }
                    throw $result
                }
                $last = 'No current Fan/TDP readback from the authenticated PowerAgent and desktop.'
            } catch {
                $last = $_.Exception.Message
            }
            Start-Sleep -Seconds 2
        } while ($watch.Elapsed.TotalSeconds -lt 100)
        throw "Could not verify the restart handoff. $last"
    } catch {
        $failure = $_.Exception
        try { Restore-ONEXTweaksOemRestart $Plan $folder | Out-Null }
        catch { throw [AggregateException]::new('The OEM handoff and its first recovery attempt failed.', [Exception[]]@($failure,$_.Exception)) }
        throw $failure
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or ![Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell 5.1.' }
$root = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
if ($PSScriptRoot -ine (Join-Path $root 'Tools\Installer')) { throw 'Run release completion from its protected installed directory.' }
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleHandoff.ps1')
. (Join-Path $PSScriptRoot 'DriverPrerequisites.ps1')
. (Join-Path $PSScriptRoot 'GameBarPrerequisite.ps1')

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ($ResumeOneXConsoleHandoff -and (!$identity.IsSystem -or (Get-Process -Id $PID).SessionId -ne 0)) {
        throw 'OEM restart recovery uses the protected LocalSystem logon task.'
    }
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Release completion requires the installer administrator phase.' }
} finally { $identity.Dispose() }

$state = Get-ONEXTweaksReleaseStateDirectory
if ($ResumeOneXConsoleHandoff -and !(Get-ONEXTweaksPendingOemRestart $state)) { return }
$plan = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly:$ResumeOneXConsoleHandoff
if (!$plan) { throw 'The protected release ownership receipt is missing.' }
$sid = $plan.Receipt.OwnerSid
if (!$ResumeOneXConsoleHandoff -and (Get-ONEXTweaksInteractiveSid) -cne $sid) { throw 'Use the signed-in desktop of the installation owner.' }
$lockPath = Join-Path $state 'release-transaction.lock'
Assert-ONEXTweaksProtectedItem $lockPath
$access = if ($PSCmdlet.ParameterSetName -eq 'Report' -or $WhatIfPreference) { 'Read' } else { 'ReadWrite' }
$lock = Open-ONEXTweaksCompletionLock $lockPath $access ([bool]$ResumeOneXConsoleHandoff)
try {
    $fresh = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly:$ResumeOneXConsoleHandoff
    if (!$fresh -or $fresh.Digest -cne $plan.Digest) { throw 'The release receipt changed. Inspect the release again.' }
    if ($RestoreOneXConsoleStartup) {
        if ($PSCmdlet.ShouldProcess('owned OneXConsole logon task','Restore startup')) {
            Invoke-ONEXTweaksOneXConsoleHandoff -Operation Restore -StateDirectory $state -UserSid $sid
        }
        return
    }

    foreach ($file in $fresh.Files.Keys) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Owned release file unavailable: $file" }
    }
    foreach ($tool in @('Complete-ONEXTweaksRelease.ps1','Uninstall-ONEXTweaksRelease.ps1','OneXConsoleSetup.ps1','OneXConsoleHandoff.ps1','DriverPrerequisites.ps1','GameBarPrerequisite.ps1')) {
        $installed = Get-ONEXTweaksVerifiedTool $fresh "Tools\Installer\$tool"
        if ((Join-Path $PSScriptRoot $tool) -ine $installed) { throw 'Run release completion from its protected installed directory.' }
    }
    if ($ResumeOneXConsoleHandoff) {
        $pending = Get-ONEXTweaksPendingOemRestart $state
        if ($pending -and $PSCmdlet.ShouldProcess('verified OneXConsole restart handoff','Resume or restore')) {
            Invoke-ONEXTweaksOemRestartResume $fresh $pending
        }
        return
    }
    $profile = Get-ItemProperty -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -Name ProfileImagePath
    $local = Join-Path ([Environment]::ExpandEnvironmentVariables($profile.ProfileImagePath)) 'AppData\Local'
    $folders = Get-ItemProperty -LiteralPath "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" -Name 'Local AppData'
    if ($local -ine $folders.'Local AppData') { throw 'Redirected Local AppData requires a separate installation check.' }
    $packages = @(Get-AppxPackage -User $sid -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main)
    if ($packages.Count -ne 1) { throw 'The owner account needs one registered ONEXTweaks widget.' }
    $gameBar = @(Get-ONEXTweaksGameBarWidgetHost $sid)
    if (!$gameBar.Count) { throw 'Install or update Xbox Game Bar for the installation owner from Microsoft Store to enable widgets.' }

    $hardware = Get-ONEXTweaksHardwareCompatibility
    $requiresHardware = $hardware -eq 'Supported'
    $oem = Get-OneXConsolePrerequisiteState
    $session = (Get-Process -Id $PID).SessionId
    $runtime = Get-ONEXTweaksRuntimeStatus $oem $packages[0].PackageFamilyName $sid $session $local
    $rtssSession = if ($runtime.RtssTask -eq 'Ready') { Get-ONEXTweaksRtssSessionState $sid $session } else { 'Unavailable' }
    $runtime | Add-Member -NotePropertyName RtssSession -NotePropertyValue $rtssSession
    $components = @(
        foreach ($component in $prerequisites) {
            if ($component.Service -eq 'PawnIO' -and !$requiresHardware) { continue }
            [pscustomobject]@{ Name=$component.Name; Status=(Get-PrerequisiteState $component) }
        }
        [pscustomobject]@{ Name=$oneXConsolePrerequisite.Name; Status=$oem }
    )
    $restartPending = Test-ONEXTweaksMachineRestartPending $state $local
    $ready = Get-ONEXTweaksReleaseReadiness $fresh $packages[0] $runtime $components $requiresHardware $restartPending
    $result = [pscustomobject]@{ SetupComplete=$false; Status=$ready; Hardware=$hardware; Prerequisites=$components;
        Backend=$runtime.Backend; RtssTask=$runtime.RtssTask; RtssSession=$runtime.RtssSession; OneXConsoleStartup=$runtime.OneXConsoleStartup;
        RestartRequired=$false }
    $pending = Get-ONEXTweaksPendingOemRestart $state
    if ($pending) {
        $result.RestartRequired = $pending.Phase -ne 'RestartRestoring'
        $result.Status = if ($result.RestartRequired) { 'Restart required for OEM handoff' } else { 'OEM recovery in progress' }
        return $result
    }
    if ($Complete -and !$SameBootOnly -and $ready -cin @('PowerAgent hardware readback unavailable','PowerAgent hardware readback temporarily stale') -and
        $requiresHardware -and $oem -eq 'Installed' -and
        $runtime.RtssTask -eq 'Ready' -and $runtime.RtssSession -eq 'Running' -and $fresh.Tasks.ContainsKey('ONEXTweaks-OneXConsoleHandoff') -and
        (Test-ONEXTweaksOemGuiForOwner $sid $session) -and $PSCmdlet.ShouldProcess('verified OneXConsole logon task','Prepare restart handoff')) {
        $guard = Get-ONEXTweaksRemainingTask $fresh 'ONEXTweaks-OneXConsoleHandoff'
        if (!$guard -or !$guard.Settings.Enabled) { throw 'The OEM restart recovery task is not armed.' }
        $handoff = Invoke-ONEXTweaksOneXConsoleHandoff -Operation PrepareRestart -StateDirectory $state -UserSid $sid `
            -BootId (Get-ONEXTweaksOemHandoffBootId) -OemStatus $oem
        if ($handoff -ceq 'Restart required for OEM handoff') {
            $result.Status = $handoff
            $result.RestartRequired = $true
            $result.OneXConsoleStartup = 'Temporarily disabled pending hardware verification'
            return $result
        }
    }
    if (!$Complete -or $ready -ne 'Ready' -or !$PSCmdlet.ShouldProcess('verified ONEXTweaks release','Complete installation')) { return $result }

    $result.OneXConsoleStartup = Invoke-ONEXTweaksOneXConsoleHandoff -Operation Apply -StateDirectory $state -UserSid $sid -BackendState $runtime.Backend -OemStatus $oem
    $result.SetupComplete = $result.OneXConsoleStartup -in @('Disabled by ONEXTweaks','Already disabled before installation','Not applicable','Not found')
    $result.Status = if ($result.SetupComplete) { 'Complete' } else { 'OneXConsole startup review required' }
    return $result
} finally { $lock.Dispose() }
