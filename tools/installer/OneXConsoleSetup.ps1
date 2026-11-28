$oneXConsolePrerequisite = [pscustomobject]@{
    Name = 'OneXConsole 0.10.4-fix3'
    RequiredFor = 'ONEXPLAYER 3 hardware (PowerAgent)'
    Service = $null
    VendorPackage = 'OneXConsole-0.10.4-fix3.exe'
    Url = 'https://onexplayerstore.com/pages/drivers-and-faqs'
    Hash = '8B1C4D19EA2A0DF5F2B6636715A6595573A94836B26D62A25938D5DE5CFC0E88'
    Publisher = 'Shenzhen One-Netbook Technolog Co., Ltd.'
    Arguments = '/S'
}

function Get-ONEXTweaksHardwareCompatibility {
    $boards = @(Get-CimInstance Win32_BaseBoard)
    if ($boards.Count -ne 1 -or $boards[0].Manufacturer -cne 'ONE-NETBOOK' -or $boards[0].Product -cne 'ONEXPLAYER 3') {
        return 'ONEXPLAYER 3 not detected'
    }
    return 'Supported'
}

function Get-OneXConsolePrerequisiteState {
    if ((Get-ONEXTweaksHardwareCompatibility) -ne 'Supported') { return 'Not applicable' }
    $directory = Join-Path $env:ProgramFiles 'OneXConsole'
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { return 'Missing' }
    $hardware = Join-Path $directory 'resources\resources'
    $pins = @{
        'CompatLayerCT.exe' = @(
            '505DB9A04EB5F23D8D0DE31307ADB9D57D5DA1900908D9D995EDFC49EA5C7328',
            'B82263110012916610A5FC3C1EA69E79D1AF1CA631727CFD08278098172EC845'
        )
        'PawnIOLib.dll' = 'D71F62627D66983BB9F5B1C269F27BCD8C1B8A46E794377A6330F84C198F4443'
        'tools\pawnio\IntelMSR.bin' = 'D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F'
        'tools\pawnio\LpcIO.bin' = 'B3896A1CAB0D808FCA31FE2EBCAE045D59DAC690DA87B17C858BB8DA357EB45E'
        'tools\pawnio\LpcACPIEC.bin' = 'C38FD116E7AFF4D1FDB0A494E296BE0A6708E5A22FC72F14587442FB7F8F7906'
    }
    foreach ($file in $pins.Keys) {
        $path = Join-Path $hardware $file
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -notin $pins[$file]) {
            return 'Review existing installation'
        }
    }
    return 'Installed'
}

# Inspect task definitions in the root folder once. A failed enumeration must not
# be interpreted as an absent OEM task.
function Get-OneXConsoleGuiStartupState($Task) {
    if (!$Task) { return 'Not found' }
    $script = Join-Path $env:ProgramFiles 'OneXConsole\resources\resources\tools\OneXConsoleStartup.vbs'
    $systems = @('wscript.exe', (Join-Path $env:SystemRoot 'System32\wscript.exe'))
    if (@($Task.Actions).Count -ne 1 -or @($Task.Triggers).Count -ne 1 -or
        $Task.Triggers[0].CimClass.CimClassName -ne 'MSFT_TaskLogonTrigger' -or
        $Task.Actions[0].Execute -notin $systems -or $Task.Actions[0].Arguments -ine ('"' + $script + '"') -or
        !(Test-Path -LiteralPath $script -PathType Leaf) -or
        (Get-FileHash -LiteralPath $script).Hash -ne 'A5471B7F07DBFE737A195E7D75B1282786BFB95F9E65B3E1D94298422D907CB0') {
        return 'Review existing logon task'
    }
    if (!$Task.Settings.Enabled) { return 'Disabled' }
    return 'Enabled; reversible handoff pending'
}

function Get-ONEXTweaksAccountSid([string]$Account) {
    if (!$Account) { return }
    if ($Account -like 'S-1-*') { return $Account }
    try {
        return ([Security.Principal.NTAccount]$Account).Translate([Security.Principal.SecurityIdentifier]).Value
    } catch [Security.Principal.IdentityNotMappedException] { return }
}

function Test-ONEXTweaksUserPrincipal($Task, [string]$Sid) {
    return $Task.Principal.LogonType -eq 'Interactive' -and (Get-ONEXTweaksAccountSid $Task.Principal.UserId) -eq $Sid
}

