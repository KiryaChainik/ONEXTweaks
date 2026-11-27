#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName='Report')]
param(
    [Parameter(ParameterSetName='Report')][switch]$Report,
    [Parameter(Mandatory, ParameterSetName='Both')][switch]$Uninstall,
    [Parameter(Mandatory, ParameterSetName='Owner')][switch]$OwnerPhase,
    [Parameter(Mandatory, ParameterSetName='Machine')][switch]$MachinePhase,
    [Parameter(ParameterSetName='Both')][Parameter(ParameterSetName='Owner')][switch]$DiscardWidgetData
)

function Assert-ONEXTweaksProtectedItem([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected reparse point: $Path" }
    $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
    $trusted = @('S-1-5-18','S-1-5-32-544')
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) { throw "Unprotected owner: $Path" }
    # Synchronize alone is included in ordinary read permissions. Exclude it
    # from the write mask, which otherwise covers content, metadata and ACLs.
    $writes = [Security.AccessControl.FileSystemRights]'Write,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and ($rule.FileSystemRights -band $writes) -and $rule.IdentityReference.Value -notin $trusted) {
            throw "Unprotected write grant: $Path"
        }
    }
}

function Assert-ONEXTweaksProtectedStateDirectory([string]$StateDirectory) {
    Assert-ONEXTweaksProtectedItem (Split-Path -LiteralPath $StateDirectory)
    Assert-ONEXTweaksProtectedItem $StateDirectory
}

function Get-ONEXTweaksReleaseStateDirectory([string]$CommonApplicationData = [Environment]::GetFolderPath('CommonApplicationData')) {
    $current = Join-Path $CommonApplicationData 'ONEXTweaksInstaller\Installation'
    $previous = Join-Path $CommonApplicationData 'ONEXTweaks\Installation'
    $records = @('desktop-enrollment.json','owned-release.json','upgrade-1109.json','runtime-update-1110.json','onexconsole-handoff.json','pending-restart.json')
    $previousRecords = @($records | Where-Object { Test-Path -LiteralPath (Join-Path $previous $_) -ErrorAction Stop })
    if (!$previousRecords.Count) { return $current }

    # Keep using a valid prior installer transaction without trusting legacy app data.
    Assert-ONEXTweaksProtectedStateDirectory $previous
    foreach ($record in $previousRecords) { Assert-ONEXTweaksProtectedItem (Join-Path $previous $record) }
    if (@($records | Where-Object { Test-Path -LiteralPath (Join-Path $current $_) -ErrorAction Stop }).Count) {
        throw 'Conflicting ONEXTweaks installer records exist in two directories.'
    }
    return $previous
}

function Get-ONEXTweaksReleaseTaskDigest([string]$Name) {
    return Get-ONEXTweaksTaskXmlDigest (Export-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop)
}

function Get-ONEXTweaksTaskXmlDigest([xml]$Xml) {
    $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
    $ns.AddNamespace('s','http://schemas.microsoft.com/windows/2004/02/mit/task')
    $settings = $xml.SelectSingleNode('/s:Task/s:Settings', $ns)
    if (!$settings) { throw "Invalid registered task definition: $Name" }
    $enabled = $settings.SelectSingleNode('s:Enabled', $ns)
    if ($enabled) { [void]$settings.RemoveChild($enabled) }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($xml.DocumentElement.OuterXml))).Replace('-','')
    } finally { $sha.Dispose() }
}

function Get-ONEXTweaksComTask($Folder, [string]$Name) {
    try { return $Folder.GetTask($Name) }
    catch {
        if ($_.Exception.GetBaseException().HResult -ne -2147024894) { throw }
    }
}

function Get-ONEXTweaksTaskFolder {
    $scheduler = New-Object -ComObject Schedule.Service
    $scheduler.Connect()
    $scheduler.GetFolder('\')
}

function Test-ONEXTweaksRegisteredTask([string]$Name) {
    $null -ne (Get-ONEXTweaksComTask (Get-ONEXTweaksTaskFolder) $Name)
}

function Get-ONEXTweaksComOwnedTask($Plan, $Folder, [string]$Name) {
    if (!$Plan.Tasks.ContainsKey($Name)) { return }
    $expected = $Plan.Tasks[$Name].DefinitionSha256
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $seen = $false
    do {
        $task = Get-ONEXTweaksComTask $Folder $Name
        if ($task) {
            $seen = $true
            if ((Get-ONEXTweaksTaskXmlDigest $task.Xml) -ieq $expected) { return $task }
        }
        Start-Sleep -Milliseconds 500
    } while ($watch.Elapsed.TotalSeconds -lt 12)
    if ($seen) { throw "Scheduled task did not match its owned definition: $Name" }
}

function Get-ONEXTweaksOwnedFile([string]$Root, $Row) {
    if ($Row.Path -isnot [string] -or $Row.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid release file receipt.' }
    $path = $Root
    foreach ($part in ($Row.Path -split '[\\/]')) {
        if ($part -notmatch '^[A-Za-z0-9_@+() -][A-Za-z0-9_.@+() -]*$' -or $part -match '[ .]$|^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)') {
            throw "Unsafe release file path: $($Row.Path)"
        }
        $path = Join-Path $path $part
        if (Test-Path -LiteralPath $path -ErrorAction Stop) { Assert-ONEXTweaksProtectedItem $path }
    }
    if (Test-Path -LiteralPath $path -ErrorAction Stop) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf -ErrorAction Stop) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash -ine $Row.Sha256) {
            throw "Release file was replaced: $($Row.Path)"
        }
    }
    return $path
}

