function Assert-ONEXTweaksLegacyRuntimeItem([string]$Path, [switch]$File) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint -or
        ($File -and $item -isnot [IO.FileInfo]) -or (!$File -and $item -isnot [IO.DirectoryInfo])) {
        throw "Unexpected legacy runtime item: $Path"
    }
    $acl = Get-Acl -LiteralPath $Path
    $administrators = @('S-1-5-18','S-1-5-32-544')
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $administrators) {
        throw "Untrusted legacy runtime owner: $Path"
    }
    $installerSid = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
    $write = [Security.AccessControl.FileSystemRights]'Write,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow' -or !($rule.FileSystemRights -band $write)) { continue }
        if ($rule.IdentityReference.Value -in $administrators -or
            ($rule.IsInherited -and $rule.IdentityReference.Value -ceq $installerSid)) { continue }
        throw "Untrusted legacy runtime write grant: $Path"
    }
}

function Get-ONEXTweaksLegacyTask($Folder, [string]$Name) {
    try { $Folder.GetTask($Name) }
    catch {
        if ($_.Exception.GetBaseException().HResult -ne -2147024894) { throw }
    }
}

function Test-ONEXTweaksUpgradeReportPair($Bridge, $Widget, [DateTimeOffset]$NotBefore) {
    if (!$Bridge -or !$Widget -or !$Bridge.PSObject.Properties['version'] -or $Bridge.version -cne '0.29.1110') { return $false }
    foreach ($name in @('connected','processId','updatedUtc')) {
        if (!$Bridge.PSObject.Properties[$name]) { return $false }
    }
    foreach ($name in @('ready','pid')) {
        if (!$Widget.PSObject.Properties[$name]) { return $false }
    }
    [long]$bridgePid = 0
    if ($Bridge.connected -isnot [bool] -or !$Bridge.connected -or $Widget.ready -isnot [bool] -or !$Widget.ready -or
        ![long]::TryParse([string]$Bridge.processId,[ref]$bridgePid) -or $bridgePid -lt 1 -or $Widget.pid -ne $bridgePid) { return $false }
    $stamp = $Bridge.updatedUtc
    if ($stamp -is [datetime] -or $stamp -is [DateTimeOffset]) { return ([DateTimeOffset]$stamp) -ge $NotBefore }
    $observed = [DateTimeOffset]::MinValue
    [DateTimeOffset]::TryParse([string]$stamp, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$observed) -and $observed -ge $NotBefore
}

function Get-ONEXTweaksTaskSid([string]$Account) {
    if ($Account -match '^S-\d-') { return $Account }
    ([Security.Principal.NTAccount]::new($Account)).Translate([Security.Principal.SecurityIdentifier]).Value
}

