#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

function Read-Ast([string]$Path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path,[ref]$tokens,[ref]$errors)
    if ($errors) { throw ($errors | Out-String) }
    return $ast
}

# Use the production handoff and hash check with files in a scratch directory.
# Full migration needs a signed-in Windows desktop with the historical package.
$setupAst = Read-Ast (Join-Path $PSScriptRoot 'setup/Invoke-ONEXTweaksSetup.ps1')
$updaterAst = Read-Ast (Join-Path $PSScriptRoot 'Update-ONEXTweaksRelease.ps1')
$handoff = $setupAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.CommandElements[0].Extent.Text -ceq '$upgrade' -and
        '-Upgrade' -cin $node.CommandElements.Extent.Text
},$true)
$check = $updaterAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -ceq 'Assert-ONEXTweaksUpgradeFile' -and
        $node.CommandElements[1].Extent.Text -ceq '$WidgetPackage'
},$true)
$verify = $updaterAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -ceq 'Assert-ONEXTweaksUpgradeFile'
},$true)
if (!$handoff -or !$check -or !$verify) { throw 'The Setup upgrade hash contract is missing.' }

foreach ($name in 'Get-ONEXTweaksUpgradePackage','Restore-ONEXTweaksLegacyRegistration') {
    $definition = $updaterAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name
    },$true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$oldPackage = 'ONEXTweaks.Widget_0.29.1109.0_x64__3a23eh8tyg7na'
$newPackage = 'ONEXTweaks.Widget_0.29.1110.0_x64__3a23eh8tyg7na'
$deployment = @{Name=$null;Changes=0}
function Get-AppxPackage {
    if ($deployment.Name) { [pscustomobject]@{PackageFullName=$deployment.Name;Status='Ok'} }
}
function Add-AppxPackage([string]$Path, [switch]$ForceUpdateFromAnyVersion, [switch]$ForceTargetApplicationShutdown) {
    if ($Path -cne 'verified-legacy.msix' -or !$ForceUpdateFromAnyVersion -or !$ForceTargetApplicationShutdown) {
        throw 'Rollback did not use the verified legacy media.'
    }
    $deployment.Name = $oldPackage
    $deployment.Changes++
}
Restore-ONEXTweaksLegacyRegistration 'verified-legacy.msix'
Restore-ONEXTweaksLegacyRegistration 'verified-legacy.msix'
if ($deployment.Changes -ne 1) { throw 'Missing registration recovery did not restore 1109 exactly once.' }
$deployment.Name = $newPackage
Restore-ONEXTweaksLegacyRegistration 'verified-legacy.msix'
if ($deployment.Changes -ne 2) { throw 'Downgrade did not restore 1109.' }
$deployment.Name = 'Another widget'
$rejected = $false
try { Restore-ONEXTweaksLegacyRegistration 'verified-legacy.msix' }
catch { $rejected = $_.Exception.Message -like '*healthy, known ONEXTweaks*' }
if (!$rejected -or $deployment.Changes -ne 2) { throw 'Rollback attempted to modify an unverified package.' }

$folder = Join-Path ([IO.Path]::GetTempPath()) ('ONEX-upgrade-hash-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $folder
try {
    $upgrade = Join-Path $folder 'Observe-Upgrade.ps1'
    [IO.File]::WriteAllText($upgrade,@'
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Upgrade,
    [Parameter(Mandatory)][string]$WidgetPackage,
    [Parameter(Mandatory)][string]$WidgetSha256,
    [string]$RollbackPackage
)
$PSBoundParameters
'@)
    $packageFile = Join-Path $folder 'rebuilt-1110.msix'
    $previous = Join-Path $folder 'archived-1109.msix'
    [IO.File]::WriteAllText($packageFile,'A different signed build would have a different checksum.')
    $metadata = @{ WidgetSha256 = (Get-FileHash -LiteralPath $packageFile).Hash }
    $runtimeArguments = @{}
    $result = & ([scriptblock]::Create($handoff.Extent.Text))
    if ($result['WidgetSha256'] -cne $metadata.WidgetSha256 -or $result['WidgetPackage'] -cne $packageFile) {
        throw 'Setup did not forward the rebuilt package checksum.'
    }
    $WidgetPackage = $result['WidgetPackage']
    $WidgetSha256 = $result['WidgetSha256']
    . ([scriptblock]::Create($verify.Extent.Text))
    $checkPackage = [scriptblock]::Create($check.Extent.Text)
    if ((& $checkPackage) -cne $packageFile) { throw 'A rebuilt package with its expected checksum was rejected.' }
    [IO.File]::AppendAllText($packageFile,'tampered')
    try { $null = & $checkPackage }
    catch {
        if ($_.Exception.Message -notlike 'Upgrade media hash differs:*') { throw }
        Write-Host 'Setup forwards the current package checksum and the upgrader rejects a changed package.'
        return
    }
    throw 'A changed package was accepted.'
} finally {
    Remove-Item -LiteralPath $folder -Recurse -Force
}