function Get-ONEXTweaksUninstallPlan([string]$StateDirectory,[string]$Root,[switch]$OwnerOnly) {
    $receiptPath = Join-Path $StateDirectory 'owned-release.json'
    if (!(Test-Path -LiteralPath $receiptPath -ErrorAction Stop)) {
        if (Test-Path -LiteralPath (Join-Path $StateDirectory 'onexconsole-handoff.json') -ErrorAction Stop) { throw 'OEM handoff exists without a release receipt.' }
        return
    }
    Assert-ONEXTweaksProtectedStateDirectory $StateDirectory
    Assert-ONEXTweaksProtectedItem $receiptPath
    $receipt = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json -ErrorAction Stop
    $installId = [guid]::Empty
    if ($receipt.Schema -ne 1 -or ![guid]::TryParse([string]$receipt.InstallId, [ref]$installId) -or $installId -eq [guid]::Empty -or
        $receipt.OwnerSid -notmatch '^S-1-(?:\d+-)+\d+$' -or
        $receipt.PackageOwned -isnot [bool] -or $receipt.PackageFamilyName -cnotmatch '^ONEXTweaks\.Widget_([a-z0-9]{13})$') { throw 'Invalid release identity receipt.' }
    $publisherId = $Matches[1]
    if ($receipt.PackageFullName -cnotmatch ('^ONEXTweaks\.Widget_[0-9]+(\.[0-9]+){3}_x64__' + $publisherId + '$') -or
        $receipt.Files -isnot [array] -or $receipt.Tasks -isnot [array]) { throw 'Invalid release package or ownership lists.' }
    if ($receipt.PSObject.Properties['GuideOwned'] -and $receipt.GuideOwned -isnot [bool]) { throw 'Invalid Guide ownership value.' }
    if ($receipt.PSObject.Properties['PowerStateOwned'] -and $receipt.PowerStateOwned -isnot [bool]) { throw 'Invalid hardware state ownership value.' }
    if (Test-Path -LiteralPath $Root -ErrorAction Stop) { Assert-ONEXTweaksProtectedItem $Root }
    $files = @{}
    foreach ($row in $receipt.Files) {
        $full = Get-ONEXTweaksOwnedFile $Root $row
        if ($files.ContainsKey($full)) { throw "Duplicated release file: $full" }
        $files[$full] = $row
    }
    $names = @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-PowerAgent','ONEXTweaks-ControllerInputRecovery','ONEXTweaks-RTSS',
        'ONEXTweaks-OneXConsoleHandoff')
    $tasks = @{}
    foreach ($row in $receipt.Tasks) {
        if ($row.Name -cnotin $names -or $row.DefinitionSha256 -notmatch '^[a-fA-F0-9]{64}$' -or $tasks.ContainsKey($row.Name)) {
            throw 'Invalid release task receipt.'
        }
        $tasks[$row.Name] = $row
    }
    $plan = [pscustomobject]@{ Receipt=$receipt; ReceiptPath=$receiptPath; Digest=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;
        Root=$Root; StateDirectory=$StateDirectory; Files=$files; Tasks=$tasks }
    if (!$OwnerOnly) {
        foreach ($name in $tasks.Keys) { $null = Get-ONEXTweaksRemainingTask $plan $name }
    }
    return $plan
}

function Get-ONEXTweaksInteractiveSid {
    $session = (Get-Process -Id $PID -ErrorAction Stop).SessionId
    $owners = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe' AND SessionId=$session" -ErrorAction Stop | ForEach-Object {
        $owner = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid -ErrorAction Stop
        if ($owner.ReturnValue -ne 0 -or !$owner.Sid) { throw 'Cannot resolve interactive desktop account.' }
        $owner.Sid
    } | Sort-Object -Unique)
    if ($owners.Count -ne 1) { throw 'Use the signed-in desktop of the installation owner.' }
    return $owners[0]
}

function Get-ONEXTweaksOwnerPackage($Plan, [switch]$TargetUser) {
    if (!$Plan.Receipt.PackageOwned) { return }
    $arguments = @{ Name='ONEXTweaks.Widget'; ErrorAction='Stop' }
    if ($TargetUser) { $arguments.User = $Plan.Receipt.OwnerSid }
    $found = @(Get-AppxPackage @arguments)
    if (!$found.Count) { return }
    if ($found.Count -ne 1 -or $found[0].PackageFullName -cne $Plan.Receipt.PackageFullName -or
        $found[0].PackageFamilyName -cne $Plan.Receipt.PackageFamilyName) { throw 'Widget registration has changed since installation.' }
    return $found[0]
}

