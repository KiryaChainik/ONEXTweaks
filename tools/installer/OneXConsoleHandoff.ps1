# Dot-source OneXConsoleSetup.ps1 first. StateDirectory is an existing,
# administrator-controlled installer directory that survives upgrades and rollback.
function Get-ONEXTweaksOemTaskSnapshot([string]$UserSid, $SchedulerFolder) {
    if ($SchedulerFolder) {
        $registered = Get-ONEXTweaksComTask $SchedulerFolder 'OneXConsole'
        if (!$registered) { return [pscustomobject]@{ State = 'Not found' } }
        $definition = $registered.Definition
        if ($definition.Actions.Count -ne 1 -or $definition.Triggers.Count -ne 1 -or
            $definition.Actions.Item(1).Type -ne 0 -or $definition.Triggers.Item(1).Type -ne 9) {
            return [pscustomobject]@{ State = 'Review existing logon task' }
        }
        $action = $definition.Actions.Item(1)
        $task = [pscustomobject]@{
            Settings = @{ Enabled=[bool]$registered.Enabled }
            Principal = @{ UserId=$definition.Principal.UserId;
                RunLevel=$(if ($definition.Principal.RunLevel -eq 1) { 'Highest' });
                LogonType=$(if ($definition.Principal.LogonType -eq 3) { 'Interactive' }) }
            Actions = @(@{ Execute=$action.Path; Arguments=$action.Arguments; WorkingDirectory=$action.WorkingDirectory })
            Triggers = @(@{ UserId=$definition.Triggers.Item(1).UserId; CimClass=@{ CimClassName='MSFT_TaskLogonTrigger' } })
        }
        [xml]$xml = $registered.Xml
    } else {
        $tasks = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object {
            $_.TaskName -ieq 'OneXConsole' -and $_.TaskPath -eq '\'
        })
        if (!$tasks.Count) { return [pscustomobject]@{ State = 'Not found' } }
        if ($tasks.Count -ne 1) { throw 'Ambiguous OneXConsole root task.' }
        $task = $tasks[0]
        $registered = $task
    }
    $state = Get-OneXConsoleGuiStartupState $task
    if ($state -notin @('Disabled','Enabled; reversible handoff pending') -or
        $task.Settings.Enabled -isnot [bool] -or $task.Principal.RunLevel -ne 'Highest' -or
        !(Test-ONEXTweaksUserPrincipal $task $UserSid) -or $task.Actions[0].Execute -ine 'wscript.exe' -or
        $task.Triggers[0].UserId -or $task.Actions[0].WorkingDirectory) {
        return [pscustomobject]@{ State = 'Review existing logon task' }
    }
    # Include the complete vendor definition in the receipt. The enabled bit is
    # the only value that our restore is entitled to change.
    if (!$SchedulerFolder) { [xml]$xml = Export-ScheduledTask -TaskPath '\' -TaskName 'OneXConsole' -ErrorAction Stop }
    $ns = New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('s', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $settings = $xml.SelectSingleNode('/s:Task/s:Settings', $ns)
    if (!$settings) { throw 'OneXConsole task settings unavailable.' }
    $enabled = $settings.SelectSingleNode('s:Enabled', $ns)
    $definitionEnabled = !$enabled -or [Xml.XmlConvert]::ToBoolean($enabled.InnerText)
    if ($definitionEnabled -ne $task.Settings.Enabled) { throw 'OneXConsole task changed during inspection.' }
    if ($enabled) { [void]$settings.RemoveChild($enabled) }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($xml.DocumentElement.OuterXml))
        $fingerprint = [BitConverter]::ToString($digest).Replace('-', '')
    } finally { $sha.Dispose() }
    return [pscustomobject]@{ State = 'Recognized'; Enabled = $task.Settings.Enabled; Fingerprint = $fingerprint; Task = $registered }
}

function Write-ONEXTweaksOemReceipt([string]$Path, $Receipt, [switch]$Replace) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $stream = [IO.File]::Open($temporary, 'CreateNew', 'Write', 'None')
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes(($Receipt | ConvertTo-Json -Compress))
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally { $stream.Dispose() }
        if ($Replace) {
            [IO.File]::Replace($temporary, $Path, [Management.Automation.Language.NullString]::Value)
        } else { [IO.File]::Move($temporary, $Path) }
    } finally { [IO.File]::Delete($temporary) }
}

