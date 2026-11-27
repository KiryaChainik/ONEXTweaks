#Requires -Version 5.1
param([switch]$WindowsScheduler, [string]$Bundle)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Initialize-ONEXTweaksReleaseRuntime.ps1') -Prepare -WidgetPackageFullName 'ONEXTweaks.Widget_0.29.1110.0_x64__0123456789abc'
. (Join-Path $PSScriptRoot 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $PSScriptRoot 'OneXConsoleSetup.ps1')
. (Join-Path $PSScriptRoot 'ONEXTweaksLegacy1109.ps1')

function Test-OemHandoffReadback {
    $family = 'ONEXTweaks.Widget_0123456789abc'
    $live = [pscustomobject]@{widgetPackageFamily=$family;connected=$true;processId=42;sessionId=1;version='0.29.1110';
        state=@{available=$true;canWrite=$true};homeHardware=@{fan=@{available=$false}}}
    if ((Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true $family 15.4) -cne 'PowerAgent hardware readback temporarily stale' -or
        (Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true $family 45) -notlike 'Bridge report outside*' -or
        (Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true "$family-wrong" 15.4) -notlike 'Widget package family differs*') {
        throw 'Handoff readiness did not distinguish recent hardware failure from unrelated or old telemetry.'
    }
    if ((Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true $family 1 -RequiresFan $false) -cne 'Healthy' -or
        (Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true $family 1) -cne 'PowerAgent hardware readback unavailable') {
        throw 'Writable TDP must be sufficient only when fan qualification is not required.'
    }
    $live.state.canWrite = $false
    if ((Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true $family 1 -RequiresFan $false) -cne 'PowerAgent hardware readback unavailable') {
        throw 'A TDP-only handoff accepted power limits that cannot be changed.'
    }
    $live.homeHardware.fan.available = $true
    if ((Get-ONEXTweaksBridgeReportState $live 42 1 '0.29.1110' $true $family 15.4) -notlike 'Bridge report outside*') {
        throw 'Stale previously healthy telemetry unexpectedly allowed setup.'
    }
    'PASS: OEM handoff distinguishes recent hardware failure, expired readback and mismatched Bridge reports'
}

function Test-MigrationReadinessPair {
    Set-StrictMode -Version Latest
    $start = [DateTimeOffset]::UtcNow
    $old = [pscustomobject]@{version='0.29.1109';connected=$true;updatedUtc=$start.ToString('o')}
    $fresh = [pscustomobject]@{version='0.29.1110';connected=$true;processId=30;updatedUtc=$start.ToString('o')}
    $ack = [pscustomobject]@{ready=$true;pid=30}
    if ((Test-ONEXTweaksUpgradeReportPair $old $ack $start) -or
        (Test-ONEXTweaksUpgradeReportPair ([pscustomobject]@{}) $ack $start) -or
        (Test-ONEXTweaksUpgradeReportPair $fresh ([pscustomobject]@{ready=$true;pid=31}) $start) -or
        (Test-ONEXTweaksUpgradeReportPair $fresh $ack ($start.AddSeconds(1))) -or
        !(Test-ONEXTweaksUpgradeReportPair $fresh $ack $start)) { throw 'Migration readiness accepted old telemetry or missed a verified report.' }
    $fresh.updatedUtc = 'incomplete'
    if (Test-ONEXTweaksUpgradeReportPair $fresh $ack $start) { throw 'Migration readiness accepted malformed telemetry time.' }
    'PASS: upgrade ignores the old or incomplete Bridge report and waits for a fresh matched PID'
}

function Test-ReleaseStateDirectory([string]$Root) {
    $previous = Join-Path $Root 'ONEXTweaks\Installation'
    $current = Join-Path $Root 'ONEXTweaksInstaller\Installation'
    $backup = Join-Path $Root 'ONEXTweaks\GameBarBackup'
    [void][IO.Directory]::CreateDirectory($backup)
    $checks = [Collections.Generic.List[string]]::new()
    $denied = $null
    function Assert-ONEXTweaksProtectedItem([string]$Path) {
        $checks.Add($Path)
        if ($Path -ceq $denied) { throw 'Fixture directory is writable by ordinary users.' }
    }

    if ((Get-ONEXTweaksReleaseStateDirectory $Root) -cne $current -or $checks.Count) {
        throw 'Legacy application data misdirected a new installer transaction.'
    }
    [void][IO.Directory]::CreateDirectory($previous)
    $enrollment = Join-Path $previous 'desktop-enrollment.json'
    [IO.File]::WriteAllText($enrollment, '{}')
    if ((Get-ONEXTweaksReleaseStateDirectory $Root) -cne $previous -or
        ($checks -join '|') -cne (@((Split-Path -LiteralPath $previous),$previous,$enrollment) -join '|')) {
        throw 'A prior installer transaction was not selected with its protection checks.'
    }
    $denied = Split-Path -LiteralPath $previous
    $rejected = $false
    try { $null = Get-ONEXTweaksReleaseStateDirectory $Root }
    catch { $rejected = $_.Exception.Message -ceq 'Fixture directory is writable by ordinary users.' }
    if (!$rejected) { throw 'An unprotected prior transaction did not block setup.' }
    $denied = $null
    [void][IO.Directory]::CreateDirectory($current)
    [IO.File]::WriteAllText((Join-Path $current 'upgrade-1109.json'), '{}')
    $rejected = $false
    try { $null = Get-ONEXTweaksReleaseStateDirectory $Root }
    catch { $rejected = $_.Exception.Message -like 'Conflicting ONEXTweaks installer records*' }
    if (!$rejected) { throw 'Conflicting installer transactions were not rejected.' }
    'PASS: legacy application data, prior protected records and conflicting installer roots'
}

