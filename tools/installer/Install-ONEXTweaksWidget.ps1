#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Widget')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Widget')][string]$WidgetPackage,
    [Parameter(Mandatory, ParameterSetName = 'Widget')][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$WidgetSha256,
    [Parameter(ParameterSetName = 'Widget')][string[]]$FrameworkPackage = @(),
    [Parameter(ParameterSetName = 'Widget')][switch]$InspectPackage,
    [Parameter(ParameterSetName = 'Widget')][switch]$Install,
    [Parameter(ParameterSetName = 'Widget')][switch]$CloseActiveWidget,
    [Parameter(ParameterSetName = 'Widget')][ValidatePattern('^S-1-(?:[0-9]+-)+[0-9]+$')][string]$TargetUserSid,
    [Parameter(Mandatory, ParameterSetName = 'DependencyLinks')][switch]$ListDependencies
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'DriverPrerequisites.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'GameBarPrerequisite.ps1')
$prerequisites += $oneXConsolePrerequisite
$gameBarStore = 'https://apps.microsoft.com/detail/9NZKPSTSNW4P'
$dependencyLinks = @(
    [pscustomobject]@{
        Name = 'Xbox Game Bar'
        RequiredFor = 'Widget'
        Setup = 'Install from Microsoft Store if missing.'
        Url = $gameBarStore
    }
    [pscustomobject]@{
        Name = 'VIIPER 0.9.0'
        RequiredFor = 'Virtual Xbox 360 and DS4'
        Setup = 'libVIIPER.dll ships with Bridge.'
        Url = 'https://github.com/Alia5/VIIPER/releases/tag/v0.9.0'
    }
    foreach ($driver in $prerequisites) {
        [pscustomobject]@{
            Name = $driver.Name
            RequiredFor = $driver.RequiredFor
            Setup = $(if ($driver.PSObject.Properties['VendorPackage']) {
                'Uses the compatible OEM installer in Dependencies\OEM if missing.'
            } else { 'Installed automatically if missing.' })
            Url = $driver.Url
        }
    }
)
if ($PSCmdlet.ParameterSetName -eq 'DependencyLinks') { return $dependencyLinks }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-Package([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    if ($file -isnot [IO.FileInfo] -or $file.Extension -notin @('.appx', '.msix')) {
        throw "Expected an individual .appx or .msix package at '$Path'."
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        foreach ($entry in @('AppxManifest.xml', 'AppxBlockMap.xml', 'AppxSignature.p7x')) {
            $entries = @($archive.Entries | Where-Object { $_.FullName -ieq $entry })
            if ($entries.Count -ne 1 -or $entries[0].FullName -cne $entry -or $entries[0].Length -eq 0) {
                throw "Missing or ambiguous $entry in '$Path'."
            }
        }
        $settings = New-Object Xml.XmlReaderSettings
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 2097152
        $stream = $archive.GetEntry('AppxManifest.xml').Open()
        try {
            $reader = [Xml.XmlReader]::Create($stream, $settings)
            try {
                $manifest = New-Object Xml.XmlDocument
                $manifest.XmlResolver = $null
                $manifest.Load($reader)
            } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }

        $ns = New-Object Xml.XmlNamespaceManager($manifest.NameTable)
        $ns.AddNamespace('appx', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
        $ns.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
        $identity = $manifest.SelectSingleNode('/appx:Package/appx:Identity', $ns)
        if ($null -eq $identity) { throw "No package identity in '$Path'." }
        $framework = $manifest.SelectSingleNode('/appx:Package/appx:Properties/appx:Framework', $ns)
        $gameBarWidget = $manifest.SelectSingleNode(
            "/appx:Package/appx:Applications/appx:Application[@Id='App']/appx:Extensions/uap3:Extension/uap3:AppExtension[@Name='microsoft.gameBarUIExtension' and @Id='GamingWidget']", $ns)
        $target = $manifest.SelectSingleNode(
            "/appx:Package/appx:Dependencies/appx:TargetDeviceFamily[@Name='Windows.Universal' or @Name='Windows.Desktop']", $ns)
        $dependencies = @(
            foreach ($dependency in $manifest.SelectNodes('/appx:Package/appx:Dependencies/appx:PackageDependency', $ns)) {
                [pscustomobject]@{
                    Name = $dependency.GetAttribute('Name')
                    Publisher = $dependency.GetAttribute('Publisher')
                    MinVersion = [version]$dependency.GetAttribute('MinVersion')
                }
            }
        )
        [pscustomobject]@{
            Path = $file.FullName
            Name = $identity.GetAttribute('Name')
            Publisher = $identity.GetAttribute('Publisher')
            Version = [version]$identity.GetAttribute('Version')
            Architecture = $identity.GetAttribute('ProcessorArchitecture')
            Framework = $null -ne $framework -and $framework.InnerText -eq 'true'
            HasGameBarWidget = $null -ne $gameBarWidget
            MinWindowsVersion = $(if ($null -ne $target) { [version]$target.GetAttribute('MinVersion') })
            Dependencies = $dependencies
        }
    } finally { $archive.Dispose() }
}

function Get-MatchingFramework($Requirement, $Packages) {
    $Packages | Where-Object {
        $_.Name -ceq $Requirement.Name -and $_.Publisher -ceq $Requirement.Publisher -and
        $_.Architecture -in @('x64', 'neutral') -and [version]$_.Version -ge $Requirement.MinVersion
    } | Sort-Object @{ Expression = { [version]$_.Version }; Descending = $true } | Select-Object -First 1
}

function Assert-WidgetHash([string]$Path) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ine $WidgetSha256) { throw 'Widget SHA-256 differs from the expected release hash.' }
}

function Read-PendingSetupRestart([string]$Path, [string]$BootId) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    try {
        $marker = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($marker.BootId -ceq $BootId) { return $marker }
    } catch {
        throw 'ONEXTweaks could not read its pending-restart state. Repair the setup state before continuing.'
    }
}

