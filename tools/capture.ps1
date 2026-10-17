param(
    [string]$Name = '',
    [string]$OutDir = '',
    [int]$Delay = 5,
    [string]$Rect = '',
    [switch]$NoCountdown
)

# Simple screen grabber used to produce README screenshots.
# Run it while the game (or installer) is on screen:
#   pwsh -File tools\capture.ps1 -Name panel-multipliers -Delay 5
# Optional region: -Rect "100,100,900,700" (x,y,w,h)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not $OutDir) {
    $OutDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\screenshots'
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not $Name) {
    $Name = 'shot-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
}

if (-not $Name.EndsWith('.png', [StringComparison]::OrdinalIgnoreCase)) {
    $Name += '.png'
}

$path = Join-Path $OutDir $Name

if (-not $NoCountdown) {
    for ($i = $Delay; $i -gt 0; $i--) {
        Write-Host "  $i..."
        Start-Sleep -Seconds 1
    }
}

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

if ($Rect) {
    $parts = $Rect.Split(',') | ForEach-Object { [int]$_.Trim() }
    if ($parts.Count -ne 4) { throw '-Rect 需要 "x,y,w,h" 四个整数' }
    $x = $parts[0]; $y = $parts[1]; $w = $parts[2]; $h = $parts[3]
} else {
    $x = $screen.X; $y = $screen.Y; $w = $screen.Width; $h = $screen.Height
}

$bitmap = New-Object System.Drawing.Bitmap $w, $h
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

try {
    $graphics.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

$file = Get-Item $path
Write-Host ''
Write-Host ("  已保存: " + $file.FullName) -ForegroundColor Green
Write-Host ("  尺寸: " + $w + 'x' + $h + '  ' + [Math]::Round($file.Length / 1KB, 1) + ' KB')