$label = 'ONEXTweaks-SetupQA-' + [guid]::NewGuid().ToString('N')
$temp = Join-Path ([IO.Path]::GetTempPath()) $label
$null = [IO.Directory]::CreateDirectory($temp)
try {
    Test-ReleaseStateDirectory $temp
    Test-MigrationReadinessPair
    Test-OemHandoffReadback
    $record = Join-Path $temp 'receipt.json'
    Write-ONEXTweaksInstallationRecord $record @{ Schema=1; Files=@(@{Path='test.txt';Sha256=('C'*64)}) }
    $decoded = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
    if ($decoded.Schema -ne 1 -or $decoded.Files[0].Sha256 -ne ('C'*64)) { throw 'Installation record round-trip failed.' }
    $duplicateBlocked = $false
    try { Write-ONEXTweaksInstallationRecord $record @{Schema=2} }
    catch { $duplicateBlocked = $_.Exception.GetBaseException() -is [IO.IOException] }
    if (!$duplicateBlocked -or (Get-Content -LiteralPath $record -Raw | ConvertFrom-Json).Schema -ne 1) { throw 'Existing setup record was replaced.' }
    'PASS: existing setup ownership record survives repeated writes'
    if ($Bundle) {
        $runtime = Join-Path $Bundle 'Runtime'
        $manifestPath = Join-Path $runtime 'runtime-manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $metadata = Get-Content -LiteralPath (Join-Path $Bundle 'test-bundle.json') -Raw | ConvertFrom-Json
        if ((Get-FileHash -LiteralPath $manifestPath).Hash -cne $metadata.RuntimeManifestSha256 -or
            (Get-FileHash -LiteralPath (Join-Path $Bundle $metadata.WidgetFile)).Hash -cne $metadata.WidgetSha256) { throw 'Bundle metadata hash mismatch.' }
        $destination = Join-Path $temp 'Uninstalled'
        $hardwareFiles = Get-ONEXTweaksRuntimePayload $manifest $runtime $destination $true
        $desktopFiles = Get-ONEXTweaksRuntimePayload $manifest $runtime $destination $false
        if ($hardwareFiles.Count -ne $metadata.RuntimeFiles -or $desktopFiles.Count -ge $hardwareFiles.Count -or
            $desktopFiles.ContainsKey('PowerAgent\ONEXTweaks.PowerAgent.exe')) { throw 'Unexpected hardware and desktop payload validation.' }
        if ($env:OS -eq 'Windows_NT') {
            Add-Type -AssemblyName System.Web.Extensions
            $agent = [Reflection.Assembly]::LoadFrom($hardwareFiles['PowerAgent\ONEXTweaks.PowerAgent.exe'].Source)
            $seed = [IO.File]::ReadAllText($hardwareFiles['PowerAgent\home-oem-seed.json'].Source)
            $serializer = [System.Web.Script.Serialization.JavaScriptSerializer]::new()
            $deserialize = $serializer.GetType().GetMethods() | Where-Object { $_.Name -eq 'Deserialize' -and $_.IsGenericMethodDefinition }
            $method = $deserialize.MakeGenericMethod(@($agent.GetType('ONEXTweaks.Power.HomeSettings')))
            $homeSettings = $method.Invoke($serializer, [object[]]@($seed.PSObject.BaseObject))
            $flags = [Reflection.BindingFlags]'NonPublic,Static'
            $hardware = $agent.GetType('ONEXTweaks.Power.Hardware')
            $hardware.GetMethod('ValidateHome', $flags).Invoke($null, [object[]]@($homeSettings.PSObject.BaseObject))
            $modelCheck = $hardware.GetMethod('IsSupportedModel', $flags)
            $oemCheck = $hardware.GetMethod('IsQualifiedOemHardware', $flags)
            if (!$modelCheck.Invoke($null, [object[]]@('ONE-NETBOOK', 'ONEXPLAYER 3')) -or
                $modelCheck.Invoke($null, [object[]]@('ONE-NETBOOK', 'ONEXPLAYER X2')) -or
                $modelCheck.Invoke($null, [object[]]@('Other', 'ONEXPLAYER 3'))) { throw 'The TDP model allowlist changed.' }
            if (!$oemCheck.Invoke($null, [object[]]@('1002-B', '5.01')) -or
                $oemCheck.Invoke($null, [object[]]@('1002-C', '5.01')) -or
                $oemCheck.Invoke($null, [object[]]@('1002-B', '5.09')) -or
                $oemCheck.Invoke($null, [object[]]@('1002-B', '5.39'))) { throw 'An unverified board or BIOS was admitted to EC controls.' }
            $sampleType = $agent.GetType('ONEXTweaks.Power.Sample')
            $sample = [Activator]::CreateInstance($sampleType, $true)
            $fields = [Reflection.BindingFlags]'NonPublic,Instance'
            $sampleType.GetField('Pl1', $fields).SetValue($sample, [double]25)
            $sampleType.GetField('Pl2', $fields).SetValue($sample, [double]52)
            $enabled = $sampleType.GetField('Enabled', $fields)
            $locked = $sampleType.GetField('Locked', $fields)
            $limitsCheck = $hardware.GetMethod('CanRestoreLimits', $flags)
            foreach ($pair in @(@($true, $false), @($true, $true), @($false, $false))) {
                $enabled.SetValue($sample, $pair[0])
                $locked.SetValue($sample, $pair[1])
                if ($limitsCheck.Invoke($null, [object[]]@($sample)) -ne ($pair[0] -and !$pair[1])) {
                    throw 'Power limits require an enabled, unlocked and restorable sample.'
                }
            }
            $enabled.SetValue($sample, $true)
            $sampleType.GetField('Pl2', $fields).SetValue($sample, [double]64)
            if ($limitsCheck.Invoke($null, [object[]]@($sample))) { throw 'Unexpected factory limit was accepted for rollback.' }
        }
        'PASS: real runtime manifest, hardware and generic x64 payloads'
    }
    if ($WindowsScheduler) {
        $protected = Join-Path $temp 'Protected'
        New-ONEXTweaksProtectedDirectory $protected
        $testFile = Join-Path $protected 'fixture.txt'
        [IO.File]::WriteAllText($testFile, 'fixture')
        Assert-ONEXTweaksProtectedItem $testFile
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        try { $sid = $identity.User.Value } finally { $identity.Dispose() }
        $com = New-Object -ComObject Schedule.Service
        $com.Connect()
        $scheduler = $com.GetFolder('\')
        $folder = $scheduler.CreateFolder($label, $null)
        try {
            $pair = [pscustomobject]@{ Service=$com; Tasks=$folder }
            $exe = Join-Path ([Environment]::SystemDirectory) 'cmd.exe'
            Register-ONEXTweaksRuntimeTask $pair 'NoExecution' $exe "serve-desktop $sid" $sid Hardware
            $task = $folder.GetTask('NoExecution')
            $definition = $task.Definition
            if ($task.State -ne 3 -or $definition.Triggers.Count -ne 0 -or $definition.Actions.Count -ne 1 -or
                (Get-ONEXTweaksAccountSid $definition.Principal.UserId) -ne 'S-1-5-18' -or
                $definition.Principal.LogonType -ne 5 -or $definition.Actions.Item(1).Path -ine $exe -or
                $definition.Actions.Item(1).Arguments -cne "serve-desktop $sid") {
                $details = @{ State=$task.State; Triggers=$definition.Triggers.Count; Actions=$definition.Actions.Count;
                    UserId=$definition.Principal.UserId; LogonType=$definition.Principal.LogonType;
                    ImageMatches=$definition.Actions.Item(1).Path -ieq $exe; ArgumentsMatch=$definition.Actions.Item(1).Arguments -ceq "serve-desktop $sid" }
                throw ('Unexpected registered PowerAgent definition. ' + ($details | ConvertTo-Json -Compress))
            }
            $security = $task.GetSecurityDescriptor(7)
            $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($security)
            $rules = @($descriptor.DiscretionaryAcl | Where-Object { $_.SecurityIdentifier.Value -eq $sid })
            if ($rules.Count -ne 1 -or ($rules[0].AccessMask -band 0x10000000) -ne 0) { throw 'Owner must not have full control of the SYSTEM action.' }
            'PASS: actual Windows scheduler accepted an inert SYSTEM task and owner-specific ACL, protected runtime permissions inherited'
        } finally {
            if ($folder.GetTasks(1).Count) { $folder.DeleteTask('NoExecution', 0) }
            $scheduler.DeleteFolder($label, 0)
        }
    }
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
