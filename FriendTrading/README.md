# Friend Trading

A Slay the Spire 2 multiplayer mod that adds reciprocal card and relic trading at rest sites.

## What It Does

When a run has more than one living player, rest sites gain two extra options:

- **Trade Card**: choose another player and one removable card from your deck.
- **Trade Relic**: choose another player and one tradable relic.

Trades resolve only when both players choose the same trade type and target each other. Relic trades use the game's `RelicModel.IsTradable` rule and explicitly reject relics with `HasUponPickupEffect`, so one-time pickup relics are not eligible.

## How It Works

Friend Trading is a Harmony mod with no custom game scenes:

- [Code/ModEntry.cs](Code/ModEntry.cs) applies Harmony patches on load.
- [Code/Patches/RestSiteTradeOptionPatches.cs](Code/Patches/RestSiteTradeOptionPatches.cs) appends the two rest-site options in multiplayer and clears stale pending trades when a rest site begins.
- [Code/RestSite](Code/RestSite) contains the option implementations, target selection, and reciprocal trade coordinator.

The implementation uses the base game's multiplayer-safe `PlayerChoiceSynchronizer`, `CardSelectCmd.FromDeckGeneric`, `RelicSelectCmd.FromChooseARelicScreen`, `CardPileCmd`, and `RelicCmd` APIs.

## Building

See [the root README](../README.md#building) for the full toolchain requirements and `.env` setup. Once `GODOT_EXE` is configured:

```powershell
cd FriendTrading
./build.ps1
```

This produces `FriendTrading.pck` and `bin/Debug/FriendTrading.dll`.

## Installing

From the `FriendTrading/` directory:

```powershell
./install.ps1
```

`install.ps1` copies the DLL, PDB, deps.json, PCK, manifest, and mod image into `<sts2>/mods/FriendTrading/`.
