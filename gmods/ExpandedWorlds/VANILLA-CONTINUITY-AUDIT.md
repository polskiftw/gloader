# Expanded Worlds - Vanilla Continuity Audit

Authority: clean Terraria 1.4.5.8 retail decompile produced from the matching retail binary.

This file is the checklist for deciding whether Expanded Worlds is allowed to change a world-generation result. The default answer is **no**.

## Canonical physical size series

Terraria's own dimensions and network-section grid establish the sequence:

| Tier | Preset | Tiles | 200 x 150 sections |
| ---: | --- | ---: | ---: |
| 1 | Small | 4,200 x 1,200 | 21 x 8 |
| 2 | Medium | 6,400 x 1,800 | 32 x 12 |
| 3 | Large | 8,400 x 2,400 | 42 x 16 |
| 4 | XL | 10,600 x 3,000 | 53 x 20 |
| 5 | Huge | 12,600 x 3,600 | 63 x 24 |
| 6 | THICC | 14,800 x 4,200 | 74 x 28 |

Horizontal section deltas continue `+11, +10`; vertical section deltas continue `+4`. This also keeps the same slight aspect-ratio wobble visible at vanilla Medium instead of creating a new aspect-ratio family.

All expanded widths remain categorically Large to vanilla `WorldGen.GetWorldSize()`. The physical dimensions are carried separately only until Terraria begins generation.

## Rule 1: physical formulas remain untouched

No Expanded Worlds patch is allowed to reinterpret a source formula merely because a larger canvas produces a surprising number.

Examples that remain Terraria-owned include:

- `Main.maxTilesX` / `Main.maxTilesY` formulas;
- `WorldGenRange` `WorldWidth`, `WorldHeight`, and `WorldArea` scaling;
- floating-point width/height ratios such as `maxTilesX / 4200.0`;
- integer division such as `maxTilesX / 4200`;
- Jungle, Underground Desert, Hive, cave, ore, track, and other geometry already driven by the physical canvas;
- secret/special-seed branches and their RNG.

There is therefore no Desert/Jungle/Hive/feature-geometry/secret-seed aspect-ratio correction layer.

## Rule 2: explicit size tables continue only when the sequence is unique

The following 1.4.5.8 Small/Medium/Large tables have one obvious arithmetic continuation and are extended mechanically:

| Source rule | Vanilla terms | Continuation rule |
| --- | --- | --- |
| Sky-lake base | 1, 2, 3 | `tier` |
| Statue multiplier | 2, 3, 4 | `tier + 1` |
| Glow Tulips | 2, 4, 6 | `2 * tier` |
| Boulder Pet base quota | 2, 4, 6 | `2 * tier` |
| Spike Cave base | 3, 5, 7 | `2 * tier + 1` |
| Chillet Eggs | 6, 9, 12 | `3 * tier + 3` |
| Dirtiest Block base | 3, 6, 9 | `3 * tier` |
| Dual Dungeon bookshelf minimum | 5, 10, 15 | `5 * tier` |
| Dual Dungeon water-candle minimum | 5, 10, 15 | `5 * tier` |
| Early Dual Dungeon altars | 20, 30, 40 | `10 * tier + 10` |
| Early desert drop traps | 8, 12, 16 | `4 * tier + 4` |
| Early snow drop traps | 6, 10, 14 | `4 * tier + 2` |
| Early cavern drop traps | 4, 6, 8 | `2 * tier + 2` |
| Early pit traps | 4, 8, 12 | `4 * tier` |
| Early biome clumps | 40, 60, 80 | `20 * tier + 20` |
| Flooded-pit quota | 2, 4, 6 | `2 * tier` |
| Shimmer specialized rooms | 2, 4, 6 | `2 * tier` |
| Living Tree rooms | 2, 6, 10 | `4 * tier - 2` |
| Living Mahogany rooms | 2, 6, 10 | `4 * tier - 2` |
| Beehive rooms | 5, 8, 11 | `3 * tier + 2` |
| Crystal rooms | 6, 10, 14 | `4 * tier + 2` |
| Specialized halls | 3, 4, 5 | `tier + 2` |
| Dual Dungeon trap base | 30, 50, 70 | `20 * tier + 10` |
| Dual Dungeon trap RNG exclusive max | 11, 16, 21 | `5 * tier + 6` |

Downstream vanilla modifiers stay downstream. No Traps, Celebration, Remix, Error World, Care Bears, and other seed behavior is not duplicated inside these continuation functions.

## Rule 3: ambiguous tables stop at Large

No interpolation is invented for a table whose first three terms do not determine one unique rule.

Audited examples retained at vanilla Large behavior:

- Early Dual Dungeon Shadow Orb / Crimson Heart quota: `8, 14, 18`;
- Spider specialized rooms: `2, 6, 8`;
- Lihzahrd painting cap: `1, 2, 2 + random`.

## Rule 4: file/network-schema ceilings are not redesigned

`RandomizeTreeStyle` and `RandomizeCaveBackgrounds` visibly progress from two to three to four regions, but Terraria 1.4.5.8 stores exactly three X boundaries and four styles in its current world/runtime/network model. Expanded Worlds therefore retains the Large four-region representation rather than inventing an incompatible format.

## `GetWorldSize()` audit

Every 1.4.5.8 worldgen/runtime use was classified:

- TerrainPass: Small-specific adjustment; expanded worlds naturally take the Large path.
- ExtraSpawnPointManager: Small-specific spacing; expanded worlds naturally take the Large path.
- DungeonGlobalBookshelves: exact `5/10/15`; continued.
- DungeonGlobalGroundFurniture (both implementations): exact `5/10/15`; continued.
- DungeonGlobalEarlyDualDungeonFeatures first switch: only unique sequences continued; ambiguous `8/14/18` retained.
- DungeonGlobalEarlyDualDungeonFeatures flooded-pit switch: exact `2/4/6`; continued.
- DungeonGlobalPaintings: ambiguous; retained.
- DungeonGlobalTraps: exact base/range progressions; continued.
- DualDungeonLayoutProvider specialized rooms: unique sequences continued; Spider `2/6/8` retained.
- DualDungeonLayoutProvider specialized halls: exact `3/4/5`; continued.
- WorldGen Boulder Pet trap: exact `2/4/6`; continued before vanilla No Traps multiplier.
- WorldGen Dirtiest Block: exact `3/6/9`; continued before vanilla Celebration multiplier.
- WorldGen Spike Caves: exact base `3/5/7`; continued before vanilla `Next(2)`.
- WorldGen Chillet Eggs: exact `6/9/12`; continued.
- UI world-size selection: remains vanilla Large categorically; no fake enum value.

Glow Tulips use equivalent explicit physical-width categorization rather than `GetWorldSize`; their `2/4/6` table is continued by the same rule.

## Capacity-only exceptions

Two fixed current-source scratch arrays can be exceeded by otherwise valid canonical expanded generation:

- Floating Island metadata arrays: enlarged only as necessary for Terraria's own worst-case generated record count.
- `WorldGen.heartPos`: enlarged only as necessary for Terraria's own Crimson record count.

No content count or RNG decision is made by the capacity code.

## Non-worldgen support

The remaining patches are mechanical support for a larger legal canvas:

- apply physical dimensions before `clearWorld`;
- allocate tile/map/section storage using Terraria's exact formulas;
- extend the client MapRenderer's fixed render-target ceiling;
- present XL/Huge/THICC names and buttons;
- use vanilla Large's copied-seed category prefix because Terraria exposes only three size categories.

Client and server compile the same generation context, tier continuations, and capacity guards so the same seed/preset does not have two Expanded Worlds rule sets.
