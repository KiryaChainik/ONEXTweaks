#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
. (Join-Path $PSScriptRoot 'Update-ONEXTweaksRuntime.ps1') -Action Inspect -OwnerSid 'S-1-5-21-1-2-3-4' -RuntimeManifest fixture -RuntimeManifestSha256 ('A' * 64)
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'Initialize-ONEXTweaksReleaseRuntime.ps1') -RuntimeManifest fixture -RuntimeManifestSha256 ('A' * 64)

function Assert-UpdateTest([bool]$Passed, [string]$Message) {
    if (!$Passed) { throw $Message }
}

# Exercise the same scheduler inventory that runs before any desktop phase.
function Get-ONEXTweaksComTask($Folder, [string]$Name) {
    if ($Folder -ne 'fixture') { throw 'Unexpected scheduler.' }
    $live[$Name]
}

$temporary = $root
try {
    [void][IO.Directory]::CreateDirectory($root)
    $launcher = Join-Path $root 'CompanionLauncher.exe'
    [IO.File]::WriteAllText($launcher,'owned launcher')
    $old = [pscustomobject]@{Path='CompanionLauncher.exe';Sha256=('A' * 64)}
    $receipt = [pscustomobject]@{Files=@($old);PowerStateOwned=$false;InstallId=[guid]::NewGuid().ToString()}
    $plan = [pscustomobject]@{Root=$root;Receipt=$receipt}
    $candidate = @{'CompanionLauncher.exe' = @{Row=$old}}
    Assert-UpdateTest (!(Get-ONEXTweaksRuntimeDifference $plan $candidate)) 'The same payload would cause a second update.'
    $candidate['PowerAgent\ONEXTweaks.PowerAgent.exe'] = @{Row=@{Path='PowerAgent\ONEXTweaks.PowerAgent.exe';Sha256=('B' * 64)}}
    Assert-UpdateTest (Get-ONEXTweaksRuntimeDifference $plan $candidate) 'The update skipped an installation without PowerAgent.'
    $candidate.Remove('PowerAgent\ONEXTweaks.PowerAgent.exe')
    $candidate['CompanionLauncher.exe'] = @{Row=@{Path=$old.Path;Sha256=('B' * 64)}}
    Assert-UpdateTest (Get-ONEXTweaksRuntimeDifference $plan $candidate) 'The update skipped replacement at the same package version.'
    $candidate['CompanionLauncher.exe'] = @{Row=$old}
    [IO.File]::Delete($launcher)
    Assert-UpdateTest (Get-ONEXTweaksRuntimeDifference $plan $candidate) 'An owned missing runtime file was treated as current.'
    Assert-UpdateTest ((@(Get-ONEXTweaksMissingRuntimeFiles $plan $candidate) -join ',') -ceq $old.Path) 'The file snapshot omitted original absence.'

    $xml = '<Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Settings><Enabled>true</Enabled></Settings><Actions><Exec><Command>original.exe</Command></Exec></Actions></Task>'
    $digest = Get-ONEXTweaksTaskXmlDigest ([xml]$xml)
    $expected = @(
        'ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-RTSS' | ForEach-Object {
            [pscustomobject]@{Name=$_;DefinitionSha256=$digest}
        }
    )
    $live = @{
        'ONEXTweaks-Startup' = [pscustomobject]@{Xml=$xml.Replace('<Enabled>true','<Enabled>false');Enabled=$false}
        'ONEXTweaks-WidgetBridge' = [pscustomobject]@{Xml=$xml;Enabled=$true}
    }
    $snapshot = Get-ONEXTweaksRuntimeTaskSnapshot 'fixture' $expected @('ONEXTweaks-PowerAgent','ONEXTweaks-OneXConsoleHandoff')
    Assert-UpdateTest (($snapshot.Missing -join ',') -ceq 'ONEXTweaks-ControllerInputRecovery,ONEXTweaks-RTSS') 'The task scan stopped at the first missing task.'
    Assert-UpdateTest (($snapshot.Enabled -join ',') -ceq 'ONEXTweaks-WidgetBridge') 'The snapshot lost the intentional startup state.'
    $live['ONEXTweaks-RTSS'] = [pscustomobject]@{Xml=$xml.Replace('original.exe','foreign.exe');Enabled=$true}
    $live['ONEXTweaks-PowerAgent'] = [pscustomobject]@{Xml=$xml;Enabled=$true}
    $rejected = $false
    try { $null = Get-ONEXTweaksRuntimeTaskSnapshot 'fixture' $expected @('ONEXTweaks-PowerAgent') }
    catch { $rejected = $_.Exception.Message -clike '*ONEXTweaks-RTSS, ONEXTweaks-PowerAgent*' }
    Assert-UpdateTest $rejected 'The scan accepted a changed task or adopted an unowned task.'
    $manifest = 'A' * 64
    $pending = [pscustomobject]@{Manifest=$manifest;Phase='Staged'}
    Assert-UpdateTest ((Get-ONEXTweaksRuntimeJournalAction $pending Inspect $manifest) -ceq 'Staged') 'A finished stage could not be resumed.'
    Assert-UpdateTest ((Get-ONEXTweaksRuntimeJournalAction $pending Inspect ('B' * 64)) -ceq 'Restore') 'A different signed Setup would reuse an earlier staged payload.'
    Assert-UpdateTest ((Get-ONEXTweaksRuntimeJournalAction $pending Rollback $manifest) -ceq 'Restore') 'Rollback would not start.'
    $pending.Phase = 'RollingBack'
    foreach ($action in 'Inspect','Stage','Commit','Rollback') {
        Assert-UpdateTest ((Get-ONEXTweaksRuntimeJournalAction $pending $action $manifest) -ceq 'Restore') "An interrupted rollback could be bypassed by $action."
    }
    $pending.Phase = 'Committed'
    Assert-UpdateTest ((Get-ONEXTweaksRuntimeJournalAction $pending Inspect ('B' * 64)) -ceq 'Finalize') 'A committed update would be rolled back by a different signed Setup.'
    if ($env:OS -eq 'Windows_NT') {
        $transactionId = [guid]::NewGuid().ToString('N')
        $prepared = [pscustomobject]@{Name='ONEXTweaks-RTSS'; Enabled=$false;
            Definition=@{RegistrationInfo=@{Description="ONEXTweaks Overlay $transactionId"}};
            Descriptor='O:BAD:PAI(A;;FA;;;SY)(A;;FA;;;BA)'}
        $prepared | Add-Member -MemberType ScriptMethod -Name GetSecurityDescriptor -Value { $this.Descriptor }
        Assert-ONEXTweaksPendingTaskOwnership $prepared Overlay $transactionId
        $prepared.Descriptor += '(A;;FA;;;BU)'
        $blocked = $false
        try { Assert-ONEXTweaksPendingTaskOwnership $prepared Overlay $transactionId }
        catch { $blocked = $_.Exception.Message -like '*no verifiable repair ownership*' }
        Assert-UpdateTest $blocked 'A writable unrecorded task could be claimed after a crash.'
        $desktopSid = 'S-1-5-21-1-2-3-4'
        $prepared.Descriptor = "O:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;$desktopSid)(A;;FR;;;$desktopSid)"
        Assert-UpdateTest (Test-ONEXTweaksReleaseTaskAccess $prepared $desktopSid -RequireOwnerAccess) 'The shipped RTSS task ACL was rejected.'
        $prepared.Descriptor += '(A;;FA;;;BU)'
        Assert-UpdateTest (!(Test-ONEXTweaksReleaseTaskAccess $prepared $desktopSid -RequireOwnerAccess)) 'Changing only an ACL was treated as unchanged.'
    }
    $journal = Join-Path $root 'runtime-update-1110.json'
    Write-ONEXTweaksRuntimeRecord $journal @{Phase='Switching';Previous=$receipt}
    Write-ONEXTweaksRuntimeRecord $journal @{Phase='Staged';Previous=$receipt}
    $record = [IO.File]::ReadAllText($journal) | ConvertFrom-Json
    Assert-UpdateTest ($record.Phase -ceq 'Staged' -and $record.Previous.InstallId -ceq $receipt.InstallId) 'The runtime journal lost the original installation.'
    Assert-UpdateTest (!(Get-ChildItem -LiteralPath $root -Filter '*.tmp')) 'The journal left temporary files.'

    # Run the real filesystem rollback with only Windows package and scheduler calls substituted.
    $parsed = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Update-ONEXTweaksRuntime.ps1'),[ref]$null,[ref]$null)
    $rollbackNames = @('Restore-ONEXTweaksRuntimeUpdate','Assert-ONEXTweaksRuntimeFiles','Assert-ONEXTweaksRuntimeTree',
        'Assert-ONEXTweaksHotfixFolder','Remove-ONEXTweaksRuntimeScratch')
    foreach ($definition in $parsed.FindAll({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $rollbackNames
    },$false)) {
        . ([scriptblock]::Create($definition.Extent.Text))
    }
    function Assert-ONEXTweaksProtectedItem([string]$Path) { }
    function Open-ONEXTweaksPackageMaintenance([string]$Package) { [IO.MemoryStream]::new() }
    function Stop-ONEXTweaksHotfixRuntime($Previous, $Journal, [bool]$CandidateActive) { }
    function Enable-ONEXTweaksHotfixRuntime($Receipt, [string[]]$Enabled, [string[]]$Missing) { }
    function Move-ONEXTweaksReleasedRuntime([string]$Source, [string]$Destination) {
        if ($Source -ceq $saved -and $fault.BlockRestore) { throw 'Interrupted before restoring saved runtime' }
        [IO.Directory]::Move($Source,$Destination)
    }
    $root = Join-Path $temporary 'Current'
    $saved = Join-Path $temporary 'Saved'
    $staged = Join-Path $temporary 'Staged'
    $receiptPath = Join-Path $temporary 'owned-release.json'
    $handoffPath = Join-Path $temporary 'onexconsole-handoff.json'
    $journalPath = Join-Path $temporary 'runtime-update-1110.json'
    $package = 'fixture'
    $configuration = @()
    $fault = @{BlockRestore=$true}
    [void][IO.Directory]::CreateDirectory($root)
    [void][IO.Directory]::CreateDirectory($saved)
    [IO.File]::WriteAllText((Join-Path $saved 'CompanionLauncher.exe'),'previous')
    [IO.File]::WriteAllText((Join-Path $root 'CompanionLauncher.exe'),'candidate')
    $receipt.Files = @(@{Path='CompanionLauncher.exe';Sha256=(Get-FileHash -LiteralPath (Join-Path $saved 'CompanionLauncher.exe')).Hash})
    $rollback = [pscustomobject]@{Schema=2;HadRoot=$true;HadHandoff=$false;Enabled=@();MissingFiles=@();
        NewTasks=@();MissingTasks=@('ONEXTweaks-Startup');CandidateFiles=@(
            [pscustomobject]@{Path='CompanionLauncher.exe';Sha256=(Get-FileHash -LiteralPath (Join-Path $root 'CompanionLauncher.exe')).Hash})}
    $unknown = Join-Path $root 'unrelated.txt'
    [IO.File]::WriteAllText($unknown,'keep')
    $kept = $false
    try { Restore-ONEXTweaksRuntimeUpdate $rollback $receipt $saved $staged }
    catch { $kept = $_.Exception.Message -like '*unowned file:*' -and [IO.File]::Exists($unknown) }
    Assert-UpdateTest $kept 'Rollback would discard an unrelated file in the active runtime.'
    [IO.File]::Delete($unknown)
    $interrupted = $false
    try { Restore-ONEXTweaksRuntimeUpdate $rollback $receipt $saved $staged }
    catch { $interrupted = $_.Exception.Message -ceq 'Interrupted before restoring saved runtime' }
    Assert-UpdateTest ($interrupted -and ![IO.Directory]::Exists($root) -and [IO.Directory]::Exists($saved)) 'Rollback did not reach the interrupted rename fixture.'
    $fault.BlockRestore = $false
    Restore-ONEXTweaksRuntimeUpdate $rollback $receipt $saved $staged
    Assert-UpdateTest ([IO.File]::ReadAllText((Join-Path $root 'CompanionLauncher.exe')) -ceq 'previous' -and
        [IO.File]::Exists($receiptPath) -and ![IO.Directory]::Exists($staged + '-discard')) 'Retry did not restore and clean the previous runtime.'

    & {
        $freshRoot = Join-Path $temporary 'FreshRoot'
        $freshState = Join-Path $temporary 'FreshState'
        $freshJournal = Join-Path $freshState 'fresh-runtime.json'
        $freshReceipt = Join-Path $freshState 'owned-release.json'
        $source = Join-Path $temporary 'signed-source.exe'
        [void][IO.Directory]::CreateDirectory($freshState)
        [IO.File]::WriteAllText($source,'from the verified payload')
        $relative = 'CompanionLauncher.exe'
        $payload = @{}
        $payload[$relative] = @{Source=$source;Row=@{Path=$relative;Sha256=(Get-FileHash -LiteralPath $source).Hash}}
        $owner = 'S-1-5-21-1-2-3-4'
        $selection = [pscustomobject]@{OwnerSid=$owner;InstallId=[guid]::NewGuid().ToString('N')}
        $fixture = @{Interrupt=$true;Task=$null}
        $schedulerTasks = [pscustomobject]@{Fixture=$fixture}
        $schedulerTasks | Add-Member -MemberType ScriptMethod -Name DeleteTask -Value { $this.Fixture.Task = $null }
        $folder = @{Tasks=$schedulerTasks}
        function Get-ONEXTweaksUninstallPlan { }
        function Get-ScheduledTask { }
        function New-ONEXTweaksProtectedDirectory([string]$Path) { [void][IO.Directory]::CreateDirectory($Path) }
        function Get-ONEXTweaksComTask { $fixture.Task }
        function Assert-ONEXTweaksPendingTaskOwnership { }
        function Set-ONEXTweaksReleaseTaskRights { }
        function Register-ONEXTweaksReleaseTask($Folder, [string]$Name, $Definition, [string]$Sid, [string]$TransactionId, [switch]$Guarded) {
            $fixture.Task = [pscustomobject]@{Name=$Name;Xml=$xml;Enabled=$false}
            if ($fixture.Interrupt) { throw 'Interrupted before recording the first task' }
            return $fixture.Task
        }
        $taskName = 'ONEXTweaks-Startup'
        $interrupted = $false
        try { Install-ONEXTweaksFirstRuntime $folder $selection $payload @($taskName) $false '' $freshRoot $freshState $freshJournal $manifest }
        catch { $interrupted = $_.Exception.Message -ceq 'Interrupted before recording the first task' }
        Assert-UpdateTest ($interrupted -and [IO.File]::Exists($freshJournal) -and ![IO.File]::Exists($freshReceipt) -and
            $fixture.Task -and !$fixture.Task.Enabled) 'First install failed without leaving sufficient recovery state.'
        [IO.File]::WriteAllText((Join-Path $freshRoot $relative),'an interrupted copy')
        $unexpected = Join-Path $freshRoot 'unknown.txt'
        [IO.File]::WriteAllText($unexpected,'keep')
        $rejected = $false
        try { Install-ONEXTweaksFirstRuntime $folder $selection $payload @($taskName) $false '' $freshRoot $freshState $freshJournal $manifest }
        catch { $rejected = $_.Exception.Message -like '*unrelated file*' }
        Assert-UpdateTest ($rejected -and [IO.File]::Exists($unexpected) -and $fixture.Task) 'First-install recovery touched unrelated files or tasks.'
        [IO.File]::Delete($unexpected)
        $fixture.Interrupt = $false
        Install-ONEXTweaksFirstRuntime $folder $selection $payload @($taskName) $false '' $freshRoot $freshState $freshJournal $manifest
        $retained = @(Get-ChildItem -LiteralPath $temporary -Directory -Filter 'FreshRoot-recovery-*')
        Assert-UpdateTest ($retained.Count -eq 1 -and
            [IO.File]::ReadAllText((Join-Path $retained[0].FullName $relative)) -ceq 'an interrupted copy') 'Recovery destroyed data from a changed runtime file.'
        Assert-UpdateTest ([IO.File]::ReadAllText((Join-Path $freshRoot $relative)) -ceq 'from the verified payload' -and
            ![IO.File]::Exists($freshJournal) -and [IO.File]::Exists($freshReceipt) -and $fixture.Task.Enabled) 'Retry could not complete the first runtime installation.'
    }
    'PASS runtime replacement, combined absent files and tasks, intentional disablement, conflicts, and interrupted update and rollback routing.'
} finally {
    if ([IO.Directory]::Exists($temporary)) { [IO.Directory]::Delete($temporary,$true) }
}
