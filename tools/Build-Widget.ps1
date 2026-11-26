#Requires -Version 5.1
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$Dotnet = (Join-Path $env:LOCALAPPDATA 'ONEXTweaks\toolchain\dotnet-10.0.403\dotnet.exe'),
    [string]$MSBuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
    [string]$UwpSdks = 'C:\Program Files\dotnet\sdk\6.0.423\Sdks',
    [switch]$InstalledDistribution,
    [switch]$Native
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!(Test-Path -LiteralPath $Dotnet) -or !(Test-Path -LiteralPath $MSBuild) -or !(Test-Path -LiteralPath $UwpSdks)) {
    throw 'Install .NET SDKs 10.0.403 and 6.0.423 and Visual Studio 2022 with UWP tools and Windows SDK 26100.'
}
$buildEnvironment = @{}
foreach ($name in @('MSBuildSDKsPath', 'MSBuildEnableWorkloadResolver')) {
    $buildEnvironment[$name] = @{ Exists = Test-Path ('Env:' + $name); Value = [Environment]::GetEnvironmentVariable($name, 'Process') }
}
Push-Location $root
try {
    # UWP uses SDK6 below; the bridge must resolve SDK10 independently of the caller.
    Remove-Item Env:MSBuildSDKsPath -ErrorAction SilentlyContinue

    $distributionArguments = if ($InstalledDistribution) { @('-p:OnexInstalledDistribution=true') } else { @() }
    foreach ($project in 'CompanionLauncher','DependencyManager','PackagedLauncher','WidgetBridge') {
        $projectName = if ($project -eq 'PackagedLauncher') { 'CompanionLauncher' } else { $project }
        & $Dotnet build "src/ONEXTweaks.$project/ONEXTweaks.$projectName.csproj" -c Release --nologo @distributionArguments
        if ($LASTEXITCODE) { throw "$project build failed." }
    }
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:MSBuildSDKsPath = $UwpSdks
    Push-Location (Join-Path $root 'src\ONEXTweaks.Widget')
    try {
        # CoreRuntime wraps TargetPath during packaging; regenerate the managed
        # payload before each pass instead of wrapping an incremental wrapper.
        $nativeToolchain = $Native.IsPresent.ToString().ToLowerInvariant()
        & $MSBuild XboxGamingBar/XboxGamingBar.csproj /restore /t:Rebuild /p:Configuration=$Configuration /p:Platform=x64 /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly /p:UseDotNetNativeToolchain=$nativeToolchain /p:GenerateTestArtifacts=false /m:1 /nr:false /p:UseSharedCompilation=false /v:minimal /nologo
        if ($LASTEXITCODE) { throw 'UWP frontend build failed' }
    } finally { Pop-Location }
} finally {
    Pop-Location
    foreach ($name in $buildEnvironment.Keys) {
        if ($buildEnvironment[$name].Exists) { [Environment]::SetEnvironmentVariable($name, $buildEnvironment[$name].Value, 'Process') }
        else { Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue }
    }
}
# The output MSIX is unsigned. Use your own local development certificate to sign it;
# the ONEXPLAYER's private signing key is deliberately not exported or distributed.
