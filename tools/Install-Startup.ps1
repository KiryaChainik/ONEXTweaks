#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName='Development')]
param(
    [Parameter(ParameterSetName='Development',Position=0)]
    [string]$LauncherBinary=(Join-Path $PSScriptRoot '..\src\ONEXTweaks.CompanionLauncher\bin\Release\net48\ONEXTweaks.CompanionLauncher.exe'),
    [Parameter(ParameterSetName='Prepare',Mandatory)][switch]$PrepareRelease,
    [Parameter(ParameterSetName='Install',Mandatory)][switch]$InstallRelease,
    [Parameter(ParameterSetName='Prepare',Mandatory)][Parameter(ParameterSetName='Install',Mandatory)]
    [string]$UserSid
)
$ErrorActionPreference='Stop'

function Test-StartupUser([string]$Account, [string]$Sid) {
    if ($Account -eq $Sid) { return $true }
    if (!$Account -or $Account.StartsWith('S-1-', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    try { return ([Security.Principal.NTAccount]$Account).Translate([Security.Principal.SecurityIdentifier]).Value -eq $Sid }
    catch [Security.Principal.IdentityNotMappedException] { return $false }
}

function Get-StartupPlan($Task, [string]$Sid, [string]$InstalledLauncher, [string]$DevelopmentLauncher) {
    if ($null -eq $Task) { return [pscustomobject]@{Source='Missing';Enabled=$true} }
    [xml]$xml = $Task.Xml
    $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
    $ns.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $principals = $xml.SelectNodes('/t:Task/t:Principals/*', $ns)
    $actions = $xml.SelectNodes('/t:Task/t:Actions/*', $ns)
    $triggers = $xml.SelectNodes('/t:Task/t:Triggers/*', $ns)
    if ($principals.Count -ne 1 -or $actions.Count -ne 1 -or $triggers.Count -ne 1 -or
        !$principals[0].SelectSingleNode('self::t:Principal', $ns) -or !$actions[0].SelectSingleNode('self::t:Exec', $ns) -or
        !$triggers[0].SelectSingleNode('self::t:LogonTrigger', $ns)) { throw 'Unexpected startup principal, action or trigger.' }
    $principal = $principals[0].CreateNavigator()
    $action = $actions[0].CreateNavigator()
    $trigger = $triggers[0].CreateNavigator()
    $level = $principal.Evaluate('string(t:RunLevel)', $ns)
    if (!(Test-StartupUser ($principal.Evaluate('string(t:UserId)', $ns)) $Sid) -or
        !(Test-StartupUser ($trigger.Evaluate('string(t:UserId)', $ns)) $Sid) -or
        $principal.Evaluate('string(t:LogonType)', $ns) -cne 'InteractiveToken' -or ($level -and $level -cne 'LeastPrivilege') -or
        $trigger.Evaluate('string(t:Enabled)', $ns) -cin @('false','0')) { throw 'Unexpected startup user, privilege or logon trigger.' }
    $command = $action.Evaluate('string(t:Command)', $ns)
    $arguments = $action.Evaluate('string(t:Arguments)', $ns)
    $directory = $action.Evaluate('string(t:WorkingDirectory)', $ns)
    if ($command -ieq $InstalledLauncher -and $arguments -ceq '--startup') { $source = 'Installed' }
    elseif ($command -ieq $DevelopmentLauncher -and (!$arguments -or $arguments -ceq '--startup')) { $source = 'Legacy' }
    else { throw 'Unexpected ONEXTweaks startup action.' }
    if ($directory -and $directory -ine [IO.Path]::GetDirectoryName($command)) {
        throw 'Unexpected startup working directory.'
    }
    if ($Task.Enabled -isnot [bool]) { throw 'Cannot determine ONEXTweaks startup setting.' }
    return [pscustomobject]@{Source=$source;Enabled=$Task.Enabled}
}

function Get-ExistingStartupTask($Folder) {
    try { return $Folder.GetTask('ONEXTweaks-Startup') }
    catch {
        # Only ERROR_FILE_NOT_FOUND means that creating the task is safe.
        if ($_.Exception.GetBaseException().HResult -ne -2147024894) { throw }
        return $null
    }
}

function New-ReleaseStartupXml([string]$Sid, [string]$Launcher, [bool]$Enabled) {
    $command = [Security.SecurityElement]::Escape($Launcher)
    $directory = [Security.SecurityElement]::Escape([IO.Path]::GetDirectoryName($Launcher))
    $enabledText = [Xml.XmlConvert]::ToString($Enabled)
    return @"
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Start ONEXTweaks in the signed-in user session.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>$Sid</UserId></LogonTrigger></Triggers>
  <Principals><Principal id="User"><UserId>$Sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><Enabled>$enabledText</Enabled><ExecutionTimeLimit>PT2M</ExecutionTimeLimit><RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>
  <Actions Context="User"><Exec><Command>$command</Command><Arguments>--startup</Arguments><WorkingDirectory>$directory</WorkingDirectory></Exec></Actions>
</Task>
"@
}

function Set-ReleaseStartupTask($Folder, [string]$Sid, [string]$Launcher, [string]$Legacy) {
    $existing = Get-ExistingStartupTask $Folder
    $plan = Get-StartupPlan $existing $Sid $Launcher $Legacy
    # Register the chosen state and permissions together. A disabled task never
    # becomes enabled between registration and a follow-up settings write.
    $xml = New-ReleaseStartupXml $Sid $Launcher $plan.Enabled
    $flags = if ($plan.Source -eq 'Missing') { 2 } else { 4 } # TASK_CREATE or TASK_UPDATE
    $sddl = 'D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;' + $Sid + ')'
    $null = $Folder.RegisterTask('ONEXTweaks-Startup', $xml, $flags, $Sid, $null, 3, $sddl)
    $actual = Get-StartupPlan ($Folder.GetTask('ONEXTweaks-Startup')) $Sid $Launcher $Legacy
    if ($actual.Source -ne 'Installed' -or $actual.Enabled -ne $plan.Enabled) { throw 'Windows did not confirm the release startup definition or setting.' }
    return $actual
}

if ($MyInvocation.InvocationName -eq '.') { return }

if ($PrepareRelease -or $InstallRelease) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $service = $null
    $folder = $null
    try {
        if ($UserSid -cne $identity.User.Value) { throw 'Release startup must use the Windows account that requested installation.' }
        $releaseRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
        $releaseLauncher = Join-Path $releaseRoot 'CompanionLauncher.exe'
        $legacyLauncher = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ONEXTweaks\CompanionLauncher.exe'
        $service = New-Object -ComObject Schedule.Service
        $service.Connect()
        $folder = $service.GetFolder('\')
        if ($PrepareRelease) { return Get-StartupPlan (Get-ExistingStartupTask $folder) $UserSid $releaseLauncher $legacyLauncher }
        if (![Environment]::Is64BitProcess) { throw 'Run release installation in 64-bit PowerShell.' }
        foreach ($path in @($releaseRoot,$releaseLauncher)) {
            if ((Get-Item -LiteralPath $path -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Unexpected reparse point in release launcher path: $path"
            }
        }
        if (!(Test-Path -LiteralPath $releaseLauncher -PathType Leaf)) { throw 'Install the release launcher first.' }
        foreach ($name in @('ONEXTweaks-PowerAgent','ONEXTweaks-WidgetBridge')) { $null = $folder.GetTask($name) }
        return Set-ReleaseStartupTask $folder $UserSid $releaseLauncher $legacyLauncher
    } finally {
        foreach ($object in @($folder,$service)) {
            if ($object -and [Runtime.InteropServices.Marshal]::IsComObject($object)) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($object) }
        }
        $identity.Dispose()
    }
}

$root=Join-Path $env:LOCALAPPDATA 'ONEXTweaks'
$destination=Join-Path $root 'CompanionLauncher.exe'
$taskName='ONEXTweaks-Startup'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$sid=$identity.User.Value
if(!(Test-Path -LiteralPath $LauncherBinary)){throw 'Build the companion launcher first.'}
foreach($name in @('ONEXTweaks-PowerAgent','ONEXTweaks-WidgetBridge')){$null=Get-ScheduledTask -TaskPath '\' -TaskName $name}
$plan = & $PSCommandPath -PrepareRelease -UserSid $sid
if ($plan.Source -eq 'Installed') { throw 'A release startup task already owns this name.' }
$enabled = $plan.Enabled
$null=New-Item -ItemType Directory -Path $root -Force
Copy-Item -LiteralPath $LauncherBinary -Destination $destination -Force
if(Test-Path -LiteralPath ($LauncherBinary+'.config')){Copy-Item -LiteralPath ($LauncherBinary+'.config') -Destination ($destination+'.config') -Force}
$action=New-ScheduledTaskAction -Execute $destination -WorkingDirectory $root
$principal=New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Limited
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $sid
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$settings.Enabled=$enabled
$null=Register-ScheduledTask -TaskPath '\' -TaskName $taskName -Action $action -Principal $principal -Trigger $trigger -Settings $settings -Description 'Start the ONEXTweaks helper and widget together in the signed-in user session.' -Force:($plan.Source -eq 'Legacy')
# Only this unelevated task is editable through the widget. The privileged agent's
# fixed action and permissions remain under the existing installer.
$service=New-Object -ComObject Schedule.Service
try{
    $service.Connect();$folder=$service.GetFolder('\');$task=$folder.GetTask($taskName)
    $task.SetSecurityDescriptor(('D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;'+$sid+')'),0)
    $task.Enabled=$enabled
}finally{
    foreach($object in @($task,$folder,$service)){if($object -and [Runtime.InteropServices.Marshal]::IsComObject($object)){[void][Runtime.InteropServices.Marshal]::ReleaseComObject($object)}}
}
# Retire the earlier independent logon trigger. Manual task runs remain available
# even when the new common startup setting is switched off.
foreach($name in @('ONEXTweaks-WidgetBridge','ONEXTweaks-PowerAgent')){
    $companion=Get-ScheduledTask -TaskName $name
    if(@($companion.Triggers | Where-Object {$_}).Count){
        [xml]$xml=Export-ScheduledTask -TaskName $name
        $ns=New-Object Xml.XmlNamespaceManager($xml.NameTable);$ns.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
        $triggers=$xml.SelectSingleNode('/t:Task/t:Triggers',$ns)
        if($triggers){$triggers.RemoveAll()}
        $null=Register-ScheduledTask -TaskName $name -Xml $xml.OuterXml -Force
    }
}
$bridge=Get-ScheduledTask ONEXTweaks-WidgetBridge
$bridge.Settings.RestartCount=3
$bridge.Settings.RestartInterval='PT1M'
$null=Set-ScheduledTask -TaskName ONEXTweaks-WidgetBridge -Settings $bridge.Settings
[pscustomobject]@{Task=$taskName;Enabled=(Get-ScheduledTask -TaskName $taskName).Settings.Enabled;RunLevel='Limited';Paired=$true}
