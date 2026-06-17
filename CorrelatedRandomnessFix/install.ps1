$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$mod  = Split-Path -Leaf $root
$sts2 = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
$dest = Join-Path $sts2 "mods\$mod"

$files = @(
    "bin\Debug\$mod.dll",
    "bin\Debug\$mod.pdb",
    "bin\Debug\$mod.deps.json",
    "$mod.pck",
    'mod_manifest.json',
    'mod_image.png'
) | ForEach-Object { Join-Path $root $_ }

foreach ($f in $files) {
    if (-not (Test-Path $f)) { throw "Missing build artifact: $f. Run ./build.ps1 first." }
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $files $dest -Force

Write-Host ""
Write-Host "Installed $mod." -ForegroundColor Green
Write-Host "  dest: $dest"
