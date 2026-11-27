#Requires -Version 5.1
param(
    [Parameter(Mandatory)][ValidatePattern('^S-1-5-21(?:-[0-9]{1,10}){4}$')][string]$OwnerSid,
    [ValidateSet('Install','Register','OpenWidget')][string]$Phase = 'Install'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$setup = Join-Path $PSScriptRoot 'Setup\Installer'
$metadata = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test-bundle.json')) | ConvertFrom-Json
$packageFile = Join-Path $PSScriptRoot $metadata.WidgetFile
$widgetArguments = @{ WidgetPackage=$packageFile; WidgetSha256=$metadata.WidgetSha256; TargetUserSid=$OwnerSid }
$installWidget = Join-Path $setup 'Install-ONEXTweaksWidget.ps1'
$initialize = Join-Path $setup 'Initialize-ONEXTweaksReleaseRuntime.ps1'
$runtimeArguments = @{ RuntimeManifest=(Join-Path $PSScriptRoot 'Runtime\runtime-manifest.json'); RuntimeManifestSha256=$metadata.RuntimeManifestSha256 }
. (Join-Path $setup 'Uninstall-ONEXTweaksRelease.ps1')
. (Join-Path $setup 'GameBarPrerequisite.ps1')

function Write-SetupLine([string]$Kind, [string]$Message) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Kind + ' ' + $Message + [Environment]::NewLine)
    $setupOutput.Write($bytes, 0, $bytes.Length)
    $setupOutput.Flush()
}

function Write-SetupResult([string]$Status) { Write-SetupLine RESULT ($Status -replace '[\r\n]+',' ') }

