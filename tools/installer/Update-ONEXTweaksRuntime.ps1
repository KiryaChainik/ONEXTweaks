#Requires -Version 5.1
param(
    [Parameter(Mandatory)][ValidateSet('Inspect','Stage','Commit','Rollback')][string]$Action,
    [Parameter(Mandatory)][ValidatePattern('^S-1-5-21(?:-[0-9]{1,10}){4}$')][string]$OwnerSid,
    [Parameter(Mandatory)][string]$RuntimeManifest,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$RuntimeManifestSha256
)
function Write-ONEXTweaksRuntimeRecord([string]$Path, $Record) {
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary,($Record | ConvertTo-Json -Depth 10 -Compress))
        if ([IO.File]::Exists($Path)) {
            [IO.File]::Replace($temporary,$Path,[Management.Automation.Language.NullString]::Value)
        } else { [IO.File]::Move($temporary,$Path) }
    } finally { [IO.File]::Delete($temporary) }
}

function Get-ONEXTweaksRuntimeDifference($Plan, $Files) {
    foreach ($row in $Plan.Receipt.Files) {
        if (!$Files.ContainsKey($row.Path)) { throw "The update would remove an installed runtime file: $($row.Path)" }
        if (![IO.File]::Exists((Join-Path $Plan.Root $row.Path))) { throw "The installed runtime is incomplete: $($row.Path)" }
    }
    $changed = @($Plan.Receipt.Files | Where-Object { $_.Sha256 -ine $Files[$_.Path].Row.Sha256 })
    return ($changed.Count -gt 0 -or $Plan.Receipt.Files.Count -ne $Files.Count)
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Initialize-ONEXTweaksReleaseRuntime.ps1') -RuntimeManifest $RuntimeManifest -RuntimeManifestSha256 $RuntimeManifestSha256
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleHandoff.ps1')

$package = 'ONEXTweaks.Widget_0.29.1110.0_x64__3a23eh8tyg7na'
$root = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
$state = Get-ONEXTweaksReleaseStateDirectory
$receiptPath = Join-Path $state 'owned-release.json'
$journalPath = Join-Path $state 'runtime-update-1110.json'
$handoffPath = Join-Path $state 'onexconsole-handoff.json'
$commonTasks = @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-RTSS')
$hardwareTasks = [ordered]@{'ONEXTweaks-PowerAgent'='Hardware';'ONEXTweaks-OneXConsoleHandoff'='OemHandoff'}
$configuration = @('home-oem-settings.json','led-color-profile.json','boost-modes.json')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if ($identity.User.Value -cne $OwnerSid -or
        !([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or
        (Get-ONEXTweaksInteractiveSid) -cne $OwnerSid -or ![Environment]::Is64BitProcess) {
        throw 'The 1110 runtime update needs administrator rights on the widget owner desktop.'
    }
} finally { $identity.Dispose() }
$installedPackage = Get-AppxPackage -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main
if (!$installedPackage -or $installedPackage.PackageFullName -cne $package -or $installedPackage.Status -ne 'Ok') {
    throw 'The owner needs the registered ONEXTweaks 1110 widget before updating its runtime.'
}

function Get-ONEXTweaksHotfixTask($Row) {
    $task = Get-ONEXTweaksComTask $scheduler.Tasks $Row.Name
    if (!$task -or (Get-ONEXTweaksTaskXmlDigest $task.Xml) -ine $Row.DefinitionSha256) {
        throw "Missing or changed release task: $($Row.Name)"
    }
    return $task
}

function Stop-ONEXTweaksHotfixRuntime($Previous, $Journal, [bool]$CandidateActive) {
    $releaseFiles = @{}
    $releaseTasks = @{}
    $activeFiles = if ($CandidateActive) { $Journal.CandidateFiles } else { $Previous.Files }
    foreach ($row in $activeFiles) { $releaseFiles[(Join-Path $root $row.Path)] = $row }
    foreach ($row in $Previous.Tasks) { $releaseTasks[$row.Name] = $row }
    $activePlan = [pscustomobject]@{Root=$root;Receipt=$Previous;Files=$releaseFiles;Tasks=$releaseTasks}
    $lease = Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev\privateReports\controller-capture-lease.json'
    if ((Test-Path -LiteralPath $root) -and (Test-Path -LiteralPath $lease)) {
        Suspend-ONEXTweaksOwnedController $activePlan $lease
    }
    foreach ($name in @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-ControllerInputRecovery',
            'ONEXTweaks-OneXConsoleHandoff','ONEXTweaks-PowerAgent')) {
        $row = $Previous.Tasks | Where-Object Name -ceq $name
        $task = if ($row) { Get-ONEXTweaksHotfixTask $row } elseif ($Journal.AddHardware -and $hardwareTasks.Contains($name)) {
            $created = Get-ONEXTweaksComTask $scheduler.Tasks $name
            if ($created -and $created.Definition.RegistrationInfo.Description -cne "ONEXTweaks $($hardwareTasks[$name]) $($Journal.Id)") {
                throw "Unexpected hardware task during rollback: $name"
            }
            $created
        }
        if (!$task) { continue }
        if ($name -eq 'ONEXTweaks-PowerAgent' -and (Test-Path -LiteralPath $root)) {
            Wait-ONEXTweaksHardwareRestore ([pscustomobject]@{Root=$root;Receipt=[pscustomobject]@{PowerStateOwned=$true}})
        }
        Stop-ONEXTweaksReleaseTask $task $name
        if (!$row) { $scheduler.Tasks.DeleteTask($name,0) }
    }
    if (Test-Path -LiteralPath $root) {
        Stop-ONEXTweaksOwnedBridgeProcesses $activePlan
    }
}

function Enable-ONEXTweaksHotfixRuntime($Previous, $Journal) {
    foreach ($row in $Previous.Tasks) {
        $task = Get-ONEXTweaksHotfixTask $row
        $enabled = $row.Name -in $Journal.Enabled
        if ($task.Enabled -ne $enabled) { $task.Enabled = $enabled }
    }
}

function Assert-ONEXTweaksHotfixFolder($Plan) {
    $transient = @('power-agent-result.json','installer-hardware-ready.json','installer-hardware-ready.json.tmp',
        'onexconsole-resume-error.json','hardware-health.jsonl','hardware-health.jsonl.prev','fan-control-journal',
        'game-lighting-lease.json','game-lighting-lease.json.tmp','home-oem-settings.json.tmp','boost-modes.json.tmp')
    foreach ($file in Get-ONEXTweaksPrivateFiles $Plan.Root) {
        if ($Plan.Files.ContainsKey($file.FullName)) { continue }
        if ($Plan.Receipt.PowerStateOwned -and $file.DirectoryName -ieq (Join-Path $Plan.Root 'PowerAgent') -and
            ($file.Name -in ($configuration + $transient) -or (Test-ONEXTweaksLegacyFanMarker $file))) {
            Assert-ONEXTweaksProtectedItem $file.FullName
            continue
        }
        throw "The previous runtime contains an unowned file: $($file.FullName)"
    }
}

function Move-ONEXTweaksReleasedRuntime([string]$Source,[string]$Destination) {
    for ($attempt=0; ; $attempt++) {
        try { [IO.Directory]::Move($Source,$Destination); return }
        catch {
            if ($attempt -ge 20 -or ($_.Exception.GetBaseException().HResult -band 0xffff) -notin 5,32,33,1224) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Remove-ONEXTweaksRuntimeScratch([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Assert-ONEXTweaksProtectedItem $Path
        $null = @(Get-ONEXTweaksPrivateFiles $Path)
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Restore-ONEXTweaksRuntimeUpdate($Journal, $Previous, [string]$Saved, [string]$Staged) {
    $maintenance = Open-ONEXTweaksPackageMaintenance $package
    try {
        if ([IO.Directory]::Exists($Saved)) {
            Assert-ONEXTweaksProtectedItem $Saved
            Assert-ONEXTweaksRuntimeFiles $Previous.Files $Saved
            if ([IO.Directory]::Exists($root)) {
                Stop-ONEXTweaksHotfixRuntime $Previous $Journal $true
                if (!$Journal.HadHandoff -and [IO.File]::Exists($handoffPath)) {
                    $null = Invoke-ONEXTweaksOneXConsoleHandoff -Operation Restore -StateDirectory $state -UserSid $OwnerSid -SchedulerFolder $scheduler.Tasks
                }
                Move-ONEXTweaksReleasedRuntime $root ($Staged + '-discard')
            }
            Move-ONEXTweaksReleasedRuntime $Saved $root
        }
        # Saved may be absent before the first rename or after an interrupted
        # rollback. In either case, the previous files must already be live.
        Assert-ONEXTweaksRuntimeFiles $Previous.Files $root
        Write-ONEXTweaksRuntimeRecord $receiptPath $Previous
        Enable-ONEXTweaksHotfixRuntime $Previous $Journal
    } finally { $maintenance.Dispose() }
    $startup = Get-ONEXTweaksHotfixTask ($Previous.Tasks | Where-Object Name -ceq 'ONEXTweaks-Startup')
    if ($startup.Enabled) { $null = $startup.Run($null) }
    Remove-ONEXTweaksRuntimeScratch $Staged
    Remove-ONEXTweaksRuntimeScratch ($Staged + '-discard')
    [IO.File]::Delete($journalPath)
}

function Assert-ONEXTweaksRuntimeFiles($Rows, [string]$Directory) {
    Assert-ONEXTweaksProtectedItem $Directory
    foreach ($row in $Rows) {
        $path = Get-ONEXTweaksOwnedFile $Directory $row
        if (![IO.File]::Exists($path)) { throw "Previous runtime recovery file missing: $($row.Path)" }
    }
}

if (!(Test-Path -LiteralPath $state)) { return 'No update' }
Assert-ONEXTweaksProtectedStateDirectory $state
$lockPath = Join-Path $state 'release-transaction.lock'
Assert-ONEXTweaksProtectedItem $lockPath
$lock = [IO.File]::Open($lockPath,'Open','ReadWrite','None')
try {
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $scheduler = [pscustomobject]@{Service=$service;Tasks=$service.GetFolder('\')}
    if (Test-Path -LiteralPath $journalPath) {
        Assert-ONEXTweaksProtectedItem $journalPath
        $journal = [IO.File]::ReadAllText($journalPath) | ConvertFrom-Json
        if ($journal.Schema -ne 1 -or $journal.Id -cnotmatch '^[a-f0-9]{32}$' -or
            $journal.OwnerSid -cne $OwnerSid -or $journal.Manifest -cne $RuntimeManifestSha256 -or
            $journal.Phase -cnotin @('Switching','Staged','Committed') -or
            $journal.Previous.OwnerSid -cne $OwnerSid -or $journal.Previous.PackageFullName -cne $package) {
            throw 'Resume this runtime update with its original signed Setup and owner.'
        }
        $saved = "$root-update-$($journal.Id)-old"
        $staged = "$root-update-$($journal.Id)-new"
        if ($journal.Phase -ceq 'Committed' -or $Action -ceq 'Commit') {
            if ($journal.Phase -ceq 'Switching') { throw 'The runtime has not finished staging.' }
            $null = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly
            $journal.Phase = 'Committed'
            Write-ONEXTweaksRuntimeRecord $journalPath $journal
            Remove-ONEXTweaksRuntimeScratch $saved
            [IO.File]::Delete($journalPath)
            return 'Committed'
        }
        if ($Action -ceq 'Rollback' -or $journal.Phase -ceq 'Switching') {
            Restore-ONEXTweaksRuntimeUpdate $journal $journal.Previous $saved $staged
            return 'Rolled back'
        }
        return 'Staged'
    }
    if ($Action -in 'Commit','Rollback') { return 'No update' }
    $plan = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly
    if (!$plan) { return 'No update' }
    if ($plan.Receipt.OwnerSid -cne $OwnerSid -or $plan.Receipt.PackageFullName -cne $package) { throw 'The installed runtime belongs to another release or desktop.' }
    $manifestItem = Get-Item -LiteralPath $RuntimeManifest
    if ($manifestItem.Attributes -band [IO.FileAttributes]::ReparsePoint -or $manifestItem.Length -gt 1048576 -or
        (Get-FileHash -LiteralPath $RuntimeManifest).Hash -cne $RuntimeManifestSha256) { throw 'Unverified runtime update manifest.' }
    $manifest = [IO.File]::ReadAllText($RuntimeManifest) | ConvertFrom-Json
    if ($manifest.PackageFullName -cne $package) { throw 'The update does not match the installed widget.' }
    $id = [guid]::NewGuid().ToString('N')
    $saved = "$root-update-$id-old"
    $staged = "$root-update-$id-new"
    $hardware = (Get-ONEXTweaksHardwareCompatibility) -eq 'Supported'
    $files = Get-ONEXTweaksRuntimePayload $manifest $manifestItem.DirectoryName $staged $hardware
    if (!(Get-ONEXTweaksRuntimeDifference $plan $files)) { return 'Current' }
    if ($Action -ceq 'Inspect') { return 'Update required' }

    if (Test-Path -LiteralPath (Join-Path $state 'upgrade-1109.json')) { throw 'Finish the earlier migration before updating the runtime.' }
    Assert-ONEXTweaksHotfixFolder $plan
    if (!$plan.Receipt.PSObject.Properties['PowerStateOwned']) { throw 'The previous runtime ownership is incomplete.' }
    $addHardware = $hardware -and !$plan.Receipt.PowerStateOwned
    $expectedTasks = @($commonTasks)
    if ($plan.Receipt.PowerStateOwned) { $expectedTasks += @($hardwareTasks.Keys) }
    if ($plan.Tasks.Count -ne $expectedTasks.Count -or @($expectedTasks | Where-Object { !$plan.Tasks.ContainsKey($_) }).Count) {
        throw 'The old installation does not own the expected set of tasks.'
    }
    if ($addHardware) {
        foreach ($name in $hardwareTasks.Keys) {
            if (Get-ONEXTweaksComTask $scheduler.Tasks $name) { throw "Unowned hardware task: $name" }
        }
    }
    $hadHandoff = Test-Path -LiteralPath $handoffPath
    if ($hadHandoff) {
        Assert-ONEXTweaksProtectedItem $handoffPath
        $handoff = [IO.File]::ReadAllText($handoffPath) | ConvertFrom-Json
        if ($handoff.Phase -cnotin 'Disabled','Released','PreDisabled') { throw 'Finish the pending OneXConsole handoff first.' }
    }
    $enabled = @(foreach ($row in $plan.Receipt.Tasks) {
        $task = Get-ONEXTweaksHotfixTask $row
        if ($task.Enabled) { $row.Name }
    })
    $journal = [pscustomobject]@{Schema=1;Id=$id;OwnerSid=$OwnerSid;Manifest=$RuntimeManifestSha256;Phase='Switching';
        Previous=$plan.Receipt;CandidateFiles=@($files.Values | ForEach-Object Row);
        Enabled=$enabled;AddHardware=$addHardware;HadHandoff=$hadHandoff}
    New-ONEXTweaksProtectedDirectory $staged
    try {
        foreach ($item in $files.Values) {
            $path = Join-Path $staged $item.Row.Path
            $null = [IO.Directory]::CreateDirectory((Split-Path -LiteralPath $path))
            [IO.File]::Copy($item.Source,$path)
            $null = Get-ONEXTweaksOwnedFile $staged $item.Row
        }
        Write-ONEXTweaksRuntimeRecord $journalPath $journal
        $maintenance = Open-ONEXTweaksPackageMaintenance $package
        try {
            Stop-ONEXTweaksHotfixRuntime $plan.Receipt $journal $false
            Assert-ONEXTweaksHotfixFolder $plan
            if ($hardware) {
                foreach ($name in $configuration) {
                    $source = Join-Path $root "PowerAgent\$name"
                    if (!(Test-Path -LiteralPath $source)) { continue }
                    Assert-ONEXTweaksProtectedItem $source
                    [IO.File]::Copy($source,(Join-Path $staged "PowerAgent\$name"))
                }
                $settings = Join-Path $staged 'PowerAgent\home-oem-settings.json'
                if (![IO.File]::Exists($settings)) { [IO.File]::Copy((Join-Path $staged 'PowerAgent\home-oem-seed.json'),$settings) }
            }
            Move-ONEXTweaksReleasedRuntime $root $saved
            Move-ONEXTweaksReleasedRuntime $staged $root
            $newTasks = @($plan.Receipt.Tasks)
            if ($addHardware) {
                $created = [Collections.Generic.List[string]]::new()
                Register-ONEXTweaksHardwareTasks $scheduler $root $OwnerSid $created $id
                $newTasks += @(foreach ($name in $created) {
                    $task = Get-ONEXTweaksComTask $scheduler.Tasks $name
                    [pscustomobject]@{Name=$name;DefinitionSha256=(Get-ONEXTweaksTaskXmlDigest $task.Xml)}
                })
            }
            $next = ($plan.Receipt | ConvertTo-Json -Depth 8) | ConvertFrom-Json
            $next.Files = @($files.Values | ForEach-Object Row)
            $next.Tasks = $newTasks
            $next.PowerStateOwned = $hardware
            Write-ONEXTweaksRuntimeRecord $receiptPath $next
            Enable-ONEXTweaksHotfixRuntime $plan.Receipt $journal
            $journal.Phase = 'Staged'
            Write-ONEXTweaksRuntimeRecord $journalPath $journal
        } finally { $maintenance.Dispose() }
        return 'Staged'
    } catch {
        if (!(Test-Path -LiteralPath $journalPath)) { Remove-ONEXTweaksRuntimeScratch $staged }
        throw
    }
} finally { $lock.Dispose() }
