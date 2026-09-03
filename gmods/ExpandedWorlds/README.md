# Expanded Worlds

Expanded Worlds adds three larger creation presets to Terraria 1.4.5.8 through gloader without replacing Terraria's world generator.

> **Expanded Worlds supplies a larger vanilla-shaped canvas. Terraria supplies the world.**

## Canonical sizes

The custom sizes continue the numerical pattern established by Terraria's own Small, Medium, and Large dimensions.

| Tier | Name | Tiles | Network sections | Width vs Small | Height vs Small |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Small | `4,200 x 1,200` | `21 x 8` | `1.0000x` | `1.0000x` |
| 2 | Medium | `6,400 x 1,800` | `32 x 12` | `1.5238x` | `1.5000x` |
| 3 | Large | `8,400 x 2,400` | `42 x 16` | `2.0000x` | `2.0000x` |
| 4 | XL | **`10,600 x 3,000`** | **`53 x 20`** | `2.5238x` | `2.5000x` |
| 5 | Huge | **`12,600 x 3,600`** | **`63 x 24`** | `3.0000x` | `3.0000x` |
| 6 | THICC | **`14,800 x 4,200`** | **`74 x 28`** | `3.5238x` | `3.5000x` |

Terraria's network sections are `200 x 150` tiles. Vanilla's section counts are:

```text
horizontal: 21, 32, 42
vertical:    8, 12, 16
```

The canonical continuation is:

```text
horizontal: 21, 32, 42, 53, 63, 74
             +11 +10 +11 +10 +11

vertical:    8, 12, 16, 20, 24, 28
              +4  +4  +4  +4  +4
```

That gives the physical dimensions above. It deliberately preserves the tiny width/height mismatch already present at vanilla Medium instead of introducing a new aspect-ratio family.


## Vanilla-continuity policy

The source authority is the clean Terraria **1.4.5.8 retail decompile from the matching retail binary**.

Expanded Worlds follows four rules:

1. **Physical-dimension formulas remain Terraria's formulas.** If vanilla uses `Main.maxTilesX`, `Main.maxTilesY`, `WorldWidth`, `WorldArea`, `maxTilesX / 4200.0`, `maxTilesY / 1200`, or another physical expression, Expanded Worlds does not reinterpret it.
2. **Exact discrete Small/Medium/Large sequences may continue.** A categorical sequence is extended only when its first three terms define one unambiguous continuation.
3. **Vanilla storage ceilings may grow, but generation behavior may not be replaced.** Capacity patches only prevent valid vanilla generation from overrunning fixed scratch arrays or startup-sized backing storage.
4. **Ambiguous or format-limited rules stay vanilla Large.** If there is no single defensible continuation, or extending it would require changing Terraria's `.wld`/network schema, the Large behavior is retained.

There is no aspect-ratio correction layer anymore. The old Desert, Jungle, Hive, feature-geometry, and secret-seed proxy repairs existed because the previous custom sizes deliberately broke the relationship between width and height. With canonical co-growing sizes, those patches are both unnecessary and less vanilla than simply allowing Terraria's own formulas to see the new dimensions.

## What is not changed

Expanded Worlds does **not**:

- replace `WorldGen.GenerateWorld`;
- replace, reorder, or invent worldgen passes;
- reseed Terraria's world RNG;
- force Snow, Jungle, Dungeon, evil, Desert, or other macro features to a chosen side;
- move or resize completed biomes in a post-generation cleanup pass;
- substitute custom terrain/cave algorithms;
- reinterpret continuous width/height/area formulas;
- introduce a custom `.wld` format;
- introduce fake Terraria `WorldSizeId` enum values.

A seed may therefore produce noticeably different geography at different physical sizes. That is expected: each size is a fresh run of Terraria's generator on a different canvas.

## Terraria still sees Large categorically

Terraria 1.4.5.8 `WorldGen.GetWorldSize()` returns:

- `0` through width `4200`;
- `1` through width `6400`;
- `2` for anything wider.

XL, Huge, and THICC therefore naturally categorize as vanilla **Large**. The New World UI keeps Terraria's private size state at Large and carries the custom physical preset separately until generation starts.

`WorldDimensions.cs` validates the six vanilla `WorldGen.WorldSize*` constants against the audited 1.4.5.8 values before the mod runs. If that source contract changes, Expanded Worlds fails instead of silently guessing.

