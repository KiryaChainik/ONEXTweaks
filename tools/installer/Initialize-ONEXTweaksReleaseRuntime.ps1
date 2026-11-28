#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName='Prepare')]
param(
    [Parameter(Mandatory, ParameterSetName='Prepare')][switch]$Prepare,
    [Parameter(Mandatory, ParameterSetName='Prepare')][ValidatePattern('^ONEXTweaks\.Widget_\d+(\.\d+){3}_x64__[a-z0-9]{13}$')][string]$WidgetPackageFullName,
    [Parameter(Mandatory, ParameterSetName='Stage')][string]$RuntimeManifest,
    [Parameter(Mandatory, ParameterSetName='Stage')][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$RuntimeManifestSha256,
    [Parameter(ParameterSetName='Stage')][ValidatePattern('^[a-f0-9]{32}$')][string]$LegacyUpgradeId
)

function New-ONEXTweaksProtectedDirectory([string]$Path) {
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $security.SetOwner($admins)
    foreach ($grant in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@('S-1-5-32-545','ReadAndExecute'))) {
        $sid = [Security.Principal.SecurityIdentifier]::new($grant[0])
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, $grant[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $security.AddAccessRule($rule)
    }
    $null = [IO.Directory]::CreateDirectory($Path, $security)
    Assert-ONEXTweaksProtectedItem $Path
}

function Write-ONEXTweaksInstallationRecord([string]$Path, $Record, [switch]$AllowReplace) {
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, ($Record | ConvertTo-Json -Depth 6 -Compress))
        if ($AllowReplace -and [IO.File]::Exists($Path)) {
            [IO.File]::Replace($temporary,$Path,[Management.Automation.Language.NullString]::Value)
        } else { [IO.File]::Move($temporary, $Path) }
    } finally { [IO.File]::Delete($temporary) }
}

function Register-ONEXTweaksRuntimeTask($Folder, [string]$Name, [string]$Exe, [string]$Arguments, [string]$Owner,
    [ValidateSet('Bridge','Hardware','Overlay','Recovery','OemHandoff')][string]$Purpose, [string]$UpgradeId, [bool]$Enabled = $true,
    [switch]$Guarded) {
    $system = $Purpose -in @('Hardware','OemHandoff')
    $account = if ($system) { 'S-1-5-18' } else { $Owner }
    $logon = if ($system) { 5 } else { 3 }
    $definition = $Folder.Service.NewTask(0)
    $definition.RegistrationInfo.Description = if ($UpgradeId) { "ONEXTweaks $Purpose $UpgradeId" } else { "ONEXTweaks $Purpose" }
    $definition.Principal.UserId = $account
    $definition.Principal.LogonType = $logon
    $definition.Principal.RunLevel = [int]($Purpose -eq 'Overlay')
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.Enabled = $Enabled
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    if ($Purpose -in @('Recovery','OemHandoff')) {
        $trigger = $definition.Triggers.Create(9) # TASK_TRIGGER_LOGON
        $trigger.UserId = $Owner
        $definition.Settings.Hidden = $true
        $definition.Settings.StartWhenAvailable = $true
        $definition.Settings.ExecutionTimeLimit = 'PT1M'
        if ($Purpose -eq 'OemHandoff') {
            $trigger.Delay = 'PT15S'
            $definition.Settings.ExecutionTimeLimit = 'PT4M'
            $definition.Settings.RestartCount = 2
            $definition.Settings.RestartInterval = 'PT1M'
        }
    }
    $action = $definition.Actions.Create(0)
    $action.Path = $Exe
    $action.Arguments = $Arguments
    $action.WorkingDirectory = Split-Path -LiteralPath $Exe
    $rights = Get-ONEXTweaksReleaseTaskRights $Purpose $Owner -Guarded:$Guarded
    $flags = if ($Guarded) { 18 } else { 2 } # TASK_CREATE, plus TASK_DONT_ADD_PRINCIPAL_ACE while guarded.
    $task = $Folder.Tasks.RegisterTaskDefinition($Name, $definition, $flags, $account, $null, $logon, $rights)
    if ($Guarded) { return $task }
}

