#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host 'Running GitHub merge and release gate tests'
& python -B -m unittest discover -s (Join-Path $root 'tools/ci') -p 'test_*.py' -v
if ($LASTEXITCODE) { throw 'GitHub gate tests failed.' }

$projects = @(
    'Awake.Core/Awake.Core',
    'CompanionBootstrap/CompanionBootstrap',
    'CpuTuningLease/CpuTuningLease',
    'DisplayLayoutPolicy/DisplayLayoutPolicy',
    'IntelSyncPolicy/IntelSyncPolicy',
    'ONEXTweaks.Backend.Tests/ONEXTweaks.Backend.Tests',
    'PerformanceSelection/PerformanceSelection',
    'PowerStartup/PowerStartup',
    'Profiles.Core.Tests/Profiles.Core.Tests',
    'RefreshRatePolicy/RefreshRatePolicy',
    'ResolutionPolicy/ResolutionPolicy',
    'StatePushes/StatePushes'
)
foreach ($project in $projects) {
    Write-Host "Running $project"
    & dotnet run -c Release --project (Join-Path $root "tests/$project.csproj")
    if ($LASTEXITCODE) { throw "Test project failed: $project" }
}

$installerTests = @(
    'Test-ONEXTweaksGuideRestore',
    'Test-ONEXTweaksReleaseCompletion',
    'Test-ONEXTweaksReleaseRuntime',
    'Test-ONEXTweaksUpgradeHash',
    'Test-ONEXTweaksWidgetInstaller',
    'Test-OneXConsoleHandoff',
    'Test-UninstallONEXTweaksRelease'
)
foreach ($test in $installerTests) {
    Write-Host "Running $test"
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root "tools/installer/$test.ps1")
    if ($LASTEXITCODE) { throw "Installer test failed: $test" }
}
