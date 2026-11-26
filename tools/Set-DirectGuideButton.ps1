#Requires -Version 5.1
param([switch]$Restore, [ValidateRange(0,3)][int]$ControllerIndex = 0)
$ErrorActionPreference = 'Stop'
$path = 'HKCU:\Software\ONEXTweaks\Overlay'
$nativePath = 'HKCU:\Software\Microsoft\GameBar'
if ($Restore) {
    $saved = $null
    # Stop this account's Guide handler before restoring native state.
    if (Test-Path -LiteralPath $path) {
        $saved = Get-Item -LiteralPath $path
        if ($saved.GetValue('DirectGuideEnabled') -eq 1) {
            Set-ItemProperty -LiteralPath $path -Name DirectGuideEnabled -Value 0 -Type DWord
        }
        if ($saved.GetValue('NativeNexusDefaultSaved') -eq 1) {
            $native = Get-Item -LiteralPath $nativePath -ErrorAction SilentlyContinue
            $current = if ($native) { $native.GetValue('UseNexusForGameBarEnabled') }
            if ($current -is [int] -and $current -eq 0) {
                if ($saved.GetValue('NativeNexusDefaultExists') -eq 1) {
                    $original = $saved.GetValue('NativeNexusDefault')
                    if ($original -isnot [int]) { throw 'Invalid saved native Guide preference' }
                    New-ItemProperty -LiteralPath $nativePath -Name UseNexusForGameBarEnabled -Value $original -PropertyType DWord -Force | Out-Null
                } else {
                    Remove-ItemProperty -LiteralPath $nativePath -Name UseNexusForGameBarEnabled
                }
            }
        }
    }
    & (Join-Path $PSScriptRoot 'Set-GameBarStartPage.ps1') -Restore
    if ($saved -and $saved.GetValue('NativeNexusDefaultSaved') -eq 1) {
        foreach ($name in @('NativeNexusDefaultSaved','NativeNexusDefaultExists','NativeNexusDefault')) {
            if ($saved.GetValueNames() -contains $name) { Remove-ItemProperty -LiteralPath $path -Name $name }
        }
    }
    return
}
& (Join-Path $PSScriptRoot 'Set-GameBarStartPage.ps1') | Out-Null
if (!(Test-Path $path)) { New-Item $path -Force | Out-Null }
$own = Get-Item $path
if ($own.GetValue('NativeNexusDefaultSaved') -ne 1) {
    $native = Get-Item -LiteralPath $nativePath -ErrorAction SilentlyContinue
    $exists = $null -ne $native -and $native.GetValueNames() -contains 'UseNexusForGameBarEnabled'
    New-ItemProperty $path -Name NativeNexusDefaultExists -Value ([int]$exists) -PropertyType DWord -Force | Out-Null
    if ($exists) { New-ItemProperty $path -Name NativeNexusDefault -Value ([int]$native.GetValue('UseNexusForGameBarEnabled')) -PropertyType DWord -Force | Out-Null }
    New-ItemProperty $path -Name NativeNexusDefaultSaved -Value 1 -PropertyType DWord -Force | Out-Null
}
New-ItemProperty $path -Name GuideControllerIndex -Value $ControllerIndex -PropertyType DWord -Force | Out-Null
New-ItemProperty $path -Name DirectGuideEnabled -Value 1 -PropertyType DWord -Force | Out-Null
# Steam independently polls Guide. Inspect only its preference; change it through
# Steam's live UI so the running client does not overwrite an offline config edit.
$steamGuideEnabled = $null
try {
    $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
    $values = @(Get-ChildItem (Join-Path $steamPath 'userdata') -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $configFile = Join-Path $_.FullName 'config\localconfig.vdf'
        if (Test-Path $configFile) {
            $match = [regex]::Match([IO.File]::ReadAllText($configFile), '"Controller_CheckGuideButton"\s+"(?<value>[01])"')
            if ($match.Success) { $match.Groups['value'].Value }
        }
    } | Sort-Object -Unique)
    if ($values.Count -eq 1) { $steamGuideEnabled = $values[0] -eq '1' }
} catch { }
@{
    directGuideEnabled = $true
    controllerIndex = $ControllerIndex
    restartBridge = $true
    steamGuideFocusEnabled = $steamGuideEnabled
    steamGuideFocusConflict = $steamGuideEnabled -eq $true
    steamControllerSettingsUri = 'steam://settings/controller'
    steamGuideFocusInstruction = $(if ($steamGuideEnabled -eq $true) { 'Steam > Settings > Controller > Advanced settings: turn off Guide button focuses Steam. Keep Steam Input enabled.' } else { $null })
} | ConvertTo-Json