function Save-PendingSetupRestart([string]$Path, [string]$BootId, [string]$Component) {
    $null = New-Item -ItemType Directory -Path (Split-Path -LiteralPath $Path) -Force
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, (@{ BootId = $BootId; Component = $Component } | ConvertTo-Json -Compress))
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

if ($Install -and $InspectPackage) { throw 'Choose either -Install or -InspectPackage.' }
if ($CloseActiveWidget -and !$Install) { throw 'Closing the current widget requires -Install.' }
Assert-WidgetHash $WidgetPackage
$widget = Read-Package $WidgetPackage
if ($widget.Name -cne 'ONEXTweaks.Widget' -or [string]::IsNullOrWhiteSpace($widget.Publisher) -or
    $widget.Architecture -ne 'x64' -or $widget.Framework -or !$widget.HasGameBarWidget -or
    $null -eq $widget.MinWindowsVersion) {
    throw 'The package is not the x64 ONEXTweaks Game Bar widget.'
}

$bundleFrameworkPaths = @(
    foreach ($architecture in @('x64', 'neutral')) {
        $directory = Join-Path (Join-Path (Split-Path -LiteralPath $widget.Path) 'Dependencies') $architecture
        if (!(Test-Path -LiteralPath $directory -PathType Container)) { continue }
        Get-ChildItem -LiteralPath $directory -File | Where-Object Extension -in @('.appx', '.msix') | ForEach-Object { $_.FullName }
    }
)
$supplied = @(
    foreach ($path in $bundleFrameworkPaths) {
        $package = Read-Package $path
        if ($package.Framework -and ($widget.Dependencies | Where-Object { Get-MatchingFramework $_ @($package) })) { $package }
    }
    foreach ($path in $FrameworkPackage) {
        $package = Read-Package $path
        $matching = $widget.Dependencies | Where-Object { Get-MatchingFramework $_ @($package) }
        if (!$package.Framework -or !$matching) {
            throw "Framework '$path' does not satisfy a declared widget dependency."
        }
        $package
    }
)

if ($InspectPackage) {
    [pscustomobject]@{
        Mode = 'Package inspection'
        Widget = $widget
        SuppliedFrameworks = $supplied
        DependencyLinks = $dependencyLinks
        TrustVerified = $false
    }
    return
}

if ($env:OS -ne 'Windows_NT' -or $PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Host checks and installation require Windows PowerShell 5.1. Use -InspectPackage to inspect the bundle on another platform.'
}

