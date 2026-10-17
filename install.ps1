param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll = Join-Path $here 'Mods\SpellBrigadeGainMultipliers.dll'
$gameName = 'The Spell Brigade'

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

function Get-SteamRoots {
    $roots = New-Object System.Collections.Generic.List[string]

    try {
        $steamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steamPath) { $roots.Add(($steamPath -replace '/', '\')) }
    } catch { }

    foreach ($p in 'C:\Program Files (x86)\Steam', 'C:\Program Files\Steam', 'D:\Steam', 'D:\SteamLibrary', 'E:\SteamLibrary', 'F:\SteamLibrary') {
        if (Test-Path $p) { $roots.Add($p) }
    }

    $libraries = New-Object System.Collections.Generic.List[string]
    foreach ($root in @($roots)) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path $vdf)) { continue }

        foreach ($match in Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"') {
            $libraries.Add(($match.Matches[0].Groups[1].Value -replace '\\\\', '\'))
        }
    }

    foreach ($lib in $libraries) { $roots.Add($lib) }
    return $roots
}

function Find-Game {
    foreach ($root in Get-SteamRoots) {
        foreach ($candidate in @((Join-Path $root ('steamapps\common\' + $gameName)), (Join-Path $root $gameName))) {
            if (Test-Path (Join-Path $candidate 'TheSpellBrigade.exe')) { return $candidate }
        }
    }
    return $null
}

Write-Host ''
Write-Host '  Spell Brigade - Gain Multipliers / Stat Editor' -ForegroundColor Cyan
Write-Host '  安装器' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $dll)) {
    Write-Host "  找不到 Mod 文件：$dll" -ForegroundColor Red
    Write-Host '  请保持压缩包解压后的目录结构（Mods 子文件夹里应有 dll）。'
    exit 1
}

if (-not $GameDir) { $GameDir = Find-Game }

if (-not $GameDir) {
    Write-Host '  没能自动找到游戏目录。' -ForegroundColor Yellow
    Write-Host '  请把 The Spell Brigade 文件夹路径粘贴进来（该文件夹里应有 TheSpellBrigade.exe）：'
    $GameDir = Read-Host '  路径'
}

$GameDir = $GameDir.Trim().Trim('"')

if (-not (Test-Path (Join-Path $GameDir 'TheSpellBrigade.exe'))) {
    Write-Host "  路径无效：$GameDir 里没有 TheSpellBrigade.exe" -ForegroundColor Red
    exit 1
}

Write-Host "  游戏目录: $GameDir"

if (-not (Test-Path (Join-Path $GameDir 'MelonLoader'))) {
    Write-Host '  警告：该目录没有 MelonLoader，本 Mod 需要先装 MelonLoader。' -ForegroundColor Yellow
    $answer = Read-Host '  仍然继续？(y/N)'
    if ($answer -notmatch '^[yY]') { exit 1 }
}

$mods = Join-Path $GameDir 'Mods'
if (-not (Test-Path $mods)) { New-Item -ItemType Directory -Path $mods | Out-Null }

$target = Join-Path $mods 'SpellBrigadeGainMultipliers.dll'

try {
    Copy-Item $dll $target -Force
} catch {
    if (-not (Test-Admin)) {
        Write-Host '  需要管理员权限（游戏在 Program Files 下），正在申请…' -ForegroundColor Yellow
        $script = $MyInvocation.MyCommand.Path
        Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-GameDir', "`"$GameDir`"")
        exit
    }
    throw
}

Write-Host ''
Write-Host "  安装完成: $target" -ForegroundColor Green
Write-Host ('  SHA256: ' + (Get-FileHash $target -Algorithm SHA256).Hash)
Write-Host ''
Write-Host '  进游戏后按 F8 打开面板。'
