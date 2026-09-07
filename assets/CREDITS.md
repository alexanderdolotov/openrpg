# Asset credits

All art in this project is [Kenney](https://kenney.nl) assets, CC0 1.0
Universal (public domain) — no attribution legally required, noted here
anyway for provenance:

- **Characters** (`characters/characters.png`, 64×288) — cropped from
  [RPG Urban Pack](https://kenney.nl/assets/rpg-urban-pack)'s
  `Tilemap/tilemap_packed.png`. A standard 4-direction character sheet:
  4 columns (facing left/down/up/right, in that order — matching the
  source's own column order exactly, no reshuffling needed) × 3 rows
  PER CHARACTER (a walk cycle within that facing: neutral, mid-step,
  neutral), 16×16 per frame, six color variants stacked vertically (18
  rows total). See `CharacterSpriteBuilder.cs`.
  
  This went through two wrong readings before landing here. First, the
  original crop took only 3 of the sheet's 4 columns (dropping "up"
  entirely) and — the bigger mistake — `CharacterSpriteBuilder`
  treated those 3 columns as walk-animation FRAMES on one fixed row,
  when columns are actually the facing DIRECTION and rows are the walk
  frames within it; reading the sheet sideways like that meant every
  "walk cycle" was really flashing between different characters'
  facings each loop, which read as the character spinning while
  walking rather than walking in a straight line. Re-cropped to all 4
  columns and `CharacterSpriteBuilder` rewritten to key off the correct
  axis (a real per-facing walk cycle, direction picked from the
  dominant axis of movement) rather than `FlipH`-mirroring one fixed
  pose for left/right.
- **World tiles** (`world/*.png`) — cropped from
  [Roguelike/RPG Pack](https://kenney.nl/assets/roguelike-rpg-pack)'s
  `Spritesheet/roguelikeSheet_transparent.png`, 16×16 each unless noted:
  an apple tree (fruited and bare — maps directly to
  `AppleTree.AppleCount > 0`), water, grass, dirt, an oak tree (16×32,
  round canopy, standalone), a pine tree (16×32, conical canopy, yields
  pinecones), a bare/dead tree (16×48, standalone), a plain bush, and a
  bush with berry dots (reused/tinted per-type — see `Bush.cs` — for
  blueberry/blackberry/raspberry, since the source sheet only has one
  berry-dot color, not three distinct ones).
- **Pond** (`world/pond.png`, 48×48) and **grass patch**
  (`world/grass_patch.png`, 48×48) — also cropped from the Roguelike/RPG
  Pack sheet above, each a premade 3×3-tile shape (a round pond with a
  sandy shore, and a round grass mound) rather than single 16×16 tiles.
  Both replace earlier procedurally-drawn stand-ins (a hand-drawn
  12-point polygon shoreline for the pond, a handful of code-drawn
  blades for the grass) that read as faceted/thin rather than like
  actual ground features — see `Lake.cs`/`GatherableFoliage.cs` for
  where they're used. Stitched from three 16×16 tiles each (not a
  direct 50×50 crop) — the sheet has a 1px transparent margin between
  tiles, and a straight crop across that margin left thin gap lines
  cutting through the middle of both sprites once scaled up in-game.
  The pond crop's corner background (the sheet draws it pre-composited
  onto its own patch of grass, a different shade than this project's
  own ground) was flood-filled to transparent from all four edges so it
  drops onto any ground without a color-
  mismatched square behind the shoreline; grass_patch.png was already
  transparent outside its rounded shape as cropped.

Both packs' original zips (with every tile, not just what's used here)
aren't checked in — only the specific crops actually referenced by code
are. Re-crop from the source packs if more tiles are ever needed; the
tile pitch in both is 16×16 (Roguelike/RPG Pack has a 1px margin between
tiles, RPG Urban Pack's packed tilemap doesn't).
