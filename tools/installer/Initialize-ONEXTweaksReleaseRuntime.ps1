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

function Write-ONEXTweaksInstallationRecord([string]$Path, $Record) {
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, ($Record | ConvertTo-Json -Depth 6 -Compress))
        [IO.File]::Move($temporary, $Path)
    } finally { [IO.File]::Delete($temporary) }
}

function Register-ONEXTweaksRuntimeTask($Folder, [string]$Name, [string]$Exe, [string]$Arguments, [string]$Owner,
    [ValidateSet('Bridge','Hardware','Overlay','Recovery','OemHandoff')][string]$Purpose) {
    $system = $Purpose -in @('Hardware','OemHandoff')
    $account = if ($system) { 'S-1-5-18' } else { $Owner }
    $logon = if ($system) { 5 } else { 3 }
    $definition = $Folder.Service.NewTask(0)
    $definition.RegistrationInfo.Description = "ONEXTweaks $Purpose"
    $definition.Principal.UserId = $account
    $definition.Principal.LogonType = $logon
    $definition.Principal.RunLevel = [int]($Purpose -eq 'Overlay')
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
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
    $rights = 'D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;' + $Owner + ')'
    if ($Purpose -eq 'OemHandoff') { $rights = 'D:P(A;;GA;;;SY)(A;;GA;;;BA)' }
    $null = $Folder.Tasks.RegisterTaskDefinition($Name, $definition, 2, $account, $null, $logon, $rights)
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
    if (Test-Path -LiteralPath $receipt) { throw 'A release already owns the machine. Use the release upgrade or uninstall path.' }
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
    if (Test-Path -LiteralPath $root) { throw 'An existing machine runtime is present. Use a reviewed migration to preserve it.' }

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
    $files = Get-ONEXTweaksRuntimePayload $manifest $manifestFile.DirectoryName $root $hardware
    $rtss = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ } | ForEach-Object {
        Join-Path $_ 'RivaTuner Statistics Server\RTSS.exe'
    } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$rtss -or (Get-RtssPrerequisiteState) -ne 'Installed') { throw 'Install and verify RTSS before staging the runtime.' }
    $names = @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-RTSS','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-PowerAgent',
        'ONEXTweaks-OneXConsoleHandoff')
    $collisions = @(Get-ScheduledTask -TaskPath '\' | Where-Object TaskName -in $names)
    if ($collisions.Count) { throw ('Existing runtime tasks require a reviewed migration: ' + ($collisions.TaskName -join ', ')) }

    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = [pscustomobject]@{ Service=$service; Tasks=$service.GetFolder('\') }
    $launcher = Join-Path $root 'CompanionLauncher.exe'
    New-ONEXTweaksProtectedDirectory $root
    $createdTasks = New-Object 'Collections.Generic.List[string]'
    try {
        foreach ($item in $files.Values) {
            $destination = Join-Path $root $item.Row.Path
            $null = [IO.Directory]::CreateDirectory((Split-Path -LiteralPath $destination))
            [IO.File]::Copy($item.Source, $destination, $false)
            $null = Get-ONEXTweaksOwnedFile $root $item.Row
        }
        Register-ONEXTweaksRuntimeTask $folder 'ONEXTweaks-WidgetBridge' (Join-Path $root 'WidgetBridge\ONEXTweaks.WidgetBridge.exe') '' $owner Bridge
        $createdTasks.Add('ONEXTweaks-WidgetBridge')
        Register-ONEXTweaksRuntimeTask $folder 'ONEXTweaks-ControllerInputRecovery' (Join-Path $root 'WidgetBridge\ONEXTweaks.WidgetBridge.exe') '--controller-login-recovery' $owner Recovery
        $createdTasks.Add('ONEXTweaks-ControllerInputRecovery')
        if ($hardware) {
            [IO.File]::Copy((Join-Path $root 'PowerAgent\home-oem-seed.json'), (Join-Path $root 'PowerAgent\home-oem-settings.json'), $false)
            Register-ONEXTweaksRuntimeTask $folder 'ONEXTweaks-PowerAgent' (Join-Path $root 'PowerAgent\ONEXTweaks.PowerAgent.exe') "serve-desktop $owner" $owner Hardware
            $createdTasks.Add('ONEXTweaks-PowerAgent')
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
            Register-ONEXTweaksRuntimeTask $folder 'ONEXTweaks-OneXConsoleHandoff' $windowsPowerShell $resumeArguments $owner OemHandoff
            $createdTasks.Add('ONEXTweaks-OneXConsoleHandoff')
        }
        Register-ONEXTweaksRuntimeTask $folder 'ONEXTweaks-RTSS' $rtss '' $owner Overlay
        $createdTasks.Add('ONEXTweaks-RTSS')
        $startupXml = New-ReleaseStartupXml $owner $launcher $true
        $startupRights = 'D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;' + $owner + ')'
        $null = $folder.Tasks.RegisterTask('ONEXTweaks-Startup', $startupXml, 2, $owner, $null, 3, $startupRights)
        $createdTasks.Add('ONEXTweaks-Startup')
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