function Get-ONEXTweaksOemHandoffBootId {
    return (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-ONEXTweaksOneXConsoleHandoff {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Apply','PrepareRestart','CommitRestart','RestoreRestart','ConfirmRestartRestore','Restore')][string]$Operation,
        [Parameter(Mandatory)][string]$StateDirectory,
        [Parameter(Mandatory)][ValidatePattern('^S-1-\d+(-\d+)+$')][string]$UserSid,
        [string]$BackendState,
        [string]$OemStatus,
        [string]$BootId,
        $SchedulerFolder
    )
    if ($Operation -eq 'Apply') {
        if ($OemStatus -eq 'Not applicable') { return 'Not applicable' }
        if ($OemStatus -ne 'Installed' -or $BackendState -ne 'Healthy') { return 'Waiting for verified backend and OEM components' }
    }
    if ($Operation -in @('PrepareRestart','CommitRestart')) {
        if ($BootId -notmatch '^\d+$' -or $OemStatus -ne 'Installed') { throw 'Unverified OneXConsole restart handoff.' }
        if ($Operation -eq 'CommitRestart' -and $BackendState -ne 'Healthy') { throw 'Unverified post-restart hardware readiness.' }
    }
    $path = Join-Path $StateDirectory 'onexconsole-handoff.json'
    if (!(Test-Path -LiteralPath $StateDirectory -PathType Container -ErrorAction Stop)) {
        if ($Operation -in @('Restore','CommitRestart','RestoreRestart','ConfirmRestartRestore')) { return 'No owned handoff' }
        throw 'Installer state directory unavailable for OneXConsole handoff.'
    }
    # All installer instances share one exclusive lock. The lock file contains no
    # ownership data and can be removed with the installer directory on uninstall.
    $lock = [IO.File]::Open((Join-Path $StateDirectory 'onexconsole-handoff.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $receipt = $null
        if (Test-Path -LiteralPath $path -ErrorAction Stop) {
            $receipt = [IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
            if ($receipt.Schema -ne 1 -or $receipt.Task -cne '\OneXConsole' -or $receipt.UserSid -cne $UserSid -or
                $receipt.OriginalEnabled -isnot [bool] -or $receipt.OriginalEnabled -ne ($receipt.Phase -ne 'PreDisabled') -or
                $receipt.Fingerprint -notmatch '^[A-F0-9]{64}$' -or
                $receipt.Phase -notin @('Prepared','RestartPrepared','RestartPending','RestartRestoring','Disabled','Released','PreDisabled') -or
                ($receipt.Phase -in @('RestartPrepared','RestartPending','RestartRestoring') -and
                    (!$receipt.PSObject.Properties['BootId'] -or $receipt.BootId -notmatch '^\d+$'))) {
                throw 'Unrecognized OneXConsole ownership receipt. Retain it for review.'
            }
        } elseif ($Operation -in @('Restore','CommitRestart','RestoreRestart','ConfirmRestartRestore')) { return 'No owned handoff' }
        if ($Operation -eq 'CommitRestart' -and (!$receipt -or $receipt.Phase -notin @('RestartPrepared','RestartPending'))) {
            return 'No pending restart handoff'
        }
        if ($Operation -in @('RestoreRestart','ConfirmRestartRestore') -and
            (!$receipt -or $receipt.Phase -notin @('RestartPrepared','RestartPending','RestartRestoring'))) {
            return 'No pending restart handoff'
        }
        if ($Operation -eq 'PrepareRestart' -and $receipt) {
            if ($receipt.Phase -eq 'RestartPending' -and $receipt.BootId -ceq $BootId) { return 'Restart required for OEM handoff' }
            return 'Review existing OEM handoff'
        }
        if ($receipt -and $receipt.Phase -in @('Released','PreDisabled')) {
            if ($Operation -eq 'Restore') {
                [IO.File]::Delete($path)
                return 'No owned handoff'
            }
            if ($receipt.Phase -eq 'Released') { return 'Re-enabled externally' }
            return 'Already disabled before installation'
        }
        $snapshot = Get-ONEXTweaksOemTaskSnapshot $UserSid $SchedulerFolder
        if ($snapshot.State -ne 'Recognized') {
            if ($receipt) { throw "OneXConsole restore identity is unavailable ($($snapshot.State)). Retain the receipt." }
            return $snapshot.State
        }
        if ($receipt -and $receipt.Fingerprint -cne $snapshot.Fingerprint) {
            throw 'OneXConsole task definition changed. Retain the receipt for review.'
        }
        if ($receipt -and $receipt.Phase -eq 'Prepared' -and !$snapshot.Enabled) {
            # A crash or external edit between the intent and commit has ambiguous
            # ownership. Never enable a task unless our disable was acknowledged.
            throw 'OneXConsole is disabled with an incomplete ownership receipt. Inspect before restoration.'
        }
        if ($Operation -eq 'CommitRestart') {
            if ($receipt.Phase -ne 'RestartPending' -or $receipt.BootId -ceq $BootId -or $snapshot.Enabled) {
                return 'Restart handoff must be restored'
            }
            $receipt.Phase = 'Disabled'
            Write-ONEXTweaksOemReceipt $path $receipt -Replace
            return 'Disabled by ONEXTweaks'
        }
        if ($Operation -eq 'Apply' -and $receipt -and $receipt.Phase -in @('RestartPrepared','RestartPending','RestartRestoring')) {
            return 'Restart required for OEM handoff'
        }
        if ($Operation -eq 'ConfirmRestartRestore') {
            if ($receipt.Phase -cne 'RestartRestoring' -or !$snapshot.Enabled) { throw 'The OEM restart restore is not ready to confirm.' }
            [IO.File]::Delete($path)
            return 'Restored'
        }
        if ($Operation -eq 'RestoreRestart') {
            if ($receipt.Phase -cne 'RestartRestoring') {
                $receipt.Phase = 'RestartRestoring'
                Write-ONEXTweaksOemReceipt $path $receipt -Replace
            }
            if ($snapshot.Enabled) { return 'OEM startup restored; launch pending' }
        }
        if ($Operation -eq 'PrepareRestart') {
            if (!$snapshot.Enabled) { return 'OEM autostart already disabled' }
            $receipt = [pscustomobject]@{ Schema=1; Task='\OneXConsole'; UserSid=$UserSid; Fingerprint=$snapshot.Fingerprint;
                OriginalEnabled=$true; Phase='RestartPrepared'; BootId=$BootId }
            Write-ONEXTweaksOemReceipt $path $receipt
        }
        if ($Operation -eq 'Restore' -and $snapshot.Enabled) {
            [IO.File]::Delete($path)
            return 'Already enabled'
        }
        if ($Operation -eq 'Apply') {
            if ($receipt -and $receipt.Phase -eq 'Disabled') {
                if ($snapshot.Enabled) {
                    $receipt.Phase = 'Released'
                    Write-ONEXTweaksOemReceipt $path $receipt -Replace
                    return 'Re-enabled externally'
                }
                return 'Disabled by ONEXTweaks'
            }
            if (!$receipt) {
                $phase = 'Prepared'
                if (!$snapshot.Enabled) { $phase = 'PreDisabled' }
                $receipt = [pscustomobject]@{ Schema = 1; Task = '\OneXConsole'; UserSid = $UserSid;
                    Fingerprint = $snapshot.Fingerprint; OriginalEnabled = $snapshot.Enabled; Phase = $phase }
                Write-ONEXTweaksOemReceipt $path $receipt
                if (!$snapshot.Enabled) { return 'Already disabled before installation' }
            }
        }
        $before = Get-ONEXTweaksOemTaskSnapshot $UserSid $SchedulerFolder
        if ($before.State -ne 'Recognized' -or $before.Fingerprint -cne $receipt.Fingerprint -or $before.Enabled -ne $snapshot.Enabled) {
            throw 'OneXConsole task changed immediately before handoff. Retain the receipt.'
        }
        if ($Operation -in @('Apply','PrepareRestart')) {
            if ($before.Enabled -ne $true) { throw 'The OneXConsole task is no longer enabled before handoff.' }
            if ($SchedulerFolder) { $before.Task.Enabled = $false } else { Disable-ScheduledTask -InputObject $before.Task -ErrorAction Stop | Out-Null }
            # Commit acknowledgement before readback so a transient scheduler
            # read failure still leaves uninstall with a recoverable decision.
            $receipt.Phase = if ($Operation -eq 'PrepareRestart') { 'RestartPending' } else { 'Disabled' }
            Write-ONEXTweaksOemReceipt $path $receipt -Replace
        } else {
            if ($SchedulerFolder) { $before.Task.Enabled = $true } else { Enable-ScheduledTask -InputObject $before.Task -ErrorAction Stop | Out-Null }
        }
        $after = Get-ONEXTweaksOemTaskSnapshot $UserSid $SchedulerFolder
        if ($after.State -ne 'Recognized' -or $after.Fingerprint -cne $receipt.Fingerprint -or
            $after.Enabled -ne ($Operation -in @('Restore','RestoreRestart'))) {
            throw 'OneXConsole task readback failed. Retain the receipt.'
        }
        if ($Operation -eq 'Restore') {
            [IO.File]::Delete($path)
            return 'Restored'
        }
        if ($Operation -eq 'RestoreRestart') { return 'OEM startup restored; launch pending' }
        if ($Operation -eq 'PrepareRestart') { return 'Restart required for OEM handoff' }
        return 'Disabled by ONEXTweaks'
    } finally { $lock.Dispose() }
}
