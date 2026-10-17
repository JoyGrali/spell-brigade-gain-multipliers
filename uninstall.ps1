param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
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
Write-Host '  卸载器' -ForegroundColor Cyan
Write-Host ''

if (-not $GameDir) { $GameDir = Find-Game }

if (-not $GameDir) {
    $GameDir = Read-Host '  请粘贴 The Spell Brigade 文件夹路径'
}

$GameDir = $GameDir.Trim().Trim('"')
$target = Join-Path $GameDir 'Mods\SpellBrigadeGainMultipliers.dll'

if (-not (Test-Path $target)) {
    Write-Host "  没有找到已安装的 Mod：$target" -ForegroundColor Yellow
    exit 0
}

try {
    Remove-Item $target -Force
} catch {
    if (-not (Test-Admin)) {
        Write-Host '  需要管理员权限，正在申请…' -ForegroundColor Yellow
        $script = $MyInvocation.MyCommand.Path
        Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-GameDir', "`"$GameDir`"")
        exit
    }
    throw
}

Write-Host "  已卸载: $target" -ForegroundColor Green
Write-Host '  （配置文件 UserData\MelonPreferences.cfg 里的 [GainMultipliers] 段保留，重新装回来会继续沿用。）'
