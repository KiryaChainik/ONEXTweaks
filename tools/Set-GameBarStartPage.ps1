#Requires -Version 5.1
param([switch]$Restore)
$ErrorActionPreference='Stop'
$family='Microsoft.XboxGamingOverlay_8wekyb3d8bbwe'
$name='InvokeToHomeWidget'
$folder=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\GameBarBackup'
$backup=Join-Path $folder 'start-page-before.json'
if($Restore -and !(Test-Path -LiteralPath $backup -PathType Leaf)){return}
# This is the Game Bar's native preference, accessed through the Windows app-data API.
# No private registry guesses, package hive loading or Game Bar binary changes.
$data=[Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime]::CreateForPackageFamily($family)
$values=$data.LocalSettings.Values
if($Restore){
    $saved=Get-Content -LiteralPath $backup -Raw -Encoding UTF8|ConvertFrom-Json
    if($saved.family -ne $family -or $saved.name -ne $name){throw 'Unexpected Game Bar preference backup'}
    if($saved.valueExists -isnot [bool] -or ($saved.valueExists -and $saved.value -isnot [bool])){
        throw 'Invalid saved Game Bar preference'
    }
    $current=$values[$name]
    if($current -is [bool] -and !$current){
        if($saved.valueExists){$values.set_Item($name,[bool]$saved.value)}
        else{$null=$values.Remove($name)}
        $data.SignalDataChanged()
        if(($saved.valueExists -and $values[$name] -ne $saved.value) -or
            (!$saved.valueExists -and $null -ne $values[$name])){
            throw 'Windows did not confirm restoration of the Game Bar preference'
        }
    }
    Remove-Item -LiteralPath $backup -Force
    return
}
$current=$values[$name]
if($null -ne $current -and $current -isnot [bool]){throw 'Unexpected Game Bar start-page preference type'}
if(!(Test-Path -LiteralPath $backup -PathType Leaf)){
    $null=New-Item -ItemType Directory $folder -Force
    @{family=$family;name=$name;valueExists=($null -ne $current);value=$current}|ConvertTo-Json|Set-Content -Encoding UTF8 $backup
} elseif($null -eq $current -or $current){
    # A newer user or Windows preference supersedes the previous setup write.
    return
}
$values.set_Item($name,$false)
$data.SignalDataChanged()
if($values[$name] -ne $false){throw 'Game Bar start-page preference did not persist'}
@{forcedHome=$false;preference=$name;originalSaved=$true;restartGameBar=$true}|ConvertTo-Json