# Add-AppxPackage always acts on this token. Validate any SID captured by a
# launcher before querying AppX, and before allowing machine-wide setup.
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $currentSid = $currentIdentity.User.Value
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} finally { $currentIdentity.Dispose() }
if ($TargetUserSid -and $TargetUserSid -ne $currentSid) {
    throw 'Start setup in the intended Windows account. The target SID differs from the current Windows token.'
}
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
$installState = Get-ONEXTweaksReleaseStateDirectory
$enrollment = Join-Path $installState 'desktop-enrollment.json'
if (Test-Path -LiteralPath $enrollment -PathType Leaf) {
    Assert-ONEXTweaksProtectedStateDirectory $installState
    Assert-ONEXTweaksProtectedItem $enrollment
    $desktop = Get-Content -LiteralPath $enrollment -Raw | ConvertFrom-Json
    if ($desktop.Schema -ne 1 -or $desktop.OwnerSid -cne $currentSid) {
        throw 'The ONEXTweaks release is prepared for another Windows desktop account.'
    }
}

$os = Get-CimInstance -ClassName Win32_OperatingSystem
$minWindows = [version]'10.0.22000.0'
if ($widget.MinWindowsVersion -gt $minWindows) { $minWindows = $widget.MinWindowsVersion }
$windowsVersion = [version]$os.Version
if ($windowsVersion.Revision -lt 0) { $windowsVersion = [version]($windowsVersion.ToString() + '.0') }
$processors = @(Get-CimInstance -ClassName Win32_Processor)
# Win32_Processor describes the platform CPU even when a script runs under emulation.
$isX64 = $processors.Count -gt 0 -and @($processors | Where-Object Architecture -ne 9).Count -eq 0
if ($os.ProductType -ne 1 -or $windowsVersion -lt $minWindows -or
    !$isX64 -or
    ![Environment]::Is64BitProcess) {
    throw "Use 64-bit Windows PowerShell on x64 Windows 11 ($minWindows or later)."
}

$widgetRegistration = @(Get-AppxPackage -Name $widget.Name -PackageTypeFilter Main)
$current = $widgetRegistration | Where-Object { $_.Publisher -ceq $widget.Publisher } | Select-Object -First 1
$blockers = New-Object 'Collections.Generic.List[string]'
if ($widgetRegistration.Count -gt 1 -or ($widgetRegistration.Count -eq 1 -and $null -eq $current)) {
    $blockers.Add('The current account has a conflicting ONEXTweaks widget package identity.')
}
if ($null -ne $current -and [version]$current.Version -gt $widget.Version) {
    $blockers.Add("The current account already has a newer widget ($($current.Version)).")
}
$gameBar = @(Get-ONEXTweaksGameBarWidgetHost)
if ($gameBar.Count -eq 0) { $blockers.Add("Install or update Xbox Game Bar for this Windows account from Microsoft Store to enable widgets. $gameBarStore") }

$installedFrameworks = @(Get-AppxPackage -PackageTypeFilter Framework | Where-Object { $_.Status -eq 'Ok' })
$frameworkPlan = @(
    foreach ($requirement in $widget.Dependencies) {
        $installed = Get-MatchingFramework $requirement $installedFrameworks
        $package = Get-MatchingFramework $requirement $supplied
        $source = 'Installed'
        if ($null -eq $installed) {
            $source = 'Offline package'
            if ($null -eq $package) {
                $source = 'Missing'
                $blockers.Add("Missing x64/neutral framework: $($requirement.Name) $($requirement.MinVersion) or later.")
            }
        }
        [pscustomobject]@{
            Name = $requirement.Name
            MinVersion = $requirement.MinVersion
            Source = $source
            Path = $(if ($source -eq 'Offline package') { $package.Path })
        }
    }
)