function Get-ONEXTweaksEnrolledDesktopSid($Task) {
    if (!$Task -or @($Task.Actions).Count -ne 1 -or $Task.Principal.LogonType -ne 'ServiceAccount' -or
        (Get-ONEXTweaksAccountSid $Task.Principal.UserId) -ne 'S-1-5-18') { return }
    $agent = Join-Path $env:ProgramFiles 'ONEXTweaks\PowerAgent\ONEXTweaks.PowerAgent.exe'
    if ($Task.Actions[0].Execute -ieq $agent -and $Task.Actions[0].Arguments -cmatch '^serve-desktop (S-1-5-21-\d+-\d+-\d+-\d+)$') {
        return $Matches[1]
    }
}

function Get-ONEXTweaksBridgeReportState($Health, [int]$BridgePid, [int]$Session, [version]$BridgeVersion, [bool]$RequiresHardware, [string]$ExpectedFamily,
    [double]$AgeSeconds = 0) {
    if (!$ExpectedFamily -or $Health.widgetPackageFamily -cne $ExpectedFamily) { return 'Widget package family differs from registration' }
    if ($Health.connected -isnot [bool] -or !$Health.connected) { return 'Bridge reports no widget connection' }
    if ($Health.processId -ne $BridgePid -or $Health.sessionId -ne $Session) { return 'Bridge process or session differs from the report' }
    if (([version]$Health.version).ToString(3) -ne $BridgeVersion.ToString(3)) { return 'Bridge version differs from the installed file' }
    $hardwareUnavailable = $RequiresHardware -and ($Health.state.available -isnot [bool] -or !$Health.state.available -or
        (($Health.state.canWrite -isnot [bool] -or !$Health.state.canWrite) -and
            ($Health.homeHardware.fan.available -isnot [bool] -or !$Health.homeHardware.fan.available)))
    if ($AgeSeconds -lt 0 -or $AgeSeconds -gt 10) {
        # OEM can hold the hardware pipe long enough to age a live Bridge report.
        # This state permits only the guarded restart, never successful setup.
        if ($hardwareUnavailable -and $AgeSeconds -gt 10 -and $AgeSeconds -le 30) {
            return 'PowerAgent hardware readback temporarily stale'
        }
        return "Bridge report outside the freshness window ($([Math]::Round($AgeSeconds, 1)) seconds)"
    }
    if ($hardwareUnavailable) { return 'PowerAgent hardware readback unavailable' }
    return 'Healthy'
}

