# Join In Progress

Join In Progress lets a new player enter a running multiplayer game at a safe map checkpoint. The joining player chooses a character, then works through an ordered catch-up sequence built from the run's saved `MapPointHistory`. When catch-up is complete, the host broadcasts the player's final state and unlocks the next map choice for everyone.

## How it works

- Every player must have the mod installed; it is gameplay-affecting.
- A new connection is accepted only when every connected player is on the open map, no room/combat action is running, and nobody is traveling.
- Joining during an unfinished room shows a native Join In Progress popup explaining that the party must return to the map first.
- Steam friend lobbies remain discoverable after the run starts. The game's original lobby size still limits the total player count.
- The catch-up sequence preserves act, floor, and within-floor room order.
- The host generates a deterministic personal reward plan for the late player's slot. Combat cards, potion rolls, elite/chest relics, and shop inventories therefore belong to the joining character rather than copying another player.
- Shops use the native merchant generator, including randomized prices and sale cards. Full potion belts can replace an existing potion during catch-up.
- Rest sites offer Rest or Smith; ordinary events show the recorded outcome; completed Ancient rooms offer the late player's generated Ancient relic choices.
- Floor damage follows an original player using the same character when possible, falling back to the reference player. Damage is capped at 1 remaining HP.
- The host validates HP, gold, inventory, Ancient selections, and history against its retained reward plan. Final snapshots include synchronization counters and require acknowledgements from every connected peer before map travel unlocks.

## Known limits

The base game does not record a complete executable recipe for arbitrary event outcomes. Ordinary events therefore copy only compatible logged changes (HP, gold cost, gained/removed/upgraded cards, ordinary relics, and potions). Card transformations/enchantments and relic removal are not reconstructed. This deliberately avoids re-entering historical rooms, which would move the entire party and corrupt the live room stack.

The reward plan is generated before catch-up choices are made, so a relic selected on an early replay floor does not modify later precomputed offers. Relics with custom pickup logic remain unavailable because their nested screens can advance multiplayer synchronization IDs. Ancient choices are limited to replay-safe relic options; Ancient non-relic options are not reconstructed.

Treasure rooms use a deterministic personal relic for the late player instead of attempting to recreate the original party's historical shared-relic vote.

The mod targets Slay the Spire 2 `v0.107.1` (`59260271`). It patches private multiplayer internals and may need updates when the game changes.

## Build

Set `GODOT_EXE` in the repository root `.env`, then run:

```powershell
./build.ps1
```

Artifacts are written to `JoinInProgress.pck` and `bin/Debug/JoinInProgress.dll`.