## Exact discrete continuations

Some Terraria rules are not physical formulas. They are explicit Small/Medium/Large tables. Expanded Worlds continues only clean arithmetic sequences.

| Rule | Small | Medium | Large | XL | Huge | THICC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Sky-lake base | 1 | 2 | 3 | **4** | **5** | **6** |
| Statue multiplier | 2 | 3 | 4 | **5** | **6** | **7** |
| Glow Tulips | 2 | 4 | 6 | **8** | **10** | **12** |
| Boulder Pet base quota | 2 | 4 | 6 | **8** | **10** | **12** |
| Spike Cave base | 3 | 5 | 7 | **9** | **11** | **13** |
| Chillet Eggs | 6 | 9 | 12 | **15** | **18** | **21** |
| Dirtiest Block base | 3 | 6 | 9 | **12** | **15** | **18** |

Terraria's downstream behavior remains downstream:

- No Traps still doubles the Boulder Pet base quota itself;
- Spike Caves still add Terraria's `genRand.Next(2)` itself;
- Celebration still multiplies the Dirtiest Block base by five itself;
- extra-floating-island secret-seed multipliers still apply to the sky-lake base inside Terraria's own Floating Islands pass.

### Dual Dungeon sequences added in 1.4.5

The clean 1.4.5.8 source introduced additional explicit size tables. The unambiguous ones are continued the same way on client and server generation.

| Dual Dungeon rule | Small | Medium | Large | XL | Huge | THICC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Bookshelf minimum | 5 | 10 | 15 | **20** | **25** | **30** |
| Water-candle minimum | 5 | 10 | 15 | **20** | **25** | **30** |
| Early altars | 20 | 30 | 40 | **50** | **60** | **70** |
| Early desert drop traps | 8 | 12 | 16 | **20** | **24** | **28** |
| Early snow drop traps | 6 | 10 | 14 | **18** | **22** | **26** |
| Early cavern drop traps | 4 | 6 | 8 | **10** | **12** | **14** |
| Early pit traps | 4 | 8 | 12 | **16** | **20** | **24** |
| Early biome clumps | 40 | 60 | 80 | **100** | **120** | **140** |
| Flooded-pit quota | 2 | 4 | 6 | **8** | **10** | **12** |
| Shimmer specialized rooms | 2 | 4 | 6 | **8** | **10** | **12** |
| Living Tree rooms | 2 | 6 | 10 | **14** | **18** | **22** |
| Living Mahogany rooms | 2 | 6 | 10 | **14** | **18** | **22** |
| Beehive rooms | 5 | 8 | 11 | **14** | **17** | **20** |
| Crystal rooms | 6 | 10 | 14 | **18** | **22** | **26** |
| Specialized halls | 3 | 4 | 5 | **6** | **7** | **8** |
| Temple-trap base | 30 | 50 | 70 | **90** | **110** | **130** |
| Temple-trap RNG exclusive max | 11 | 16 | 21 | **26** | **31** | **36** |

The ambiguous 1.4.5 sequences are intentionally not extrapolated. Examples include the early Shadow Orb/Crimson Heart quota `8/14/18`, Spider specialized rooms `2/6/8`, and Lihzahrd painting cap `1/2/(2 + Next(2))`.

## Rules intentionally capped by Terraria's file/runtime schema

Two obvious visual-region sequences are **not** extended: tree-background regions and cave-background regions.

Small uses two styles, Medium three, and Large four. However, Terraria's current world format and runtime logic serialize/use exactly three X boundaries plus four styles. Extending those counts would require changing the `.wld` schema, networking, and multiple consumers. That would violate this mod's vanilla-format rule, so expanded worlds use Terraria's normal Large four-region behavior.

This distinction is intentional: a mathematically visible sequence is not enough if continuing it requires inventing a new Terraria data model.

## Continuous worldgen stays vanilla

A large amount of Terraria 1.4.5.8 already scales from the physical canvas and therefore needs no patch. Examples include rules using:

- `Main.maxTilesX` or `Main.maxTilesY` directly;
- `WorldGenRange` with `WorldWidth` or `WorldArea`;
- `maxTilesX / 4200.0` floating-point scale;
- `maxTilesY / 1200` source arithmetic;
- physical tile area.