function Get-ONEXTweaksPrivateFiles([string]$Source) {
    # Traverse a directory at a time so that a junction is rejected before it
    # is followed. File/directory relative names come solely from enumeration.
    $todo = [Collections.Generic.Stack[string]]::new()
    $todo.Push($Source)
    while ($todo.Count) {
        $path = $todo.Pop()
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected link in widget data: $path" }
        if ($item.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $path -Force -ErrorAction Stop) { $todo.Push($child.FullName) }
        } else { $item }
    }
}

function Backup-ONEXTweaksWidgetData($Plan, [string]$Local) {
    $source = Join-Path (Join-Path $Local 'Packages') $Plan.Receipt.PackageFamilyName
    if (!(Test-Path -LiteralPath $source -PathType Container -ErrorAction Stop)) { return }
    foreach ($parent in @((Join-Path $Local 'Packages'),$source)) {
        if ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected widget data location: $parent" }
    }
    $root = Join-Path $Local 'ONEXTweaks'
    foreach ($folder in @($root,(Join-Path $root 'UninstallBackups'))) {
        [void][IO.Directory]::CreateDirectory($folder)
        if ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected backup location: $folder" }
    }
    $destination = Join-Path (Join-Path $root 'UninstallBackups') ([guid]::NewGuid().ToString('N'))
    $target = $destination + '.partial'
    $hashes = @{}
    $persistent = @('LocalState','RoamingState','Settings','LocalCache')
    foreach ($folder in $persistent) {
        $directory = Join-Path $source $folder
        if (!(Test-Path -LiteralPath $directory -ErrorAction Stop)) { continue }
        foreach ($file in Get-ONEXTweaksPrivateFiles $directory) {
            $relative = $file.FullName.Substring($source.Length+1)
            $copy = Join-Path $target $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copy))
            [IO.File]::Copy($file.FullName,$copy,$false)
            $hashes[$relative] = (Get-FileHash -LiteralPath $copy -Algorithm SHA256 -ErrorAction Stop).Hash
        }
    }
    foreach ($folder in $persistent) {
        $directory = Join-Path $source $folder
        if (!(Test-Path -LiteralPath $directory -ErrorAction Stop)) { continue }
        foreach ($file in Get-ONEXTweaksPrivateFiles $directory) {
            $relative = $file.FullName.Substring($source.Length+1)
            if ($hashes[$relative] -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash) {
                throw 'Widget data changed during preservation. Close the widget and retry.'
            }
            $hashes.Remove($relative)
        }
    }
    if ($hashes.Count) { throw 'Widget data changed during preservation. Close the widget and retry.' }
    if ([IO.Directory]::Exists($target)) {
        [IO.Directory]::Move($target,$destination)
        return $destination
    }
}

