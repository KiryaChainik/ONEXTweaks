$prerequisites = @(
    [pscustomobject]@{
        Name = 'PawnIO 2.2.0'
        RequiredFor = 'Hardware telemetry and controls'
        Service = 'PawnIO'
        ProductPattern = '^PawnIO$'
        Version = [version]'2.2.0.0'
        Url = 'https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0'
        Download = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
        Hash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
        Publisher = 'namazso.eu'
        Arguments = '-install -silent'
    }
    [pscustomobject]@{
        Name = 'usbip-win2 0.9.8.1 x64'
        RequiredFor = 'Virtual Xbox 360 and DS4'
        Service = 'usbip2_ude'
        ProductPattern = '^USBip(?:\s|$)'
        Version = [version]'0.9.8.1'
        Url = 'https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1'
        Download = 'https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe'
        Hash = '38CAD6D4432B52D5BB9409D9AD03B72FDFFC4ADA4CD3A48FBECA1A2752A8518A'
        Publisher = 'Cloudyne Systems (Scheibling Consulting AB)'
        # Install the bundled VC++ runtime before the driver helper devnode.exe runs.
        Arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /COMPONENTS="main,client" /TASKS="vcredist"'
    }
    [pscustomobject]@{
        Name = 'HidHide 1.5.230'
        RequiredFor = 'Virtual controller capture'
        Service = 'HidHide'
        ProductPattern = '^HidHide$'
        Version = [version]'1.5.230.0'
        # The MSI advertises 1.5.230, but its stock signed SYS has this version and hash.
        DriverBinaryVersion = [version]'1.4.181.0'
        DriverBinaryHash = 'E3BB8B6A4E4FF90290B34E710EDBEF88CF71CEC6BA0CADCB8E1685D5DD65DDB2'
        Url = 'https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0'
        Download = 'https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe'
        Hash = 'F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6'
        Publisher = 'Nefarius Software Solutions e.U.'
        Arguments = '/exenoui /qn /norestart'
    }
    [pscustomobject]@{
        Name = 'RivaTuner Statistics Server 7.3.7'
        RequiredFor = 'In-game overlay and RTSS FPS limiter'
        Service = $null
        Url = 'https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/'
        Download = 'https://ftp.nluug.nl/pub/games/PC/guru3d/afterburner/%5BGuru3D%5D-RTSSSetup737Build28314.zip'
        ArchiveHash = '9B084A8CB3E53EC1A673894D0B66E22B16C9FD8785636B020B2D422F3F2A820E'
        InstallerEntry = 'RTSSSetup737.exe'
        Hash = '04F4704CED86A68B650BF87156C02E69160E6C049B576661250250BEEABAA5EF'
        Publisher = 'MICRO-STAR INTERNATIONAL CO., LTD.'
        Arguments = '/S'
    }
)

function Get-RtssPrerequisiteState {
    $locations = @(
        foreach ($folder in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if ($folder) { Join-Path $folder 'RivaTuner Statistics Server' }
        }
    )
    $partial = $false
    foreach ($location in $locations) {
        $exe = Join-Path $location 'RTSS.exe'
        $hooks = Join-Path $location 'RTSSHooks64.dll'
        if ((Test-Path -LiteralPath $exe -PathType Leaf) -and (Test-Path -LiteralPath $hooks -PathType Leaf)) {
            return 'Installed'
        }
        if (Test-Path -LiteralPath $location -PathType Container) { $partial = $true }
    }
    $registry = 'HKLM:\SOFTWARE\WOW6432Node\Unwinder\RTSS'
    if (Test-Path -LiteralPath $registry) {
        $entry = Get-ItemProperty -LiteralPath $registry
        if ($entry.PSObject.Properties['InstallDir'] -and $entry.InstallDir -and
            (Test-Path -LiteralPath (Join-Path $entry.InstallDir 'RTSS.exe') -PathType Leaf)) {
            $partial = $true
        }
    }
    if ($partial) { return 'Review existing installation' }
    return 'Missing'
}