This includes large portions of terrain, caves, ores, surface chests, Floating Islands, Marble/Granite, cabins/cave houses, minecart tracks, Jungle geometry, Underground Desert geometry, Hive geometry, and numerous secret-seed features.

Those formulas are deliberately **not patched**. The canonical dimensions restore the co-growing width/height relationship they were written against.

Integer expressions remain integer expressions too. For example, if Terraria itself uses integer `maxTilesX / 4200`, an intermediate custom tier may legitimately receive the same integer quantum as the previous tier. Expanded Worlds does not convert such source arithmetic to floating point merely to make every button increase every count.

## Secret and special seeds

Terraria remains authoritative for secret/special seed registration, activation, RNG, and pass behavior. Expanded Worlds does not maintain a replacement secret-seed implementation.

The mod only touches a secret-seed result when an already-identified **discrete vanilla size table** needs an unambiguous next tier or when fixed scratch storage must be large enough for Terraria's own generated record count.

## Fixed worldgen scratch capacity

The current 1.4.5.8 source has two audited fixed record buffers that canonical expanded worlds can exceed.

### Floating Island metadata

Vanilla has 300 records. With the continued sky-lake base and the source's worst Error World + full extra-island multiplier, the upper bounds are:

- XL: **280**
- Huge: **350**
- THICC: **390**

Only the four parallel metadata arrays are enlarged when necessary. Island/lake generation itself stays in Terraria.

### Crimson heart positions

`WorldGen.heartPos` has 100 records. The Remix worst-case bounds are:

- XL: **80**
- Huge: **96**
- THICC: **112**

Only THICC exceeds vanilla capacity. The array grows; Crimson generation does not change.

Current 1.4.5 Dungeon generation uses dynamic `List<T>` state, so Expanded Worlds does not carry the old fixed-Dungeon-array compatibility machinery.

## Backing storage and map renderer

Terraria has world-sized storage created from startup dimensions. Expanded Worlds preserves vanilla formulas while ensuring the storage is large enough for the selected physical canvas:

- `Main.tile` uses exactly `[maxTilesX, maxTilesY]`, matching 1.4.5.8;
- client `WorldMap` is recreated at the exact physical dimensions when necessary;
- section tables use vanilla `maxTilesX / 200 + 1` by `maxTilesY / 150 + 1` sizing;
- already-created `RemoteClient` section tables are enlarged to those same vanilla dimensions;
- static section tables are initialized against maximum supported THICC dimensions.

The client `MapRenderer` has a separate vanilla `5 x 2` render-target ceiling. Canonical THICC needs physical X target index 7 and Y target index 2. Its width has an 800-tile final tail, while vanilla treats the final allocated X target as a special 400-tile tail, so one unused guard column is required. The resulting backing grid is `9 x 3`, while `DrawMap` renders only through physical X index 7. THICC's vertical tail is exactly vanilla's special 600-tile final-row size, so no vertical guard row is needed.

These are storage/rendering accommodations, not world-generation rules.

## Client/server parity

World-size state, physical dimensions, discrete tier continuations, and generation scratch-capacity guards are shared under the same source files for both `GLOADER_CLIENT` and `GLOADER_SERVER` builds.

Headless generation uses:

```powershell
$env:GLOADER_EXPANDED_WORLD='XL'
$env:GLOADER_EXPANDED_WORLD='HUGE'
$env:GLOADER_EXPANDED_WORLD='THICC'
```

Terraria still enters through its normal Large autocreate path; Expanded Worlds replaces only the physical canvas before `clearWorld` allocation/generation begins.

## World files

Expanded Worlds introduces no custom `.wld` format. Terraria writes the real width and height into its normal header.

`WorldMetadata.cs` only fixes presentation around dimensions Terraria does not have names for:

- recognized custom dimensions display as XL/Huge/THICC instead of Unknown;
- copied full seeds retain vanilla Large's size prefix because Terraria's seed format knows only Small/Medium/Large categories.

## CI continuity audit

`tests/ExpandedWorldsMath` locks the canonical dimensions, section cadence, every pure discrete continuation above, the two capacity upper bounds, rejection of the obsolete dimensions, and syntax parsing of every Expanded Worlds source file in both client and server preprocessor modes.

The audit intentionally distinguishes **vanilla physical arithmetic** from **source-backed categorical continuation**. That is the core design rule of the mod.
