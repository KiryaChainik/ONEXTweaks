#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName='Report')]
param(
    [Parameter(Mandatory,ParameterSetName='Upgrade')][switch]$Upgrade,
    [Parameter(Mandatory,ParameterSetName='Recover')][switch]$Recover,
    [Parameter(Mandatory,ParameterSetName='Upgrade')][Parameter(Mandatory,ParameterSetName='Report')][string]$WidgetPackage,
    [Parameter(Mandatory,ParameterSetName='Upgrade')][Parameter(Mandatory,ParameterSetName='Report')][string]$RollbackPackage,
    [Parameter(Mandatory,ParameterSetName='Upgrade')][Parameter(Mandatory,ParameterSetName='Report')]
    [Alias('RuntimeManifest')][string]$BundleRuntimeManifest,
    [Parameter(Mandatory,ParameterSetName='Upgrade')][Parameter(Mandatory,ParameterSetName='Report')]
    [Alias('RuntimeManifestSha256')][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$BundleRuntimeManifestSha256,
    [Parameter(ParameterSetName='Upgrade')][ValidateRange(1,600)][int]$RuntimeTimeoutSeconds=210
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or ![Environment]::Is64BitProcess) { throw 'Use x64 Windows PowerShell 5.1.' }
$oldPackage = 'ONEXTweaks.Widget_0.29.1109.0_x64__3a23eh8tyg7na'
$newPackage = 'ONEXTweaks.Widget_0.29.1110.0_x64__3a23eh8tyg7na'
$family = 'ONEXTweaks.Widget_3a23eh8tyg7na'
$oldHash = '1D67F65DBF8EF0F9ED5ACD6B56A4C82C0080333B031E846207FA5BE76A61EA81'
$newHash = '719252E33EA6A0D4175AEFC1EBB08465AB554985C677D5ED0CDB6ACD0AF3F578'
$names = @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-PowerAgent','ONEXTweaks-RTSS')
$hardwareSettings = @('home-oem-settings.json','led-color-profile.json','boost-modes.json')

. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'DriverPrerequisites.ps1')
. (Join-Path $PSScriptRoot 'ONEXTweaksLegacy1109.ps1')
$initialize = Join-Path $PSScriptRoot 'Initialize-ONEXTweaksReleaseRuntime.ps1'
. $initialize -Prepare -WidgetPackageFullName $newPackage
$install = Join-Path $PSScriptRoot 'Install-ONEXTweaksWidget.ps1'