function Get-PrerequisiteState($Driver) {
    if ($Driver.PSObject.Properties['VendorPackage']) { return Get-OneXConsolePrerequisiteState }
    if (!$Driver.Service) { return Get-RtssPrerequisiteState }
    $service = Get-CimInstance Win32_SystemDriver -Filter "Name='$($Driver.Service)'"
    $products = @(
        foreach ($registryRoot in @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
        )) {
            if (!(Test-Path -LiteralPath $registryRoot)) { continue }
            Get-ItemProperty -Path "$registryRoot\*" | Where-Object {
                $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -match $Driver.ProductPattern
            }
        }
    )

    if (!$service) {
        if (!$products.Count) { return 'Missing' }
        return 'Driver service missing from Windows'
    }
    if ($service.StartMode -eq 'Disabled') { return 'Driver service disabled' }
    if ($service.PSObject.Properties['ExitCode'] -and $service.ExitCode -notin @(0, 1077)) {
        return "Driver service reported error $($service.ExitCode)"
    }
    $path = $null
    if ($service.PSObject.Properties['PathName'] -and $service.PathName) {
        $path = [Environment]::ExpandEnvironmentVariables($service.PathName.Trim('"'))
        $path = $path -replace '^\\\?\?\\', ''
        if ($path.StartsWith('\SystemRoot\', [StringComparison]::OrdinalIgnoreCase)) {
            $path = Join-Path $env:SystemRoot $path.Substring(12)
        } elseif ($path.StartsWith('System32\', [StringComparison]::OrdinalIgnoreCase)) {
            $path = Join-Path $env:SystemRoot $path
        }
    }
    if (!$path -or $path -notmatch '^[a-z]:[\\/]' -or ![IO.Path]::GetExtension($path).Equals('.sys', [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $path -PathType Leaf)) {
        return 'Driver file missing from the registered service path'
    }
    if ($service) {
        foreach ($product in $products) {
            if (!$product.PSObject.Properties['DisplayVersion']) { continue }
            $installed = $null
            if (![version]::TryParse($product.DisplayVersion, [ref]$installed)) { continue }
            if ($installed.Build -lt 0) { $installed = [version]($installed.ToString() + '.0.0') }
            if ($installed.Revision -lt 0) { $installed = [version]($installed.ToString() + '.0') }
            if ($installed -ge $Driver.Version) { return 'Installed' }
        }
        # An OEM or manually installed driver can have no Apps & Features entry.
        $info = (Get-Item -LiteralPath $path).VersionInfo
        $binaryVersion = New-Object Version($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
        if ($binaryVersion -ge $Driver.Version) { return 'Installed' }
        if ($Driver.PSObject.Properties['DriverBinaryVersion'] -and $binaryVersion -eq $Driver.DriverBinaryVersion -and
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $Driver.DriverBinaryHash) { return 'Installed' }
    }
    return 'Review existing driver'
}

function Save-PrerequisiteInstaller($Driver, [string]$Directory, [string]$OemDirectory) {
    if ($Driver.PSObject.Properties['VendorPackage']) {
        $path = Join-Path $Directory $Driver.VendorPackage
        Copy-Item -LiteralPath (Join-Path $OemDirectory $Driver.VendorPackage) -Destination $path
    } else {
        $path = Join-Path $Directory ([uri]$Driver.Download).Segments[-1]
        Write-Verbose "Downloading $($Driver.Name)."
        Invoke-WebRequest -Uri $Driver.Download -UseBasicParsing -OutFile $path -MaximumRedirection 5 -TimeoutSec 90
    }
    if ($Driver.PSObject.Properties['ArchiveHash']) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $Driver.ArchiveHash) {
            throw "$($Driver.Name) archive does not match the pinned SHA-256."
        }
        $archive = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $entries = @($archive.Entries | Where-Object FullName -ceq $Driver.InstallerEntry)
            if ($entries.Count -ne 1 -or !$entries[0].Length) { throw "$($Driver.Name) installer is missing from its archive." }
            $path = Join-Path $Directory $Driver.InstallerEntry
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $path)
        } finally { $archive.Dispose() }
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $Driver.Hash) {
        throw "$($Driver.Name) download does not match the pinned SHA-256."
    }
    return $path
}

function Assert-PrerequisiteInstallerSignature($Driver, [string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ine $Driver.Publisher) {
        throw "$($Driver.Name) is missing a valid signature from the expected publisher."
    }
}

function Install-Prerequisite($Driver, [string]$Path) {
    # Deny writers and replacement of the checked file while the vendor installer runs.
    $held = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Driver.Hash) {
            throw "$($Driver.Name) download does not match the pinned SHA-256."
        }
        Assert-PrerequisiteInstallerSignature $Driver $Path
        if ((Get-PrerequisiteState $Driver) -ne 'Missing') {
            throw "$($Driver.Name) changed since preflight. Run setup again to refresh the plan."
        }
        Write-Verbose "Installing $($Driver.Name). Windows may ask for administrator approval."
        $launch = @{
            FilePath = $Path
            ArgumentList = $Driver.Arguments
            WorkingDirectory = [Environment]::SystemDirectory
            Verb = 'RunAs'
            Wait = $true
            PassThru = $true
        }
        $process = Start-Process @launch
        try {
            if ($process.ExitCode -notin @(0, 3010, 1641)) {
                throw "$($Driver.Name) setup returned exit code $($process.ExitCode). Run setup again after resolving the error."
            }
            if ($process.ExitCode -in @(3010, 1641)) { return 'Restart required' }
            if ((Get-PrerequisiteState $Driver) -ne 'Installed') {
                throw "Windows did not confirm the installation of $($Driver.Name)."
            }
            return 'Installed'
        } finally { $process.Dispose() }
    } finally { $held.Dispose() }
}