$operation = 'First registration'
if ($null -ne $current) {
    $operation = 'Update'
    if ([version]$current.Version -eq $widget.Version) {
        $operation = 'Repair registration'
        if ($current.Status -eq 'Ok' -and $current.Architecture -eq 'x64' -and
            !($frameworkPlan | Where-Object { $_.Source -ne 'Installed' })) {
            $operation = 'Already registered'
        }
    }
}
$oemDirectory = Join-Path (Join-Path (Split-Path -LiteralPath $widget.Path) 'Dependencies') 'OEM'
$hardwareCompatibility = Get-ONEXTweaksHardwareCompatibility
if ($hardwareCompatibility -eq 'Supported') {
    $powerTask = Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object TaskName -eq 'ONEXTweaks-PowerAgent'
    $enrolledSid = Get-ONEXTweaksEnrolledDesktopSid $powerTask
    if ($enrolledSid -and $enrolledSid -ne $currentSid) {
        $blockers.Add('The machine hardware agent is enrolled to another desktop account.')
    }
}
$componentPlan = @(
    foreach ($component in $prerequisites) {
        $status = if ($hardwareCompatibility -ne 'Supported' -and $component.Service -eq 'PawnIO') {
            'Not applicable'
        } else { Get-PrerequisiteState $component }
        if ($status -eq 'Missing' -and $component.PSObject.Properties['VendorPackage'] -and
            !(Test-Path -LiteralPath (Join-Path $oemDirectory $component.VendorPackage) -PathType Leaf)) {
            $status = 'Compatible OEM installer missing from bundle'
        }
        [pscustomobject]@{
            Name = $component.Name
            Service = $component.Service
            Status = $status
        }
    }
)
$oem = $componentPlan | Where-Object Name -eq $oneXConsolePrerequisite.Name
$expectedFamily = if ($null -ne $current) { $current.PackageFamilyName }
$runtime = Get-ONEXTweaksRuntimeStatus $oem.Status $expectedFamily
$bootId = $os.LastBootUpTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
$restartPath = Join-Path $env:LOCALAPPDATA 'ONEXTweaks\Setup\pending-restart.json'
$pendingRestart = Read-PendingSetupRestart $restartPath $bootId
$plan = [pscustomobject]@{
    Mode = 'Windows preflight'
    Operation = $operation
    TargetUserSid = $currentSid
    Widget = $widget
    CurrentWidgetVersion = $(if ($null -ne $current) { $current.Version.ToString() })
    GameBarVersion = @($gameBar | ForEach-Object { $_.Version.ToString() })
    HardwareCompatibility = $hardwareCompatibility
    Frameworks = $frameworkPlan
    Drivers = @($componentPlan | Where-Object Service)
    Applications = @($componentPlan | Where-Object { !$_.Service })
    Backend = $runtime.Backend
    RtssTask = $runtime.RtssTask
    OneXConsoleStartup = $runtime.OneXConsoleStartup
    WidgetRegistered = $operation -eq 'Already registered'
    PrerequisitesReady = !$pendingRestart -and !($componentPlan | Where-Object Status -notin @('Installed', 'Not applicable'))
    RestartRequired = $null -ne $pendingRestart
    RestartComponent = $(if ($pendingRestart) { $pendingRestart.Component })
    SetupComplete = $false
    CompletionStatus = 'Full release bundle verification pending'
    DependencyLinks = $dependencyLinks
    Blockers = $blockers.ToArray()
    TrustVerified = $false
}

if (!$Install) { return $plan }
if ($blockers.Count) { throw ($blockers -join [Environment]::NewLine) }
if ($pendingRestart) {
    $plan.Mode = 'Restart required before setup can continue'
    return $plan
}
$missing = @($componentPlan | Where-Object Status -eq 'Missing')
if ($operation -eq 'Already registered' -and !$missing.Count) {
    if (!$WhatIfPreference) { $plan.Mode = 'Widget registered' }
    return $plan
}
if (!$PSCmdlet.ShouldProcess('ONEXTweaks for the current Windows account and missing prerequisites', 'Install')) {
    return $plan
}

# A credential UAC prompt can substitute a different administrator while
# keeping the desktop session ID. Only the driver installers need elevation.
if ($elevated) {
    $session = (Get-Process -Id $PID).SessionId
    $shells = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe' AND SessionId=$session")
    $owners = @(foreach ($shell in $shells) {
        $owner = Invoke-CimMethod -InputObject $shell -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or !$owner.Sid) { throw 'Could not confirm the signed-in desktop account. Start setup without elevation.' }
        $owner.Sid
    })
    if (!$session -or !$owners.Count -or @($owners | Where-Object { $_ -ne $currentSid }).Count) {
        throw 'Start setup without elevation from the signed-in desktop account. Approve the individual driver prompts when asked.'
    }
}

