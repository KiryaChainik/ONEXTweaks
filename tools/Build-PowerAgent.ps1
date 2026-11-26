#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\ONEXTweaks.PowerAgent'
$output = Join-Path $root 'artifacts\PowerAgent'
$null = New-Item -Path $output -ItemType Directory -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath $source -File -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /langversion:5 /codepage:65001 /reference:System.Management.dll /reference:System.Web.Extensions.dll ('/out:' + (Join-Path $output 'ONEXTweaks.PowerAgent.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Power agent build failed' }
Write-Output 'Power agent built for the Windows .NET Framework runtime.'