function Get-ONEXTweaksReleaseTaskRights([string]$Purpose, [string]$Owner, [switch]$Guarded) {
    $administrators = 'D:P(A;;GA;;;SY)(A;;GA;;;BA)'
    if ($Guarded) { return 'O:BAG:BA' + $administrators }
    if ($Purpose -eq 'OemHandoff') { return $administrators }
    $access = if ($Purpose -eq 'Startup') { 'GA' } else { 'GRGX' }
    return $administrators + "(A;;$access;;;$Owner)"
}

function Assert-ONEXTweaksPendingTaskOwnership($Task, [string]$Purpose, [string]$TransactionId) {
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($Task.GetSecurityDescriptor(5))
    $trusted = @('S-1-5-18','S-1-5-32-544')
    $acl = $descriptor.DiscretionaryAcl
    $verified = @($acl | Where-Object {
        $_ -is [Security.AccessControl.CommonAce] -and $_.AceQualifier -eq 'AccessAllowed' -and
        $_.AceFlags -eq 'None' -and $_.AccessMask -in 0x1F01FF,0x10000000
    } | ForEach-Object { $_.SecurityIdentifier.Value } | Sort-Object -Unique)
    if ($Task.Enabled -or $Task.Definition.RegistrationInfo.Description -cne "ONEXTweaks $Purpose $TransactionId" -or
        !$descriptor.Owner -or $descriptor.Owner.Value -notin $trusted -or
        !($descriptor.ControlFlags -band [Security.AccessControl.ControlFlags]::DiscretionaryAclProtected) -or
        !$acl -or $acl.Count -ne 2 -or @($trusted | Where-Object { $_ -notin $verified }).Count) {
        throw "The prepared task has no verifiable repair ownership: $($Task.Name). Review its definition before retrying Setup."
    }
}

function Set-ONEXTweaksReleaseTaskRights($Task, [string]$Purpose, [string]$Owner) {
    $Task.SetSecurityDescriptor((Get-ONEXTweaksReleaseTaskRights $Purpose $Owner),16)
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($Task.GetSecurityDescriptor(5))
    $expected = @('S-1-5-18:2032127','S-1-5-32-544:2032127')
    if ($Purpose -ne 'OemHandoff') {
        $mask = if ($Purpose -eq 'Startup') { 0x1F01FF } else { 0x1200A9 }
        $expected += "${Owner}:$mask"
    }
    $actual = @($descriptor.DiscretionaryAcl | ForEach-Object {
        if ($_ -isnot [Security.AccessControl.CommonAce] -or $_.AceQualifier -ne 'AccessAllowed' -or $_.AceFlags -ne 'None') {
            throw "Unexpected access control entry on $($Task.Name)."
        }
        $mask = if ($_.AccessMask -eq 0x10000000) { 0x1F01FF } else { $_.AccessMask }
        "$($_.SecurityIdentifier.Value):$mask"
    })
    if ($descriptor.Owner.Value -notin @('S-1-5-32-544','S-1-5-18') -or
        !($descriptor.ControlFlags -band [Security.AccessControl.ControlFlags]::DiscretionaryAclProtected) -or
        (($actual | Sort-Object) -join ',') -cne (($expected | Sort-Object) -join ',')) {
        throw "Windows did not apply the release task permissions: $($Task.Name)"
    }
}

function Get-ONEXTweaksRtssExecutable {
    $rtss = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ } | ForEach-Object {
        Join-Path $_ 'RivaTuner Statistics Server\RTSS.exe'
    } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$rtss -or (Get-RtssPrerequisiteState) -ne 'Installed') { throw 'Install and verify RTSS before staging the runtime.' }
    return $rtss
}

