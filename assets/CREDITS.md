# Asset credits

All art in this project is [Kenney](https://kenney.nl) assets, CC0 1.0
Universal (public domain) — no attribution legally required, noted here
anyway for provenance:

- **Characters** (`characters/characters.png`) — cropped from
  [RPG Urban Pack](https://kenney.nl/assets/rpg-urban-pack)'s
  `Tilemap/tilemap_packed.png`. Six character color variants, 3 walk
  frames each, 16×16 per frame, laid out as 3 columns × 18 rows (3 rows
  per character). See `CharacterSpriteBuilder.cs`.
- **World tiles** (`world/*.png`) — cropped from
  [Roguelike/RPG Pack](https://kenney.nl/assets/roguelike-rpg-pack)'s
  `Spritesheet/roguelikeSheet_transparent.png`, 16×16 each: an apple
  tree (fruited and bare — maps directly to `AppleTree.AppleCount > 0`),
  water, grass, dirt.

Both packs' original zips (with every tile, not just what's used here)
aren't checked in — only the specific crops actually referenced by code
are. Re-crop from the source packs if more tiles are ever needed; the
tile pitch in both is 16×16 (Roguelike/RPG Pack has a 1px margin between
tiles, RPG Urban Pack's packed tilemap doesn't).