function Get-ONEXTweaksBackendState($Tasks, [string]$Sid, [int]$Session, [bool]$RequiresHardware, [string]$ExpectedFamily, [string]$LocalAppData = $env:LOCALAPPDATA) {
    $startup = $Tasks | Where-Object TaskName -eq 'ONEXTweaks-Startup'
    $agent = $Tasks | Where-Object TaskName -eq 'ONEXTweaks-PowerAgent'
    $bridge = $Tasks | Where-Object TaskName -eq 'ONEXTweaks-WidgetBridge'
    if (!$startup -or !$bridge -or ($RequiresHardware -and !$agent)) { return 'Release companion tasks missing' }
    $root = Join-Path $env:ProgramFiles 'ONEXTweaks'
    $launcher = Join-Path $root 'CompanionLauncher.exe'
    $bridgeExe = Join-Path $root 'WidgetBridge\ONEXTweaks.WidgetBridge.exe'
    if (@($startup.Actions).Count -ne 1 -or $startup.Actions[0].Execute -ine $launcher -or
        $startup.Actions[0].Arguments -cne '--startup' -or !(Test-ONEXTweaksUserPrincipal $startup $Sid) -or
        $startup.Principal.RunLevel -ne 'Limited' -or
        @($bridge.Actions).Count -ne 1 -or $bridge.Actions[0].Execute -ine $bridgeExe -or $bridge.Actions[0].Arguments -or
        !(Test-ONEXTweaksUserPrincipal $bridge $Sid) -or $bridge.Principal.RunLevel -ne 'Limited') {
        return 'Release companion task identities unconfirmed'
    }
    if ($RequiresHardware -and (Get-ONEXTweaksEnrolledDesktopSid $agent) -cne $Sid) {
        return 'Release PowerAgent task does not match the enrolled desktop owner'
    }
    if (!$bridge.Settings.Enabled -or $bridge.State -ne 'Running' -or
        ($RequiresHardware -and (!$agent.Settings.Enabled -or $agent.State -ne 'Running'))) {
        return 'Release companions are not running'
    }
    $rows = @(Get-CimInstance Win32_Process -Filter "Name='ONEXTweaks.WidgetBridge.exe'" | Where-Object {
        $_.SessionId -eq $Session -and $_.ExecutablePath -ieq $bridgeExe
    })
    $report = Join-Path $LocalAppData 'ONEXTweaks\dev\Reports\widget-bridge.json'
    try {
        $health = Get-Content -LiteralPath $report -Raw -ErrorAction Stop | ConvertFrom-Json
        # The same executable also hosts watchdogs. Match the reporter's live PID.
        $actors = @($rows | Where-Object ProcessId -eq $health.processId)
        if ($actors.Count -ne 1) { return 'Reported Bridge PID is not running in the current release directory and session' }
        if ((Invoke-CimMethod -InputObject $actors[0] -MethodName GetOwnerSid).Sid -ne $Sid) { return 'Bridge belongs to a different Windows account' }
        $stamp = $health.updatedUtc
        if ($stamp -is [string]) {
            $observed = [DateTimeOffset]::Parse($stamp, [Globalization.CultureInfo]::InvariantCulture)
        } else {
            $observed = [DateTimeOffset]$stamp
        }
        $age = ([DateTimeOffset]::UtcNow - $observed).TotalSeconds
        if (([DateTimeOffset]$actors[0].CreationDate) -gt $observed) { return 'Bridge process started after the report' }
        $assembly = Get-Item -LiteralPath ([IO.Path]::ChangeExtension($bridgeExe, '.dll'))
        $version = [version]$assembly.VersionInfo.FileVersion
        return Get-ONEXTweaksBridgeReportState $health $actors[0].ProcessId $Session $version $RequiresHardware $ExpectedFamily $age
    } catch { return ('Release Bridge readback unavailable: ' + $_.Exception.Message) }
}

function Get-ONEXTweaksRtssTaskState($Task, [string]$Sid) {
    if (!$Task) { return 'Missing' }
    # Match the first RTSS.exe Bridge selects, including a partial x86 install.
    foreach ($folder in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (!$folder) { continue }
        $directory = Join-Path $folder 'RivaTuner Statistics Server'
        $exe = Join-Path $directory 'RTSS.exe'
        if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
        if ((Test-Path -LiteralPath (Join-Path $directory 'RTSSHooks64.dll') -PathType Leaf) -and
            $Task.Settings.Enabled -and @($Task.Actions).Count -eq 1 -and $Task.Actions[0].Execute -ieq $exe -and
            !$Task.Actions[0].Arguments -and $Task.Actions[0].WorkingDirectory -ieq $directory -and
            $Task.Principal.RunLevel -eq 'Highest' -and (Test-ONEXTweaksUserPrincipal $Task $Sid)) { return 'Ready' }
        return 'Review existing RTSS task or installation'
    }
    return 'RTSS executable missing'
}

function Get-ONEXTweaksRuntimeStatus([string]$OemStatus, [string]$ExpectedFamily, [string]$Sid,
    [int]$Session = (Get-Process -Id $PID).SessionId, [string]$LocalAppData = $env:LOCALAPPDATA) {
    $result = [pscustomobject]@{ Backend = 'Unverified'; RtssTask = 'Unverified'; OneXConsoleStartup = 'Waiting for verified OEM components' }
    if ($OemStatus -eq 'Not applicable') { $result.OneXConsoleStartup = 'Not applicable' }
    try {
        $tasks = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop)
        if (!$Sid) { $Sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
        if ($OemStatus -eq 'Installed') {
            $result.OneXConsoleStartup = Get-OneXConsoleGuiStartupState ($tasks | Where-Object TaskName -eq 'OneXConsole')
        }
        $result.RtssTask = Get-ONEXTweaksRtssTaskState ($tasks | Where-Object TaskName -eq 'ONEXTweaks-RTSS') $Sid
        $requiresHardware = $OemStatus -ne 'Not applicable'
        $result.Backend = Get-ONEXTweaksBackendState $tasks $Sid $Session $requiresHardware $ExpectedFamily $LocalAppData
    } catch {
        Write-Verbose ("Runtime verification failed: " + $_.Exception.Message)
    }
    return $result
}