function Get-ONEXTweaksOemHandoffCommand([string]$Root) {
    $resumeTool = Join-Path $root 'Tools\Installer\Complete-ONEXTweaksRelease.ps1'
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $resumeError = Join-Path $root 'PowerAgent\onexconsole-resume-error.json'
    $resumeCommand = @'
$ErrorActionPreference = 'Stop'
try {
    $resume = @(& '__TOOL__' -ResumeOneXConsoleHandoff -Confirm:$false)
    if ($resume -contains 'Disabled by ONEXTweaks') { [IO.File]::Delete('__ERROR__') }
} catch {
    $detail = if ($_.Exception -is [AggregateException]) { @($_.Exception.InnerExceptions | ForEach-Object Message) } else { @() }
    @{ Utc=[DateTimeOffset]::UtcNow.ToString('o'); Error=$_.Exception.Message; Causes=$detail; Id=$_.FullyQualifiedErrorId; At=$_.ScriptStackTrace } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath '__ERROR__' -Encoding UTF8
    exit 1
}
'@
    $resumeCommand = $resumeCommand.Replace('__TOOL__',$resumeTool.Replace("'","''")).Replace('__ERROR__',$resumeError.Replace("'","''"))
    $resumeArguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -EncodedCommand ' +
        [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($resumeCommand))
    return [pscustomobject]@{Exe=$windowsPowerShell; Arguments=$resumeArguments}
}

function Get-ONEXTweaksReleaseTaskDefinition([string]$Name, [string]$Root, [string]$Owner, [string]$Rtss) {
    $bridge = Join-Path $Root 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'
    switch -CaseSensitive ($Name) {
        'ONEXTweaks-Startup' { return [pscustomobject]@{Purpose='Startup';Exe=(Join-Path $Root 'CompanionLauncher.exe');Arguments='--startup'} }
        'ONEXTweaks-WidgetBridge' { return [pscustomobject]@{Purpose='Bridge';Exe=$bridge;Arguments=''} }
        'ONEXTweaks-ControllerInputRecovery' { return [pscustomobject]@{Purpose='Recovery';Exe=$bridge;Arguments='--controller-login-recovery'} }
        'ONEXTweaks-RTSS' { return [pscustomobject]@{Purpose='Overlay';Exe=$Rtss;Arguments=''} }
        'ONEXTweaks-PowerAgent' {
            return [pscustomobject]@{Purpose='Hardware';Exe=(Join-Path $Root 'PowerAgent\ONEXTweaks.PowerAgent.exe');Arguments="serve-desktop $Owner"}
        }
        'ONEXTweaks-OneXConsoleHandoff' {
            $command = Get-ONEXTweaksOemHandoffCommand $Root
            return [pscustomobject]@{Purpose='OemHandoff';Exe=$command.Exe;Arguments=$command.Arguments}
        }
        default { throw "Unknown release task: $Name" }
    }
}

function Register-ONEXTweaksReleaseTask($Folder, [string]$Name, $Definition, [string]$Owner, [string]$TransactionId,
    [switch]$Guarded) {
    if (!$Definition.Exe) { throw "Release task has no verified executable: $Name" }
    if ($Guarded -and $TransactionId -cnotmatch '^[a-f0-9]{32}$') { throw 'A protected transaction ID is required to prepare a disabled task.' }
    $enabled = !$Guarded
    if ($Definition.Purpose -eq 'Startup') {
        $xml = New-ReleaseStartupXml $Owner $Definition.Exe $enabled
        if ($TransactionId) {
            $xml = $xml.Replace('Start ONEXTweaks in the signed-in user session.', "ONEXTweaks Startup $TransactionId")
        }
        $rights = Get-ONEXTweaksReleaseTaskRights $Definition.Purpose $Owner -Guarded:$Guarded
        $flags = if ($Guarded) { 18 } else { 2 }
        $task = $Folder.Tasks.RegisterTask($Name,$xml,$flags,$Owner,$null,3,$rights)
        if ($Guarded) { return $task }
        return
    }
    Register-ONEXTweaksRuntimeTask $Folder $Name $Definition.Exe $Definition.Arguments $Owner $Definition.Purpose $TransactionId $enabled -Guarded:$Guarded
}

