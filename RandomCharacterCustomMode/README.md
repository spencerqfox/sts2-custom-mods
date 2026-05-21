# Random Character Custom Mode

Adds the normal **Random** character button to custom runs.

## Behavior

- Custom-mode character selection gets the same random character button used by standard singleplayer and multiplayer.
- The button is only shown when at least one lobby player has every normal character unlocked, matching the standard character-select visibility rule.
- When a custom run starts with Random selected, the existing lobby random-resolution path chooses one of the normal characters before run creation.

## Compatibility Notes

This mod patches custom-run setup UI and relies on the game's existing `RandomCharacter` resolution in `StartRunLobby`. Game updates that rename or reshape `NCustomRunScreen` character-selection methods may require patch updates.
