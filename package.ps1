param(
    [string]$Version = '2.5.0'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dll = Join-Path $root 'out\SpellBrigadeGainMultipliers.dll'
if (-not (Test-Path $dll)) { throw "先运行 build.ps1（找不到 $dll）" }

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "SpellBrigadeGainMultipliers-$Version"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Mods') | Out-Null

Copy-Item $dll (Join-Path $stage 'Mods\SpellBrigadeGainMultipliers.dll')
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'README.en.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'docs\screenshots') | Out-Null
Copy-Item (Join-Path $root 'docs\screenshots\*.png') (Join-Path $stage 'docs\screenshots')
Copy-Item (Join-Path $root 'install.ps1') $stage
Copy-Item (Join-Path $root 'uninstall.ps1') $stage
Copy-Item (Join-Path $root '安装.bat') $stage
Copy-Item (Join-Path $root '卸载.bat') $stage

$zip = Join-Path $dist "SpellBrigadeGainMultipliers-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

# also keep a flat copy of the dll for people who just want to drop it in
Copy-Item $dll (Join-Path $dist 'SpellBrigadeGainMultipliers.dll') -Force

'zip: ' + $zip
'  size: ' + [Math]::Round((Get-Item $zip).Length / 1KB, 1) + ' KB'
'  sha256: ' + (Get-FileHash $zip -Algorithm SHA256).Hash


