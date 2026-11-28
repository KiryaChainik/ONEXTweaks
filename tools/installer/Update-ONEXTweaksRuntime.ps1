#Requires -Version 5.1
param(
    [Parameter(Mandatory)][ValidateSet('Assess','Inspect','Stage','Commit','Rollback')][string]$Action,
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

function Get-ONEXTweaksMissingRuntimeFiles($Plan, $Files) {
    foreach ($row in $Plan.Receipt.Files) {
        if (!$Files.ContainsKey($row.Path)) { throw "The update would remove an installed runtime file: $($row.Path)" }
        if (![IO.File]::Exists((Join-Path $Plan.Root $row.Path))) { $row.Path }
    }
}

function Get-ONEXTweaksRuntimeDifference($Plan, $Files, [string[]]$Missing) {
    if (!$PSBoundParameters.ContainsKey('Missing')) { $Missing = @(Get-ONEXTweaksMissingRuntimeFiles $Plan $Files) }
    $changed = @($Plan.Receipt.Files | Where-Object { $_.Sha256 -ine $Files[$_.Path].Row.Sha256 })
    return ($Missing.Count -gt 0 -or $changed.Count -gt 0 -or $Plan.Receipt.Files.Count -ne $Files.Count)
}

function Get-ONEXTweaksRuntimeTaskSnapshot($Folder, $Rows, [string[]]$NewNames, [string]$OwnerSid) {
    $missing = [Collections.Generic.List[string]]::new()
    $enabled = [Collections.Generic.List[string]]::new()
    $conflicts = [Collections.Generic.List[string]]::new()
    foreach ($row in $Rows) {
        $task = Get-ONEXTweaksComTask $Folder $row.Name
        if (!$task) { $missing.Add($row.Name); continue }
        if ((Get-ONEXTweaksTaskXmlDigest $task.Xml) -ine $row.DefinitionSha256 -or
            ($OwnerSid -and !(Test-ONEXTweaksReleaseTaskAccess $task $OwnerSid -RequireOwnerAccess))) {
            $conflicts.Add($row.Name)
        } elseif ($task.Enabled) { $enabled.Add($row.Name) }
    }
    foreach ($name in $NewNames) {
        if (Get-ONEXTweaksComTask $Folder $name) { $conflicts.Add($name) }
    }
    if ($conflicts.Count) { throw ('Windows has task definitions that do not match this installation: ' + ($conflicts -join ', ')) }
    return [pscustomobject]@{ Missing=$missing.ToArray(); Enabled=$enabled.ToArray() }
}

function Get-ONEXTweaksRuntimeJournalAction($Journal, [string]$Action, [string]$Manifest) {
    if ($Action -ceq 'Assess') { return 'Pending update' }
    if ($Journal.Phase -ceq 'Committed') { return 'Finalize' }
    if ($Action -ceq 'Rollback' -or $Journal.Phase -ceq 'RollingBack') { return 'Restore' }
    if ($Journal.Manifest -ine $Manifest -and $Action -ceq 'Inspect') { return 'Restore' }
    if ($Journal.Manifest -ine $Manifest) { throw 'Inspect and recover the interrupted runtime update before staging this Setup.' }
    if ($Journal.Phase -ceq 'Switching') {
        if ($Action -ceq 'Commit') { throw 'The runtime has not finished staging.' }
        return 'Restore'
    }
    if ($Action -ceq 'Commit') { return 'Finalize' }
    return 'Staged'
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Initialize-ONEXTweaksReleaseRuntime.ps1') -RuntimeManifest $RuntimeManifest -RuntimeManifestSha256 $RuntimeManifestSha256
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleHandoff.ps1')
. (Join-Path $PSScriptRoot 'DriverPrerequisites.ps1')
. (Join-Path (Split-Path -LiteralPath $PSScriptRoot) 'Install-Startup.ps1')

$manifestItem = Get-Item -LiteralPath $RuntimeManifest
if ($manifestItem.Attributes -band [IO.FileAttributes]::ReparsePoint -or $manifestItem.Length -gt 1048576 -or
    (Get-FileHash -LiteralPath $RuntimeManifest).Hash -cne $RuntimeManifestSha256) { throw 'Unverified runtime update manifest.' }
$manifest = [IO.File]::ReadAllText($RuntimeManifest) | ConvertFrom-Json
$package = [string]$manifest.PackageFullName
if ($package -cnotmatch '^ONEXTweaks\.Widget_(?:[0-9]{1,5}\.){3}[0-9]{1,5}_x64__3a23eh8tyg7na$') {
    throw 'The runtime update manifest does not describe the supported widget family.'
}
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
        throw 'The runtime update needs administrator rights on the widget owner desktop.'
    }
} finally { $identity.Dispose() }
$installedPackage = @(Get-AppxPackage -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main)
if ($installedPackage.Count -gt 1 -or ($installedPackage.Count -eq 1 -and $installedPackage[0].PackageFullName -cne $package)) {
    throw 'A different widget registration is using this Windows account.'
}
if ($Action -ne 'Assess' -and ($installedPackage.Count -ne 1 -or $installedPackage[0].Status -ne 'Ok')) {
    throw 'The owner needs the registered release widget before updating its runtime.'
}