function Get-ONEXTweaksRuntimePayload($Manifest, [string]$Directory, [string]$Root, [bool]$Hardware) {
    if ($Manifest.Schema -ne 1 -or $Manifest.Files -isnot [array]) { throw 'Invalid runtime manifest.' }
    $required = @('CompanionLauncher.exe','ONEXTweaks.DependencyManager.exe',
        'WidgetBridge\ONEXTweaks.WidgetBridge.exe','WidgetBridge\ONEXTweaks.WidgetBridge.dll',
        'WidgetBridge\ONEXTweaks.ControllerObserver.exe','WidgetBridge\libVIIPER.dll',
        'Tools\Set-DirectGuideButton.ps1','Tools\Set-GameBarStartPage.ps1',
        'Tools\Installer\Complete-ONEXTweaksRelease.ps1','Tools\Installer\Uninstall-ONEXTweaksRelease.ps1',
        'Tools\Installer\OneXConsoleSetup.ps1','Tools\Installer\OneXConsoleHandoff.ps1','Tools\Installer\DriverPrerequisites.ps1',
        'Tools\Installer\GameBarPrerequisite.ps1')
    if ($Hardware) { $required += @('PowerAgent\ONEXTweaks.PowerAgent.exe','PowerAgent\home-oem-seed.json') }
    $files = @{}
    foreach ($row in $Manifest.Files) {
        if ($row.Path -match '^PowerAgent[\\/]' -and !$Hardware) { continue }
        if ($row.Path -match '/' -or $row.Path -match '^PowerAgent\\(home-oem-settings\.json|led-color-profile\.json|boost-modes\.json|game-lighting-lease\.json|fan-control-journal|power-agent-result\.json|installer-hardware-ready\.json(?:\.tmp)?|onexconsole-resume-error\.json|hardware-health\.jsonl(?:\.prev)?)$' -or $files.ContainsKey($row.Path) -or
            ($row.Path -notin $required -and $row.Path -notmatch '^(WidgetBridge|PowerAgent)\\')) { throw "Unexpected runtime path: $($row.Path)" }
        $null = Get-ONEXTweaksOwnedFile $Root $row
        $source = $Directory
        foreach ($part in ($row.Path -split '\\')) {
            $source = Join-Path $source $part
            $item = Get-Item -LiteralPath $source -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected bundle reparse point: $source" }
        }
        if ($item -isnot [IO.FileInfo] -or (Get-FileHash -LiteralPath $source).Hash -ine $row.Sha256) { throw "Release payload hash mismatch: $source" }
        $files[$row.Path] = [pscustomobject]@{ Row=$row; Source=$source }
    }
    foreach ($name in $required) {
        if (!$files.ContainsKey($name)) { throw "Required release file missing: $name" }
    }
    return $files
}