function Open-ONEXTweaksPackageMaintenance([string]$PackageFullName) {
    if (!('OnexPackageMaintenance' -as [type])) {
        # vtable order from the Windows SDK IPackageDebugSettings interface.
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public sealed class OnexPackageMaintenance : IDisposable
{
    [ComImport, Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPackageDebugSettings
    {
        void EnableDebugging([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr command, IntPtr environment);
        void DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] string name);
        void Suspend([MarshalAs(UnmanagedType.LPWStr)] string name);
        void Resume([MarshalAs(UnmanagedType.LPWStr)] string name);
        void TerminateAllProcesses([MarshalAs(UnmanagedType.LPWStr)] string name);
        void SetTargetSessionId(uint session);
        void EnumerateBackgroundTasks([MarshalAs(UnmanagedType.LPWStr)] string name, out uint count, out IntPtr ids, out IntPtr names);
        void ActivateBackgroundTask(ref Guid id);
        void StartServicing([MarshalAs(UnmanagedType.LPWStr)] string name);
        void StopServicing([MarshalAs(UnmanagedType.LPWStr)] string name);
    }
    private readonly string name;
    private readonly IPackageDebugSettings settings;

    public OnexPackageMaintenance(string package)
    {
        name = package;
        var type = Type.GetTypeFromCLSID(new Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D"));
        settings = (IPackageDebugSettings)Activator.CreateInstance(type);
        try { settings.StartServicing(name); }
        catch { Marshal.ReleaseComObject(settings); throw; }
    }

    public void Dispose()
    {
        try { settings.StopServicing(name); }
        finally { Marshal.ReleaseComObject(settings); }
    }
}
'@ -ErrorAction Stop
    }
    New-Object OnexPackageMaintenance -ArgumentList $PackageFullName
}

function Get-ONEXTweaksVerifiedTool($Plan, [string]$Relative) {
    $path = Join-Path $Plan.Root $Relative
    if (!$Plan.Files.ContainsKey($path) -or !(Test-Path -LiteralPath $path -PathType Leaf -ErrorAction Stop)) { throw "Owned tool unavailable: $Relative" }
    return Get-ONEXTweaksOwnedFile $Plan.Root $Plan.Files[$path]
}

function Invoke-ONEXTweaksOwnerRemoval($Plan, [string]$Local, [bool]$Discard) {
    $lease = Join-Path $Local 'ONEXTweaks\dev\privateReports\controller-capture-lease.json'
    Assert-ONEXTweaksReleasedController $Plan $lease
    $package = Get-ONEXTweaksOwnerPackage $Plan
    $settings = 'HKCU:\Software\ONEXTweaks'
    $key = Join-Path $settings 'Installation'
    if (!$package -and (Test-Path -LiteralPath $key -ErrorAction Stop)) {
        $old = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
        if ($old.PSObject.Properties['OwnerRemovalReceipt'] -and $old.OwnerRemovalReceipt -ceq $Plan.Digest) { return }
    }
    $backup = $null
    if ($package -and !$Discard) {
        $maintenance = Open-ONEXTweaksPackageMaintenance $package.PackageFullName
        try {
            for ($attempt=0; $attempt -lt 8; $attempt++) {
                try { $backup = Backup-ONEXTweaksWidgetData $Plan $Local; break }
                catch {
                    $cause = $_.Exception.GetBaseException()
                    if ($attempt -eq 7 -or $cause -isnot [IO.IOException] -or ($cause.HResult -band 0xffff) -notin @(32,33)) { throw }
                    Start-Sleep -Milliseconds 500
                }
            }
        } finally { $maintenance.Dispose() }
    }
    if ($Plan.Receipt.PSObject.Properties['GuideOwned'] -and $Plan.Receipt.GuideOwned) {
        $guide = Get-ONEXTweaksVerifiedTool $Plan 'Tools\Set-DirectGuideButton.ps1'
        $null = Get-ONEXTweaksVerifiedTool $Plan 'Tools\Set-GameBarStartPage.ps1'
        & $guide -Restore | Out-Null
    }
    # Prepare the owner receipt key before unregistering the widget. AppContainer
    # settings don't create the corresponding HKCU keys for a clean desktop.
    foreach ($path in @($settings, $key)) {
        if (!(Test-Path -LiteralPath $path -ErrorAction Stop)) { $null = New-Item -Path $path -ErrorAction Stop }
    }
    if ($package) {
        Assert-ONEXTweaksReleasedController $Plan $lease
        Remove-AppxPackage -Package $package.PackageFullName -Confirm:$false -ErrorAction Stop
        if (Get-ONEXTweaksOwnerPackage $Plan) { throw 'Windows still reports the widget registration.' }
    }
    $null = New-ItemProperty -LiteralPath $key -Name OwnerRemovalReceipt -PropertyType String -Value $Plan.Digest -Force -ErrorAction Stop
    return $backup
}

function Get-ONEXTweaksRemainingTask($Plan, [string]$Name) {
    if (!$Plan.Tasks.ContainsKey($Name)) { return }
    $expected = $Plan.Tasks[$Name].DefinitionSha256
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        try {
            $tasks = @(Get-ScheduledTask -TaskPath '\' -TaskName $Name -ErrorAction Stop |
                Where-Object { $_.TaskName -ieq $Name -and $_.TaskPath -eq '\' })
        } catch {
            if ($_.Exception.GetBaseException().HResult -ne -2147024894 -and
                $_.FullyQualifiedErrorId -notmatch '^HRESULT 0x80070002(?:,|$)|^CmdletizationQuery_NotFound') { throw }
            $tasks = @()
        }
        if ($tasks.Count -gt 1) { throw "Multiple registered tasks match the owned task: $Name" }
        if (!$tasks.Count -and !(Test-ONEXTweaksRegisteredTask $Name)) { return }
        if ($tasks.Count) {
            try {
                if ((Get-ONEXTweaksReleaseTaskDigest $Name) -ieq $expected) { return $tasks[0] }
            } catch {
                if ($_.Exception.GetBaseException().HResult -ne -2147024894 -and
                    $_.FullyQualifiedErrorId -notmatch '^HRESULT 0x80070002(?:,|$)') { throw }
            }
        }
        if ($attempt -lt 7) { Start-Sleep -Milliseconds 500 }
    }
    throw "Scheduled task was replaced or could not be verified: $Name"
}

function Assert-ONEXTweaksReleasedController($Plan, [string]$Lease, $SchedulerFolder) {
    if (!(Test-Path -LiteralPath $Lease -ErrorAction Stop)) { return }
    $name = 'ONEXTweaks-ControllerInputRecovery'
    if ($Plan.Tasks.ContainsKey($name)) {
        $expected = Join-Path $Plan.Root 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'
        $ready = $false
        if ($SchedulerFolder) {
            $task = Get-ONEXTweaksComOwnedTask $Plan $SchedulerFolder $name
            if ($task) {
                $definition = $task.Definition
                $ready = $definition.Principal.RunLevel -eq 0 -and $definition.Principal.LogonType -eq 3 -and
                    (Get-ONEXTweaksAccountSid $definition.Principal.UserId) -ceq $Plan.Receipt.OwnerSid -and
                    $definition.Actions.Count -eq 1 -and $definition.Actions.Item(1).Path -ieq $expected -and
                    $definition.Actions.Item(1).Arguments -ceq '--controller-login-recovery'
            }
        } else {
            $task = Get-ONEXTweaksRemainingTask $Plan $name
            $ready = $task -and $task.Principal.RunLevel -eq 'Limited' -and (Test-ONEXTweaksUserPrincipal $task $Plan.Receipt.OwnerSid) -and
                @($task.Actions).Count -eq 1 -and $task.Actions[0].Execute -ieq $expected -and
                $task.Actions[0].Arguments -ceq '--controller-login-recovery'
        }
        if ($ready) {
            $null = Get-ONEXTweaksVerifiedTool $Plan 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'
            if ($SchedulerFolder) { $null = $task.Run($null) } else { Start-ScheduledTask -InputObject $task -ErrorAction Stop }
            for ($i=0; $i -lt 40; $i++) {
                if (!(Test-Path -LiteralPath $Lease -ErrorAction Stop)) { return }
                Start-Sleep -Milliseconds 250
            }
        }
    }
    throw 'Controller capture is active or recovery is incomplete. Release controller capture on the owner desktop before removing tasks and files.'
}

function Suspend-ONEXTweaksOwnedController($Plan, [string]$Lease) {
    if (!(Test-Path -LiteralPath $Lease)) { return }
    $bridge = Get-ONEXTweaksVerifiedTool $Plan 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'
    $launcher = Get-ONEXTweaksVerifiedTool $Plan 'CompanionLauncher.exe'
    $writers = @(foreach ($name in @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge')) {
        if (!$Plan.Tasks.ContainsKey($name)) { throw "Missing controller writer identity: $name" }
        Get-ONEXTweaksRemainingTask $Plan $name
    })
    $enabled = @($writers | Where-Object { $_.Settings.Enabled } | ForEach-Object TaskName)
    try {
        foreach ($task in $writers) {
            if ($task.Settings.Enabled) { Disable-ScheduledTask -InputObject $task | Out-Null }
            if ($task.State -in @('Running','Queued')) { Stop-ScheduledTask -InputObject $task }
        }
        $session = (Get-Process -Id $PID).SessionId
        foreach ($process in Get-Process -Name 'ONEXTweaks.WidgetBridge','CompanionLauncher' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $session -and $_.Path -in @($bridge,$launcher) }) {
            if (!$process.HasExited) {
                $process.Kill()
                if (!$process.WaitForExit(8000)) { throw 'An owned controller writer did not stop.' }
            }
        }
        Assert-ONEXTweaksReleasedController $Plan $Lease
    } catch {
        foreach ($name in $enabled) {
            $task = Get-ONEXTweaksRemainingTask $Plan $name
            if ($task) { Enable-ScheduledTask -InputObject $task | Out-Null }
        }
        if ($enabled -contains 'ONEXTweaks-WidgetBridge') {
            $task = Get-ONEXTweaksRemainingTask $Plan 'ONEXTweaks-WidgetBridge'
            if ($task) { Start-ScheduledTask -InputObject $task }
        }
        throw
    }
}

function Wait-ONEXTweaksHardwareRestore($Plan) {
    if (!$Plan.Receipt.PSObject.Properties['PowerStateOwned'] -or !$Plan.Receipt.PowerStateOwned) { return }
    $directory = Join-Path $Plan.Root 'PowerAgent'
    for ($i=0; $i -lt 40; $i++) {
        $pending = @('fan-control-journal','game-lighting-lease.json' | Where-Object {
            Test-Path -LiteralPath (Join-Path $directory $_)
        })
        if (!$pending.Count) { return }
        Start-Sleep -Milliseconds 250
    }
    throw 'PowerAgent has not restored firmware fan or temporary lighting state. Keep the recovery files and complete recovery before uninstall.'
}

function Test-ONEXTweaksLegacyFanMarker($Item) {
    if ($Item -isnot [IO.FileInfo] -or $Item.Name -cnotmatch '^fan-watchdog-[1-9][0-9]{0,9}-[1-9][0-9]{0,18}\.ready$') {
        return $false
    }
    Assert-ONEXTweaksProtectedItem $Item.FullName
    return $Item.Length -eq 5 -and [IO.File]::ReadAllText($Item.FullName) -ceq 'ready'
}

function Remove-ONEXTweaksLegacyFanResidue([string]$Root) {
    Assert-ONEXTweaksProtectedItem $Root
    $children = @(Get-ChildItem -LiteralPath $Root -Force -ErrorAction Stop)
    if ($children.Count -ne 1 -or $children[0] -isnot [IO.DirectoryInfo] -or $children[0].Name -cne 'PowerAgent') { return }
    $directory = $children[0].FullName
    Assert-ONEXTweaksProtectedItem $directory
    $markers = @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)
    if (!$markers.Count) { return }
    foreach ($marker in $markers) {
        if (!(Test-ONEXTweaksLegacyFanMarker $marker)) { return }
    }
    foreach ($marker in $markers) { [IO.File]::Delete($marker.FullName) }
    [IO.Directory]::Delete($directory)
    [IO.Directory]::Delete($Root)
}

