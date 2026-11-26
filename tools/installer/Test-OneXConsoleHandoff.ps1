# Fixture-only. Scheduler commands are local functions, including all writes.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleHandoff.ps1')
$dir = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
$savedPrograms = $env:ProgramFiles
$sid = 'S-1-5-21-101-202-303-1001'
$vendorHash = 'A5471B7F07DBFE737A195E7D75B1282786BFB95F9E65B3E1D94298422D907CB0'

function Check($Condition, $Message) { if (!$Condition) { throw $Message } }
function ExpectFailure([scriptblock]$Action, [string]$Message) {
    try { $result = & $Action } catch {
        if ($_.Exception.Message -like "*$Message*") { return }
        throw
    }
    throw "Expected exception containing '$Message'; received '$result'."
}
function Get-ScheduledTask($TaskPath) {
    Check ($TaskPath -ceq '\') 'Unexpected scheduler folder.'
    if ($fixture.Exists) { $fixture.Task }
}
function Export-ScheduledTask($TaskPath, $TaskName) {
    Check ($TaskPath -ceq '\' -and $TaskName -ceq 'OneXConsole') 'Unexpected task export.'
    $enabled = ([string]$fixture.Task.Settings.Enabled).ToLowerInvariant()
    "<Task xmlns='http://schemas.microsoft.com/windows/2004/02/mit/task'><RegistrationInfo><Author>$($fixture.Author)</Author></RegistrationInfo><Settings><Enabled>$enabled</Enabled></Settings></Task>"
}
function Get-FileHash($LiteralPath) {
    Check ($LiteralPath -eq $scriptPath) 'Unexpected OEM file hash.'
    [pscustomobject]@{ Hash = $fixture.Hash }
}
function Disable-ScheduledTask($InputObject) {
    $pending = [IO.File]::ReadAllText($script:testReceiptPath) | ConvertFrom-Json
    Check ($pending.Phase -in @('Prepared','RestartPrepared') -and $pending.OriginalEnabled -and $pending.UserSid -eq $sid) 'Original state was not saved before mutation.'
    Check ([object]::ReferenceEquals($fixture.Task, $InputObject)) 'Unexpected disable target.'
    if ($fixture.FailDisable) { throw 'Disable denied' }
    $fixture.Disables++
    $fixture.Task.Settings.Enabled = $false
}
function Enable-ScheduledTask($InputObject) {
    Check ([object]::ReferenceEquals($fixture.Task, $InputObject)) 'Unexpected restore target.'
    $fixture.Enables++
    $fixture.Task.Settings.Enabled = $true
}
function Apply { Invoke-ONEXTweaksOneXConsoleHandoff Apply $dir $sid -BackendState Healthy -OemStatus Installed }
function Restore { Invoke-ONEXTweaksOneXConsoleHandoff Restore $dir $sid }
function Prepare-Restart { Invoke-ONEXTweaksOneXConsoleHandoff PrepareRestart $dir $sid -BootId '100' -OemStatus Installed }
function Commit-Restart([string]$Boot, [string]$Backend = 'Healthy') {
    Invoke-ONEXTweaksOneXConsoleHandoff CommitRestart $dir $sid -BootId $Boot -OemStatus Installed -BackendState $Backend
}
try {
    [void][IO.Directory]::CreateDirectory($dir)
    $receipt = Join-Path $dir 'onexconsole-handoff.json'
    $script:testReceiptPath = $receipt
    $env:ProgramFiles = Join-Path $dir 'Programs'
    $scriptPath = Join-Path $env:ProgramFiles 'OneXConsole\resources\resources\tools\OneXConsoleStartup.vbs'
    [void][IO.Directory]::CreateDirectory((Split-Path $scriptPath))
    [IO.File]::WriteAllText($scriptPath, 'Fixture only')
    $task = [pscustomobject]@{
        TaskName = 'OneXConsole'; TaskPath = '\'; Settings = [pscustomobject]@{ Enabled = $true }
        Actions = @([pscustomobject]@{ Execute = 'wscript.exe'; Arguments = '"' + $scriptPath + '"'; WorkingDirectory = '' })
        Triggers = @([pscustomobject]@{ UserId = ''; CimClass = [pscustomobject]@{ CimClassName = 'MSFT_TaskLogonTrigger' } })
        Principal = [pscustomobject]@{ LogonType = 'Interactive'; RunLevel = 'Highest'; UserId = $sid }
    }
    $fixture = @{ Task = $task; Exists = $true; Hash = $vendorHash; Disables = 0; Enables = 0; Author = 'Vendor' }
    Check ((Invoke-ONEXTweaksOneXConsoleHandoff Apply $dir $sid -OemStatus Installed -BackendState Unverified) -like 'Waiting*') 'Backend guard missing.'
    Check ((Invoke-ONEXTweaksOneXConsoleHandoff Apply $dir $sid -OemStatus Missing -BackendState Healthy) -like 'Waiting*') 'OEM guard missing.'
    $fixture.Exists = $false
    Check ((Apply) -eq 'Not found') 'Absent task was claimed.'
    $fixture.Exists = $true
    $task.Settings.Enabled = $false
    Check ((Apply) -eq 'Already disabled before installation') 'Pre-disabled task was claimed.'
    $task.Settings.Enabled = $true
    Check ((Apply) -eq 'Already disabled before installation' -and $task.Settings.Enabled) 'Repair undid a later manual enable of a previously disabled task.'
    Check ((Restore) -eq 'No owned handoff') 'Pre-disabled task was restored.'
    $task.Settings.Enabled = $true
    $task.Principal.UserId = 'S-1-5-18'
    Check ((Apply) -eq 'Review existing logon task') 'Another account was accepted.'
    $task.Principal.UserId = $sid
    $task.Principal.RunLevel = 'Limited'
    Check ((Apply) -eq 'Review existing logon task') 'Different OEM privilege level was accepted.'
    $task.Principal.RunLevel = 'Highest'
    $task.Triggers[0].UserId = 'S-1-5-18'
    Check ((Apply) -eq 'Review existing logon task') 'Another logon trigger was accepted.'
    $task.Triggers[0].UserId = ''
    $fixture.Hash = 'Wrong'
    Check ((Apply) -eq 'Review existing logon task') 'Modified VBS was accepted.'
    $fixture.Hash = $vendorHash
    Check (!(Test-Path -LiteralPath $receipt) -and $fixture.Disables -eq 0 -and $fixture.Enables -eq 0) 'Unowned task was changed.'
    $recognized = Get-ONEXTweaksOemTaskSnapshot $sid
    Check ($recognized.State -eq 'Recognized' -and $recognized.Enabled) "Expected the enabled OEM fixture; received '$($recognized.State)'."
    $fixture.FailDisable = $true
    ExpectFailure { Apply } 'Disable denied'
    $task.Settings.Enabled = $false
    ExpectFailure { Restore } 'incomplete ownership'
    $task.Settings.Enabled = $true
    $fixture.FailDisable = $false
    Check ((Apply) -eq 'Disabled by ONEXTweaks') 'Recoverable intent could not be applied.'
    Check ((Apply) -eq 'Disabled by ONEXTweaks' -and $fixture.Disables -eq 1) 'Repair repeated the disable.'
    $fixture.Author = 'Replaced after install'
    ExpectFailure { Restore } 'definition changed'
    Check ((Test-Path -LiteralPath $receipt) -and $fixture.Enables -eq 0) 'Foreign registration was restored.'
    $fixture.Author = 'Vendor'
    ExpectFailure { Invoke-ONEXTweaksOneXConsoleHandoff Restore $dir 'S-1-5-18' } 'ownership receipt'
    $held = [IO.File]::Open((Join-Path $dir 'onexconsole-handoff.lock'), 'Open', 'ReadWrite', 'None')
    try { ExpectFailure { Restore } 'onexconsole-handoff.lock' } finally { $held.Dispose() }
    Check ((Restore) -eq 'Restored' -and $task.Settings.Enabled) 'The original enabled state was not restored.'
    Check ((Restore) -eq 'No owned handoff' -and $fixture.Enables -eq 1) 'Repeated uninstall mutated the task.'
    [void](Apply)
    $task.Settings.Enabled = $true
    Check ((Apply) -eq 'Re-enabled externally' -and $fixture.Disables -eq 2) 'Repair overrode a manual re-enable.'
    $task.Settings.Enabled = $false
    Check ((Restore) -eq 'No owned handoff' -and !$task.Settings.Enabled -and $fixture.Enables -eq 1) 'Uninstall overrode the new external owner.'
    $task.Settings.Enabled = $true
    [void](Apply)
    $task.Settings.Enabled = $true
    Check ((Restore) -eq 'Already enabled' -and $fixture.Enables -eq 1) 'Uninstall rewrote an enabled task.'
    Check ((Prepare-Restart) -eq 'Restart required for OEM handoff' -and !$task.Settings.Enabled) 'Restart did not suppress the verified OEM task.'
    Check ((Prepare-Restart) -eq 'Restart required for OEM handoff') 'Retry did not reuse the same boot intention.'
    Check ((Apply) -eq 'Restart required for OEM handoff') 'Same-boot readiness committed an unfinished restart.'
    Check ((Commit-Restart '100') -eq 'Restart handoff must be restored') 'Same boot committed a cold handoff.'
    ExpectFailure { Commit-Restart '200' 'Unavailable' } 'post-restart hardware readiness'
    Check ((Commit-Restart '200') -eq 'Disabled by ONEXTweaks') 'The verified next boot did not commit.'
    Check ((Restore) -eq 'Restored' -and $task.Settings.Enabled) 'Uninstall lost the pre-restart enabled state.'
    [void](Prepare-Restart)
    $pending = [IO.File]::ReadAllText($receipt) | ConvertFrom-Json
    $pending.Phase = 'RestartPrepared'
    Write-ONEXTweaksOemReceipt $receipt $pending -Replace
    Check ((Commit-Restart '200') -eq 'Restart handoff must be restored') 'A torn acknowledgement was committed.'
    Check ((Restore) -eq 'Restored' -and $task.Settings.Enabled) 'A torn acknowledgement stranded the OEM startup task.'
    $task.Settings.Enabled = $false
    Check ((Prepare-Restart) -eq 'OEM autostart already disabled' -and !(Test-Path -LiteralPath $receipt)) 'An OEM task initially off was claimed.'
    $task.Settings.Enabled = $true
    [void](Prepare-Restart)
    $rollback = Invoke-ONEXTweaksOneXConsoleHandoff RestoreRestart $dir $sid
    Check ($rollback -eq 'OEM startup restored; launch pending' -and $task.Settings.Enabled) 'Failed health checks did not restore the original startup.'
    Check (([IO.File]::ReadAllText($receipt) | ConvertFrom-Json).Phase -eq 'RestartRestoring') 'Rollback discarded its retry state before OEM startup.'
    Check ((Invoke-ONEXTweaksOneXConsoleHandoff RestoreRestart $dir $sid) -eq $rollback) 'Retry of the OEM launch mutated the restored task.'
    Check ((Invoke-ONEXTweaksOneXConsoleHandoff ConfirmRestartRestore $dir $sid) -eq 'Restored' -and !(Test-Path -LiteralPath $receipt)) 'Confirmed OEM restart did not close recovery.'
    'OneXConsole ownership, recovery, identity, backend gating and restore fixtures passed.'
} finally {
    $env:ProgramFiles = $savedPrograms
    if ([IO.Directory]::Exists($dir)) { [IO.Directory]::Delete($dir, $true) }
}
