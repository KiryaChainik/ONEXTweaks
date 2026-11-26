#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName='Compile')]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory,ParameterSetName='Sign')][string]$SignTool,
    [Parameter(Mandatory,ParameterSetName='Sign')][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint,
    [Parameter(ParameterSetName='Sign')][ValidatePattern('^https://')][string]$TimestampUrl
)
$ErrorActionPreference = 'Stop'
$build = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'build.json')) | ConvertFrom-Json
$payload = Join-Path $PSScriptRoot 'Payload.zip'
if ((Get-FileHash -LiteralPath $payload).Hash -cne $build.PayloadSha256) { throw 'The setup payload changed after preparation.' }
if (!$build.Development -and !$build.Beta -and (!$CertificateThumbprint -or !$TimestampUrl)) { throw 'A distribution build must be signed and timestamped.' }

$framework = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$output = Join-Path (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName 'ONEXTweaks-Setup-x64.exe'
if (Test-Path -LiteralPath $output) { throw 'Use a new directory for each Setup build.' }
$manifest = Join-Path $PSScriptRoot 'setup.manifest'
$references = @('System.dll','System.Core.dll','System.IO.Compression.dll','System.Drawing.dll','System.Security.dll','System.Windows.Forms.dll') | ForEach-Object {
    '/reference:' + (Join-Path $framework $_)
}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ @references "/out:$output" "/win32manifest:$manifest" `
    "/resource:$payload,ONEXTweaks.Setup.Payload" (Join-Path $PSScriptRoot 'Setup.cs') (Join-Path $PSScriptRoot 'PayloadInfo.cs')
if ($LASTEXITCODE) { throw 'Could not compile ONEXTweaks Setup.' }
if ($CertificateThumbprint) {
    $timestamp = if ($TimestampUrl) { @('/tr',$TimestampUrl,'/td','SHA256') } else { @() }
    & $SignTool sign /fd SHA256 /sha1 $CertificateThumbprint @timestamp $output | Out-Null
    if ($LASTEXITCODE) { throw 'Setup signing failed.' }
    $signature = Get-AuthenticodeSignature -FilePath $output
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ine $CertificateThumbprint) { throw 'Setup signature did not validate.' }
}
[pscustomobject]@{ Setup=$output; Sha256=(Get-FileHash -LiteralPath $output).Hash; Development=[bool]$build.Development; Beta=[bool]$build.Beta }
