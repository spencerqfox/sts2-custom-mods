# Door Remaker

![Door Remaker screenshot](DoorRemakerScreenshot.png)

A Slay the Spire 2 mod for reworking the **Doormaker** boss fight in Act 3.

## Current Status

DoorRemaker currently implements a Hunger-phase redesign and leaves the rest of the Doormaker fight vanilla:

- The project builds as a normal DLL + PCK mod.
- The mod now patches only `HungerPower` behavior for Doormaker.
- Scrutiny, Grasp, move order, HP, and the rest of the fight remain unchanged.
- `mod_image.png` is currently a temporary placeholder copied from other mods so packaging and install scripts can stay standard.

## Current DoorRemaker Behavior

### Hunger Scaling Rule

DoorRemaker leaves Doormaker's phase loop intact, but changes what Hunger does:

- The first time Hunger becomes active in a combat, the quota is `1`.
- The second time Hunger becomes active in the same combat, the quota is `3`.
- The third time Hunger becomes active, the quota is `5`.
- Each later Hunger phase continues increasing by `2`.

### Devoured Counter Behavior

While Hunger is active, qualifying player cards still receive vanilla `Devoured`, but the mod no longer adds the `Exhaust` keyword directly.

- Only Devoured **Attack** and **Skill** plays count against the Hunger quota.
- Manual plays, auto-plays, and replay iterations all count if they are qualifying Devoured Attack/Skill plays.
- The player receives a visible **Devoured** counter power.
- That counter shows how many qualifying cards can still be forced to exhaust on the current turn.
- The counter resets to the full Hunger quota at the start of each affected player turn while Hunger remains active.
- When the counter reaches `0`, the mod clears `Devoured` from currently affected cards for the rest of that turn, so the card overlay and Devoured hover entry disappear until the next affected player turn.
- A quota charge is only spent when the mod actually changes the played card's result pile to `Exhaust`.

### What Stays Vanilla

- Hunger still applies only through Doormaker's normal phase rotation.
- Newly-entered qualifying Attack/Skill cards still receive vanilla `Devoured` while Hunger is active.
- When Hunger ends, the mod still removes the Hunger-specific effect from player cards by clearing `Devoured`.
- Scrutiny and Grasp are unchanged.

## Current State Of The Vanilla Fight

This section documents the existing game behavior from decompiled game code, so future DoorRemaker changes can be measured against the actual baseline.

### Encounter Structure

- Act 3 is implemented by `Glory`.
- `Glory.BossDiscoveryOrder` includes `QueenBoss`, `TestSubjectBoss`, and `DoormakerBoss`.
- `DoormakerBoss` is a boss-room encounter with custom BGM `event:/music/act3_boss_queen`.
- `DoormakerBoss.GenerateMonsters()` creates a **single-monster** fight containing only `Doormaker`.
- `DoormakerBoss.ExtraAssetPaths` includes the `Devoured` overlay asset, which matches one of the boss's rotating phase mechanics.

### Opening State

- `Doormaker.Title` returns the localized `DOOR.name` while the portal is closed, and only switches to the Doormaker name after opening.
- `Doormaker.AfterAddedToRoom()` stores the monster's original HP in a private field, then sets max and current HP to `999999999` and enables `ShowsInfiniteHp`.
- The boss's real HP is fixed at `489`, or `512` on `AscensionLevel.ToughEnemies`.
- Because `AfterAddedToRoom()` runs before the first move, the fight begins with the monster visually presenting as a door with effectively infinite-looking HP even though its real HP is preserved off to the side.

### Move Order

`Doormaker.GenerateMoveStateMachine()` defines four move states, but only one of them is an opener. The sequence is fixed:

`DramaticOpenMove -> HungerMove -> ScrutinyMove -> GraspMove -> HungerMove -> ScrutinyMove -> GraspMove -> ...`

