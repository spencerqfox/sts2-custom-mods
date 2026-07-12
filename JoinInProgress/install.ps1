$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$modName = Split-Path $root -Leaf
$sts2 = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
$destination = Join-Path $sts2 "mods\$modName"
$artifacts = @(
    "$root\bin\Debug\$modName.dll",
    "$root\bin\Debug\$modName.pdb",
    "$root\bin\Debug\$modName.deps.json",
    "$root\$modName.pck",
    "$root\mod_manifest.json",
    "$root\mod_image.png"
)

foreach ($artifact in $artifacts) {
    if (-not (Test-Path $artifact)) {
        throw "Missing artifact: $artifact. Run .\build.ps1 first."
    }
}

New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item $artifacts -Destination $destination -Force
Write-Host "Installed $modName to $destination" -ForegroundColor Green
