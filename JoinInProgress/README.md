# Join In Progress

Join In Progress lets a new player enter a running multiplayer game at a safe map checkpoint. The joining player chooses a character, then works through an ordered catch-up sequence built from the run's saved `MapPointHistory`. When catch-up is complete, the host broadcasts the player's final state and unlocks the next map choice for everyone.

## How it works

- Every player must have the mod installed; it is gameplay-affecting.
- A new connection is accepted only when every connected player is on the open map, no room/combat action is running, and nobody is traveling.
- Steam friend lobbies remain discoverable after the run starts. The game's original lobby size still limits the total player count.
- The catch-up sequence preserves act, floor, and within-floor room order.
- Catch-up prefers the recorded history of an original player using the character selected by the joiner, falling back to the first original player only when that character was not already in the party.
- Combat rooms present recorded card, relic, and potion choices; rest sites offer Rest or Smith; shops use the recorded stock with base prices; chests present recorded relics; events show the recorded event and let the joiner follow or pass on compatible logged changes.
- At each floor boundary, the joiner takes the largest `DamageTaken` value recorded for any original party member on that floor. Damage is capped at 1 remaining HP so catch-up can always reach the next room.
- Gold gains/losses from the reference party member are replayed. Shop spending is determined by the joiner's own purchases.
- The host validates the submitted HP, gold, deck, relics, and floor history against the recorded offers before accepting it. A canceled or disconnected join is rolled back on every peer so it cannot leave the map locked.
- Personal reward/shop RNG counters are advanced to the reference player's progress, and recorded relic offers are removed from the joiner's personal relic bag before normal play resumes.

## Known limits

The base game records event choice text but not every unchosen option, and it records shop stock without its rolled price. Events therefore copy only compatible logged changes (HP, gold cost, gained/removed/upgraded cards, ordinary relics, and potions) rather than executing the original event code. Card transformations/enchantments and relic removal are not reconstructed. Catch-up shops use canonical base prices, and unpurchased historical merchant relics cannot be inferred from the log. This deliberately avoids re-entering historical rooms, which would move the entire party and corrupt the live room stack.

Relics with any custom pickup logic are shown as unavailable during catch-up. Those relics can open nested reward/card-selection screens or enqueue actions whose multiplayer synchronization IDs cannot safely be replayed from run history; the joiner can choose another recorded relic or skip that reward.

The mod targets Slay the Spire 2 `v0.107.1` (`59260271`). It patches private multiplayer internals and may need updates when the game changes.

## Build

Set `GODOT_EXE` in the repository root `.env`, then run:

```powershell
./build.ps1
```

Artifacts are written to `JoinInProgress.pck` and `bin/Debug/JoinInProgress.dll`.
