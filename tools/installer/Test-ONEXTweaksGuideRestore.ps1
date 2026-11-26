#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path -LiteralPath $PSScriptRoot) 'Set-DirectGuideButton.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ONEXTweaks-Guide-' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($scratch)
try {
    $entry = Join-Path $scratch 'Set-DirectGuideButton.ps1'
    [IO.File]::Copy($source, $entry)
    [IO.File]::WriteAllText((Join-Path $scratch 'Set-GameBarStartPage.ps1'), 'param([switch]$Restore) if (!$Restore) { throw "Expected delegated restore." }')
    function Invoke-GuideFixture([bool]$OriginalExists, [int]$Original, [int]$Current, $Expected) {
        $overlay = 'HKCU:\Software\ONEXTweaks\Overlay'
        $native = 'HKCU:\Software\Microsoft\GameBar'
        $registry = @{
            $overlay = @{ DirectGuideEnabled=1; NativeNexusDefaultSaved=1; NativeNexusDefaultExists=[int]$OriginalExists; NativeNexusDefault=$Original }
            $native = @{ UseNexusForGameBarEnabled=$Current }
        }
        function Test-Path($LiteralPath) { $registry.ContainsKey($LiteralPath) }
        function Get-Item($LiteralPath) {
            $key = [pscustomobject]@{ Values=$registry[$LiteralPath] }
            $key | Add-Member ScriptMethod GetValue { param($name) $this.Values[$name] }
            $key | Add-Member ScriptMethod GetValueNames { @($this.Values.Keys) }
            return $key
        }
        function Set-ItemProperty($LiteralPath,$Name,$Value,$Type) { $registry[$LiteralPath][$Name]=$Value }
        function New-ItemProperty($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force) { $registry[$LiteralPath][$Name]=$Value }
        function Remove-ItemProperty($LiteralPath,$Name) { $registry[$LiteralPath].Remove($Name) }
        & $entry -Restore
        if ($registry[$native].UseNexusForGameBarEnabled -cne $Expected -or
            $registry[$overlay].DirectGuideEnabled -ne 0 -or $registry[$overlay].ContainsKey('NativeNexusDefaultSaved')) {
            throw 'Guide restoration changed the wrong preference or failed to clear its own record.'
        }
    }
    Invoke-GuideFixture $true 1 0 1
    Invoke-GuideFixture $false 0 0 $null
    Invoke-GuideFixture $true 0 1 1
    'PASS: Guide restore reinstates the original value or absence and preserves later user changes'
} finally { Remove-Item -LiteralPath $scratch -Recurse -Force }
