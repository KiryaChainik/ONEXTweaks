#Requires -Version 5.1
param([Parameter(Mandatory)][string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$null = New-Item -ItemType Directory -Path $OutputRoot
$dotnet = (Get-Command dotnet.exe).Source
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Workload.Universal -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
$uwpSdks = Join-Path (Split-Path -Parent $dotnet) 'sdk\6.0.423\Sdks'
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.26100.0\x64'
$signTool = Join-Path $sdkBin 'signtool.exe'
if (!(Test-Path -LiteralPath $signTool)) { throw "SignTool is missing: $signTool" }

& (Join-Path $root 'tools/Build-Widget.ps1') -Configuration Release -InstalledDistribution -Dotnet $dotnet -MSBuild $msbuild -UwpSdks $uwpSdks
& (Join-Path $root 'tools/Build-PowerAgent.ps1')
$bridge = Join-Path $OutputRoot 'Bridge'
& $dotnet publish (Join-Path $root 'src/ONEXTweaks.WidgetBridge/ONEXTweaks.WidgetBridge.csproj') -c Release -r win-x64 --self-contained true -o $bridge
if ($LASTEXITCODE) { throw 'Bridge publish failed.' }

$widgetRoot = Join-Path $root 'src/ONEXTweaks.Widget/XboxGamingBar'
$packages = @(Get-ChildItem -LiteralPath (Join-Path $widgetRoot 'AppPackages') -Recurse -File -Filter '*.msix')
if ($packages.Count -ne 1) { throw "Expected one widget MSIX, found $($packages.Count)." }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $widgetRoot 'Package.appxmanifest') -Raw
$certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $manifest.Package.Identity.Publisher -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddHours(2)
try {
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint $packages[0].FullName
    if ($LASTEXITCODE) { throw 'CI package signing failed.' }
    $bundle = Join-Path $OutputRoot 'Bundle'
    & python (Join-Path $root 'tools/installer/build_runtime_bundle.py') `
        --widget $packages[0].FullName --package-family ONEXTweaks.Widget_3a23eh8tyg7na --bridge $bridge `
        --launcher (Join-Path $root 'src/ONEXTweaks.CompanionLauncher/bin/Release/net48/ONEXTweaks.CompanionLauncher.exe') `
        --dependency-manager (Join-Path $root 'src/ONEXTweaks.DependencyManager/bin/Release/net48/ONEXTweaks.DependencyManager.exe') `
        --power-agent (Join-Path $root 'artifacts/PowerAgent/ONEXTweaks.PowerAgent.exe') --output $bundle
    if ($LASTEXITCODE) { throw 'CI runtime bundle failed.' }
} finally {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)"
}

$payload = Join-Path $OutputRoot 'Payload'
& python (Join-Path $root 'tools/installer/setup/build_payload.py') $bundle $payload --development
if ($LASTEXITCODE) { throw 'CI Setup payload failed.' }
& (Join-Path $payload 'Build-ONEXTweaksSetup.ps1') -OutputDirectory (Join-Path $OutputRoot 'Setup')