function Remove-ONEXTweaksOwnedMachineState($Plan) {
    $selectionPath = Join-Path $Plan.StateDirectory 'desktop-enrollment.json'
    if (Test-Path -LiteralPath $selectionPath) {
        Assert-ONEXTweaksProtectedItem $selectionPath
        $selection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
        if ($selection.InstallId -cne $Plan.Receipt.InstallId -or $selection.OwnerSid -cne $Plan.Receipt.OwnerSid) {
            throw 'The desktop enrollment has changed. Keep the release receipt for review.'
        }
        [IO.File]::Delete($selectionPath)
    }
    if (!$Plan.Receipt.PSObject.Properties['PowerStateOwned'] -or !$Plan.Receipt.PowerStateOwned) { return }
    $directory = Join-Path $Plan.Root 'PowerAgent'
    $hardwareData = Join-Path $Plan.StateDirectory 'RetainedHardwareSettings'
    if (Test-Path -LiteralPath $directory -PathType Container) {
        Assert-ONEXTweaksProtectedItem $directory
        # Older PowerAgents left this marker behind if Task Scheduler stopped the watchdog.
        foreach ($marker in Get-ChildItem -LiteralPath $directory -File -Force -Filter 'fan-watchdog-*.ready' -ErrorAction Stop) {
            if (Test-ONEXTweaksLegacyFanMarker $marker) { [IO.File]::Delete($marker.FullName) }
        }
    }
    foreach ($name in @('home-oem-settings.json','led-color-profile.json','boost-modes.json')) {
        $file = Join-Path $directory $name
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        Assert-ONEXTweaksProtectedItem $file
        $null = [IO.Directory]::CreateDirectory($hardwareData)
        Assert-ONEXTweaksProtectedItem $hardwareData
        $backup = Join-Path $hardwareData ([string]$Plan.Receipt.InstallId)
        $null = [IO.Directory]::CreateDirectory($backup)
        Assert-ONEXTweaksProtectedItem $backup
        [IO.File]::Move($file, (Join-Path $backup $name))
    }
    foreach ($name in 'power-agent-result.json','installer-hardware-ready.json','installer-hardware-ready.json.tmp','onexconsole-resume-error.json',
        'hardware-health.jsonl','hardware-health.jsonl.prev') {
        $report = Join-Path $directory $name
        if (Test-Path -LiteralPath $report -PathType Leaf) {
            Assert-ONEXTweaksProtectedItem $report
            [IO.File]::Delete($report)
        }
    }
}