- The first move is always `DramaticOpenMove`, and its intent is `SummonIntent`.
- `HungerMove` uses `SingleAttackIntent`.
- `ScrutinyMove` uses `SingleAttackIntent`.
- `GraspMove` uses `MultiAttackIntent(GraspDamage, 2)` plus a `BuffIntent`.
- There is no random branching in the move state machine shown in `Doormaker.cs`.

### DramaticOpenMove

`DramaticOpenMove()` is the transition from "door" to the actual boss:

- Sets `IsPortalOpen = true`.
- Restores max/current HP to the previously stored original HP.
- Removes every current power on the boss.
- Disables `ShowsInfiniteHp`.
- Applies `HungerPower`.
- Swaps the visual to `monsters/beta/door_maker_placeholder_3.png`.
- Plays the localized line `DOORMAKER.moves.DRAMATIC_OPEN.speakLine`.
- Updates the music parameter `queen_progress` to `1f`.

Mechanically, this means the opener does **not** attack. It converts the fake infinite-HP door presentation into the real combat state and establishes the first phase power.

### Repeating Attack Cycle

#### HungerMove

- Deals `30` damage, or `35` on `AscensionLevel.DeadlyEnemies`.
- Uses attacker animation `"Attack"` and hit FX `"vfx/vfx_attack_blunt"`.
- After attacking, swaps the active phase power to `ScrutinyPower`.
- Then updates the visual to `monsters/beta/door_maker_placeholder_2.png`.

#### ScrutinyMove

- Deals `24` damage, or `26` on `AscensionLevel.DeadlyEnemies`.
- Uses attacker animation `"Attack"` and hit FX `"vfx/vfx_bite"`.
- After attacking, swaps the active phase power to `GraspPower`.
- Then updates the visual to `monsters/beta/door_maker_placeholder_4.png`.

#### GraspMove

- Deals `2 x 10` damage, or `2 x 11` on `AscensionLevel.DeadlyEnemies`.
- Uses attacker animation `"Attack"` and hit FX `"vfx/vfx_attack_blunt"`.
- Applies `StrengthPower` to itself for `3`, or `4` on `AscensionLevel.DeadlyEnemies`.
- After that, swaps the active phase power back to `HungerPower`.
- Then updates the visual to `monsters/beta/door_maker_placeholder_3.png`.

### Rotating Phase Powers

The boss keeps exactly one of three special powers active at a time. `SwapPhasePower<T>()` explicitly removes `HungerPower`, `ScrutinyPower`, and `GraspPower` before applying the next one, so phases do not stack.

#### HungerPower

- `HungerPower.AfterApplied()` iterates all player creatures on the opposing side and afflicts all current Attack and Skill cards with `Devoured`.
- `HungerPower.AfterCardEnteredCombat()` afflicts newly-entered Attack and Skill cards the same way.
- When `Devoured` is applied through this power, if the card does not already have `Exhaust`, the power adds `Exhaust` and records that it did so.
- `HungerPower.AfterRemoved()` clears `Devoured` from affected cards and removes `Exhaust` only when this power was the thing that added it.
- `Devoured.CanAfflictCardType()` only allows the affliction on Attack and Skill cards.

In practice, the hunger phase marks player Attack/Skill cards with `Devoured` and may temporarily force those cards to exhaust if they were not already exhausting.

#### ScrutinyPower

- `ScrutinyPower.ShouldDraw(Player player, bool fromHandDraw)` returns `true` only when `fromHandDraw` is true.
- For non-hand draws, it flashes and returns `false`.

Mechanically, this means the scrutiny phase blocks normal drawing while still allowing draw flows the game categorizes as hand-draws.

#### GraspPower

- `GraspPower.AfterApplied()` iterates all player cards currently in combat and afflicts them with `Weighted`.
- `GraspPower.AfterCardEnteredCombat()` afflicts newly-entered cards with `Weighted`.
- `GraspPower.AfterRemoved()` clears `Weighted` from afflicted cards.
- `Weighted.OnPlay()` makes the player lose energy equal to the affliction amount when the card is played.
- `GraspPower` applies itself with amount `1`, so the current practical effect is that afflicted cards cost **1 extra energy on play**.

