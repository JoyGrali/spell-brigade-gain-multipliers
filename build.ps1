param(
    [string]$GameDir   = 'C:\Program Files (x86)\Steam\steamapps\common\The Spell Brigade',
    [string]$OutDir    = (Join-Path $PSScriptRoot 'out'),
    # csc chokes on non-ASCII paths, so the actual compile happens in an ASCII temp folder.
    [string]$BuildRoot = (Join-Path $env:TEMP 'spellbrigade-gainmults-build')
)

$ErrorActionPreference = 'Stop'

$mlDir  = Join-Path $GameDir 'MelonLoader'
$il2Dir = Join-Path $mlDir 'Il2CppAssemblies'
$net6   = Join-Path $mlDir 'net6'

$refs = New-Object System.Collections.Generic.List[string]

# Only managed assemblies: the .NET runtime folder also contains native binaries
# (coreclr.dll, clrjit.dll, mscordaccore.dll, ...) which carry no managed metadata.
$nativePattern = '^(coreclr|clrjit|clretwrc|dbgshim|hostpolicy|mscordaccore|mscordbi|mscorrc|msquic|Microsoft\.DiaSymReader\.Native|System\.IO\.Compression\.Native)'
function Add-ManagedAssemblies([string]$dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.dll) {
        if ($f.BaseName -match $nativePattern) { continue }
        $refs.Add($f.FullName)
    }
}

Add-ManagedAssemblies (Join-Path $GameDir 'dotnet')
foreach ($n in 'MelonLoader.dll', '0Harmony.dll', 'Il2CppInterop.Runtime.dll', 'Il2CppInterop.Common.dll', 'Il2CppInterop.HarmonySupport.dll') {
    $p = Join-Path $net6 $n
    if (-not (Test-Path $p)) { throw "missing $p" }
    $refs.Add($p)
}
Get-ChildItem $il2Dir -Filter *.dll | ForEach-Object { $refs.Add($_.FullName) }

$seen = @{}
$unique = New-Object System.Collections.Generic.List[string]
foreach ($r in $refs) {
    $name = [System.IO.Path]::GetFileName($r)
    if ($seen.ContainsKey($name)) { continue }
    $seen[$name] = $true
    $unique.Add($r)
}

if (Test-Path $BuildRoot) { Remove-Item $BuildRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null

$refsRsp = Join-Path $BuildRoot 'refs.rsp'
$refLines = $unique | ForEach-Object { '/r:"' + $_ + '"' }
Set-Content -Path $refsRsp -Value $refLines -Encoding ascii

$sources = @()
Get-ChildItem $PSScriptRoot -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\out\\' } | ForEach-Object {
    $dest = Join-Path $BuildRoot $_.Name
    Copy-Item $_.FullName $dest -Force
    $sources += $dest
}

$dllOut = Join-Path $BuildRoot 'SpellBrigadeGainMultipliers.dll'
$csc = 'C:\Program Files\dotnet\sdk\9.0.202\Roslyn\bincore\csc.dll'
if (-not (Test-Path $csc)) { throw "csc not found" }

$cscArgs = @(
    '/nologo',
    '/target:library',
    '/langversion:latest',
    '/nostdlib+',
    '/deterministic',
    "/out:$dllOut",
    "@$refsRsp"
) + $sources

& dotnet $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Copy-Item $dllOut (Join-Path $OutDir 'SpellBrigadeGainMultipliers.dll') -Force
'built: ' + (Join-Path $OutDir 'SpellBrigadeGainMultipliers.dll')
