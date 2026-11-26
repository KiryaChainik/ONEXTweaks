#Requires -Version 5.1
#Requires -RunAsAdministrator
param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\GamingConfiguration'
$name = 'OEMGameBarWidget'
$family = 'ONEXTweaks.Widget_3a23eh8tyg7na'
$folder = Join-Path $env:ProgramData 'ONEXTweaks\GameBarBackup'
$backup = Join-Path $folder 'registration-before.json'

if ($Restore) {
    if (!(Test-Path $backup)) { throw 'Original Game Bar registration backup is missing' }
    $saved = Get-Content $backup -Raw | ConvertFrom-Json
    $current = (Get-Item $path).GetValue($name)
    if ($current -and $current -ne $family) { throw 'Another app changed the OEM widget; preserve its registration' }
    if ($saved.valueExists) { New-ItemProperty $path -Name $name -PropertyType $saved.kind -Value $saved.value -Force | Out-Null }
    else { Remove-ItemProperty $path -Name $name -ErrorAction SilentlyContinue }
    return
}

$package = Get-AppxPackage -Name ONEXTweaks.Widget
if (!$package -or $package.PackageFamilyName -ne $family) { throw 'Install the ONEXTweaks widget for this Windows user first' }
New-Item $folder -ItemType Directory -Force | Out-Null
& icacls.exe $folder /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE) { throw 'Cannot protect Game Bar registration backup' }
if (!(Test-Path $backup)) {
    $keyExists = Test-Path $path
    $key = if ($keyExists) { Get-Item $path } else { $null }
    $exists = $key -and ($key.GetValueNames() -contains $name)
    @{ keyExists = $keyExists; valueExists = [bool]$exists; value = $(if($exists){$key.GetValue($name)}else{$null}); kind = $(if($exists){$key.GetValueKind($name).ToString()}else{$null}) } |
        ConvertTo-Json | Set-Content $backup -Encoding UTF8
}
if (!(Test-Path $path)) { New-Item $path -Force | Out-Null }
New-ItemProperty $path -Name $name -PropertyType String -Value $family -Force | Out-Null
if ((Get-Item $path).GetValue($name) -ne $family) { throw 'OEM widget registration did not persist' }
@{ registered = $family; backup = $backup; restartGameBar = $true } | ConvertTo-Json
