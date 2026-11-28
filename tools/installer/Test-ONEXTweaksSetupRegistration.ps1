#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$setupPath = Join-Path $PSScriptRoot 'setup/Invoke-ONEXTweaksSetup.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($setupPath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$branches = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.IfStatementAst] -and
        $node.Clauses[0].Item1.Extent.Text.Contains("'runtime-update-1110.json'")
}, $true))
if ($branches.Count -ne 1) { throw 'Expected one same-package runtime update branch in Setup.' }

$root = Join-Path ([IO.Path]::GetTempPath()) ('onextweaks-registration-' + [guid]::NewGuid().ToString('N'))
$state = $root
$runtimeArguments = @{}
$metadata = @{ PackageFullName = 'ONEXTweaks.Widget_0.29.1110.1_x64__3a23eh8tyg7na' }
$OwnerSid = 'S-1-5-21-1-2-3-4'
$healthyPackage = [pscustomobject]@{ PackageFullName = $metadata.PackageFullName; Status = 'Ok' }

function Get-AppxPackage([string]$User, [string]$Name, [string]$PackageTypeFilter) {
    if ($User -cne $OwnerSid -or $Name -cne 'ONEXTweaks.Widget' -or $PackageTypeFilter -cne 'Main') { throw 'Unexpected AppX lookup.' }
    $fixture.Trace.Add('Read registration')
    if ($fixture.Registered) { $healthyPackage }
}

function Invoke-DesktopPhase([string]$Action) {
    if ($Action -cne 'Register') { throw 'Unexpected owner desktop phase.' }
    $fixture.Trace.Add('Register')
    if ($fixture.RegisterResult -eq 0) { $fixture.Registered = !$fixture.MissingReadback }
    return $fixture.RegisterResult
}

function Write-SetupLine([string]$Kind, [string]$Message) {}
function Write-SetupResult([string]$Status) { $fixture.Result = $Status }

$updater = {
    param([string]$OwnerSid, [string]$Action)
    if ($OwnerSid -cne $fixture.ExpectedOwner -or ($Action -cne 'Assess' -and !$fixture.Registered)) { throw 'The updater received an unregistered or foreign widget.' }
    $fixture.Trace.Add($Action)
    switch ($Action) {
        'Assess' {
            if ($fixture.Blocker) { throw $fixture.Blocker }
            if ($fixture.WrongOwner) { return 'No update' }
            return $fixture.UpdateStatus
        }
        'Inspect' {
            if ($fixture.RecoveryPending) { $fixture.RecoveryPending = $false; return 'Rolled back' }
            return $fixture.UpdateStatus
        }
        'Stage' { return 'Staged' }
        default { throw 'Unexpected runtime updater action.' }
    }
}

function Test-UpdatePath([string]$Trace, [switch]$AlreadyRegistered, [switch]$Unhealthy, [switch]$WrongOwner, [switch]$MissingReadback,
    [switch]$RecoveryPending, [string]$Blocker, [int]$RegisterResult = 0, [string]$UpdateStatus = 'Update required', [int]$ExitCode = 0, [string]$ExpectedError) {
    $package = if ($Unhealthy) { [pscustomobject]@{PackageFullName=$metadata.PackageFullName;Status='Modified'} }
        elseif ($AlreadyRegistered) { $healthyPackage }
    $fixture = [pscustomobject]@{
        Registered = [bool]$AlreadyRegistered
        MissingReadback = [bool]$MissingReadback
        ExpectedOwner = $OwnerSid
        WrongOwner = [bool]$WrongOwner
        Blocker = $Blocker
        RecoveryPending = [bool]$RecoveryPending
        RegisterResult = $RegisterResult
        UpdateStatus = $UpdateStatus
        Trace = [Collections.Generic.List[string]]::new()
        Result = $null
    }
    $failure = $null
    $global:LASTEXITCODE = 0
    try { & $branchScript } catch { $failure = $_.Exception.Message }
    if (($ExpectedError -and $failure -notlike $ExpectedError) -or (!$ExpectedError -and $failure)) {
        throw "Unexpected Setup error: $failure"
    }
    if ($global:LASTEXITCODE -ne $ExitCode) { throw "Unexpected Setup exit code: $global:LASTEXITCODE" }
    if ($ExitCode -eq 3010 -and $fixture.Result -notlike 'Restart Windows,*') { throw 'Setup did not explain the required reboot.' }
    $observed = $fixture.Trace -join ', '
    if ($observed -cne $Trace) { throw "Expected '$Trace', observed '$observed'." }
}

try {
    $null = [IO.Directory]::CreateDirectory($root)
    $receipt = Join-Path $state 'owned-release.json'
    [IO.File]::WriteAllText($receipt, 'Existing owned runtime')
    # Run the production branch as a script so its reboot exit code is captured.
    $branchScript = Join-Path $root 'SetupRuntimeBranch.ps1'
    [IO.File]::WriteAllText($branchScript, $branches[0].Extent.Text)
    Test-UpdatePath 'Assess, Register, Read registration, Inspect, Stage'
    Test-UpdatePath 'Assess, Register, Read registration, Inspect, Stage' -Unhealthy
    Test-UpdatePath 'Assess, Register, Read registration, Inspect, Stage' -AlreadyRegistered
    Test-UpdatePath 'Assess, Register, Read registration, Inspect' -AlreadyRegistered -UpdateStatus Current
    Test-UpdatePath 'Assess, Register, Read registration, Inspect' -UpdateStatus Staged
    Test-UpdatePath 'Assess, Register, Read registration, Inspect, Inspect, Stage' -RecoveryPending
    Test-UpdatePath 'Assess, Register' -RegisterResult 3010 -ExitCode 3010
    Test-UpdatePath 'Assess, Register' -RegisterResult 1 -ExpectedError 'Widget registration or dependency repair failed.*'
    Test-UpdatePath 'Assess, Register, Read registration' -MissingReadback -ExpectedError 'Windows did not confirm*'
    Test-UpdatePath 'Assess' -WrongOwner -ExpectedError 'The existing installation could not be verified*'
    Test-UpdatePath 'Assess' -Blocker 'Foreign task' -ExpectedError 'Foreign task'
    if ([IO.File]::ReadAllText($receipt) -cne 'Existing owned runtime') { throw 'Setup replaced the existing runtime ownership receipt.' }
    'PASS Setup repairs the owned 1110 registration before inspecting or staging the runtime update.'
} finally {
    if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
}
