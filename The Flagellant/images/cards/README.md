# Card art

Drop card portrait PNGs here. They get packed into the mod at `res://images/cards/<name>.png`
and picked up by `FlagellantCard`'s default art naming convention.

- **Format:** PNG, landscape, **1000x760**.
- **Default name:** `flagellant<classname>.png`, lowercased. For example, `Flog` uses
  `flagellantflog.png`, and `FlagellantStrike` uses `flagellantstrike.png`.
- Override `CardArtImagePath` only when a card needs a custom filename.

If the expected PNG is missing the card falls back to the default beta placeholder, so the
mod still builds and runs without art.
