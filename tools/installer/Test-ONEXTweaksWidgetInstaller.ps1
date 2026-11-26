#Requires -Version 5.1
param([string]$RealWidgetPackage, [string]$RealWidgetSha256)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$installer = Join-Path $PSScriptRoot 'Install-ONEXTweaksWidget.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('onextweaks-installer-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temp

function New-PackageFixture([string]$FileName, [string]$Manifest, [switch]$Unsigned) {
    $path = Join-Path $temp $FileName
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $contents = @{ 'AppxManifest.xml' = $Manifest; 'AppxBlockMap.xml' = '<fixture/>' }
        if (!$Unsigned) { $contents.Add('AppxSignature.p7x', 'Fixture bytes only') }
        foreach ($name in $contents.Keys) {
            $writer = New-Object IO.StreamWriter($archive.CreateEntry($name).Open())
            try { $writer.Write($contents[$name]) } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }
    return $path
}

function Assert-Rejected([scriptblock]$Action, [string]$ExpectedError) {
    try {
        $null = & $Action
    } catch {
        if ($_.Exception.Message -like "*$ExpectedError*") { return }
        throw
    }
    throw "Expected rejection: $ExpectedError"
}

function Test-WindowsWorkflow {
    $state = [pscustomobject]@{
        FixturePath = (Get-Item -LiteralPath $widget).FullName
        Version = $null
        GameBar = $true
        GameBarWidgetHost = $true
        GameBarActivation = $true
        ProcessorArchitecture = 9
        Frameworks = $false
        Deployments = 0
        ClosedTarget = $false
        BootTime = [datetime]::UtcNow.AddHours(-1)
        Offline = @()
        HidHideInstalled = $true
        HidHideFilePresent = $true
        HidHideServiceError = 0
        HidHideByInf = $false
        RtssInstalled = $true
        RtssInstalls = 0
        OemBoard = $false
        BiosVersion = '5.01'
        Downloads = 0
        DriverInstalls = 0
        BadDriverHash = $false
        WrongDriverPublisher = $false
        DriverExit = 0
    }

    function Get-ScheduledTask($TaskPath, $ErrorAction) { return @() }

    function Get-Process([int]$Id) {
        if ($Id -ne $PID) { throw 'Unexpected process query.' }
        [pscustomobject]@{ SessionId = 2 }
    }

    # These functions intercept AppX calls in this scope. Fixtures have no valid signature.
    function Get-AppxPackage([string]$Name, [string]$PackageTypeFilter) {
        if ($PackageTypeFilter -eq 'Framework' -and $state.Frameworks) {
            return [pscustomobject]@{
                Name = 'Microsoft.Fixture'; Publisher = 'CN=Test Framework'; Version = '1.2.0.0'; Architecture = 'X64'; Status = 'Ok'
            }
        }
        if ($Name -eq 'Microsoft.XboxGamingOverlay' -and $state.GameBar) {
            return [pscustomobject]@{ PackageFullName = 'GameBar_fixture'; Version = '7.0.0.0'; Status = 'Ok' }
        }
        if ($Name -eq 'ONEXTweaks.Widget' -and $state.Version) {
            return [pscustomobject]@{
                Name = $Name; Publisher = 'CN=Fixture'; Version = $state.Version; Status = 'Ok'
                Architecture = 'X64'; PackageFamilyName = 'ONEXTweaks.Widget_fixture'
            }
        }
    }

    function Get-AppxPackageManifest([string]$Package, $ErrorAction) {
        if ($Package -cne 'GameBar_fixture') { throw 'Unexpected Game Bar manifest query.' }
        $hostExtension = if ($state.GameBarWidgetHost) { '<uap3:AppExtensionHost><uap3:Name>microsoft.gameBarUIExtension</uap3:Name></uap3:AppExtensionHost>' } else { '' }
        $protocol = if ($state.GameBarActivation) { '<uap:Protocol Name="ms-gamebar"/>' } else { '' }
        [xml]("<Package xmlns:uap='http://schemas.microsoft.com/appx/manifest/uap/windows10' xmlns:uap3='http://schemas.microsoft.com/appx/manifest/uap/windows10/3'><Applications><Application><Extensions>$hostExtension$protocol</Extensions></Application></Applications></Package>")
    }

    function Add-AppxPackage([string]$Path, [string[]]$DependencyPath, [switch]$ForceTargetApplicationShutdown) {
        if ($Path -ne $state.FixturePath) { throw 'Unexpected fixture package path.' }
        $state.Deployments++
        $state.Version = '0.29.1109.0'
        $state.Frameworks = $true
        $state.Offline = @($DependencyPath | Where-Object { $_ })
        $state.ClosedTarget = $ForceTargetApplicationShutdown.IsPresent
    }

    function Get-CimInstance([string]$ClassName, [string]$Filter) {
        if ($ClassName -eq 'Win32_Process' -and $Filter -like "Name='explorer.exe' AND SessionId=*") {
            return [pscustomobject]@{ ProcessId = 1111 }
        }
        if ($ClassName -eq 'Win32_OperatingSystem') {
            return [pscustomobject]@{ ProductType = 1; Version = '10.0.26200.0'; LastBootUpTime = $state.BootTime }
        }
        if ($ClassName -eq 'Win32_Processor') {
            return [pscustomobject]@{ Architecture = $state.ProcessorArchitecture }
        }
        if ($ClassName -eq 'Win32_SystemDriver' -and ($Filter -ne "Name='HidHide'" -or $state.HidHideInstalled)) {
            $file = if ($Filter -eq "Name='HidHide'") { 'hidhide-fixture.sys' } else { 'other-fixture.sys' }
            $code = if ($Filter -eq "Name='HidHide'") { $state.HidHideServiceError } else { 0 }
            return [pscustomobject]@{ StartMode = 'Manual'; ExitCode = $code; PathName = (Join-Path $temp $file) }
        }
        if ($ClassName -eq 'Win32_BaseBoard' -and $state.OemBoard) {
            return [pscustomobject]@{ Manufacturer = 'ONE-NETBOOK'; Product = 'ONEXPLAYER 3'; Version = '1002-B' }
        }
        if ($ClassName -eq 'Win32_BIOS' -and $state.OemBoard) {
            return [pscustomobject]@{ SMBIOSBIOSVersion = $state.BiosVersion }
        }
    }

    function Invoke-CimMethod($InputObject,$MethodName) {
        if ($InputObject.ProcessId -ne 1111 -or $MethodName -ne 'GetOwnerSid') { throw 'Unexpected owner query.' }
        [pscustomobject]@{ ReturnValue = 0; Sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
    }

    function Test-Path([string]$LiteralPath, $PathType = 'Any') {
        if ($LiteralPath -match 'RivaTuner Statistics Server|Unwinder\\RTSS') { return $state.RtssInstalled }
        if ($LiteralPath -eq (Join-Path $env:ProgramFiles 'OneXConsole')) { return $false }
        if ($LiteralPath -like '*OneXConsoleStartup.vbs') { return $true }
        if ($LiteralPath -like '*hidhide-fixture.sys') { return $state.HidHideFilePresent }
        if ($LiteralPath -like '*other-fixture.sys') { return $true }
        Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType
    }

    function Get-ItemProperty([string]$Path, [string]$LiteralPath) {
        [pscustomobject]@{ DisplayName = 'PawnIO'; DisplayVersion = '2.2.0' }
        [pscustomobject]@{ DisplayName = 'USBip 0.9.8.1'; DisplayVersion = '0.9.8.1' }
        if ($state.HidHideInstalled -and !$state.HidHideByInf) {
            [pscustomobject]@{ DisplayName = 'HidHide'; DisplayVersion = '1.5.230' }
        }
    }

    function Get-Item([string]$LiteralPath) {
        if ($LiteralPath -like '*hidhide-fixture.sys') {
            return [pscustomobject]@{ VersionInfo = [pscustomobject]@{
                FileMajorPart = 1; FileMinorPart = 4; FileBuildPart = 181; FilePrivatePart = 0
            } }
        }
        Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath
    }

    function Invoke-WebRequest($Uri, [switch]$UseBasicParsing, $OutFile, $MaximumRedirection, $TimeoutSec) {
        $state.Downloads++
        if ($Uri -like 'https://ftp.nluug.nl/*Build28314.zip') {
            $zip = [IO.Compression.ZipFile]::Open($OutFile, [IO.Compression.ZipArchiveMode]::Create)
            try {
                $writer = New-Object IO.StreamWriter($zip.CreateEntry('RTSSSetup737.exe').Open())
                try { $writer.Write('Fixture only') } finally { $writer.Dispose() }
            } finally { $zip.Dispose() }
            return
        }
        if ($Uri -notlike 'https://github.com/nefarius/HidHide/*') { throw 'Unexpected dependency download.' }
        Set-Content -LiteralPath $OutFile -Value 'Fixture'
    }

    function Get-FileHash($LiteralPath, $Algorithm) {
        $file = [IO.Path]::GetFileName($LiteralPath)
        if ($file -eq 'hidhide-fixture.sys') {
            return [pscustomobject]@{ Hash = $(if ($state.BadDriverHash) { '0' * 64 }
                else { 'E3BB8B6A4E4FF90290B34E710EDBEF88CF71CEC6BA0CADCB8E1685D5DD65DDB2' }) }
        }
        if ($file -eq 'HidHide_1.5.230_x64.exe' -and !$state.BadDriverHash) {
            return [pscustomobject]@{ Hash = 'F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6' }
        }
        if ($file -eq 'RTSSSetup737.exe') {
            return [pscustomobject]@{ Hash = '04F4704CED86A68B650BF87156C02E69160E6C049B576661250250BEEABAA5EF' }
        }
        if ($file -like '*Build28314.zip' -and !$state.BadDriverHash) {
            return [pscustomobject]@{ Hash = '9B084A8CB3E53EC1A673894D0B66E22B16C9FD8785636B020B2D422F3F2A820E' }
        }
        if ($file -eq 'OneXConsoleStartup.vbs') {
            return [pscustomobject]@{ Hash = 'A5471B7F07DBFE737A195E7D75B1282786BFB95F9E65B3E1D94298422D907CB0' }
        }
        Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
    }

    function Get-AuthenticodeSignature($LiteralPath) {
        $certificate = [pscustomobject]@{}
        $publisher = 'Nefarius Software Solutions e.U.'
        if ($LiteralPath -like '*RTSSSetup737.exe') { $publisher = 'MICRO-STAR INTERNATIONAL CO., LTD.' }
        if ($state.WrongDriverPublisher) { $publisher = 'Another publisher' }
        Add-Member -InputObject $certificate -MemberType NoteProperty -Name Publisher -Value $publisher
        Add-Member -InputObject $certificate -MemberType ScriptMethod -Name GetNameInfo -Value { $this.Publisher }
        [pscustomobject]@{ Status = 'Valid'; SignerCertificate = $certificate }
    }

    function Start-Process($FilePath, $ArgumentList, $Verb, [switch]$Wait, [switch]$PassThru, $WorkingDirectory) {
        if (!$state.Deployments -or $Verb -ne 'RunAs') { throw 'Prerequisite setup must be elevated after widget registration.' }
        if ($FilePath -like '*RTSSSetup737.exe' -and $ArgumentList -eq '/S') {
            $state.RtssInstalls++
            $state.RtssInstalled = $true
        } elseif ($FilePath -like '*HidHide_1.5.230_x64.exe' -and $ArgumentList -match '/norestart') {
            $state.DriverInstalls++
            if ($state.DriverExit -eq 0) { $state.HidHideInstalled = $true }
        } else {
            throw 'Unexpected elevated action.'
        }
        $fakeProcess = [pscustomobject]@{ ExitCode = $state.DriverExit }
        Add-Member -InputObject $fakeProcess -MemberType ScriptMethod -Name Dispose -Value {}
        return $fakeProcess
    }

    $argsForWidget = @{ WidgetPackage = $widget; WidgetSha256 = $hash }
    Assert-Rejected { & $installer @argsForWidget -Install -TargetUserSid 'S-1-5-21-12345-11111-22222-1111' } 'target SID differs'
    $state.ProcessorArchitecture = 12
    Assert-Rejected { & $installer @argsForWidget } 'x64 Windows 11'
    $state.ProcessorArchitecture = 9
    $missing = & $installer @argsForWidget
    if ($missing.Operation -ne 'First registration' -or $missing.Frameworks[0].Source -ne 'Missing') {
        throw 'Missing dependency did not block a first registration.'
    }
    Assert-Rejected { & $installer @argsForWidget -Install } 'Missing x64/neutral framework'
    $state.GameBar = $false
    Assert-Rejected { & $installer @argsForWidget -FrameworkPackage $valid -Install } 'Install or update Xbox Game Bar'
    $state.GameBar = $true
    $state.GameBarWidgetHost = $false
    Assert-Rejected { & $installer @argsForWidget -FrameworkPackage $valid -Install } 'enable widgets'
    $state.GameBarWidgetHost = $true
    $state.GameBarActivation = $false
    Assert-Rejected { & $installer @argsForWidget -FrameworkPackage $valid -Install } 'enable widgets'
    $state.GameBarActivation = $true
    $dryRun = & $installer @argsForWidget -FrameworkPackage $valid -Install -WhatIf
    if ($dryRun.Blockers.Count -or $state.Deployments -ne 0 -or $dryRun.Frameworks[0].Path -ne $valid) {
        throw 'First registration preview does not match the supplied framework.'
    }
    $first = & $installer @argsForWidget -FrameworkPackage $valid -Install -CloseActiveWidget
    if ($first.Mode -ne 'Widget registered' -or !$first.WidgetRegistered -or $first.SetupComplete -or $state.Deployments -ne 1 -or $state.Offline.Count -ne 1 -or $state.Offline[0] -ne $valid -or $state.ClosedTarget) {
        throw 'First registration did not pass the offline dependency to AppX.'
    }
    if ($dryRun.DependencyLinks.Count -ne 7 -or $first.DependencyLinks.Count -ne 7) {
        throw 'Windows preflight and install result must both expose the dependency links.'
    }
    $repeat = & $installer @argsForWidget -Install
    if ($repeat.Operation -ne 'Already registered' -or !$repeat.WidgetRegistered -or $repeat.SetupComplete -or
        $repeat.Backend -ne 'Release companion tasks missing' -or $repeat.RtssTask -ne 'Missing' -or $state.Deployments -ne 1) {
        throw 'Repeating installation triggered a deployment.'
    }
    $state.Version = '0.29.1110.0'
    Assert-Rejected { & $installer @argsForWidget -Install } 'already has a newer widget'
    $state.Version = '0.29.1108.0'
    $update = & $installer @argsForWidget -Install -WhatIf
    if ($update.Operation -ne 'Update' -or $state.Deployments -ne 1) { throw 'Update preview triggered a deployment.' }
    $updated = & $installer @argsForWidget -Install
    if ($updated.Version -ne '0.29.1109.0' -or $state.Deployments -ne 2 -or $state.Offline.Count -or $state.ClosedTarget) {
        throw 'Update did not reuse installed frameworks.'
    }
    $state.Version = '0.29.1108.0'
    $activeUpdate = & $installer @argsForWidget -Install -CloseActiveWidget
    if ($activeUpdate.Operation -ne 'Update' -or !$state.ClosedTarget -or $state.Deployments -ne 3) {
        throw 'The opted-in update did not close only the target package.'
    }
    $state.HidHideFilePresent = $false
    $noDriverFile = & $installer @argsForWidget
    if (($noDriverFile.Drivers | Where-Object Service -eq 'HidHide').Status -ne 'Driver file missing from the registered service path') {
        throw 'An uninstall entry and driver service without a driver file counted as installed.'
    }
    $state.HidHideFilePresent = $true
    $state.HidHideServiceError = 577
    $driverFailure = & $installer @argsForWidget
    if (($driverFailure.Drivers | Where-Object Service -eq 'HidHide').Status -ne 'Driver service reported error 577') {
        throw 'A Windows driver load error counted as installed.'
    }
    $state.HidHideServiceError = 1077
    $demandStart = & $installer @argsForWidget
    if (($demandStart.Drivers | Where-Object Service -eq 'HidHide').Status -ne 'Installed') {
        throw 'An installed demand-start driver was reported as broken before first start.'
    }
    $state.HidHideServiceError = 0
    $state.HidHideByInf = $true
    $scopedDriver = & $installer @argsForWidget
    if (($scopedDriver.Drivers | Where-Object Service -eq 'HidHide').Status -ne 'Installed') {
        throw 'The signed HidHide 1.5.230 driver installed through its INF was not recognized.'
    }
    $state.BadDriverHash = $true
    $differentDriver = & $installer @argsForWidget
    if (($differentDriver.Drivers | Where-Object Service -eq 'HidHide').Status -ne 'Review existing driver') {
        throw 'A different HidHide binary of the same driver version counted as installed.'
    }
    $state.HidHideByInf = $false
    $state.BadDriverHash = $false
    $state.HidHideInstalled = $false
    $driverPlan = & $installer @argsForWidget -Install -WhatIf
    if (($driverPlan.Drivers | Where-Object Service -eq 'HidHide').Status -ne 'Missing' -or $state.Downloads) {
        throw 'Driver preview did not report HidHide without downloading it.'
    }
    $state.BadDriverHash = $true
    Assert-Rejected { & $installer @argsForWidget -Install } 'pinned SHA-256'
    if ($state.DriverInstalls) { throw 'A modified driver setup was executed.' }
    $state.BadDriverHash = $false
    $state.WrongDriverPublisher = $true
    Assert-Rejected { & $installer @argsForWidget -Install } 'expected publisher'
    if ($state.DriverInstalls) { throw 'A driver setup with the wrong publisher was executed.' }
    $state.WrongDriverPublisher = $false

    $state.DriverExit = 3010
    $pending = & $installer @argsForWidget -Install
    if (!$pending.RestartRequired -or $pending.SetupComplete) { throw 'A pending restart was recorded as completed.' }
    $state.DriverExit = 0
    $sameBoot = & $installer @argsForWidget -Install
    if (!$sameBoot.RestartRequired -or $sameBoot.RestartComponent -ne 'HidHide 1.5.230' -or $state.DriverInstalls -ne 1) {
        throw 'The installer resumed before the requested Windows restart.'
    }
    $state.BootTime = $state.BootTime.AddMinutes(1)
    $repaired = & $installer @argsForWidget -Install
    if (!$repaired.PrerequisitesReady -or $repaired.SetupComplete -or $state.DriverInstalls -ne 2 -or $state.Deployments -ne 3) {
        throw 'Repeating setup did not finish the missing driver without redeploying the widget.'
    }
    $null = & $installer @argsForWidget -Install
    if ($state.DriverInstalls -ne 2) { throw 'An installed driver ran again.' }

    $state.RtssInstalled = $false
    $state.BadDriverHash = $true
    Assert-Rejected { & $installer @argsForWidget -Install } 'archive does not match'
    if ($state.RtssInstalls) { throw 'RTSS setup ran from a wrong archive.' }
    $state.BadDriverHash = $false
    $rtssInstalled = & $installer @argsForWidget -Install
    if (!$rtssInstalled.PrerequisitesReady -or $rtssInstalled.SetupComplete -or $state.RtssInstalls -ne 1 -or $state.Deployments -ne 3) {
        throw 'RTSS setup did not install only the missing dependency.'
    }
    $state.OemBoard = $true
    $state.BiosVersion = '5.02'
    $wrongBios = & $installer @argsForWidget
    if ($wrongBios.HardwareCompatibility -notlike '*detected: 5.02*' -or
        ($wrongBios.Drivers | Where-Object Service -eq 'PawnIO').Status -ne 'Not applicable' -or
        ($wrongBios.Applications | Where-Object Name -like 'OneXConsole*').Status -ne 'Not applicable') {
        throw 'An unsupported BIOS was admitted for PowerAgent hardware dependencies.'
    }
    $state.BiosVersion = '5.01'
    $oemPlan = & $installer @argsForWidget
    if (($oemPlan.Applications | Where-Object Name -like 'OneXConsole*').Status -ne 'Compatible OEM installer missing from bundle') {
        throw 'A clean ONEXPLAYER must report the missing OEM setup.'
    }
    $oemFixtureDirectory = Join-Path (Split-Path -LiteralPath $widget) 'Dependencies/OEM'
    $null = New-Item -ItemType Directory -Path $oemFixtureDirectory -Force
    Set-Content -LiteralPath (Join-Path $oemFixtureDirectory 'OneXConsole-0.10.4-fix3.exe') -Value 'Unverified OEM installer'
    Assert-Rejected { & $installer @argsForWidget -Install } 'pinned SHA-256'

    'PASS: Windows workflow with simulated AppX deployment (first registration, WhatIf, retry, blockers, update)'
    'PASS: automatic driver setup with simulated downloads, signature check, pending restart and retry'
    'PASS: RTSS archive validation and simulated setup'
}

function Test-RuntimeReadback {
    . (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
    $variables = @('ProgramFiles','ProgramFiles(x86)','SystemRoot','LOCALAPPDATA')
    $original = @{}
    $sid = 'S-1-5-21-11111-22222-33333-1001'
    $bridgePid = 3512
    $session = 2
    $fixtureObserved = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $fixtureReport = [pscustomobject]@{
        connected = $true; processId = $bridgePid; sessionId = $session; updatedUtc = $fixtureObserved.ToString('o'); version = '0.29.1099'; widgetPackageFamily = 'ONEXTweaks.Widget_fixture'
        state = [pscustomobject]@{ available = $true }; homeHardware = [pscustomobject]@{ fan = [pscustomobject]@{ available = $true } }
    }
    try {
        foreach ($name in $variables) {
            if (![Environment]::GetEnvironmentVariable($name)) {
                $original[$name] = $null
                [Environment]::SetEnvironmentVariable($name, (Join-Path $temp $name))
            }
        }
        $oemScript = Join-Path $env:ProgramFiles 'OneXConsole\resources\resources\tools\OneXConsoleStartup.vbs'
        $rtssFolder = Join-Path $env:ProgramFiles 'RivaTuner Statistics Server'
        $rtssExe = Join-Path $rtssFolder 'RTSS.exe'
        $fixtureLauncher = Join-Path $env:ProgramFiles 'ONEXTweaks\CompanionLauncher.exe'
        $fixtureAgent = Join-Path $env:ProgramFiles 'ONEXTweaks\PowerAgent\ONEXTweaks.PowerAgent.exe'
        $fixtureBridge = Join-Path $env:ProgramFiles 'ONEXTweaks\WidgetBridge\ONEXTweaks.WidgetBridge.exe'
        function Test-Path($LiteralPath, $PathType) {
            return $LiteralPath -in @($oemScript,$rtssExe,(Join-Path $rtssFolder 'RTSSHooks64.dll'))
        }
        function Get-FileHash($LiteralPath) {
            if ($LiteralPath -ne $oemScript) { throw 'Unexpected hash request.' }
            [pscustomobject]@{ Hash = 'A5471B7F07DBFE737A195E7D75B1282786BFB95F9E65B3E1D94298422D907CB0' }
        }
        function New-TaskFixture($Name,$Exe,$Arguments,$Level) {
            [pscustomobject]@{
                TaskName = $Name; State = 'Running'; Settings = [pscustomobject]@{ Enabled = $true }
                Actions = @([pscustomobject]@{ Execute = $Exe; Arguments = $Arguments; WorkingDirectory = (Split-Path $Exe) })
                Principal = [pscustomobject]@{ LogonType = 'Interactive'; RunLevel = $Level; UserId = $sid }
                Triggers = @([pscustomobject]@{ CimClass = [pscustomobject]@{ CimClassName = 'MSFT_TaskLogonTrigger' } })
            }
        }
        $oemTask = New-TaskFixture 'OneXConsole' (Join-Path $env:SystemRoot 'System32\wscript.exe') ('"' + $oemScript + '"') 'Limited'
        if ((Get-OneXConsoleGuiStartupState $oemTask) -ne 'Enabled; reversible handoff pending' -or !$oemTask.Settings.Enabled) {
            throw 'An enabled OEM task must remain enabled.'
        }
        $oemTask.Settings.Enabled = $false
        if ((Get-OneXConsoleGuiStartupState $oemTask) -ne 'Disabled') { throw 'Expected an existing disabled OEM task.' }
        $oemTask.Actions[0].Arguments = 'unrecognized.vbs'
        if ((Get-OneXConsoleGuiStartupState $oemTask) -ne 'Review existing logon task') { throw 'Accepted a foreign disabled task.' }
        $rtss = New-TaskFixture 'ONEXTweaks-RTSS' $rtssExe '' 'Highest'
        if ((Get-ONEXTweaksRtssTaskState $rtss $sid) -ne 'Ready') { throw 'The active RTSS location was missed.' }
        $rtss.Principal.UserId = 'S-1-5-18'
        if ((Get-ONEXTweaksRtssTaskState $rtss $sid) -eq 'Ready') { throw 'Accepted another account for RTSS.' }
        $tasks = @(
            New-TaskFixture 'ONEXTweaks-Startup' $fixtureLauncher '--startup' 'Limited'
            New-TaskFixture 'ONEXTweaks-PowerAgent' $fixtureAgent "serve-desktop $sid" 'Highest'
            New-TaskFixture 'ONEXTweaks-WidgetBridge' $fixtureBridge '' 'Limited'
        )
        $tasks[1].Principal.LogonType = 'ServiceAccount'
        $tasks[1].Principal.UserId = 'S-1-5-18'
        function Get-CimInstance($ClassName,$Filter) {
            if ($ClassName -ne 'Win32_Process' -or $Filter -ne "Name='ONEXTweaks.WidgetBridge.exe'") { throw 'Unexpected process query.' }
            [pscustomobject]@{ ProcessId = $bridgePid; ExecutablePath = $fixtureBridge; SessionId = $session; CreationDate = $fixtureObserved.AddMinutes(-1) }
        }
        function Get-Item($LiteralPath) {
            if ($LiteralPath -ne [IO.Path]::ChangeExtension($fixtureBridge,'.dll')) { throw 'Unexpected version query.' }
            [pscustomobject]@{ VersionInfo = [pscustomobject]@{ FileVersion = '0.29.1099.0' } }
        }
        function Invoke-CimMethod($InputObject,$MethodName) { [pscustomobject]@{ Sid = $sid } }
        function Get-Content($LiteralPath,[switch]$Raw,$ErrorAction) {
            if ($LiteralPath -ne (Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev\Reports\widget-bridge.json')) { throw 'Unexpected report path.' }
            return $fixtureReport | ConvertTo-Json -Depth 5
        }
        # Date the synthetic report at the readback step, after setup and any
        # account lookups. Fixture preparation can exceed the real freshness bound.
        $fixtureObserved = [DateTimeOffset]::UtcNow.AddMilliseconds(-100)
        $fixtureReport.updatedUtc = $fixtureObserved.ToString('o')
        $readback = Get-ONEXTweaksBackendState $tasks $sid $session $true 'ONEXTweaks.Widget_fixture'
        if ($readback -ne 'Healthy') { throw "Rejected a current release report: $readback" }
        $tasks[1].Actions[0].Arguments = 'serve-desktop S-1-5-21-11111-22222-33333-1002'
        if ((Get-ONEXTweaksBackendState $tasks $sid $session $true 'ONEXTweaks.Widget_fixture') -eq 'Healthy') { throw 'Accepted another desktop owner for PowerAgent.' }
        $tasks[1].Actions[0].Arguments = "serve-desktop $sid"
        $tasks[1].Principal.LogonType = 'Interactive'
        if ((Get-ONEXTweaksBackendState $tasks $sid $session $true 'ONEXTweaks.Widget_fixture') -eq 'Healthy') { throw 'Accepted interactive elevation for PowerAgent.' }
        $tasks[1].Principal.LogonType = 'ServiceAccount'
        $fixtureReport.homeHardware.fan.available = $false
        if ((Get-ONEXTweaksBackendState $tasks $sid $session $true 'ONEXTweaks.Widget_fixture') -eq 'Healthy') { throw 'Accepted absent hardware readback.' }
        $fixtureReport.homeHardware.fan.available = $true
        foreach ($mutation in @(
            @{ key = 'processId'; value = 5124 }, @{ key = 'sessionId'; value = 1 }, @{ key = 'connected'; value = 'false' },
            @{ key = 'version'; value = '0.29.1000' }, @{ key = 'widgetPackageFamily'; value = 'Foreign.App_fixture' }, @{ key = 'updatedUtc'; value = $fixtureObserved.AddMinutes(-1).ToString('o') },
            @{ key = 'updatedUtc'; value = $fixtureObserved.AddMinutes(1).ToString('o') }
        )) {
            $key = $mutation.key
            $saved = $fixtureReport.$key
            $fixtureReport.$key = $mutation.value
            if ((Get-ONEXTweaksBackendState $tasks $sid $session $true 'ONEXTweaks.Widget_fixture') -eq 'Healthy') { throw "Accepted an invalid report field: $key" }
            $fixtureReport.$key = $saved
        }
        $tasks[2].Actions[0].Execute = Join-Path $temp 'development-bridge.exe'
        if ((Get-ONEXTweaksBackendState $tasks $sid $session $true 'ONEXTweaks.Widget_fixture') -eq 'Healthy') { throw 'Accepted a development task as installed.' }
        'PASS: isolated release runtime, current session telemetry, RTSS task and read-only OEM state fixtures'
    } finally {
        foreach ($name in $original.Keys) { [Environment]::SetEnvironmentVariable($name, $original[$name]) }
    }
}

$widgetManifest = @'
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3">
  <Identity Name="ONEXTweaks.Widget" Publisher="CN=Fixture" ProcessorArchitecture="x64" Version="0.29.1109.0"/>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.18362.0"/>
    <PackageDependency Name="Microsoft.Fixture" Publisher="CN=Test Framework" MinVersion="1.2.0.0"/>
  </Dependencies>
  <Applications><Application Id="App"><Extensions><uap3:Extension>
    <uap3:AppExtension Name="microsoft.gameBarUIExtension" Id="GamingWidget"/>
  </uap3:Extension></Extensions></Application></Applications>
</Package>
'@
$frameworkManifest = @'
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
  <Identity Name="Microsoft.Fixture" Publisher="CN=Test Framework" ProcessorArchitecture="x64" Version="1.2.0.0"/>
  <Properties><Framework>true</Framework></Properties>
</Package>
'@

try {
    $links = @(& $installer -ListDependencies)
    $expectedLinks = @{
        'Xbox Game Bar' = 'https://apps.microsoft.com/detail/9NZKPSTSNW4P'
        'VIIPER 0.9.0' = 'https://github.com/Alia5/VIIPER/releases/tag/v0.9.0'
        'PawnIO 2.2.0' = 'https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0'
        'usbip-win2 0.9.8.1 x64' = 'https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1'
        'HidHide 1.5.230' = 'https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0'
        'RivaTuner Statistics Server 7.3.7' = 'https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/'
        'OneXConsole 0.10.4-fix3' = 'https://onexplayerstore.com/pages/drivers-and-faqs'
    }
    if ($links.Count -ne $expectedLinks.Count -or @($links | Select-Object -ExpandProperty Name -Unique).Count -ne $links.Count) {
        throw 'The dependency catalog has missing or duplicate entries.'
    }
    foreach ($link in $links) {
        if ($expectedLinks[$link.Name] -cne $link.Url) { throw "Unexpected link for $($link.Name)." }
    }
    . (Join-Path $PSScriptRoot 'DriverPrerequisites.ps1')
    $usbip = $prerequisites | Where-Object Service -eq 'usbip2_ude'
    if ($usbip.Arguments -notmatch '(?:^| )/TASKS="vcredist"(?: |$)') {
        throw 'The USBIP setup must run its bundled Visual C++ runtime before devnode.exe.'
    }

    $widget = New-PackageFixture 'widget.msix' $widgetManifest
    $hash = (Get-FileHash -LiteralPath $widget).Hash
    $valid = New-PackageFixture 'valid.appx' $frameworkManifest
    $neutral = New-PackageFixture 'neutral.appx' ($frameworkManifest.Replace('x64', 'neutral'))
    $inspection = & $installer -WidgetPackage $widget -WidgetSha256 $hash -FrameworkPackage $valid,$neutral -InspectPackage
    if ($inspection.Widget.Dependencies.Count -ne 1 -or $inspection.SuppliedFrameworks.Count -ne 2 -or
        $inspection.TrustVerified -or $inspection.Widget.Version -ne [version]'0.29.1109.0' -or
        ($inspection.DependencyLinks.Url -join '|') -cne ($links.Url -join '|')) {
        throw 'Widget or framework metadata readback differs from fixture.'
    }
    'PASS: official dependency links in standalone list and package inspection'

    $bundledDir = Join-Path $temp 'Dependencies/x64'
    $null = New-Item -ItemType Directory -Path $bundledDir -Force
    $bundledPath = Join-Path $bundledDir 'widget-framework.appx'
    Copy-Item -LiteralPath $valid -Destination $bundledPath
    $bundled = & $installer -WidgetPackage $widget -WidgetSha256 $hash -InspectPackage
    if ($bundled.SuppliedFrameworks.Count -ne 1 -or $bundled.SuppliedFrameworks[0].Path -ne $bundledPath) {
        throw 'The installer did not discover its bundled AppX framework.'
    }
    Remove-Item -LiteralPath $bundledPath

    $badFrameworks = @(
        New-PackageFixture 'x86.appx' ($frameworkManifest.Replace('x64', 'x86'))
        New-PackageFixture 'old.appx' ($frameworkManifest.Replace('1.2.0.0', '1.1.0.0'))
        New-PackageFixture 'publisher.appx' ($frameworkManifest.Replace('CN=Test Framework', 'CN=Different'))
        New-PackageFixture 'not-framework.appx' ($frameworkManifest.Replace('>true<', '>false<'))
        New-PackageFixture 'unrelated.appx' ($frameworkManifest.Replace('Microsoft.Fixture', 'Microsoft.Other'))
    )
    foreach ($path in $badFrameworks) {
        Assert-Rejected { & $installer -WidgetPackage $widget -WidgetSha256 $hash -FrameworkPackage $path -InspectPackage } 'does not satisfy'
    }
    Assert-Rejected { & $installer -WidgetPackage $widget -WidgetSha256 ('0' * 64) -InspectPackage } 'SHA-256 differs'
    $unsigned = New-PackageFixture 'unsigned.msix' $widgetManifest -Unsigned
    Assert-Rejected {
        & $installer -WidgetPackage $unsigned -WidgetSha256 (Get-FileHash -LiteralPath $unsigned).Hash -InspectPackage
    } 'AppxSignature.p7x'
    $otherWidget = New-PackageFixture 'other.msix' ($widgetManifest.Replace('Id="GamingWidget"', 'Id="Other"'))
    Assert-Rejected {
        & $installer -WidgetPackage $otherWidget -WidgetSha256 (Get-FileHash -LiteralPath $otherWidget).Hash -InspectPackage
    } 'not the x64 ONEXTweaks Game Bar widget'
    $xmlDtd = New-PackageFixture 'dtd.msix' ('<!DOCTYPE Package [<!ENTITY fixture "fixture">]>' + $widgetManifest)
    Assert-Rejected {
        & $installer -WidgetPackage $xmlDtd -WidgetSha256 (Get-FileHash -LiteralPath $xmlDtd).Hash -InspectPackage
    } 'DTD'
    Assert-Rejected { & $installer -WidgetPackage $widget -WidgetSha256 $hash -InspectPackage -Install } 'Choose either'
    'PASS: package inspection and 10 rejection scenarios'

    Test-RuntimeReadback
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        $originalLocalAppData = $env:LOCALAPPDATA
        try {
            $env:LOCALAPPDATA = $temp
            Test-WindowsWorkflow
        } finally {
            $env:LOCALAPPDATA = $originalLocalAppData
        }
    }

    if ($RealWidgetPackage) {
        if (!$RealWidgetSha256) { throw 'Pass a trusted hash for the real widget package.' }
        $real = & $installer -WidgetPackage $RealWidgetPackage -WidgetSha256 $RealWidgetSha256 -InspectPackage
        "PASS: real widget $($real.Widget.Version); $($real.Widget.Dependencies.Count) framework dependencies"
    }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}
