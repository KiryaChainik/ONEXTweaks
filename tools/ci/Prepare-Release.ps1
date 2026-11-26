#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+(\.[0-9]+){2,3}(-[A-Za-z0-9.-]+)?$')][string]$Tag,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$RollbackWidget,
    [Parameter(Mandatory)][string]$QaRunner,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$BetaCertificateThumbprint,
    [string]$FrameworkDirectory,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gate = Join-Path $PSScriptRoot 'github_ops.py'
$RollbackWidget = (Resolve-Path -LiteralPath $RollbackWidget).Path
$QaRunner = (Resolve-Path -LiteralPath $QaRunner).Path
if ($FrameworkDirectory) { $FrameworkDirectory = (Resolve-Path -LiteralPath $FrameworkDirectory).Path }
$OutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Choose a new output directory for this candidate.' }
if ($OutputRoot.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep release candidates outside the checkout.'
}
$checkout = Join-Path $OutputRoot 'Source'
$repo = & gh repo view --json nameWithOwner --jq .nameWithOwner
if ($LASTEXITCODE -or !$repo) { throw 'Cannot identify the GitHub repository.' }

& python $gate --repo $repo verify-main $Commit
if ($LASTEXITCODE) { throw 'The requested commit did not pass the main release gate.' }
& git -C $root fetch origin main
if ($LASTEXITCODE) { throw 'Cannot fetch main.' }
$null = New-Item -ItemType Directory -Path $OutputRoot
& git -C $root worktree add --detach $checkout $Commit
if ($LASTEXITCODE) { throw 'Cannot create an isolated release checkout.' }
try {
    $builder = Join-Path $checkout 'tools/ci/Build-SetupSmoke.ps1'
    $build = & $builder -OutputRoot (Join-Path $OutputRoot 'Build') -BetaCertificateThumbprint $BetaCertificateThumbprint `
        -RollbackWidget $RollbackWidget -FrameworkDirectory $FrameworkDirectory
    $setup = ($build | Where-Object { $_.PSObject.Properties['Setup'] }).Setup
    if (!$setup -or !(Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'The trusted Windows build did not produce Setup.' }
} finally {
    # Only remove the temporary detached checkout created above. Candidate files live outside it.
    & git -C $root worktree remove --force $checkout
    if ($LASTEXITCODE) { Write-Warning "Temporary checkout remains at $checkout" }
}
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$receipt = Join-Path $OutputRoot 'vm-receipt.json'
Write-Host "Running Windows VM acceptance for $hash"
& $QaRunner -Setup $setup -Commit $Commit -Sha256 $hash -Receipt $receipt
if (!(Test-Path -LiteralPath $receipt -PathType Leaf)) { throw 'The VM runner did not produce an acceptance receipt.' }
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ine $hash) { throw 'Setup changed during VM acceptance.' }
$options = @()
if ($CheckOnly) { $options += '--check-only' }
& python $gate --repo $repo draft --commit $Commit --tag $Tag --setup $setup --receipt $receipt @options
if ($LASTEXITCODE) { throw 'The draft release gate failed.' }
