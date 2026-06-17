# Correlated Randomness Fix

A Slay the Spire 2 mod that decorrelates the base game's named RNG streams.

## The Problem

The game routes gameplay randomness through `MegaCrit.Sts2.Core.Random.Rng`, which is backed by `System.Random`.
`System.Random` output is linear in its seed, and the game creates many independent-looking streams as
`new Rng(seed + hash("name"))`. Because those seeds differ by fixed, known offsets, early outputs from
different streams are mathematically related instead of independent. This is the correlated randomness
issue described in [tck.mn's writeup](https://tck.mn/blog/correlated-randomness-sts2/).

## How It Works

This is a Harmony-only mod. [Code/ModEntry.cs](Code/ModEntry.cs) creates a `Harmony` instance and calls
`PatchAll()` on load.

[Code/Patches/RngPatch.cs](Code/Patches/RngPatch.cs) patches the `Rng(uint, int)` constructor. After the
base constructor runs, the patch replaces the private `_random` field with a new `System.Random` seeded
from a nonlinear mix of the original `Rng.Seed`, then replays the existing `Counter` with `Random.Next()`.

This deliberately keeps the game's `System.Random` behavior instead of replacing the PRNG implementation.
That makes the patch small and preserves the game's save/load model, where saved RNG state is reconstructed
from only `Seed` and `Counter` via `FastForwardCounter`.

## Maintenance Notes

The mod reaches into private game internals and is therefore tied to the current shape of `Rng`:

- It writes the private `Rng._random` field via `AccessTools.FieldRefAccess`.
- It assumes `Rng(uint, string)` continues to chain through `Rng(uint, int)`.
- It assumes saved RNG state continues to be represented as `Seed` plus `Counter`.
- This is a pragmatic decorrelation fix, not a full replacement with a modern PRNG. It addresses the
  additive-seed correlation described in the article while keeping the base game's `System.Random` quirks.

## Credits

- Issue analysis: https://tck.mn/blog/correlated-randomness-sts2/

## Building

See [the root README](../README.md#building) for the full toolchain requirements and `.env` setup. Once
`GODOT_EXE` is configured:

```powershell
cd CorrelatedRandomnessFix
./build.ps1
```

This produces `CorrelatedRandomnessFix.pck` (next to the project) and
`bin/Debug/CorrelatedRandomnessFix.dll`, then cleans intermediates. Do not upgrade the project to
`Godot.NET.Sdk/4.6.x`; the game ships on 4.5.1.

## Installing

From the `CorrelatedRandomnessFix/` directory:

```powershell
./install.ps1
```

`install.ps1` copies the DLL, PDB, deps.json, PCK, manifest, and mod image into
`<sts2>/mods/CorrelatedRandomnessFix/`. It assumes the default Windows Steam install path; edit the `$sts2`
line in the script if yours differs. Run `./build.ps1` first; `install.ps1` fails fast if any build artifact
is missing.
