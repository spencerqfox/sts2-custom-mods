# Fog of War

![Fog of War screenshot](FogOfWarScreenshot.png)

A small Slay the Spire 2 mod that hides the act map.

## What It Does

At any moment, a map node is shown when **any** of these are true:

- It is the act **boss**.
- It has already been **traveled**.
- It is the player's **current** node.
- It is an **immediate child** of the current node (a legal next step).
- The player has not yet entered the map and the node is on **row 0** (so the first move is visible).

Everything else — future rows, side branches, elite/shop/event markers further down the map — is hidden.

Path lines follow the same rule: a line between two nodes is only drawn when **both** endpoint nodes pass the visibility test. This prevents "floating" lines from leaking information about hidden nodes. See [Code/Patches/MapFogPatches.cs](Code/Patches/MapFogPatches.cs).

## How It Works

Fog of War is a code-only, Harmony-only mod. The whole feature is two source files:

- [Code/ModEntry.cs](Code/ModEntry.cs) — mod entry point, applies Harmony patches on load.
- [Code/Patches/MapFogPatches.cs](Code/Patches/MapFogPatches.cs) — the visibility rule (`FogVisibility.ShouldShow`) and three patches:
  - `NMapPoint.RefreshState` — hides non-visible node visuals.
  - `NNormalMapPoint._Ready` — ensures freshly-created nodes start in the correct visibility state.
  - `NMapScreen.RecalculateTravelability` — hides path lines whose endpoints aren't both visible.

The visibility check reaches into private game state via `AccessTools.FieldRefAccess`:

- `NMapScreen._runState`
- `NMapScreen._paths`
- `NMapScreen._mapPointDictionary`

**Maintenance note:** these field names are not a public API. A game update that renames or restructures any of them — or that changes how `NMapScreen` / `NMapPoint` update their visuals — will break this mod even if nothing here changes. Those three patch targets are the first places to look after a game patch.

## Assets

Fog of War is currently **code-only**. The `FogOfWar/images/` and `FogOfWar/localization/eng/` folders exist (Godot project scaffolding) but are empty. No icons, no strings, no VFX.

## BaseLib Dependency

[mod_manifest.json](mod_manifest.json) lists `"dependencies": ["BaseLib"]` even though the current implementation does not use BaseLib APIs. The dependency is kept so the project stays aligned with the rest of this repo's mods and so we can pull in BaseLib features (localization, settings, etc.) later without a manifest change.

## Building

See [the root README](../README.md#building) for the full toolchain requirements and `.env` setup. Once `GODOT_EXE` is configured:

```powershell
cd FogOfWar
./build.ps1
```

This produces `FogOfWar.pck` (next to the project) and `bin/Debug/FogOfWar.dll`, then cleans intermediates. Do **not** upgrade the project to `Godot.NET.Sdk/4.6.x` — the game ships on 4.5.1.

## Installing

Copy the built artifacts into `<Slay the Spire 2 install>/mods/FogOfWar/`. On a default Windows Steam install that's `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\FogOfWar\`. **BaseLib must already be installed** in `<Slay the Spire 2 install>/mods/BaseLib/` first, or FogOfWar will not load.

From the `FogOfWar/` directory:

```powershell
./install.ps1
```

`install.ps1` copies the DLL, PDB, deps.json, PCK, manifest, and mod image into `<sts2>/mods/FogOfWar/`. It assumes the default Windows Steam install path; edit the `$sts2` line in the script if yours differs. Run `./build.ps1` first — `install.ps1` fails fast if any build artifact is missing.

Installed folder should contain `FogOfWar.dll`, `FogOfWar.pdb`, `FogOfWar.deps.json`, `FogOfWar.pck`, `mod_manifest.json`, and `mod_image.png`. The game discovers the mod by reading `mod_manifest.json` on startup.