$owner = Get-ONEXTweaksInteractiveSid
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { if ($identity.User.Value -cne $owner) { throw 'Run the update from the widget owner desktop using that same account.' } }
finally { $identity.Dispose() }
$local = [Environment]::GetFolderPath('LocalApplicationData')
$root = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
$state = Get-ONEXTweaksReleaseStateDirectory
$parent = Split-Path -LiteralPath $state
$active = Join-Path $state 'upgrade-1109.json'
$ownership = Join-Path $state 'owned-release.json'
$enrollment = Join-Path $state 'desktop-enrollment.json'
$lease = Join-Path $local 'ONEXTweaks\dev\privateReports\controller-capture-lease.json'
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$tasks = $service.GetFolder('\')

function Get-ONEXTweaksUpgradePackage {
    $registered = @(Get-AppxPackage -Name ONEXTweaks.Widget -PackageTypeFilter Main)
    if ($registered.Count -ne 1 -or $registered[0].Status -ne 'Ok' -or $registered[0].PackageFullName -cnotin @($oldPackage,$newPackage)) {
        throw 'Expected one healthy, known ONEXTweaks package for the owner.'
    }
    $registered[0].PackageFullName
}

function Assert-ONEXTweaksUpgradeFile([string]$Path,[string]$Expected) {
    $file = Get-Item -LiteralPath $Path -Force
    if ($file -isnot [IO.FileInfo] -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        (Get-FileHash -LiteralPath $file.FullName).Hash -cne $Expected.ToUpperInvariant()) { throw "Upgrade media hash differs: $Path" }
    $file.FullName
}

function Get-ONEXTweaksUpgradeProfiles {
    $dev = Join-Path $local 'ONEXTweaks\dev'
    if (!(Test-Path -LiteralPath $dev -PathType Container)) { return }
    foreach ($directory in @((Split-Path -LiteralPath $dev),$dev)) {
        if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Unexpected profile directory reference: $directory"
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $dev -File -Filter '*profiles*.json') {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected profile reference: $($file.Name)" }
        [pscustomobject]@{Name=$file.Name; Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
    }
}

function Assert-ONEXTweaksUpgradeProfiles($Saved) {
    $before = @($Saved | Sort-Object Name)
    if (!$before.Count) { return }
    $current = @(Get-ONEXTweaksUpgradeProfiles | Where-Object Name -in $before.Name | Sort-Object Name)
    if (($before | ConvertTo-Json -Compress) -cne ($current | ConvertTo-Json -Compress)) {
        throw 'One or more existing profile files differ from the saved migration baseline.'
    }
}

function Backup-ONEXTweaksUpgradeProfiles([string]$Id, $Profiles) {
    if (!@($Profiles).Count) { return }
    $app = Join-Path $local 'ONEXTweaks'
    $backup = Join-Path $app 'UpgradeBackups'
    $destination = Join-Path $backup $Id
    foreach ($directory in @($backup,$destination)) {
        [void][IO.Directory]::CreateDirectory($directory)
        if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Unexpected upgrade profile backup location.'
        }
    }
    foreach ($profile in $Profiles) {
        $copy = Join-Path $destination $profile.Name
        [IO.File]::Copy((Join-Path $app ('dev\' + $profile.Name)),$copy,$false)
        $null = Assert-ONEXTweaksUpgradeFile $copy $profile.Sha256
    }
}

function Read-ONEXTweaksUpgradeReport([string]$Path) {
    $stream = [IO.File]::Open($Path, 'Open', 'Read', ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        $reader = [IO.StreamReader]::new($stream)
        try { $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Wait-ONEXTweaksUpgradeRuntime([int]$Seconds) {
    $complete = Join-Path $root 'Tools\Installer\Complete-ONEXTweaksRelease.ps1'
    $started = [DateTimeOffset]::UtcNow
    if ($hardware) { Start-ScheduledTask -TaskName ONEXTweaks-PowerAgent -TaskPath '\' }
    Start-ScheduledTask -TaskName ONEXTweaks-RTSS -TaskPath '\'
    Start-ScheduledTask -TaskName ONEXTweaks-Startup -TaskPath '\'
    Start-Process -FilePath "ms-gamebar://launchforeground/activate/$($family)_App_GamingWidget"
    $reports = Join-Path $local 'ONEXTweaks\dev\Reports'
    $status = 'Waiting for a fresh Bridge connection'
    while ([DateTimeOffset]::UtcNow -lt $started.AddSeconds($Seconds)) {
        $current = $null
        $ready = $null
        try {
            $current = Read-ONEXTweaksUpgradeReport (Join-Path $reports 'widget-bridge.json')
            $ready = Read-ONEXTweaksUpgradeReport (Join-Path $reports 'widget-ready.json')
        } catch { }
        if (Test-ONEXTweaksUpgradeReportPair $current $ready $started) {
            $result = & $complete -Complete -SameBootOnly -Confirm:$false
            if ($result.SetupComplete -and $result.Backend -ceq 'Healthy') { return $result }
            $status = $result.Status
            if ($status -ceq 'Windows restart required') { break }
        }
        Start-Sleep -Seconds 2
    }
    throw "1110 was not ready before the upgrade deadline: $status."
}

function Get-ONEXTweaksUpgradeArchive($Journal) {
    if ($Journal.Schema -ne 1 -or $Journal.OwnerSid -cne $owner -or $Journal.InstallId -cnotmatch '^[a-f0-9]{32}$') {
        throw 'The pending migration journal does not belong to this desktop.'
    }
    $archive = Join-Path $state ('Legacy1109-' + $Journal.InstallId)
    Assert-ONEXTweaksProtectedItem $archive
    $archive
}

function Restore-ONEXTweaksUpgrade($Journal, [bool]$RestartLegacy) {
    $archive = Get-ONEXTweaksUpgradeArchive $Journal
    $previous = Assert-ONEXTweaksUpgradeFile (Join-Path $archive 'ONEXTweaks-1109.msix') $oldHash
    $parked = Join-Path $archive 'Runtime'
    $plan = Get-ONEXTweaksUninstallPlan $state $root
    if ($plan) {
        if ($plan.Receipt.InstallId -cne $Journal.InstallId -or $plan.Receipt.PackageOwned) {
            throw 'An unrelated or already completed release owns the runtime.'
        }
        $remove = Get-ONEXTweaksVerifiedTool $plan 'Tools\Installer\Uninstall-ONEXTweaksRelease.ps1'
        & $remove -Uninstall -Confirm:$false | Out-Null
    }
    if (Test-Path -LiteralPath $enrollment) {
        Assert-ONEXTweaksProtectedItem $enrollment
        $saved = [IO.File]::ReadAllText($enrollment) | ConvertFrom-Json
        if ($saved.InstallId -cne $Journal.InstallId) { throw 'An unrelated desktop enrollment is present.' }
        [IO.File]::Delete($enrollment)
    }
    if ((Get-ONEXTweaksUpgradePackage) -ceq $newPackage) {
        Add-AppxPackage -Path $previous -ForceUpdateFromAnyVersion -ForceTargetApplicationShutdown
    }
    if ((Get-ONEXTweaksUpgradePackage) -cne $oldPackage) { throw 'Windows did not restore package 1109.' }
    if (Test-Path -LiteralPath $parked -PathType Container) {
        Assert-ONEXTweaksLegacyRuntimeItem $parked
        Assert-ONEXTweaksLegacyRuntimeItem (Join-Path $parked 'PowerAgent')
        if (Test-Path -LiteralPath $root) { throw 'Another runtime occupies the original directory; the legacy runtime is retained.' }
        [IO.Directory]::Move($parked,$root)
    }
    Restore-ONEXTweaksLegacyTasks $tasks $Journal.Tasks
    Assert-ONEXTweaksUpgradeProfiles $Journal.Profiles
    if ($RestartLegacy) {
        foreach ($saved in $Journal.Tasks | Where-Object { $_.Enabled -and $_.WasRunning }) {
            $task = Get-ONEXTweaksLegacyTask $tasks $saved.Name
            if (!$task.GetInstances(0).Count) { $null = $task.Run($null) }
        }
    }
    [IO.File]::Delete($active)
    [pscustomobject]@{Status='Rolled back'; Version='0.29.1109.0'; RestoredTasks=@($Journal.Tasks).Count; ProfilesPreserved=$true}
}

if ($PSCmdlet.ParameterSetName -ne 'Recover') {
    $WidgetPackage = Assert-ONEXTweaksUpgradeFile $WidgetPackage $newHash
    $RollbackPackage = Assert-ONEXTweaksUpgradeFile $RollbackPackage $oldHash
    $BundleRuntimeManifest = Assert-ONEXTweaksUpgradeFile $BundleRuntimeManifest $BundleRuntimeManifestSha256
    $hardware = (Get-ONEXTweaksHardwareCompatibility) -eq 'Supported'
    $manifest = [IO.File]::ReadAllText($BundleRuntimeManifest) | ConvertFrom-Json
    if ($manifest.PackageFullName -cne $newPackage) { throw 'Runtime does not match the target widget.' }
    # Validate every source file without probing the conflicting legacy destination.
    $candidateRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) ('ONEXTweaks-Validate-' + [guid]::NewGuid().ToString('N'))
    $payload = Get-ONEXTweaksRuntimePayload $manifest (Split-Path -LiteralPath $BundleRuntimeManifest) $candidateRoot $hardware
}

$lock = [Threading.Mutex]::new($false,'Global\ONEXTweaks.Release1109Migration.v1')
$held = $false
try {
    try { $held = $lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw 'The 1109 migration is already running.' }
    if (Test-Path -LiteralPath $active) {
        Assert-ONEXTweaksProtectedStateDirectory $state
        Assert-ONEXTweaksProtectedItem $active
        $journal = [IO.File]::ReadAllText($active) | ConvertFrom-Json
        if (!$Recover) { throw 'An interrupted 1109 migration exists. Run this updater with -Recover from the owner desktop.' }
        if ($PSCmdlet.ShouldProcess('ONEXTweaks 1109 migration','Restore the previous registration and runtime')) {
            $null = Get-ONEXTweaksUpgradeArchive $journal
            $completed = Get-ONEXTweaksUninstallPlan $state $root
            if ($completed -and $completed.Receipt.InstallId -ceq $journal.InstallId -and $completed.Receipt.PackageOwned -and
                (Get-ONEXTweaksUpgradePackage) -ceq $newPackage) {
                [IO.File]::Delete($active)
                return [pscustomobject]@{Status='Update already completed'; Version='0.29.1110.0'}
            }
            Restore-ONEXTweaksUpgrade $journal $true
        }
        return
    }
    if ($Recover) { return [pscustomobject]@{Status='No pending migration'} }
    if ((Get-ONEXTweaksUpgradePackage) -cne $oldPackage -or (Test-Path -LiteralPath $ownership) -or
        (Test-Path -LiteralPath $enrollment) -or (Test-Path -LiteralPath (Join-Path $state 'onexconsole-handoff.json'))) {
        throw 'The machine is not a supported legacy 1109 installation.'
    }
    $legacy = @(foreach ($name in $names) { Get-ONEXTweaksLegacyTaskSnapshot $tasks $name $owner $local $root })
    $hadRoot = Test-Path -LiteralPath $root -PathType Container
    if ($hadRoot) {
        Assert-ONEXTweaksLegacyRuntimeItem $root
        $oldPower = Join-Path $root 'PowerAgent'
        Assert-ONEXTweaksLegacyRuntimeItem $oldPower
        foreach ($name in $hardwareSettings + 'ONEXTweaks.PowerAgent.exe') {
            $item = Join-Path $oldPower $name
            if (Test-Path -LiteralPath $item) { Assert-ONEXTweaksLegacyRuntimeItem $item -File }
        }
        $remaining = @(Get-ChildItem -LiteralPath $root -Force | Where-Object Name -ne 'PowerAgent')
        if ($remaining.Count -or 'ONEXTweaks-PowerAgent' -notin @($legacy | ForEach-Object Name)) { throw 'Unrecognized legacy machine runtime layout.' }
    } elseif (Test-Path -LiteralPath $root) {
        throw 'The legacy runtime location is not a directory.'
    }
    if (!$Upgrade -or !$PSCmdlet.ShouldProcess('ONEXTweaks 1109 on the owner desktop','Upgrade to 1110 with recovery')) {
        return [pscustomobject]@{Status='Legacy layout recognized'; Tasks=@($legacy | ForEach-Object Name); Package=$oldPackage;
            Hardware=$hardware; ControllerCaptureActive=(Test-Path -LiteralPath $lease)}
    }

    $completionTool = $payload['Tools\Installer\Complete-ONEXTweaksRelease.ps1']
    if (!$completionTool) { throw 'The verified runtime is missing the release completion tool.' }
    # Load only the shared readiness functions in a child scope. The script's entry point runs from the installed runtime.
    $restartPending = & {
        . $completionTool.Source
        Test-ONEXTweaksMachineRestartPending $state $local
    }
    if ($restartPending) { throw 'Restart Windows before updating ONEXTweaks from 1109 to 1110.' }
    $preflight = & $install -WidgetPackage $WidgetPackage -WidgetSha256 $newHash -TargetUserSid $owner
    if ($preflight.Blockers.Count -or $preflight.RestartRequired) { throw "Widget prerequisites block the upgrade: $($preflight.Blockers -join '; ')" }
    $review = @($preflight.Drivers + $preflight.Applications | Where-Object Status -notin @('Installed','Not applicable','Missing'))
    if ($review.Count) {
        $details = @($review | ForEach-Object { "$($_.Name) ($($_.Status))" }) -join ', '
        throw "Resolve these prerequisites before updating ONEXTweaks: $details"
    }
    foreach ($saved in $legacy) { $saved.Recipe = Find-ONEXTweaksLegacyTaskRecipe $tasks $saved $owner }
    if (!(Test-Path -LiteralPath $state)) {
        New-ONEXTweaksProtectedDirectory $parent
        New-ONEXTweaksProtectedDirectory $state
    }
    Assert-ONEXTweaksProtectedStateDirectory $state
    $id = [guid]::NewGuid().ToString('N')
    $archive = Join-Path $state ('Legacy1109-' + $id)
    New-ONEXTweaksProtectedDirectory $archive
    $backup = Join-Path $archive 'ONEXTweaks-1109.msix'
    [IO.File]::Copy($RollbackPackage,$backup,$false)
    $null = Assert-ONEXTweaksUpgradeFile $backup $oldHash
    $profiles = @(Get-ONEXTweaksUpgradeProfiles)
    Backup-ONEXTweaksUpgradeProfiles $id $profiles
    $journal = [pscustomobject]@{Schema=1; InstallId=$id; OwnerSid=$owner; Tasks=$legacy; Profiles=$profiles}
    Write-ONEXTweaksInstallationRecord (Join-Path $archive 'migration.json') $journal
    Write-ONEXTweaksInstallationRecord $active $journal
    $changed = $false
    try {
        $changed = $true
        Suspend-ONEXTweaksLegacyRuntime $tasks $legacy $local $root $lease $owner
        foreach ($saved in $legacy) {
            $task = Get-ONEXTweaksLegacyTask $tasks $saved.Name
            if (!$task -or $task.Enabled -or !(Test-ONEXTweaksLegacyTaskSnapshot $task $saved $true)) { throw "Legacy task changed before removal: $($saved.Name)" }
            $tasks.DeleteTask($saved.Name,0)
        }
        $parked = Join-Path $archive 'Runtime'
        if ($hadRoot) { [IO.Directory]::Move($root,$parked) }
        $desktop = [pscustomobject]@{Schema=1; InstallId=$id; OwnerSid=$owner; PackageOwned=$false;
            PackageFullName=$newPackage; PackageFamilyName=$family; GuideOwned=$false}
        Write-ONEXTweaksInstallationRecord $enrollment $desktop
        $widget = & $install -WidgetPackage $WidgetPackage -WidgetSha256 $newHash -TargetUserSid $owner -Install -CloseActiveWidget -Confirm:$false
        if (!$widget.WidgetRegistered -or !$widget.PrerequisitesReady -or $widget.RestartRequired) { throw 'Widget setup or prerequisites did not finish.' }
        $null = & $initialize -RuntimeManifest $BundleRuntimeManifest -RuntimeManifestSha256 $BundleRuntimeManifestSha256 -LegacyUpgradeId $id -Confirm:$false
        if ($hardware -and $hadRoot) {
            Assert-ONEXTweaksLegacyRuntimeItem $parked
            Assert-ONEXTweaksLegacyRuntimeItem (Join-Path $parked 'PowerAgent')
            foreach ($name in $hardwareSettings) {
                $file = Join-Path $parked ('PowerAgent\' + $name)
                if (!(Test-Path -LiteralPath $file -PathType Leaf)) { continue }
                Assert-ONEXTweaksLegacyRuntimeItem $file -File
                $destination = Join-Path $root ('PowerAgent\' + $name)
                [IO.File]::Copy($file,$destination,$true)
                Assert-ONEXTweaksProtectedItem $destination
            }
        }
        $status = Wait-ONEXTweaksUpgradeRuntime $RuntimeTimeoutSeconds
        Assert-ONEXTweaksUpgradeProfiles $journal.Profiles
        $release = Get-ONEXTweaksUninstallPlan $state $root
        if ($release.Receipt.InstallId -cne $id) { throw 'The release receipt changed during update.' }
        $release.Receipt.PackageOwned = $true
        $sealed = $ownership + '.' + $id
        Write-ONEXTweaksInstallationRecord $sealed $release.Receipt
        [IO.File]::Replace($sealed,$ownership,[NullString]::Value)
        $changed = $false
        [IO.File]::Delete($active)
        [pscustomobject]@{Status='Updated'; Version='0.29.1110.0'; SetupComplete=$true;
            Backend=$status.Backend; RTSS=$status.RtssSession; OneXConsole=$status.OneXConsoleStartup; ProfilesPreserved=$true}
    } catch {
        $failure = $_
        if ($changed) {
            try { Restore-ONEXTweaksUpgrade $journal $true | Out-Host }
            catch { throw "Update failed: $($failure.Exception.Message) Recovery is still pending: $($_.Exception.Message)" }
        }
        throw $failure
    }
} finally {
    if ($held) { $lock.ReleaseMutex() }
    $lock.Dispose()
}