$downloadDirectory = $null
$installLock = New-Object Threading.Mutex($false, 'Global\ONEXTweaks.Install.v1')
$installLockHeld = $false
try {
    try { $installLockHeld = $installLock.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $installLockHeld = $true }
    if (!$installLockHeld) { throw 'Another ONEXTweaks installation is already running.' }

    # Another setup may have completed a prerequisite since preflight.
    $pendingRestart = Read-PendingSetupRestart $restartPath $bootId
    if ($pendingRestart) {
        $plan.Mode = 'Restart required before setup can continue'
        $plan.RestartRequired = $true
        $plan.RestartComponent = $pendingRestart.Component
        $plan.PrerequisitesReady = $false
        return $plan
    }
    $downloaded = @{}
    if ($missing.Count) {
        $downloadDirectory = Join-Path ([IO.Path]::GetTempPath()) ('onextweaks-setup-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $downloadDirectory
        foreach ($component in $prerequisites | Where-Object { $_.Name -in $missing.Name }) {
            $path = Save-PrerequisiteInstaller $component $downloadDirectory $oemDirectory
            Assert-PrerequisiteInstallerSignature $component $path
            $downloaded[$component.Name] = $path
        }
    }

    if ($operation -ne 'Already registered') {
        # AppX validates the package signatures, trust and dependencies before any driver is executed.
        $params = @{ Path = $widget.Path }
        $offline = @($frameworkPlan | Where-Object Source -eq 'Offline package' | ForEach-Object { $_.Path })
        if ($offline.Count) { $params.DependencyPath = $offline }
        if ($CloseActiveWidget -and $operation -in 'Update','Repair registration') { $params.ForceTargetApplicationShutdown = $true }
        Assert-WidgetHash $widget.Path
        Add-AppxPackage @params
    }

    $registered = Get-AppxPackage -Name $widget.Name -PackageTypeFilter Main | Where-Object {
        $_.Publisher -ceq $widget.Publisher -and [version]$_.Version -eq $widget.Version -and
        $_.Architecture -eq 'x64' -and $_.Status -eq 'Ok'
    }
    if (!$registered) { throw 'Windows did not report a healthy registration of the requested widget version.' }

    $restartRequired = $false
    $restartComponent = $null
    foreach ($component in $prerequisites) {
        if (!$downloaded.ContainsKey($component.Name)) { continue }
        $row = $componentPlan | Where-Object Name -eq $component.Name
        $row.Status = Install-Prerequisite $component $downloaded[$component.Name]
        if ($row.Status -eq 'Restart required') {
            Save-PendingSetupRestart $restartPath $bootId $component.Name
            $restartRequired = $true
            $restartComponent = $component.Name
            break
        }
    }
    $runtime = Get-ONEXTweaksRuntimeStatus $oem.Status $registered.PackageFamilyName
    [pscustomobject]@{
        Mode = 'Widget registered'
        Operation = $operation
        TargetUserSid = $currentSid
        Name = $registered.Name
        Version = $registered.Version.ToString()
        PackageFamilyName = $registered.PackageFamilyName
        HardwareCompatibility = $hardwareCompatibility
        Drivers = $plan.Drivers
        Applications = $plan.Applications
        Backend = $runtime.Backend
        RtssTask = $runtime.RtssTask
        OneXConsoleStartup = $runtime.OneXConsoleStartup
        RestartRequired = $restartRequired
        RestartComponent = $restartComponent
        WidgetRegistered = $true
        PrerequisitesReady = !$restartRequired -and !($componentPlan | Where-Object Status -notin @('Installed', 'Not applicable'))
        # This entry point installs the widget and prerequisites. The product
        # installer must additionally verify its complete versioned bundle.
        SetupComplete = $false
        CompletionStatus = $plan.CompletionStatus
        DependencyLinks = $dependencyLinks
    }
} finally {
    if ($downloadDirectory) {
        Remove-Item -LiteralPath $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($installLockHeld) { $installLock.ReleaseMutex() }
    $installLock.Dispose()
}