function Invoke-DesktopPhase([string]$Action) {
    if ((Get-ONEXTweaksInteractiveSid) -cne $OwnerSid) { throw 'The signed-in desktop changed during Setup.' }
    $scheduler = New-Object -ComObject Schedule.Service
    $scheduler.Connect()
    $folder = $scheduler.GetFolder('\')
    $name = 'ONEXTweaks-Setup-' + [guid]::NewGuid().ToString('N')
    $task = $scheduler.NewTask(0)
    $task.Principal.UserId = $OwnerSid
    $task.Principal.LogonType = 3
    $task.Principal.RunLevel = 0
    $task.Settings.DisallowStartIfOnBatteries = $false
    $task.Settings.StopIfGoingOnBatteries = $false
    $task.Settings.ExecutionTimeLimit = 'PT20M'
    $process = $task.Actions.Create(0)
    $process.Path = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
    $process.Arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "' +
        $PSCommandPath + '" -Phase ' + $Action + ' -OwnerSid ' + $OwnerSid
    $process.WorkingDirectory = $PSScriptRoot
    $registered = $folder.RegisterTaskDefinition($name,$task,2,$OwnerSid,$null,3,$null)
    try {
        $before = $registered.LastRunTime
        $null = $registered.Run($null)
        $deadline = [datetime]::UtcNow.AddMinutes(20)
        do {
            Start-Sleep -Milliseconds 750
            $registered = $folder.GetTask($name)
            if ($registered.LastRunTime -ne $before -and $registered.State -notin 2,4) {
                return [int]$registered.LastTaskResult
            }
        } while ([datetime]::UtcNow -lt $deadline)
        throw "The $Action phase in the signed-in account timed out."
    } finally {
        if ($registered.State -in 2,4) { $registered.Stop(0) }
        $folder.DeleteTask($name,0)
    }
}

function Invoke-OwnerPhase {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { if ($current.User.Value -cne $OwnerSid) { throw 'The widget setup task is running as the wrong account.' } }
    finally { $current.Dispose() }

    if ($Phase -eq 'OpenWidget') {
        Start-Process -FilePath "ms-gamebar://launchforeground/activate/$($metadata.PackageFamilyName)_App_GamingWidget"
        return 0
    }

    $registration = & $installWidget @widgetArguments -Install -CloseActiveWidget -Confirm:$false
    if ($registration.RestartRequired) { return 3010 }
    if (!$registration.WidgetRegistered -or !$registration.PrerequisitesReady) {
        throw "Widget registration or dependencies did not finish. $($registration.Blockers -join ' ')"
    }
    return 0
}

if ($Phase -ne 'Install') {
    try { exit (Invoke-OwnerPhase) }
    catch {
        $log = Join-Path $env:LOCALAPPDATA 'ONEXTweaks\Setup\setup-entry-last.log'
        $null = [IO.Directory]::CreateDirectory((Split-Path -LiteralPath $log))
        [IO.File]::WriteAllText($log, ($_ | Out-String))
        exit 1
    }
}

$setupOutput = [Console]::OpenStandardOutput()
$stage = 'Preflight'
$upgrading = $false
$updater = Join-Path $setup 'Update-ONEXTweaksRuntime.ps1'
try {
    if ((Get-ONEXTweaksInteractiveSid) -cne $OwnerSid) { throw 'Start Setup from the Windows account that will use ONEXTweaks.' }
    if (@(Get-CimInstance Win32_Processor | Where-Object Architecture -ne 9).Count) { throw 'This package requires x64 Windows hardware.' }
    if (!@(Get-ONEXTweaksGameBarWidgetHost $OwnerSid).Count) {
        throw 'Install or update Xbox Game Bar in Microsoft Store, then run this Setup again.'
    }
    $state = Get-ONEXTweaksReleaseStateDirectory
    $root = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ONEXTweaks'
    $package = Get-AppxPackage -User $OwnerSid -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main
    $legacyName = 'ONEXTweaks.Widget_0.29.1109.0_x64__3a23eh8tyg7na'
    if ($metadata.PSObject.Properties['RollbackFile'] -and
        ($package.PackageFullName -ceq $legacyName -or (Test-Path -LiteralPath (Join-Path $state 'upgrade-1109.json')))) {
        $current = [Security.Principal.WindowsIdentity]::GetCurrent()
        try { if ($current.User.Value -cne $OwnerSid) { throw 'The 1109 upgrade needs administrator rights for the signed-in account.' } }
        finally { $current.Dispose() }
        $upgrade = Join-Path $setup 'Update-ONEXTweaksRelease.ps1'
        if (Test-Path -LiteralPath (Join-Path $state 'upgrade-1109.json')) {
            $stage = '1109 update recovery'
            Write-SetupLine STEP 'Recovering the interrupted 1109 update...'
            $null = & $upgrade -Recover -Confirm:$false
            $package = Get-AppxPackage -User $OwnerSid -Name 'ONEXTweaks.Widget' -PackageTypeFilter Main
        }
        if ($package.PackageFullName -ceq $legacyName) {
            $stage = '1109 update and profile verification'
            Write-SetupLine STEP 'Updating 1109 and checking your profiles...'
            $previous = Join-Path $PSScriptRoot $metadata.RollbackFile
            $update = & $upgrade @runtimeArguments -Upgrade -WidgetPackage $packageFile -WidgetSha256 $metadata.WidgetSha256 `
                -RollbackPackage $previous -Confirm:$false
            if (!$update.SetupComplete) { throw 'The 1109 update did not report a working installation.' }
            Write-SetupResult 'ONEXTweaks was updated. Your profiles were kept.'
            exit 0
        }
    }

    if ($package -and $package.PackageFullName -cne $metadata.PackageFullName) {
        throw 'A different ONEXTweaks package is installed. This beta supports a clean installation or the included 1109 upgrade.'
    }
    if ($metadata.PackageFullName -ceq 'ONEXTweaks.Widget_0.29.1110.0_x64__3a23eh8tyg7na' -and
        ((Test-Path -LiteralPath (Join-Path $state 'owned-release.json')) -or (Test-Path -LiteralPath (Join-Path $state 'runtime-update-1110.json')))) {
        if (!$package) { throw 'The installed 1110 widget registration is missing.' }
        $stage = '1110 runtime update'
        $updateStatus = & $updater @runtimeArguments -OwnerSid $OwnerSid -Action Inspect
        if ($updateStatus -ceq 'Rolled back') {
            Write-SetupResult 'The interrupted update was rolled back. Run this Setup again to retry.'
            exit 1
        }
        if ($updateStatus -ceq 'Update required') {
            Write-SetupLine STEP 'Checking dependencies for the 1110 update...'
            $ownerResult = Invoke-DesktopPhase Register
            if ($ownerResult -eq 3010) {
                Write-SetupResult 'Restart Windows, then run this same Setup to finish installing dependencies.'
                exit 3010
            }
            if ($ownerResult -ne 0) { throw 'Update dependencies failed. See %LOCALAPPDATA%\ONEXTweaks\Setup\setup-entry-last.log.' }
            Write-SetupLine STEP 'Updating 1110 while keeping the existing widget and profiles...'
            $upgrading = $true
            $updateStatus = & $updater @runtimeArguments -OwnerSid $OwnerSid -Action Stage
        }
        $upgrading = $updateStatus -ceq 'Staged'
    }
    $plan = Get-ONEXTweaksUninstallPlan $state $root
    if (!$plan) {
        $null = & $initialize -Prepare -WidgetPackageFullName $metadata.PackageFullName -Confirm:$false
        $stage = 'Widget registration and dependency setup'
        Write-SetupLine STEP 'Registering the widget and checking its dependencies...'
        $ownerResult = Invoke-DesktopPhase Register
        if ($ownerResult -eq 3010) {
            Write-SetupResult 'Restart Windows, then run this same Setup to finish installing dependencies.'
            exit 3010
        }
        if ($ownerResult -ne 0) {
            throw 'Widget setup failed. See %LOCALAPPDATA%\ONEXTweaks\Setup\setup-entry-last.log in the signed-in account.'
        }
        $stage = 'Bridge and startup task installation'
        Write-SetupLine STEP 'Installing Bridge and the Windows startup tasks...'
        $null = & $initialize @runtimeArguments -Confirm:$false
        $plan = Get-ONEXTweaksUninstallPlan $state $root
    }
    if (!$plan -or $plan.Receipt.OwnerSid -cne $OwnerSid -or $plan.Receipt.PackageFullName -cne $metadata.PackageFullName) {
        throw 'The installed runtime belongs to a different release or user.'
    }

    $stage = 'ONEXTweaks startup'
    $complete = Get-ONEXTweaksVerifiedTool $plan 'Tools\Installer\Complete-ONEXTweaksRelease.ps1'
    foreach ($name in 'ONEXTweaks-Startup','ONEXTweaks-RTSS') {
        $task = Get-ONEXTweaksRemainingTask $plan $name
        if ($task -and $task.State -notin 'Running','Queued') { Start-ScheduledTask -InputObject $task }
    }
    Write-SetupLine STEP 'Checking the connection between Game Bar and Bridge...'
    $stage = 'Opening ONEXTweaks in Game Bar'
    if ((Invoke-DesktopPhase OpenWidget) -ne 0) { throw 'Could not open the widget in the signed-in account.' }
    $stage = 'Game Bar, Bridge and OneXConsole readiness'
    $deadline = [datetime]::UtcNow.AddMinutes(5)
    do {
        $ready = & $complete -Complete -Confirm:$false
        if ($ready.RestartRequired -or $ready.Status -ceq 'Windows restart required') {
            Write-SetupResult 'Restart Windows, then run this same Setup to finish installation.'
            exit 3010
        }
        if ($ready.SetupComplete) {
            if ($upgrading) {
                $null = & $updater @runtimeArguments -OwnerSid $OwnerSid -Action Commit
                $upgrading = $false
                Write-SetupResult 'ONEXTweaks was updated. Your profiles were kept.'
            } else { Write-SetupResult 'ONEXTweaks is installed. Open it from the Game Bar widgets menu.' }
            exit 0
        }
        Start-Sleep -Seconds 3
    } while ([datetime]::UtcNow -lt $deadline)
    throw "Setup is incomplete. $($ready.Status)"
} catch {
    $failure = $_
    $recovery = ''
    if ($upgrading) {
        try {
            if ((& $updater @runtimeArguments -OwnerSid $OwnerSid -Action Rollback) -ceq 'Rolled back') {
                $recovery = ' The previous runtime was restored.'
            }
        } catch { $recovery = " Run the same Setup to resume recovery. $($_.Exception.Message)" }
    }
    Write-SetupResult "$stage failed. $($failure.Exception.Message)$recovery"
    $details = @(($failure | Out-String).Trim(), $failure.FullyQualifiedErrorId, $failure.ScriptStackTrace) -join [Environment]::NewLine
    foreach ($line in ($details -split '\r?\n')) { Write-SetupLine ERROR $line }
    exit 1
}
