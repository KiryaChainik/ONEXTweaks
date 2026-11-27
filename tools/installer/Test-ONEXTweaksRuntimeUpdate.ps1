#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
. (Join-Path $PSScriptRoot 'Update-ONEXTweaksRuntime.ps1') -Action Inspect -OwnerSid 'S-1-5-21-1-2-3-4' -RuntimeManifest fixture -RuntimeManifestSha256 ('A' * 64)

function Assert-UpdateTest([bool]$Passed, [string]$Message) {
    if (!$Passed) { throw $Message }
}

try {
    [void][IO.Directory]::CreateDirectory($root)
    $launcher = Join-Path $root 'CompanionLauncher.exe'
    [IO.File]::WriteAllText($launcher,'owned launcher')
    $old = [pscustomobject]@{Path='CompanionLauncher.exe';Sha256=('A' * 64)}
    $receipt = [pscustomobject]@{Files=@($old);PowerStateOwned=$false;InstallId=[guid]::NewGuid().ToString()}
    $plan = [pscustomobject]@{Root=$root;Receipt=$receipt}
    $candidate = @{'CompanionLauncher.exe' = @{Row=$old}}
    Assert-UpdateTest (!(Get-ONEXTweaksRuntimeDifference $plan $candidate)) 'The same payload would cause a second update.'
    $candidate['PowerAgent\ONEXTweaks.PowerAgent.exe'] = @{Row=@{Path='PowerAgent\ONEXTweaks.PowerAgent.exe';Sha256=('B' * 64)}}
    Assert-UpdateTest (Get-ONEXTweaksRuntimeDifference $plan $candidate) 'The update skipped an installation without PowerAgent.'
    $candidate.Remove('PowerAgent\ONEXTweaks.PowerAgent.exe')
    $candidate['CompanionLauncher.exe'] = @{Row=@{Path=$old.Path;Sha256=('B' * 64)}}
    Assert-UpdateTest (Get-ONEXTweaksRuntimeDifference $plan $candidate) 'The update skipped replacement at the same package version.'
    $journal = Join-Path $root 'runtime-update-1110.json'
    Write-ONEXTweaksRuntimeRecord $journal @{Phase='Switching';Previous=$receipt}
    Write-ONEXTweaksRuntimeRecord $journal @{Phase='Staged';Previous=$receipt}
    $record = [IO.File]::ReadAllText($journal) | ConvertFrom-Json
    Assert-UpdateTest ($record.Phase -ceq 'Staged' -and $record.Previous.InstallId -ceq $receipt.InstallId) 'The runtime journal lost the original installation.'
    Assert-UpdateTest (!(Get-ChildItem -LiteralPath $root -Filter '*.tmp')) 'The journal left temporary files.'
    'PASS same-version runtime replacement, missing PowerAgent enrollment and atomic update record.'
} finally {
    if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root,$true) }
}
