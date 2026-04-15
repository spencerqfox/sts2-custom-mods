# sts2-custom-mods

Personal repo of Slay the Spire 2 gameplay mods. Two mods live here today: **Fog of War** and **Frozen Hand**. Both are DLL + PCK mods built against Godot 4.5.1 Mono and require [BaseLib](https://github.com/Alchyr/BaseLib-StS2).

## Mods

### Fog of War

![Fog of War screenshot](FogOfWar/FogOfWarScreenshot.png)

Hides map nodes and path lines. On any given map, only the act boss, nodes already traveled, the current node, and the current node's direct children are visible. Path lines are only drawn when both of their endpoints are visible. See [FogOfWar/README.md](FogOfWar/README.md).

### Frozen Hand

![Frozen Hand screenshot](FrozenHand/FrozenHandScreenshot.png)

Adds a custom Ancient relic, *Frozen Hand*, offered as an extra choice at Neow. When taken, it snapshots the player's hand, draw, discard, and exhaust at the end of each combat and restores them — in the exact same order — at the start of the next combat, instead of the normal shuffle. See [FrozenHand/README.md](FrozenHand/README.md).

## Repo Layout

```
sts2-custom-mods/
├── sts2-custom-mods.sln     # solution (also refs .tmp/ModTemplate, gitignored)
├── .env.example             # template for repo-root .env (GODOT_EXE)
├── FogOfWar/                # Fog of War mod
└── FrozenHand/              # Frozen Hand mod
```

## Toolchain

Both mods are pinned to:

- `Godot.NET.Sdk/4.5.1` — **do not** upgrade to 4.6.x; the game ships on 4.5.1.
- `net9.0`
- `Alchyr.Sts2.BaseLib` `0.1.*`
- Game references: `sts2.dll`, `0Harmony` (picked up from the Slay the Spire 2 install)

Each project imports `Sts2PathDiscovery.props`, which auto-discovers the Slay the Spire 2 install on Windows, Linux, and macOS. If discovery fails, override `Sts2DataDir` in the project's `Directory.Build.props`. **Builds hard-fail if `Sts2DataDir` cannot be resolved** — there is no silent fallback.

## Building

Each mod has its own `build.ps1`. There is no top-level build.

1. Copy `.env.example` to `.env` at the repo root and set `GODOT_EXE` to your Godot 4.5.1 Mono **console** executable (the `..._console.exe` binary — stdout is visible, which matters for export debugging).
2. From the mod directory, run `./build.ps1`. The script:
   - runs `dotnet build` (sourced only from your local NuGet cache — a cold machine may need a manual restore first),
   - calls Godot `--export-pack` to produce the `.pck`,
   - cleans up `.godot/`, `obj/`, and any stray files in `bin/Debug/`.

Artifacts land at `<Mod>/<Mod>.pck` and `<Mod>/bin/Debug/<Mod>.dll`. Both are gitignored.

## Installing

Installation is intentionally separate from build. Each mod goes into its own subfolder under the game's `mods/` directory:

```
<Slay the Spire 2 install>/mods/<ModId>/
```

On a default Windows Steam install that's `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\<ModId>\`. The game discovers mods by scanning that directory and reading each `mod_manifest.json` — there is no registry or config file to update. Each mod folder should contain:

- `<ModId>.dll`, `<ModId>.pdb`, `<ModId>.deps.json` (from `bin/Debug/`)
- `<ModId>.pck` (from the project root)
- `mod_manifest.json` and `mod_image.png` (from the project root)

Each mod has an `install.ps1` that copies the freshly-built artifacts into the game's `mods/<ModId>/` folder. From the mod's project directory:

```powershell
./install.ps1
```

The script derives the mod name from the folder, verifies all six artifacts exist, then copies them. If a build artifact is missing it fails loudly and tells you to run `./build.ps1` first. Hardcoded to the default Windows Steam install path — edit the `$sts2` line in the script for a non-default install.

Both `mod_manifest.json` files declare `has_pck`, `has_dll`, `dependencies: ["BaseLib"]`, and `affects_gameplay: true`. **BaseLib must already be installed** in `<Slay the Spire 2 install>/mods/BaseLib/` before these mods will load. (Fog of War lists BaseLib even though its current implementation is pure Harmony — see its README.)

## Maintenance Notes

Both mods reach into private game internals via Harmony (`AccessTools.FieldRefAccess`, `AccessTools.Method`) and patch specific game methods. Game updates can break either mod without any change. The specific surfaces each mod depends on are documented per-mod.