function Assert-ONEXTweaksInitialRuntime([string]$Root, [string[]]$TaskNames) {
    $collisions = @(Get-ScheduledTask -TaskPath '\' | Where-Object TaskName -in $TaskNames)
    if ($collisions.Count) { throw ('Existing runtime tasks require a reviewed migration: ' + ($collisions.TaskName -join ', ')) }
    if ((Test-Path -LiteralPath $Root) -and
        (!(Test-Path -LiteralPath $Root -PathType Container) -or !@(Get-ONEXTweaksLegacyFanResidue $Root).Count)) {
        throw 'An existing machine runtime is present. Use a reviewed migration to preserve it.'
    }
}

function Install-ONEXTweaksFirstRuntime($Folder, $Selection, $Files, [string[]]$Names, [bool]$Hardware, [string]$Rtss,
    [string]$Root, [string]$State, [string]$JournalPath, [string]$ManifestDigest) {
    $ownerSid = $Selection.OwnerSid
    $installId = $Selection.InstallId
    $receiptPath = Join-Path $State 'owned-release.json'
    $plan = Get-ONEXTweaksUninstallPlan $State $Root -OwnerOnly
    if (Test-Path -LiteralPath $JournalPath) {
        Assert-ONEXTweaksProtectedItem $JournalPath
        $pending = [IO.File]::ReadAllText($JournalPath) | ConvertFrom-Json
        if ($pending.Schema -ne 1 -or $pending.InstallId -cne $installId -or $pending.OwnerSid -cne $ownerSid -or
            $pending.Manifest -cne $ManifestDigest -or $pending.Hardware -cne $Hardware -or $pending.Tasks -isnot [array] -or
            @($pending.Tasks | Where-Object { $_.Name -cnotin $Names -or $_.DefinitionSha256 -notmatch '^[a-fA-F0-9]{64}$' }).Count) {
            throw 'Run the same verified Setup on the installation desktop to finish preparing ONEXTweaks.'
        }
        $definitionByName = @{}
        $created = @(foreach ($name in $Names) {
            $task = Get-ONEXTweaksComTask $Folder.Tasks $name
            if (!$task) { continue }
            $definition = Get-ONEXTweaksReleaseTaskDefinition $name $Root $ownerSid $Rtss
            $definitionByName[$name] = $definition
            $record = $pending.Tasks | Where-Object Name -ceq $name
            if ($record) {
                if ((Get-ONEXTweaksTaskXmlDigest $task.Xml) -cne $record.DefinitionSha256) {
                    throw "The pending first installation found a changed task: $name"
                }
            } else { Assert-ONEXTweaksPendingTaskOwnership $task $definition.Purpose $installId }
            $task
        })
        if ($plan) {
            if ($plan.Receipt.InstallId -cne $installId -or $created.Count -ne $Names.Count -or
                $plan.Tasks.Count -ne $Names.Count -or $plan.Files.Count -ne $Files.Count) {
                throw 'The completed first installation no longer matches its preparation receipt.'
            }
            foreach ($row in $plan.Receipt.Files) {
                if (!$Files.ContainsKey($row.Path) -or $Files[$row.Path].Row.Sha256 -cne $row.Sha256 -or
                    ![IO.File]::Exists((Join-Path $Root $row.Path))) { throw "The new runtime file could not be verified: $($row.Path)" }
            }
            foreach ($task in $created) {
                if (!$plan.Tasks.ContainsKey($task.Name) -or
                    $plan.Tasks[$task.Name].DefinitionSha256 -cne (Get-ONEXTweaksTaskXmlDigest $task.Xml)) {
                    throw "The new runtime task could not be verified: $($task.Name)"
                }
                Set-ONEXTweaksReleaseTaskRights $task $definitionByName[$task.Name].Purpose $ownerSid
                if (!$task.Enabled) { $task.Enabled = $true }
            }
            [IO.File]::Delete($JournalPath)
            return
        }
        $retainIncompleteRoot = $false
        if (Test-Path -LiteralPath $Root) {
            Assert-ONEXTweaksProtectedItem $Root
            foreach ($file in Get-ONEXTweaksPrivateFiles $Root) {
                $relative = $file.FullName.Substring($Root.Length + 1)
                if (!$Files.ContainsKey($relative) -and (!$Hardware -or $relative -cne 'PowerAgent\home-oem-settings.json')) {
                    throw "The pending first installation found an unrelated file: $relative"
                }
                Assert-ONEXTweaksProtectedItem $file.FullName
                $expected = if ($Files.ContainsKey($relative)) { $Files[$relative].Row } else { $Files['PowerAgent\home-oem-seed.json'].Row }
                if ((Get-FileHash -LiteralPath $file.FullName).Hash -cne $expected.Sha256) { $retainIncompleteRoot = $true }
            }
        }
        foreach ($task in $created) { $Folder.Tasks.DeleteTask($task.Name,0) }
        if ($retainIncompleteRoot) {
            # A partial write and a later edit can have the same path. Keep the
            # previous bytes before preparing a fresh copy from the signed bundle.
            $retained = "$Root-recovery-$($installId)-$([guid]::NewGuid().ToString('N'))"
            [IO.Directory]::Move($Root,$retained)
        } elseif (Test-Path -LiteralPath $Root) { Remove-Item -LiteralPath $Root -Recurse -Force }
    } else {
        if ($plan) { throw 'A release already owns the machine.' }
        Assert-ONEXTweaksInitialRuntime $Root $Names
        if (Test-Path -LiteralPath $Root) { Remove-ONEXTweaksLegacyFanResidue $Root }
    }
    $pending = [pscustomobject]@{Schema=1;InstallId=$installId;OwnerSid=$ownerSid;Manifest=$ManifestDigest;Hardware=$Hardware;Tasks=@()}
    Write-ONEXTweaksInstallationRecord $JournalPath $pending -AllowReplace
    New-ONEXTweaksProtectedDirectory $Root
    foreach ($item in $Files.Values) {
        $destination = Join-Path $Root $item.Row.Path
        $null = [IO.Directory]::CreateDirectory((Split-Path -LiteralPath $destination))
        [IO.File]::Copy($item.Source,$destination,$false)
        $null = Get-ONEXTweaksOwnedFile $Root $item.Row
    }
    if ($Hardware) {
        [IO.File]::Copy((Join-Path $Root 'PowerAgent\home-oem-seed.json'),(Join-Path $Root 'PowerAgent\home-oem-settings.json'),$false)
    }
    $created = @(foreach ($name in $Names) {
        $definition = Get-ONEXTweaksReleaseTaskDefinition $name $Root $ownerSid $Rtss
        $task = Register-ONEXTweaksReleaseTask $Folder $name $definition $ownerSid $installId -Guarded
        Assert-ONEXTweaksPendingTaskOwnership $task $definition.Purpose $installId
        $pending.Tasks += [pscustomobject]@{Name=$name;DefinitionSha256=(Get-ONEXTweaksTaskXmlDigest $task.Xml)}
        Write-ONEXTweaksInstallationRecord $JournalPath $pending -AllowReplace
        Set-ONEXTweaksReleaseTaskRights $task $definition.Purpose $ownerSid
        $task
    })
    $Selection | Add-Member -NotePropertyName Files -NotePropertyValue @($Files.Values | ForEach-Object Row)
    $Selection | Add-Member -NotePropertyName Tasks -NotePropertyValue @($pending.Tasks)
    $Selection | Add-Member -NotePropertyName PowerStateOwned -NotePropertyValue $Hardware
    Write-ONEXTweaksInstallationRecord $receiptPath $Selection
    foreach ($task in $created) { $task.Enabled = $true }
    [IO.File]::Delete($JournalPath)
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or ![Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell 5.1.' }
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'DriverPrerequisites.ps1')
. (Join-Path (Split-Path -LiteralPath $PSScriptRoot) 'Install-Startup.ps1')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (!([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Open the administrator setup phase from the widget owner desktop.'
    }
} finally { $identity.Dispose() }
$owner = Get-ONEXTweaksInteractiveSid
if ($owner -notmatch '^S-1-5-21-\d+-\d+-\d+-\d+$') { throw 'This release setup requires a local Windows desktop account.' }
$root = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
$state = Get-ONEXTweaksReleaseStateDirectory
$parent = Split-Path -LiteralPath $state
$lockPath = Join-Path $state 'release-transaction.lock'
$enrollment = Join-Path $state 'desktop-enrollment.json'
$receipt = Join-Path $state 'owned-release.json'
$freshJournal = Join-Path $state 'fresh-runtime.json'
$names = @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-RTSS','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-PowerAgent',
    'ONEXTweaks-OneXConsoleHandoff')
$package = @(Get-AppxPackage -User $owner -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main)
if (!(Test-Path -LiteralPath $state)) {
    if (!$Prepare) { throw 'Prepare the release desktop enrollment before staging.' }
    if (!$PSCmdlet.ShouldProcess('ONEXTweaks machine setup directory','Create')) { return }
    New-ONEXTweaksProtectedDirectory $parent
    New-ONEXTweaksProtectedDirectory $state
}
Assert-ONEXTweaksProtectedStateDirectory $state
if (Test-Path -LiteralPath $lockPath) { Assert-ONEXTweaksProtectedItem $lockPath }
if (!$PSCmdlet.ShouldProcess('ONEXTweaks release runtime on this Windows desktop',$PSCmdlet.ParameterSetName)) { return }
$lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $migration = Join-Path $state 'upgrade-1109.json'
    if (Test-Path -LiteralPath $migration) {
        Assert-ONEXTweaksProtectedItem $migration
        $pending = [IO.File]::ReadAllText($migration) | ConvertFrom-Json
        if (!$LegacyUpgradeId -or $pending.InstallId -cne $LegacyUpgradeId -or $pending.OwnerSid -cne $owner) {
            throw 'An interrupted or active legacy upgrade owns this setup. Use its upgrade entry point.'
        }
    } elseif ($LegacyUpgradeId) {
        throw 'The protected legacy upgrade journal is missing.'
    }
    $pendingFresh = Test-Path -LiteralPath $freshJournal
    if ((Test-Path -LiteralPath $receipt) -and (!$pendingFresh -or $Prepare -or $LegacyUpgradeId)) {
        throw 'A release already owns the machine. Use the release upgrade or uninstall path.'
    }
    if ($Prepare -and !$pendingFresh -and !(Test-Path -LiteralPath $migration)) {
        Assert-ONEXTweaksInitialRuntime $root $names
    }
    if (Test-Path -LiteralPath $enrollment) {
        Assert-ONEXTweaksProtectedItem $enrollment
        $selection = Get-Content -LiteralPath $enrollment -Raw | ConvertFrom-Json
        if ($selection.Schema -ne 1 -or $selection.OwnerSid -cne $owner) { throw 'The machine is reserved for another Windows desktop account.' }
    } elseif ($Prepare) {
        if ($package.Count -gt 1 -or ($package.Count -eq 1 -and $package[0].PackageFullName -cne $WidgetPackageFullName)) {
            throw 'Existing widget registration has a different identity or version. Review it before staging.'
        }
        $userHive = "Registry::HKEY_USERS\$owner"
        $userOverlay = Get-Item -LiteralPath "$userHive\Software\ONEXTweaks\Overlay" -ErrorAction SilentlyContinue
        $userFolders = Get-ItemProperty -LiteralPath "$userHive\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders" -Name 'Local AppData'
        $guideAlreadyManaged = $userOverlay -and ($userOverlay.GetValue('NativeNexusDefaultSaved') -eq 1 -or $userOverlay.GetValue('DirectGuideEnabled') -eq 1)
        $startPageBackup = Join-Path $userFolders.'Local AppData' 'ONEXTweaks\GameBarBackup\start-page-before.json'
        $ownsFutureGuideSetup = !$guideAlreadyManaged -and !(Test-Path -LiteralPath $startPageBackup)
        $selection = [pscustomobject]@{ Schema=1; InstallId=[guid]::NewGuid().ToString('N'); OwnerSid=$owner;
            PackageOwned=($package.Count -eq 0); PackageFullName=$WidgetPackageFullName;
            PackageFamilyName=('ONEXTweaks.Widget_' + ($WidgetPackageFullName.Split('_')[-1])); GuideOwned=$ownsFutureGuideSetup }
        Write-ONEXTweaksInstallationRecord $enrollment $selection
    } else { throw 'Prepare the release desktop enrollment before staging.' }
    if ($Prepare) {
        if ($selection.PackageFullName -cne $WidgetPackageFullName) { throw 'A different release was already prepared.' }
        return [pscustomobject]@{ Status='Desktop enrollment prepared'; OwnerSid=$owner; PackageOwned=$selection.PackageOwned }
    }
    if ($package.Count -ne 1 -or $package[0].PackageFullName -cne $selection.PackageFullName -or $package[0].Status -ne 'Ok') {
        throw 'Register the selected widget as the desktop owner first.'
    }
    $manifestFile = Get-Item -LiteralPath $RuntimeManifest -Force
    if ($manifestFile -isnot [IO.FileInfo] -or $manifestFile.Length -gt 1048576 -or $manifestFile.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Expected a regular, bounded runtime manifest.'
    }
    $bytes = [IO.File]::ReadAllBytes($manifestFile.FullName)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') } finally { $sha.Dispose() }
    if ($digest -ine $RuntimeManifestSha256) { throw 'Runtime manifest hash differs from the trusted release metadata.' }
    $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    if ($manifest.PackageFullName -cne $selection.PackageFullName) { throw 'Runtime and registered widget versions do not match.' }
    $hardware = (Get-ONEXTweaksHardwareCompatibility) -eq 'Supported'
    $validationRoot = if (!$LegacyUpgradeId) { "$root-verify-$([guid]::NewGuid().ToString('N'))" } else { $root }
    $files = Get-ONEXTweaksRuntimePayload $manifest $manifestFile.DirectoryName $validationRoot $hardware
    $rtss = Get-ONEXTweaksRtssExecutable
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = [pscustomobject]@{ Service=$service; Tasks=$service.GetFolder('\') }
    if (!$LegacyUpgradeId) {
        $activeTasks = @($names | Where-Object { $hardware -or $_ -cnotin @('ONEXTweaks-PowerAgent','ONEXTweaks-OneXConsoleHandoff') })
        Install-ONEXTweaksFirstRuntime $folder $selection $files $activeTasks $hardware $rtss $root $state $freshJournal $digest
        return [pscustomobject]@{ Status='Runtime staged'; OwnerSid=$owner; Hardware=$hardware; SetupComplete=$false }
    }
    $collisions = @(Get-ScheduledTask -TaskPath '\' | Where-Object TaskName -in $names)
    if ($collisions.Count) { throw ('Existing runtime tasks require a reviewed migration: ' + ($collisions.TaskName -join ', ')) }
    if (Test-Path -LiteralPath $root -PathType Container) { Remove-ONEXTweaksLegacyFanResidue $root }
    if (Test-Path -LiteralPath $root) { throw 'An existing machine runtime is present. Use a reviewed migration to preserve it.' }

    New-ONEXTweaksProtectedDirectory $root
    $createdTasks = New-Object 'Collections.Generic.List[string]'
    try {
        foreach ($item in $files.Values) {
            $destination = Join-Path $root $item.Row.Path
            $null = [IO.Directory]::CreateDirectory((Split-Path -LiteralPath $destination))
            [IO.File]::Copy($item.Source, $destination, $false)
            $null = Get-ONEXTweaksOwnedFile $root $item.Row
        }
        if ($hardware) {
            [IO.File]::Copy((Join-Path $root 'PowerAgent\home-oem-seed.json'), (Join-Path $root 'PowerAgent\home-oem-settings.json'), $false)
        }
        foreach ($name in $names) {
            if (!$hardware -and $name -cin @('ONEXTweaks-PowerAgent','ONEXTweaks-OneXConsoleHandoff')) { continue }
            Register-ONEXTweaksReleaseTask $folder $name (Get-ONEXTweaksReleaseTaskDefinition $name $root $owner $rtss) $owner
            $createdTasks.Add($name)
        }
        $selection | Add-Member -NotePropertyName Files -NotePropertyValue @($files.Values | ForEach-Object Row)
        $selection | Add-Member -NotePropertyName Tasks -NotePropertyValue @($createdTasks | ForEach-Object {
            [pscustomobject]@{ Name=$_; DefinitionSha256=(Get-ONEXTweaksReleaseTaskDigest $_) }
        })
        $selection | Add-Member -NotePropertyName PowerStateOwned -NotePropertyValue $hardware
        Write-ONEXTweaksInstallationRecord $receipt $selection
    } catch {
        foreach ($name in $createdTasks) { $folder.Tasks.DeleteTask($name, 0) }
        # This root had no files before staging and is writable only by setup.
        Remove-Item -LiteralPath $root -Recurse -Force
        throw
    }
    [pscustomobject]@{ Status='Runtime staged'; OwnerSid=$owner; Hardware=$hardware; SetupComplete=$false }
} finally { $lock.Dispose() }