### Death Hook

- `Doormaker.AfterDeath()` checks that the dead creature is the boss itself and then updates the music parameter `queen_progress` to `5f`.
- The method does not add any extra death-phase behavior beyond that music update.

## Scaffold Layout

The scaffold currently consists of:

- [Code/ModEntry.cs](Code/ModEntry.cs): mod entry point and `Harmony.PatchAll()` call.
- [Code/Patches/HungerPowerPatches.cs](Code/Patches/HungerPowerPatches.cs): Doormaker-specific Hunger lifecycle patches.
- [Code/Powers/DevouredCounterPower.cs](Code/Powers/DevouredCounterPower.cs): player-side counter power that forces qualifying cards to exhaust.
- [Code/State/HungerCombatStateStore.cs](Code/State/HungerCombatStateStore.cs): combat-scoped Hunger tier tracking.
- [mod_manifest.json](mod_manifest.json), [project.godot](project.godot), [DoorRemaker.csproj](DoorRemaker.csproj), [build.ps1](build.ps1), and [install.ps1](install.ps1): standard project/build/install wiring.
- [DoorRemaker/images/](DoorRemaker/images/) and [DoorRemaker/localization/eng/](DoorRemaker/localization/eng/): empty packaged folders kept so the project matches the repo's usual mod structure.

## Building

See [the root README](../README.md#building) for the full toolchain requirements and `.env` setup. Once `GODOT_EXE` is configured:

```powershell
cd DoorRemaker
./build.ps1
```

This produces `DoorRemaker.pck` and `bin/Debug/DoorRemaker.dll`, then cleans intermediate Godot and MSBuild output. Do **not** upgrade the project to `Godot.NET.Sdk/4.6.x`; the game ships on 4.5.1.

## Installing

Copy the built artifacts into `<Slay the Spire 2 install>/mods/DoorRemaker/`. On a default Windows Steam install that's `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\DoorRemaker\`. **BaseLib must already be installed** in `<Slay the Spire 2 install>/mods/BaseLib/` first, or DoorRemaker will not load.

From the `DoorRemaker/` directory:

```powershell
./install.ps1
```

`install.ps1` copies the DLL, PDB, deps.json, PCK, manifest, and placeholder mod image into `<sts2>/mods/DoorRemaker/`. It assumes the default Windows Steam install path; edit the `$sts2` line in the script if yours differs. Run `./build.ps1` first or the script will fail on missing artifacts.

## Maintenance Notes

DoorRemaker is expected to patch private game internals once implementation begins. The current baseline documentation depends directly on these decompiled types:

- `MegaCrit.Sts2.Core.Models.Acts.Glory`
- `MegaCrit.Sts2.Core.Models.Encounters.DoormakerBoss`
- `MegaCrit.Sts2.Core.Models.Monsters.Doormaker`
- `MegaCrit.Sts2.Core.Models.Powers.HungerPower`
- `MegaCrit.Sts2.Core.Models.Powers.ScrutinyPower`
- `MegaCrit.Sts2.Core.Models.Powers.GraspPower`
- `MegaCrit.Sts2.Core.Models.Afflictions.Devoured`
- `MegaCrit.Sts2.Core.Models.Afflictions.Weighted`

The current Hunger redesign also depends on:

- `MegaCrit.Sts2.Core.Models.CardModel.OnPlayWrapper`
- `MegaCrit.Sts2.Core.Hooks.Hook.ModifyCardPlayResultPileTypeAndPosition`
- `MegaCrit.Sts2.Core.Models.PowerModel`
- BaseLib `CustomPowerModel`

If a game update changes any of those types, this README's documented baseline and current Hunger behavior may need to be re-verified.