function Get-ONEXTweaksHotfixTask($Row) {
    $task = Get-ONEXTweaksComTask $scheduler.Tasks $Row.Name
    if (!$task -or (Get-ONEXTweaksTaskXmlDigest $task.Xml) -ine $Row.DefinitionSha256) {
        throw "Missing or changed release task: $($Row.Name)"
    }
    if (!(Test-ONEXTweaksReleaseTaskAccess $task $OwnerSid)) { throw "Scheduled task permissions changed: $($Row.Name)" }
    return $task
}

function Get-ONEXTweaksRuntimeRemovalMarker {
    $key = "Registry::HKEY_USERS\$OwnerSid\Software\ONEXTweaks\Installation"
    $marker = Get-ItemProperty -LiteralPath $key -Name OwnerRemovalReceipt -ErrorAction SilentlyContinue
    if ($marker) { return $marker.OwnerRemovalReceipt }
}

function Remove-ONEXTweaksRuntimeRemovalMarker([string]$Digest) {
    if ($Digest -and (Get-ONEXTweaksRuntimeRemovalMarker) -ceq $Digest) {
        Remove-ItemProperty -LiteralPath "Registry::HKEY_USERS\$OwnerSid\Software\ONEXTweaks\Installation" -Name OwnerRemovalReceipt
    }
}

function Get-ONEXTweaksNewRuntimeTask($Journal, [string]$Name) {
    $task = Get-ONEXTweaksComTask $scheduler.Tasks $Name
    if (!$task) { return }
    $record = $Journal.CreatedTasks | Where-Object Name -ceq $Name
    if ($record) { return Get-ONEXTweaksHotfixTask $record }
    # Before the digest is durable, only SYSTEM and Administrators can change
    # the disabled task. Its transaction ID binds it to this protected journal.
    if (!$Journal.PSObject.Properties['GuardedTaskCreation'] -or !$Journal.GuardedTaskCreation) {
        throw "An unverified task occupies the interrupted repair: $Name. Review its definition before retrying Setup."
    }
    $expected = Get-ONEXTweaksReleaseTaskDefinition $Name $root $OwnerSid
    Assert-ONEXTweaksPendingTaskOwnership $task $expected.Purpose $Journal.Id
    return $task
}

