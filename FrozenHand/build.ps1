$ErrorActionPreference = 'Stop'

$root  = $PSScriptRoot

# Load repo-root .env (if present) so GODOT_EXE and similar can be set per-machine.
$envFile = Join-Path $root '..\.env'
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#=\s][^=]*)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1].Trim(), $Matches[2].Trim(), 'Process')
        }
    }
}

$godot = $env:GODOT_EXE
if (-not $godot) {
    throw "GODOT_EXE is not set. Copy .env.example to .env at the repo root and set GODOT_EXE to your Godot 4.5.1 mono console executable."
}
if (-not (Test-Path $godot)) {
    throw "GODOT_EXE points to a missing file: $godot"
}

$keep  = @('FrozenHand.dll', 'FrozenHand.pdb', 'FrozenHand.deps.json')

dotnet build "$root\FrozenHand.csproj" -c Debug --ignore-failed-sources `
    --source "$env:USERPROFILE\.nuget\packages" -p:ModsPath=
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

& $godot --headless --path $root --export-pack BasicExport "$root\FrozenHand.pck"
if ($LASTEXITCODE -ne 0) { throw "godot export-pack failed ($LASTEXITCODE)" }

Remove-Item -Recurse -Force "$root\.godot" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$root\obj"    -ErrorAction SilentlyContinue

Get-ChildItem $root -Recurse -File -Include '*.import','*.uid' -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$binDebug = Join-Path $root 'bin\Debug'
if (Test-Path $binDebug) {
    Get-ChildItem $binDebug -File -Recurse |
        Where-Object { $_.Name -notin $keep } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Get-ChildItem $binDebug -Directory -Recurse |
        Sort-Object FullName -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Force) } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "  pck: $root\FrozenHand.pck"
Write-Host "  dll: $binDebug\FrozenHand.dll"