function Get-ONEXTweaksLegacyTaskXml([string]$Xml, [bool]$Compare, [bool]$IgnoreUri, [string]$CloneName) {
    $definition = [xml]$Xml
    $ns = [Xml.XmlNamespaceManager]::new($definition.NameTable)
    $ns.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
    $acl = $definition.SelectSingleNode('/t:Task/t:RegistrationInfo/t:SecurityDescriptor',$ns)
    if ($acl) { [void]$acl.ParentNode.RemoveChild($acl) }
    if ($Compare) {
        $enabled = $definition.SelectSingleNode('/t:Task/t:Settings/t:Enabled',$ns)
        if ($enabled) { [void]$enabled.ParentNode.RemoveChild($enabled) }
        foreach ($account in $definition.SelectNodes('/t:Task/t:Triggers/t:LogonTrigger/t:UserId',$ns)) {
            $account.InnerText = Get-ONEXTweaksTaskSid $account.InnerText
        }
    }
    $uri = $definition.SelectSingleNode('/t:Task/t:RegistrationInfo/t:URI',$ns)
    if ($uri) {
        if ($IgnoreUri) { [void]$uri.ParentNode.RemoveChild($uri) }
        elseif ($CloneName) { $uri.InnerText = '\' + $CloneName }
    }
    $definition.DocumentElement.OuterXml
}

function Test-ONEXTweaksLegacyTaskSnapshot($Task, $Saved, [bool]$IgnoreUri) {
    if (!$Task) { return $false }
    $actualAcl = $Task.GetSecurityDescriptor(7)
    if ($actualAcl -cne $Saved.Acl) {
        if ($Saved.Name -cne 'ONEXTweaks-Startup') { return $false }
        # Setting this leaf task's original ACL can clear the scheduler's
        # auto-inherited marker. Keep owner, group, protection and every ACE.
        $left = [Security.AccessControl.RawSecurityDescriptor]::new($actualAcl)
        $right = [Security.AccessControl.RawSecurityDescriptor]::new($Saved.Acl)
        $ignore = -bnot [int][Security.AccessControl.ControlFlags]::DiscretionaryAclAutoInherited
        foreach ($descriptor in @($left,$right)) {
            $descriptor.SetFlags([Security.AccessControl.ControlFlags]([int]$descriptor.ControlFlags -band $ignore))
        }
        $sections = [Security.AccessControl.AccessControlSections]::All
        if ($left.GetSddlForm($sections) -cne $right.GetSddlForm($sections)) { return $false }
    }
    ((Get-ONEXTweaksLegacyTaskXml $Task.Xml $true $IgnoreUri) -ceq
        (Get-ONEXTweaksLegacyTaskXml $Saved.Xml $true $IgnoreUri))
}

function Get-ONEXTweaksLegacyTaskSnapshot($Folder, [string]$Name, [string]$Owner, [string]$Local, [string]$Root) {
    $task = Get-ONEXTweaksLegacyTask $Folder $Name
    if (!$task) { return }
    $definition = $task.Definition
    $principal = $definition.Principal
    if ($definition.Actions.Count -ne 1 -or $definition.Actions.Item(1).Type -ne 0 -or
        (Get-ONEXTweaksTaskSid $principal.UserId) -cne $Owner -or $principal.LogonType -ne 3) {
        throw "Unrecognized legacy task account or actions: $Name"
    }
    $action = $definition.Actions.Item(1)
    $path = $action.Path
    $arguments = [string]$action.Arguments
    $app = Join-Path $Local 'ONEXTweaks'
    $image = Join-Path $app 'dev\project\src\ONEXTweaks.WidgetBridge\bin\Release\net10.0-windows\ONEXTweaks.WidgetBridge.exe'
    $dev = [regex]::Escape((Join-Path $app 'dev\tools'))
    $limited = $principal.RunLevel -eq 0
    $recognized = switch ($Name) {
        'ONEXTweaks-Startup' {
            $limited -and $path -ieq (Join-Path $app 'CompanionLauncher.exe') -and $arguments -ceq '--startup'
        }
        'ONEXTweaks-WidgetBridge' {
            $limited -and ($path -ieq $image -or $path -imatch "^$dev\\background\d+\\ONEXTweaks\.BackgroundHost\.exe$") -and !$arguments
        }
        'ONEXTweaks-ControllerInputRecovery' {
            if (!$limited) { $false; break }
            if ($path -ieq $image) { $arguments -ceq '--controller-login-recovery'; break }
            if ($path -ine "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -or
                $arguments -cnotmatch '^-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand (?<script>[A-Za-z0-9+/]+={0,2})$') { $false; break }
            $body = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($Matches.script))
            $runtime = [regex]::Escape((Join-Path $app 'toolchain\dotnet-'))
            $escapedImage = [regex]::Escape($image.Replace("'","''"))
            $body -cmatch ('^\$ErrorActionPreference=''Stop'';\$env:DOTNET_ROOT='''+$runtime+
                '10\.0\.403'';\$p=Start-Process -FilePath '''+$escapedImage+
                ''' -ArgumentList ''--controller-login-recovery'' -WindowStyle Hidden -PassThru;\$null=\$p\.Handle;\$p\.WaitForExit\(\);exit \$p\.ExitCode$')
        }
        'ONEXTweaks-PowerAgent' {
            !$limited -and $path -ieq (Join-Path $Root 'PowerAgent\ONEXTweaks.PowerAgent.exe') -and $arguments -ceq 'serve'
        }
        'ONEXTweaks-RTSS' {
            !$limited -and !$arguments -and $path -in @([Environment]::GetFolderPath('ProgramFilesX86'),$env:ProgramFiles |
                Where-Object { $_ } | ForEach-Object { Join-Path $_ 'RivaTuner Statistics Server\RTSS.exe' })
        }
    }
    if (!$recognized) { throw "Unrecognized legacy task command: $Name" }
    [pscustomobject]@{Name=$Name; Xml=[string]$task.Xml; Acl=[string]$task.GetSecurityDescriptor(7);
        Enabled=[bool]$task.Enabled; WasRunning=([int]$task.State -in 2,4);
        User=$principal.UserId; Logon=[int]$principal.LogonType; Image=$path; Recipe=$null}
}

function Add-ONEXTweaksLegacyTask($Folder, [string]$Name, $Saved, $Recipe) {
    # Register disabled and ignore registration triggers while restoring rights.
    $cloneName = if ($Name -cne $Saved.Name) { $Name } else { '' }
    $xml = Get-ONEXTweaksLegacyTaskXml $Saved.Xml $false $false $cloneName
    $restored = $Folder.RegisterTask($Name,$xml,[int]$Recipe.Flags,$Saved.User,$null,$Saved.Logon,$Recipe.Sddl)
    if ($Recipe.SetSddl) { $restored.SetSecurityDescriptor($Recipe.SetSddl,0) }
    $restored
}

function Find-ONEXTweaksLegacyTaskRecipe($Folder, $Saved, [string]$Owner) {
    $clone = 'ONEXTweaks-UpgradeAcl-' + [guid]::NewGuid().ToString('N')
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($Saved.Acl)
    $access = [Security.AccessControl.AccessControlSections]::Access
    $taskAcl = 'D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;' + $Owner + ')'
    $candidates = @([pscustomobject]@{Sddl=$null;SetSddl=$null})
    $groupSddl = if ($descriptor.Group) { 'G:' + $descriptor.Group.Value }
    if ($Saved.Name -eq 'ONEXTweaks-Startup') {
        # The 1109 startup installer applied its ACL after registration.
        $candidates += [pscustomobject]@{Sddl=$null;SetSddl=$taskAcl}
        if ($groupSddl) {
            $candidates += [pscustomobject]@{Sddl=$null;SetSddl=($groupSddl + $taskAcl)}
        }
        $observedProtected = $descriptor.GetSddlForm($access) -replace '^D:(?:AR|AI)*','D:PAI'
        $candidates += [pscustomobject]@{Sddl=$null;SetSddl=$observedProtected}
    }
    if ($groupSddl) {
        # Scheduler defaults to the registering token's group, which can differ from the original task's group.
        $candidates += [pscustomobject]@{Sddl=$null;SetSddl=$groupSddl}
    }
    $explicit = @($descriptor.GetSddlForm($access), $Saved.Acl)
    if ($descriptor.DiscretionaryAcl) {
        $descriptor.SetFlags($descriptor.ControlFlags -bor [Security.AccessControl.ControlFlags]::DiscretionaryAclProtected)
        $explicit += $descriptor.GetSddlForm($access)
        $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($Saved.Acl)
        for ($i=$descriptor.DiscretionaryAcl.Count-1; $i -ge 0; $i--) {
            if ($descriptor.DiscretionaryAcl[$i].IsInherited) { $descriptor.DiscretionaryAcl.RemoveAce($i) }
        }
        $explicit += $descriptor.GetSddlForm($access)
    }
    $explicit += 'D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;' + $Owner + ')'
    $candidates += @($explicit | Select-Object -Unique | ForEach-Object { [pscustomobject]@{Sddl=$_;SetSddl=$null} })
    foreach ($candidate in $candidates) {
        foreach ($flags in @(42,58)) {
            # CREATE | DISABLE | IGNORE_REGISTRATION_TRIGGERS, optionally DONT_ADD_PRINCIPAL_ACE.
            $recipe = [pscustomobject]@{Flags=$flags; Sddl=$candidate.Sddl; SetSddl=$candidate.SetSddl}
            $matches = $false
            try {
                $task = Add-ONEXTweaksLegacyTask $Folder $clone $Saved $recipe
                $matches = !$task.Enabled -and $task.Definition.RegistrationInfo.URI -eq ('\' + $clone) -and
                    (Test-ONEXTweaksLegacyTaskSnapshot $task $Saved $true)
            } catch {
                if ($_.Exception.GetBaseException().HResult -notin @(-2147024891,-2147024809)) { throw }
            }
            finally {
                if (Get-ONEXTweaksLegacyTask $Folder $clone) { $Folder.DeleteTask($clone,0) }
            }
            if ($matches) { return $recipe }
        }
    }
    throw "No exact disabled-task restore was verified for $($Saved.Name). The original task is still registered."
}

function Assert-ONEXTweaksLegacyTasks($Folder, $SavedTasks) {
    foreach ($saved in $SavedTasks) {
        $task = Get-ONEXTweaksLegacyTask $Folder $saved.Name
        if (!(Test-ONEXTweaksLegacyTaskSnapshot $task $saved $false) -or [bool]$task.Enabled -ne $saved.Enabled) {
            throw "Legacy task changed since it was inspected: $($saved.Name)"
        }
    }
}

function Stop-ONEXTweaksLegacyTasks($Folder, $SavedTasks) {
    foreach ($saved in $SavedTasks) {
        $task = Get-ONEXTweaksLegacyTask $Folder $saved.Name
        $task.Enabled = $false
        if ($task.GetInstances(0).Count) { $task.Stop(0) }
        for ($i=0; $i -lt 40 -and $task.GetInstances(0).Count; $i++) { Start-Sleep -Milliseconds 250 }
        if ($task.GetInstances(0).Count) { throw "Legacy task did not stop: $($saved.Name)" }
    }
}

function Stop-ONEXTweaksLegacyPowerAgent($Folder, $Saved, [string]$Root, [string]$Owner, [int]$Session) {
    $image = $Saved.Image
    $helper = Join-Path $Root 'PowerAgent\OneXConsole-fix3\CompatLayerCT.exe'
    $agents = @(Get-CimInstance Win32_Process -Filter "Name='ONEXTweaks.PowerAgent.exe'" | Where-Object ExecutablePath -ieq $image)
    $servers = @($agents | Where-Object CommandLine -match '\sserve\s*$')
    $helpers = @(Get-CimInstance Win32_Process -Filter "Name='CompatLayerCT.exe'" | Where-Object ExecutablePath -ieq $helper)
    if ($servers.Count -gt 1 -or $helpers.Count -gt 1) { throw 'More than one legacy hardware server is running.' }
    foreach ($actor in @($agents + $helpers)) {
        if ($actor.SessionId -ne $Session -or (Invoke-CimMethod -InputObject $actor -MethodName GetOwnerSid).Sid -cne $Owner) {
            throw 'A legacy hardware process belongs to another Windows session or user.'
        }
    }
    $orphan = $false
    $child = $null
    if ($helpers.Count) {
        Assert-ONEXTweaksLegacyRuntimeItem (Split-Path -LiteralPath $helper)
        Assert-ONEXTweaksLegacyRuntimeItem $helper -File
        if ($helpers[0].CommandLine -notmatch '\s1013\s+ONEXTweaks\.PowerAgent\s+2\s+pioforce\s*$' -or
            (Get-FileHash -LiteralPath $helper).Hash -cne 'B82263110012916610A5FC3C1EA69E79D1AF1CA631727CFD08278098172EC845') {
            throw 'The old hardware helper did not match the known ONEXTweaks copy.'
        }
        $parent = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $helpers[0].ParentProcessId)
        $orphan = !$parent -or $parent.CreationDate -gt $helpers[0].CreationDate
        if (!$orphan -and (!$servers.Count -or $parent.ProcessId -ne $servers[0].ProcessId)) {
            throw 'The old hardware helper still belongs to another process.'
        }
        $child = Get-Process -Id $helpers[0].ProcessId -ErrorAction SilentlyContinue
        if ($child -and ($child.Path -ine $helper -or [Math]::Abs(($child.StartTime - $helpers[0].CreationDate).TotalMilliseconds) -ge 1)) {
            throw 'The old hardware helper changed before shutdown.'
        }
    }

    $task = Get-ONEXTweaksLegacyTask $Folder $Saved.Name
    $task.Enabled = $false
    if ($servers.Count) {
        $server = Get-Process -Id $servers[0].ProcessId -ErrorAction SilentlyContinue
        if ($server -and !$server.HasExited) {
            if ($server.Path -ine $image -or [Math]::Abs(($server.StartTime - $servers[0].CreationDate).TotalMilliseconds) -ge 1) {
                throw 'Legacy PowerAgent changed before shutdown.'
            }
            if (!('ONEXTweaks.LegacyPowerPipe' -as [type])) {
                Add-Type @'
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace ONEXTweaks {
    public static class LegacyPowerPipe {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    }
}
'@
            }
            $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'ONEXTweaks.Power.v1', [IO.Pipes.PipeDirection]::InOut)
            try {
                $pipe.Connect(6500)
                [uint32]$serverId = 0
                if (![ONEXTweaks.LegacyPowerPipe]::GetNamedPipeServerProcessId($pipe.SafePipeHandle,[ref]$serverId) -or
                    $serverId -ne $server.Id -or $server.HasExited) { throw 'Legacy PowerAgent no longer owns its pipe.' }
                $writer = [IO.StreamWriter]::new($pipe)
                $writer.AutoFlush = $true
                $reader = [IO.StreamReader]::new($pipe)
                $writer.WriteLine('{"action":"shutdown"}')
                $reply = $reader.ReadLineAsync()
                if (!$reply.Wait(9000) -or !$reply.Result -or !(ConvertFrom-Json $reply.Result).ok) {
                    throw 'Legacy PowerAgent did not acknowledge shutdown.'
                }
            } finally { $pipe.Dispose() }
            if (!$server.WaitForExit(8000)) { throw 'Legacy PowerAgent did not exit after shutdown.' }
        }
    }
    if ($child -and !$child.HasExited) {
        # A previous interrupted shutdown can leave this exact private helper without a parent.
        if ($orphan) { $child.Kill() }
        if (!$child.WaitForExit(8000)) { throw 'The private legacy hardware helper is still running.' }
    }
    $until = [Diagnostics.Stopwatch]::StartNew()
    do {
        $remaining = @(Get-Process -Name 'ONEXTweaks.PowerAgent','CompatLayerCT' -ErrorAction SilentlyContinue |
            Where-Object Path -in @($image,$helper))
        if (!$remaining.Count -and !$task.GetInstances(0).Count) { return }
        Start-Sleep -Milliseconds 250
    } while ($until.Elapsed.TotalSeconds -lt 10)
    throw 'Legacy PowerAgent or its recovery process still holds the old runtime.'
}

function Suspend-ONEXTweaksLegacyRuntime($Folder, $SavedTasks, [string]$Local, [string]$Root, [string]$Lease, [string]$Owner) {
    Assert-ONEXTweaksLegacyTasks $Folder $SavedTasks
    $early = @($SavedTasks | Where-Object Name -in @('ONEXTweaks-Startup','ONEXTweaks-WidgetBridge'))
    $recovery = @($SavedTasks | Where-Object Name -eq 'ONEXTweaks-ControllerInputRecovery')
    $power = $SavedTasks | Where-Object Name -eq 'ONEXTweaks-PowerAgent'
    $powerDirectory = Join-Path $Root 'PowerAgent'
    foreach ($name in @('fan-control-journal','game-lighting-lease.json')) {
        if (Test-Path -LiteralPath (Join-Path $powerDirectory $name)) {
            throw "The legacy hardware operation is still active ($name). Finish it before upgrading."
        }
    }
    Stop-ONEXTweaksLegacyTasks $Folder $early
    $previousImages = @($early | ForEach-Object Image)
    $previousImages += Join-Path $Local 'ONEXTweaks\dev\project\src\ONEXTweaks.WidgetBridge\bin\Release\net10.0-windows\ONEXTweaks.WidgetBridge.exe'
    $session = (Get-Process -Id $PID).SessionId
    $processes = @(Get-Process -Name 'ONEXTweaks.BackgroundHost','ONEXTweaks.WidgetBridge','CompanionLauncher' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $session -and $_.Path -in $previousImages })
    foreach ($process in $processes) {
        if (!$process.HasExited) {
            $process.Kill()
            if (!$process.WaitForExit(8000)) { throw "Legacy process did not stop: $($process.ProcessName)" }
        }
    }
    if (Test-Path -LiteralPath $Lease) {
        if ($recovery.Count -ne 1 -or !$recovery[0].Enabled) { throw 'The active controller capture has no verified recovery task.' }
        $task = Get-ONEXTweaksLegacyTask $Folder $recovery[0].Name
        if (!$task.GetInstances(0).Count) { $null = $task.Run($null) }
        for ($i=0; $i -lt 80 -and (Test-Path -LiteralPath $Lease); $i++) { Start-Sleep -Milliseconds 250 }
        if (Test-Path -LiteralPath $Lease) { throw 'Legacy controller capture could not be released.' }
    }
    if ($power) { Stop-ONEXTweaksLegacyPowerAgent $Folder $power $Root $Owner $session }
    Stop-ONEXTweaksLegacyTasks $Folder ($recovery + @($SavedTasks | Where-Object Name -eq 'ONEXTweaks-RTSS'))
}

function Restore-ONEXTweaksLegacyTasks($Folder, $Tasks) {
    foreach ($saved in $Tasks) {
        $task = Get-ONEXTweaksLegacyTask $Folder $saved.Name
        if (!$task) { $task = Add-ONEXTweaksLegacyTask $Folder $saved.Name $saved $saved.Recipe }
        if (!(Test-ONEXTweaksLegacyTaskSnapshot $task $saved $false)) {
            throw "Legacy task restoration did not match the saved definition and rights: $($saved.Name)"
        }
        if ([bool]$task.Enabled -ne $saved.Enabled) { $task.Enabled = $saved.Enabled }
        if (!(Test-ONEXTweaksLegacyTaskSnapshot $task $saved $false) -or [bool]$task.Enabled -ne $saved.Enabled) {
            throw "Legacy task settings did not read back after restoration: $($saved.Name)"
        }
    }
}