function Stop-ONEXTweaksHotfixRuntime($Previous, $Journal, [bool]$CandidateActive) {
    $releaseFiles = @{}
    $releaseTasks = @{}
    $activeFiles = if ($CandidateActive) { $Journal.CandidateFiles } else { $Previous.Files }
    foreach ($row in $activeFiles) { $releaseFiles[(Join-Path $root $row.Path)] = $row }
    foreach ($row in $Previous.Tasks) { $releaseTasks[$row.Name] = $row }
    if ($CandidateActive -and $Journal.Schema -eq 2) {
        foreach ($row in $Journal.CreatedTasks) { $releaseTasks[$row.Name] = $row }
    }
    $activePlan = [pscustomobject]@{Root=$root;Receipt=$Previous;Files=$releaseFiles;Tasks=$releaseTasks}
    $lease = Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev\privateReports\controller-capture-lease.json'
    if ((Test-Path -LiteralPath $root) -and (Test-Path -LiteralPath $lease)) {
        Suspend-ONEXTweaksOwnedController $activePlan $lease
    }
    foreach ($name in @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-ControllerInputRecovery',
            'ONEXTweaks-OneXConsoleHandoff','ONEXTweaks-PowerAgent')) {
        $row = $Previous.Tasks | Where-Object Name -ceq $name
        if ($Journal.Schema -eq 2) {
            $task = if ($name -in $Journal.NewTasks) {
                if ($CandidateActive) { Get-ONEXTweaksNewRuntimeTask $Journal $name }
            } elseif ($row) { Get-ONEXTweaksHotfixTask $row }
        } else { $task = if ($row) { Get-ONEXTweaksHotfixTask $row } elseif ($Journal.AddHardware -and $hardwareTasks.Contains($name)) {
            $created = Get-ONEXTweaksComTask $scheduler.Tasks $name
            if ($created -and $created.Definition.RegistrationInfo.Description -cne "ONEXTweaks $($hardwareTasks[$name]) $($Journal.Id)") {
                throw "Unexpected hardware task during rollback: $name"
            }
            $created
        } }
        if ($name -eq 'ONEXTweaks-PowerAgent' -and ($task -or $Previous.PowerStateOwned) -and (Test-Path -LiteralPath $root)) {
            Wait-ONEXTweaksHardwareRestore ([pscustomobject]@{Root=$root;Receipt=[pscustomobject]@{PowerStateOwned=$true}})
        }
        if (!$task) { continue }
        Stop-ONEXTweaksReleaseTask $task $name
        if ($Journal.Schema -eq 1 -and !$row) { $scheduler.Tasks.DeleteTask($name,0) }
    }
    if (Test-Path -LiteralPath $root) {
        Stop-ONEXTweaksOwnedBridgeProcesses $activePlan
    }
}

function Enable-ONEXTweaksHotfixRuntime($Receipt, [string[]]$Enabled, [string[]]$Missing) {
    foreach ($row in $Receipt.Tasks) {
        if ($row.Name -in $Missing) { continue }
        $task = Get-ONEXTweaksHotfixTask $row
        $run = $row.Name -in $Enabled
        if ($task.Enabled -ne $run) { $task.Enabled = $run }
    }
}