function Stop-ONEXTweaksOwnedBridgeProcesses($Plan) {
    $images = @(foreach ($relative in @('CompanionLauncher.exe','WidgetBridge\ONEXTweaks.WidgetBridge.exe','WidgetBridge\ONEXTweaks.ControllerObserver.exe')) {
        $path = Join-Path $Plan.Root $relative
        if ($Plan.Files.ContainsKey($path) -and (Test-Path -LiteralPath $path -PathType Leaf -ErrorAction Stop)) {
            Get-ONEXTweaksVerifiedTool $Plan $relative
        }
    })
    if (!$images.Count) { return }
    $names = @($images | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
    $session = (Get-Process -Id $PID).SessionId
    foreach ($process in @(Get-Process -Name $names -ErrorAction SilentlyContinue)) {
        try {
            if ($process.SessionId -ne $session -or $process.Path -notin $images) { continue }
            # Keep the original process object bound to its Windows handle before
            # validating the owner. A disappearing process is already stopped.
            $null = $process.Handle
            if ($process.HasExited) { continue }
            $actor = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" -ErrorAction Stop
            if (!$actor -or $actor.SessionId -ne $session -or $actor.ExecutablePath -notin $images -or
                (Invoke-CimMethod -InputObject $actor -MethodName GetOwnerSid -ErrorAction Stop).Sid -cne $Plan.Receipt.OwnerSid) { continue }
            if (!$process.HasExited) { $process.Kill() }
            if (!$process.WaitForExit(8000)) { throw "Owned Bridge process did not stop: $($process.Id)" }
        } catch {
            if (!$process.HasExited) { throw }
        } finally { $process.Dispose() }
    }
}

function Remove-ONEXTweaksOwnedReleaseFile($Plan, $Row) {
    for ($attempt = 0; ; $attempt++) {
        try {
            $path = Get-ONEXTweaksOwnedFile $Plan.Root $Row
            if (!(Test-Path -LiteralPath $path -PathType Leaf -ErrorAction Stop)) { return }
            Remove-Item -LiteralPath $path -Force -ErrorAction Stop
            return
        } catch {
            # Windows can report access denied for a runtime image that is
            # still mapped while the Bridge process is being torn down.
            $code = $_.Exception.GetBaseException().HResult -band 0xffff
            if ($attempt -ge 20 -or $Row.Path -notmatch '^WidgetBridge[\\/]' -or $code -notin @(5,32,33,1224)) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Stop-ONEXTweaksReleaseTask($Task, [string]$Name) {
    if ($Task.Enabled) { $Task.Enabled = $false }
    # A disabled task can still have running instances.
    if (!$Task.GetInstances(0).Count) { return }
    $Task.Stop(0)
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (!$Task.GetInstances(0).Count) { return }
        Start-Sleep -Milliseconds 250
    }
    if ($Task.GetInstances(0).Count) { throw "Scheduled task did not stop: $Name" }
}

function Invoke-ONEXTweaksMachineRemoval($Plan, [string]$Lease) {
    $marker = "Registry::HKEY_USERS\$($Plan.Receipt.OwnerSid)\Software\ONEXTweaks\Installation"
    if ((Get-ItemProperty -LiteralPath $marker -Name OwnerRemovalReceipt -ErrorAction Stop).OwnerRemovalReceipt -cne $Plan.Digest) {
        throw 'Complete the owner removal phase for this release first.'
    }
    if (Get-ONEXTweaksOwnerPackage $Plan -TargetUser) { throw 'The owner still has this widget registered.' }
    Assert-ONEXTweaksReleasedController $Plan $Lease
    $folder = Get-ONEXTweaksTaskFolder
    foreach ($name in @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge','ONEXTweaks-PowerAgent','ONEXTweaks-ControllerInputRecovery')) {
        if (!$Plan.Tasks.ContainsKey($name)) { continue }
        if ($name -eq 'ONEXTweaks-PowerAgent') { Wait-ONEXTweaksHardwareRestore $Plan }
        if ($name -eq 'ONEXTweaks-ControllerInputRecovery') { Assert-ONEXTweaksReleasedController $Plan $Lease $folder }
        if (!(Get-ONEXTweaksRemainingTask $Plan $name)) { continue }
        $task = Get-ONEXTweaksComOwnedTask $Plan $folder $name
        if ($task) { Stop-ONEXTweaksReleaseTask $task $name }
    }
    Assert-ONEXTweaksReleasedController $Plan $Lease
    Stop-ONEXTweaksOwnedBridgeProcesses $Plan
    $oem = Invoke-ONEXTweaksOneXConsoleHandoff -Operation Restore -StateDirectory $Plan.StateDirectory -UserSid $Plan.Receipt.OwnerSid
    foreach ($name in $Plan.Tasks.Keys) {
        $task = Get-ONEXTweaksRemainingTask $Plan $name
        if ($task) {
            Unregister-ScheduledTask -InputObject $task -Confirm:$false -ErrorAction Stop
            if (Get-ONEXTweaksRemainingTask $Plan $name) { throw "Scheduled task still exists: $name" }
        }
    }
    Assert-ONEXTweaksReleasedController $Plan $Lease
    Remove-ONEXTweaksOwnedMachineState $Plan
    # Keep installer code until other binaries are gone so a locked runtime
    # file leaves the protected retry entry available.
    foreach ($row in @($Plan.Files.Values | Sort-Object @{Expression={ [int]($_.Path -like 'Tools\Installer\*') }},Path)) {
        Remove-ONEXTweaksOwnedReleaseFile $Plan $row
    }
    foreach ($relative in @('Tools\Installer','Tools','PowerAgent','WidgetBridge')) {
        $directory = Join-Path $Plan.Root $relative
        if ((Test-Path -LiteralPath $directory -PathType Container) -and !@(Get-ChildItem -LiteralPath $directory -Force).Count) {
            [IO.Directory]::Delete($directory)
        }
    }
    if ((Test-Path -LiteralPath $Plan.Root -PathType Container) -and !@(Get-ChildItem -LiteralPath $Plan.Root -Force).Count) {
        [IO.Directory]::Delete($Plan.Root)
    }
    [IO.File]::Delete($Plan.ReceiptPath)
    [pscustomobject]@{ State='Removed'; OneXConsole=$oem; OwnerSid=$Plan.Receipt.OwnerSid }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleHandoff.ps1')
$stateDirectory = Get-ONEXTweaksReleaseStateDirectory
if (Test-Path -LiteralPath (Join-Path $stateDirectory 'runtime-update-1110.json')) {
    throw 'Finish the pending runtime update with its Setup before uninstalling.'
}
$releaseRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
$plan = Get-ONEXTweaksUninstallPlan $stateDirectory $releaseRoot -OwnerOnly:(!$MachinePhase)
if (!$plan) { return [pscustomobject]@{ State='No owned release' } }
$mode = $PSCmdlet.ParameterSetName
$summary = [pscustomobject]@{ State='Plan'; OwnerSid=$plan.Receipt.OwnerSid; Package=$plan.Receipt.PackageFullName;
    RemovePackage=$plan.Receipt.PackageOwned; Tasks=@($plan.Tasks.Keys); Files=@($plan.Files.Keys); PreserveWidgetData=!$DiscardWidgetData }
if ($mode -eq 'Report' -or !$PSCmdlet.ShouldProcess($plan.Receipt.PackageFullName,"Uninstall phase $mode")) { return $summary }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $admin = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $sid = $identity.User.Value
} finally { $identity.Dispose() }
if ((Get-ONEXTweaksInteractiveSid) -cne $plan.Receipt.OwnerSid) { throw 'The installation owner must be signed in to this desktop.' }
$lockPath = Join-Path $stateDirectory 'release-transaction.lock'
if ($mode -ne 'Machine') {
    if ($sid -cne $plan.Receipt.OwnerSid) { throw 'Run the owner phase as the user who installed the widget.' }
    if ($mode -eq 'Both' -and !$admin) {
        $machineEntry = Get-ONEXTweaksVerifiedTool $plan 'Tools\Installer\Uninstall-ONEXTweaksRelease.ps1'
        foreach ($relative in @('Tools\Installer\OneXConsoleSetup.ps1','Tools\Installer\OneXConsoleHandoff.ps1')) { $null = Get-ONEXTweaksVerifiedTool $plan $relative }
    }
    # The installer creates this file before committing its ownership receipt.
    # A standard user can take the same exclusive lock with read-only access.
    Assert-ONEXTweaksProtectedItem $lockPath
    $ownerLock = [IO.File]::Open($lockPath, 'Open', 'Read', 'None')
    try {
        $canSuspend = $mode -eq 'Both' -and $admin
        $fresh = Get-ONEXTweaksUninstallPlan $stateDirectory $releaseRoot -OwnerOnly:(!$canSuspend)
        if (!$fresh -or $fresh.Digest -cne $plan.Digest) { throw 'Release receipt changed; repeat uninstall inspection.' }
        $ownerLocal = [Environment]::GetFolderPath('LocalApplicationData')
        if ($canSuspend) {
            Suspend-ONEXTweaksOwnedController $fresh (Join-Path $ownerLocal 'ONEXTweaks\dev\privateReports\controller-capture-lease.json')
        }
        $backup = Invoke-ONEXTweaksOwnerRemoval $fresh $ownerLocal $DiscardWidgetData.IsPresent
    } finally { $ownerLock.Dispose() }
    [pscustomobject]@{ State='Owner phase completed'; Backup=$backup; OwnerSid=$sid }
    if ($mode -eq 'Owner') { return }
    if (!$admin) {
        $powershell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
        $process = Start-Process -FilePath $powershell -ArgumentList ('-NoLogo -NoProfile -File "'+$machineEntry+'" -MachinePhase') -Verb RunAs -Wait -PassThru -ErrorAction Stop
        try { if ($process.ExitCode -ne 0) { throw "Machine uninstall exited with code $($process.ExitCode). The protected receipt is retained for retry." } }
        finally { $process.Dispose() }
        if (Test-Path -LiteralPath $plan.ReceiptPath -ErrorAction Stop) { throw 'Machine uninstall did not consume the release receipt.' }
        Remove-ItemProperty -LiteralPath 'HKCU:\Software\ONEXTweaks\Installation' -Name OwnerRemovalReceipt -ErrorAction Stop
        return [pscustomobject]@{ State='Removed'; OwnerSid=$sid }
    }
}
if (!$admin -or ![Environment]::Is64BitProcess) { throw 'Machine cleanup requires elevated 64-bit Windows PowerShell.' }
if (Test-Path -LiteralPath $lockPath -ErrorAction Stop) { Assert-ONEXTweaksProtectedItem $lockPath }
$lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $fresh = Get-ONEXTweaksUninstallPlan $stateDirectory $releaseRoot
    if (!$fresh -or $fresh.Digest -cne $plan.Digest) { throw 'Release receipt changed; repeat uninstall inspection.' }
    $profile = Get-ItemProperty -LiteralPath ("HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\"+$plan.Receipt.OwnerSid) -Name ProfileImagePath -ErrorAction Stop
    $local = Join-Path ([Environment]::ExpandEnvironmentVariables($profile.ProfileImagePath)) 'AppData\Local'
    $userFolders = Get-ItemProperty -LiteralPath ("Registry::HKEY_USERS\"+$plan.Receipt.OwnerSid+'\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders') -Name 'Local AppData' -ErrorAction Stop
    if ($local -ine $userFolders.'Local AppData') { throw 'Target profile uses a redirected Local AppData; verify capture recovery before cleanup.' }
    $lease = Join-Path $local 'ONEXTweaks\dev\privateReports\controller-capture-lease.json'
    Invoke-ONEXTweaksMachineRemoval $fresh $lease
    if ($mode -eq 'Both') { Remove-ItemProperty -LiteralPath 'HKCU:\Software\ONEXTweaks\Installation' -Name OwnerRemovalReceipt -ErrorAction Stop }
} finally { $lock.Dispose() }
