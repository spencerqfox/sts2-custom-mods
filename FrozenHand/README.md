# Frozen Hand

![Frozen Hand screenshot](FrozenHandScreenshot.png)

A Slay the Spire 2 mod that adds a custom **Ancient** relic, *Frozen Hand*, which persists your hand, draw, discard, and exhaust piles across combats.

## What It Does

When you own Frozen Hand, combat setup is different:

- **End of each combat**, the relic takes a snapshot of hand, draw, discard, and exhaust. Order is preserved exactly. Any cards still sitting in the play pile are folded back into discard or exhaust according to their normal end-of-combat rules before the snapshot is stored.
- **Start of the next combat**, the normal combat setup is short-circuited. Frozen Hand restores the four piles from its snapshot in the same order they were saved. Any cards that are in your deck but not represented in the snapshot (e.g., cards added to your deck between combats) are appended to the draw pile so the deck and the restored state stay consistent.

Upgrades are handled explicitly: snapshots store each card's permanent deck upgrade level at save time, and on restore any delta (upgrades gained between combats) is reapplied so restored cards match their current deck version.

Frozen Hand also creates a **permanent deck version** for any combat-generated Attack, Skill, or Power card that doesn't already have one, so those cards can be snapshotted and restored the same way as normal deck cards. This is a real gameplay implication — temporary cards that would normally vanish at end of combat will stick around in future combats via the snapshot system.

## How You Get It

Frozen Hand is not obtained through the normal relic flow. Its rarity is Ancient, but `IsAllowed()` returns false, so the game's own relic pool will never hand it out. Instead, it is injected as a **4th Neow option** via a Harmony postfix on `Neow.GenerateInitialOptions` (see [Code/Patches/NeowFrozenHandOptionPatch.cs](Code/Patches/NeowFrozenHandOptionPatch.cs)). The first three Neow options are randomized as usual; Frozen Hand is appended as a guaranteed fourth pick.

The injection is gated on two conditions:

- The run must have no `RunState.Modifiers` (daily-run style modifiers such as `Flight` or anything that clears the starting deck). **Ascension level does not count** — `AscensionLevel` is a separate field on `RunState`, so Frozen Hand is available at any ascension.
- The player must not already own Frozen Hand.

If either check fails, the option is not added.

## How It Works

- [Code/Relics/FrozenHandRelic.cs](Code/Relics/FrozenHandRelic.cs) — the relic itself, a BaseLib `CustomRelicModel`. Pile state is stored directly on the relic via `[SavedProperty]`: a `bool FrozenHandHasStoredPiles` flag and a `string FrozenHandStoredSnapshotJson` serialized snapshot. State is **relic-bound save data** — there is no separate save system.
- [Code/Relics/FrozenHandCardStateReflection.cs](Code/Relics/FrozenHandCardStateReflection.cs) — the snapshot data model. Each snapshotted card records its deck index and the permanent deck upgrade level at save time; restore uses those to find the current deck card and reapply any upgrade delta.
- [Code/Patches/PlayerPopulateCombatStatePatch.cs](Code/Patches/PlayerPopulateCombatStatePatch.cs) — patches `Player.PopulateCombatState`. If Frozen Hand has a stored snapshot, this patch **returns false** to skip the game's normal combat setup, and the relic restores piles itself.
- [Code/Patches/NCombatUiActivatePatch.cs](Code/Patches/NCombatUiActivatePatch.cs) — after a restore, walks the restored hand and creates any missing `NCard` UI nodes via `NCard.Create()`. Without this patch, restored hand cards exist in model state but have no visual.
- [Code/Patches/NeowFrozenHandOptionPatch.cs](Code/Patches/NeowFrozenHandOptionPatch.cs) — the Neow injection described above.

**Maintenance note:** Frozen Hand is sensitive to game updates. It patches or reflects into these specific surfaces, and any of them can break on a game patch:

- `Player.PopulateCombatState`
- `NCombatUi.Activate`
- `AncientEventModel.Done`
- Card pile fields and play-pile-destination logic used during save

## Assets

Frozen Hand has real packaged assets:

- [FrozenHand/images/relics/frozen_hand.png](FrozenHand/images/relics/frozen_hand.png) — relic icon
- [FrozenHand/localization/eng/relics.json](FrozenHand/localization/eng/relics.json) — relic name and description
- [FrozenHand/localization/eng/ancients.json](FrozenHand/localization/eng/ancients.json) — Neow option strings

At install time MCP currently emits a warning that `FrozenHandRelic` does not match the expected localization prefix pattern in `relics.json`. The warning is cosmetic and does not block build or install.

## Building

See [the root README](../README.md#building) for the full toolchain requirements and `.env` setup. Once `GODOT_EXE` is configured:

```powershell
cd FrozenHand
./build.ps1
```

The script compiles the DLL, exports `FrozenHand.pck` with Godot `--export-pack`, then cleans `.godot/`, `obj/`, and anything in `bin/Debug/` that isn't `FrozenHand.dll`, `FrozenHand.pdb`, or `FrozenHand.deps.json`.

Do **not** upgrade the project to `Godot.NET.Sdk/4.6.x` — the game ships on 4.5.1.

## Installing

Copy the built artifacts into `<Slay the Spire 2 install>/mods/FrozenHand/`. On a default Windows Steam install that's `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\FrozenHand\`. **BaseLib must already be installed** in `<Slay the Spire 2 install>/mods/BaseLib/` first, or FrozenHand will not load.

From the `FrozenHand/` directory:

```powershell
./install.ps1
```

`install.ps1` copies the DLL, PDB, deps.json, PCK, manifest, and mod image into `<sts2>/mods/FrozenHand/`. It assumes the default Windows Steam install path; edit the `$sts2` line in the script if yours differs. Run `./build.ps1` first — `install.ps1` fails fast if any build artifact is missing.

Installed folder should contain `FrozenHand.dll`, `FrozenHand.pdb`, `FrozenHand.deps.json`, `FrozenHand.pck`, `mod_manifest.json`, and `mod_image.png`. The packaged `FrozenHand.pck` is a Godot 4.5.1 export and will include entries like `.godot/imported/frozen_hand.png-*.ctex` and `FrozenHand/images/relics/frozen_hand.png.import` — that's normal; the exported `.pck` contains the imported texture artifacts instead of the raw PNG.

If the relic icon ever shows up blank in-game, inspect the installed `.pck` first before changing any code — the usual cause is a stale `.pck` left in the mods folder from a previous build. Re-run `build.ps1` and re-copy.