function Assert-ONEXTweaksHotfixFolder($Plan) {
    $transient = @('power-agent-result.json','installer-hardware-ready.json','installer-hardware-ready.json.tmp',
        'onexconsole-resume-error.json','hardware-health.jsonl','hardware-health.jsonl.prev','fan-control-journal','fan-control-journal.new',
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

function Assert-ONEXTweaksRuntimeTree($Rows, [string]$Directory, [bool]$Hardware) {
    Assert-ONEXTweaksProtectedItem $Directory
    $paths = @{}
    foreach ($row in $Rows) { $paths[(Join-Path $Directory $row.Path)] = $row }
    Assert-ONEXTweaksHotfixFolder ([pscustomobject]@{
        Root=$Directory;Files=$paths;Receipt=[pscustomobject]@{PowerStateOwned=$Hardware}
    })
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

function Remove-ONEXTweaksRuntimeScratch([string]$Path, $Rows, [bool]$Hardware) {
    if (Test-Path -LiteralPath $Path) {
        Assert-ONEXTweaksRuntimeTree $Rows $Path $Hardware
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Restore-ONEXTweaksRuntimeUpdate($Journal, $Previous, [string]$Saved, [string]$Staged) {
    $missingFiles = @(if ($Journal.Schema -eq 2) { $Journal.MissingFiles })
    $missingTasks = @(if ($Journal.Schema -eq 2) { $Journal.MissingTasks })
    $hadRoot = $Journal.Schema -eq 1 -or $Journal.HadRoot
    $candidateRows = if ($Journal.Schema -eq 2) { $Journal.CandidateFiles } else { $Previous.Files }
    $candidateHardware = [bool]($Previous.PowerStateOwned -or ($candidateRows.Path -like 'PowerAgent\*'))
    if ($Journal.Schema -eq 1 -and $Journal.AddHardware) { $candidateHardware = $true }
    $maintenance = Open-ONEXTweaksPackageMaintenance $package
    try {
        $hasBackup = [IO.Directory]::Exists($Saved)
        if ($hasBackup) {
            Assert-ONEXTweaksRuntimeFiles $Previous.Files $Saved $missingFiles
            Assert-ONEXTweaksRuntimeTree $Previous.Files $Saved $Previous.PowerStateOwned
        }
        if ($hasBackup -or !$hadRoot) {
            if ([IO.Directory]::Exists($root)) {
                if ($Journal.Schema -eq 2) {
                    Assert-ONEXTweaksRuntimeFiles $candidateRows $root
                    Assert-ONEXTweaksRuntimeTree $candidateRows $root $candidateHardware
                }
                Stop-ONEXTweaksHotfixRuntime $Previous $Journal $true
            }
            if ($Journal.Schema -eq 2) {
                foreach ($name in $Journal.NewTasks) {
                    $task = Get-ONEXTweaksNewRuntimeTask $Journal $name
                    if ($task) {
                        Stop-ONEXTweaksReleaseTask $task $name
                        $scheduler.Tasks.DeleteTask($name,0)
                    }
                }
            }
            if (!$Journal.HadHandoff -and [IO.File]::Exists($handoffPath)) {
                $null = Invoke-ONEXTweaksOneXConsoleHandoff -Operation Restore -StateDirectory $state -UserSid $OwnerSid -SchedulerFolder $scheduler.Tasks
            }
            if ([IO.Directory]::Exists($root)) {
                if ($Journal.Schema -eq 2) { Assert-ONEXTweaksRuntimeTree $candidateRows $root $candidateHardware }
                Move-ONEXTweaksReleasedRuntime $root ($Staged + '-discard')
            }
            if ($hasBackup) { Move-ONEXTweaksReleasedRuntime $Saved $root }
        }
        if ($hadRoot) { Assert-ONEXTweaksRuntimeFiles $Previous.Files $root $missingFiles }
        Write-ONEXTweaksRuntimeRecord $receiptPath $Previous
        Enable-ONEXTweaksHotfixRuntime $Previous $Journal.Enabled $missingTasks
    } finally { $maintenance.Dispose() }
    if ('ONEXTweaks-Startup' -notin $missingTasks -and 'CompanionLauncher.exe' -notin $missingFiles) {
        $startup = Get-ONEXTweaksHotfixTask ($Previous.Tasks | Where-Object Name -ceq 'ONEXTweaks-Startup')
        if ($startup.Enabled) { $null = $startup.Run($null) }
    }
    if ($Journal.Schema -eq 1) {
        # The beta.3 journal has no candidate inventory. Keep its protected
        # scratch side when its contents cannot be proven by the current receipt.
        foreach ($path in @($Staged,($Staged + '-discard'))) {
            if ([IO.Directory]::Exists($path)) { Move-ONEXTweaksReleasedRuntime $path ($path + '-retained') }
        }
    } else {
        Remove-ONEXTweaksRuntimeScratch $Staged $candidateRows $candidateHardware
        Remove-ONEXTweaksRuntimeScratch ($Staged + '-discard') $candidateRows $candidateHardware
    }
    [IO.File]::Delete($journalPath)
}

function Assert-ONEXTweaksRuntimeFiles($Rows, [string]$Directory, [string[]]$Missing) {
    Assert-ONEXTweaksProtectedItem $Directory
    foreach ($row in $Rows) {
        $path = Get-ONEXTweaksOwnedFile $Directory $row
        if ([IO.File]::Exists($path) -eq ($row.Path -in $Missing)) { throw "Runtime recovery state changed: $($row.Path)" }
    }
}

function Assert-ONEXTweaksRuntimeRepairJournal($Journal, [string]$Root, $ExpectedTasks) {
    if ($Journal.Previous.Files -isnot [array] -or $Journal.Previous.Tasks -isnot [array] -or
        $Journal.Previous.PowerStateOwned -isnot [bool] -or $Journal.Enabled -isnot [array] -or $Journal.HadHandoff -isnot [bool]) {
        throw 'The protected runtime update journal has incomplete ownership records.'
    }
    $oldNames = @($Journal.Previous.Tasks.Name)
    $taskNames = @($ExpectedTasks)
    if ($oldNames.Count -lt 4 -or @($oldNames | Where-Object { $_ -cnotin $taskNames }).Count -or
        @($Journal.Enabled | Where-Object { $_ -cnotin $oldNames }).Count) {
        throw 'The protected runtime update journal contains unexpected tasks.'
    }
    if ($Journal.Schema -eq 1) { return }
    if ($Journal.CandidateFiles -isnot [array] -or $Journal.CandidateFiles.Count -lt $Journal.Previous.Files.Count -or
        $Journal.HadRoot -isnot [bool] -or $Journal.MissingFiles -isnot [array] -or $Journal.MissingTasks -isnot [array] -or
        $Journal.NewTasks -isnot [array] -or $Journal.CreatedTasks -isnot [array] -or
        ($Journal.OwnerRemovalDigest -and $Journal.OwnerRemovalDigest -notmatch '^[0-9a-fA-F]{64}$')) {
        throw 'The protected repair journal has incomplete runtime records.'
    }
    $candidatePaths = @($Journal.CandidateFiles.Path)
    $priorPaths = @($Journal.Previous.Files.Path)
    if ('CompanionLauncher.exe' -cnotin $priorPaths -or 'WidgetBridge\ONEXTweaks.WidgetBridge.exe' -cnotin $priorPaths -or
        @($priorPaths | Where-Object { $_ -cnotin $candidatePaths }).Count -or
        @($Journal.MissingFiles | Where-Object { $_ -cnotin $priorPaths }).Count -or
        @($Journal.MissingTasks | Where-Object { $_ -cnotin $oldNames -or $_ -cnotin $Journal.NewTasks }).Count -or
        @($Journal.NewTasks | Where-Object { $_ -cnotin $taskNames -or ($_ -cin $oldNames -and $_ -cnotin $Journal.MissingTasks) }).Count) {
        throw 'The protected repair journal contains unexpected runtime records.'
    }
    foreach ($row in $Journal.CreatedTasks) {
        if ($row.Name -cnotin $Journal.NewTasks -or $row.DefinitionSha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw 'The protected repair journal contains an unexpected task definition.'
        }
    }
    # Verify relative path syntax even when the root currently holds a different payload.
    $pathCheck = "$Root-update-$($Journal.Id)-path-check"
    foreach ($row in $Journal.CandidateFiles) { $null = Get-ONEXTweaksOwnedFile $pathCheck $row }
}

if (!(Test-Path -LiteralPath $state)) { return 'No update' }
Assert-ONEXTweaksProtectedStateDirectory $state
if (Test-Path -LiteralPath (Join-Path $state 'fresh-runtime.json')) {
    throw 'Finish the pending first installation with the same signed Setup.'
}
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
        if ($journal.Schema -notin @(1,2) -or $journal.Id -cnotmatch '^[a-f0-9]{32}$' -or
            $journal.OwnerSid -cne $OwnerSid -or $journal.Manifest -cnotmatch '^[A-Fa-f0-9]{64}$' -or
            $journal.Phase -cnotin @('Switching','Staged','RollingBack','Committed') -or
            $journal.Previous.OwnerSid -cne $OwnerSid -or $journal.Previous.PackageFullName -cne $package) {
            throw 'The protected runtime update journal could not be verified for the signed-in owner.'
        }
        Assert-ONEXTweaksRuntimeRepairJournal $journal $root ($commonTasks + @($hardwareTasks.Keys))
        $journalAction = Get-ONEXTweaksRuntimeJournalAction $journal $Action $RuntimeManifestSha256
        if ($journalAction -ceq 'Pending update') { return $journalAction }
        $saved = "$root-update-$($journal.Id)-old"
        $staged = "$root-update-$($journal.Id)-new"
        if ($journalAction -ceq 'Finalize') {
            $committed = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly
            if (!$committed -or $committed.Receipt.InstallId -cne $journal.Previous.InstallId -or
                $committed.Receipt.OwnerSid -cne $OwnerSid) { throw 'The runtime receipt changed during the update.' }
            if ($journal.Phase -cne 'Committed') {
                $expected = if ($journal.Schema -eq 2) { $journal.CandidateFiles } else { $committed.Receipt.Files }
                Assert-ONEXTweaksRuntimeFiles $expected $root
                Assert-ONEXTweaksRuntimeTree $expected $root $committed.Receipt.PowerStateOwned
                foreach ($row in $committed.Receipt.Tasks) { $null = Get-ONEXTweaksHotfixTask $row }
            }
            $journal.Phase = 'Committed'
            Write-ONEXTweaksRuntimeRecord $journalPath $journal
            if ($journal.Schema -eq 2) { Remove-ONEXTweaksRuntimeRemovalMarker $journal.OwnerRemovalDigest }
            Remove-ONEXTweaksRuntimeScratch $saved $journal.Previous.Files $journal.Previous.PowerStateOwned
            [IO.File]::Delete($journalPath)
            return 'Committed'
        }
        if ($journalAction -ceq 'Restore') {
            if ($journal.Phase -cne 'RollingBack') {
                $journal.Phase = 'RollingBack'
                Write-ONEXTweaksRuntimeRecord $journalPath $journal
            }
            Restore-ONEXTweaksRuntimeUpdate $journal $journal.Previous $saved $staged
            return 'Rolled back'
        }
        return 'Staged'
    }
    if ($Action -in 'Commit','Rollback') { return 'No update' }
    $plan = Get-ONEXTweaksUninstallPlan $state $root -OwnerOnly
    if (!$plan) { return 'No update' }
    if ($plan.Receipt.OwnerSid -cne $OwnerSid -or $plan.Receipt.PackageFullName -cne $package) { throw 'The installed runtime belongs to another release or desktop.' }
    $id = [guid]::NewGuid().ToString('N')
    $saved = "$root-update-$id-old"
    $staged = "$root-update-$id-new"
    $hardware = (Get-ONEXTweaksHardwareCompatibility) -eq 'Supported'
    $files = Get-ONEXTweaksRuntimePayload $manifest $manifestItem.DirectoryName $staged $hardware
    if (Test-Path -LiteralPath (Join-Path $state 'upgrade-1109.json')) { throw 'Finish the earlier migration before updating the runtime.' }
    $hadRoot = Test-Path -LiteralPath $root -PathType Container
    if ($hadRoot) { Assert-ONEXTweaksHotfixFolder $plan }
    elseif (Test-Path -LiteralPath $root) { throw 'A file already occupies the runtime directory.' }
    if (!$plan.Receipt.PSObject.Properties['PowerStateOwned']) { throw 'The previous runtime ownership is incomplete.' }
    $addHardware = $hardware -and !$plan.Receipt.PowerStateOwned
    $expectedTasks = @($commonTasks)
    if ($plan.Receipt.PowerStateOwned) { $expectedTasks += @($hardwareTasks.Keys) }
    if ($plan.Tasks.Count -ne $expectedTasks.Count -or @($expectedTasks | Where-Object { !$plan.Tasks.ContainsKey($_) }).Count) {
        throw 'The old installation does not own the expected set of tasks.'
    }
    $hardwareAdditions = @(if ($addHardware) { $hardwareTasks.Keys })
    $snapshot = Get-ONEXTweaksRuntimeTaskSnapshot $scheduler.Tasks $plan.Receipt.Tasks $hardwareAdditions $OwnerSid
    $missingFiles = @(Get-ONEXTweaksMissingRuntimeFiles $plan $files)
    $newTasks = @($snapshot.Missing) + @($hardwareAdditions)
    $ownerRemoval = (Get-ONEXTweaksRuntimeRemovalMarker) -ceq $plan.Digest
    if (!(Get-ONEXTweaksRuntimeDifference $plan $files $missingFiles) -and !$newTasks.Count -and !$ownerRemoval) { return 'Current' }
    if ($Action -in @('Assess','Inspect')) { return 'Update required' }
    $hadHandoff = Test-Path -LiteralPath $handoffPath
    if ($hadHandoff) {
        Assert-ONEXTweaksProtectedItem $handoffPath
        $handoff = [IO.File]::ReadAllText($handoffPath) | ConvertFrom-Json
        if ($handoff.Phase -cnotin 'Disabled','Released','PreDisabled') { throw 'Finish the pending OneXConsole handoff first.' }
    }
    $rtss = if ('ONEXTweaks-RTSS' -in $newTasks) { Get-ONEXTweaksRtssExecutable } else { '' }
    $enabledAfter = @($snapshot.Enabled) + $newTasks
    if ($ownerRemoval) {
        $enabledAfter += @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-PowerAgent','ONEXTweaks-ControllerInputRecovery')
    }
    $journal = [pscustomobject]@{Schema=2;Id=$id;OwnerSid=$OwnerSid;Manifest=$RuntimeManifestSha256;Phase='Switching';
        Previous=$plan.Receipt;CandidateFiles=@($files.Values | ForEach-Object Row);
        Enabled=@($snapshot.Enabled);HadHandoff=$hadHandoff;HadRoot=$hadRoot;MissingFiles=$missingFiles;
        MissingTasks=@($snapshot.Missing);NewTasks=$newTasks;CreatedTasks=@();GuardedTaskCreation=$true;
        OwnerRemovalDigest=$(if ($ownerRemoval) { $plan.Digest } else { '' })}
    foreach ($scratch in @($saved,$staged,($staged + '-discard'))) {
        if (Test-Path -LiteralPath $scratch) { throw "A runtime transaction path already exists: $scratch" }
    }
    Write-ONEXTweaksRuntimeRecord $journalPath $journal
    New-ONEXTweaksProtectedDirectory $staged
    foreach ($item in $files.Values) {
        $path = Join-Path $staged $item.Row.Path
        $null = [IO.Directory]::CreateDirectory((Split-Path -LiteralPath $path))
        [IO.File]::Copy($item.Source,$path)
        $null = Get-ONEXTweaksOwnedFile $staged $item.Row
    }
    $maintenance = Open-ONEXTweaksPackageMaintenance $package
    try {
        Stop-ONEXTweaksHotfixRuntime $plan.Receipt $journal $false
        if ($hadRoot) { Assert-ONEXTweaksHotfixFolder $plan }
        if ($hardware) {
            foreach ($name in $configuration) {
                $source = Join-Path $root "PowerAgent\$name"
                if (!(Test-Path -LiteralPath $source) -and $plan.Receipt.PowerStateOwned) {
                    $retained = Join-Path $state "RetainedHardwareSettings\$($plan.Receipt.InstallId)"
                    $source = Join-Path $retained $name
                    if (Test-Path -LiteralPath $source) {
                        Assert-ONEXTweaksProtectedItem (Split-Path -LiteralPath $retained)
                        Assert-ONEXTweaksProtectedItem $retained
                    }
                }
                if (!(Test-Path -LiteralPath $source)) { continue }
                Assert-ONEXTweaksProtectedItem $source
                [IO.File]::Copy($source,(Join-Path $staged "PowerAgent\$name"))
            }
            $settings = Join-Path $staged 'PowerAgent\home-oem-settings.json'
            if (![IO.File]::Exists($settings)) { [IO.File]::Copy((Join-Path $staged 'PowerAgent\home-oem-seed.json'),$settings) }
        }
        if ($hadRoot) { Move-ONEXTweaksReleasedRuntime $root $saved }
        Move-ONEXTweaksReleasedRuntime $staged $root
        foreach ($name in $newTasks) {
            $definition = Get-ONEXTweaksReleaseTaskDefinition $name $root $OwnerSid $rtss
            $task = Register-ONEXTweaksReleaseTask $scheduler $name $definition $OwnerSid $id -Guarded
            if (!$task) { throw "Windows did not register the repaired task: $name" }
            Assert-ONEXTweaksPendingTaskOwnership $task $definition.Purpose $id
            $journal.CreatedTasks += [pscustomobject]@{Name=$name;DefinitionSha256=(Get-ONEXTweaksTaskXmlDigest $task.Xml)}
            Write-ONEXTweaksRuntimeRecord $journalPath $journal
            Set-ONEXTweaksReleaseTaskRights $task $definition.Purpose $OwnerSid
        }
        $next = ($plan.Receipt | ConvertTo-Json -Depth 8) | ConvertFrom-Json
        $next.Files = @($files.Values | ForEach-Object Row)
        $next.Tasks = @($plan.Receipt.Tasks | Where-Object Name -cnotin $newTasks) + $journal.CreatedTasks
        $next.PowerStateOwned = $hardware
        Write-ONEXTweaksRuntimeRecord $receiptPath $next
        Enable-ONEXTweaksHotfixRuntime $next $enabledAfter
        $journal.Phase = 'Staged'
        Write-ONEXTweaksRuntimeRecord $journalPath $journal
    } finally { $maintenance.Dispose() }
    return 'Staged'
} finally { $lock.Dispose() }
