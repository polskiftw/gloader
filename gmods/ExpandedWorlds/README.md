# Expanded Worlds

Expanded Worlds adds two wider world sizes to Terraria 1.4.5.8 through gLoader:

- **XL** — `12,600 x 2,400` tiles (`1.5x` vanilla Large area)
- **Huge** — `16,800 x 2,400` tiles (`2x` vanilla Large area)

The height deliberately remains vanilla Large's `2,400` tiles. The project does not replace Terraria's world generator and does not run a subjective post-generation "make it look bigger" pass.

> **Expanded Worlds supplies the canvas; Terraria supplies the rulebook.**

## Ground truth

The source authority for this branch is the clean Terraria **1.4.5.8** retail decompile made from the matching retail binary. The clean decompile has zero known ILSpy decompilation-error markers from the audit that produced it.

Repository CI independently decompiles the official Terraria 1.4.5.8 dedicated server and asserts source contracts for APIs and worldgen rules shared with retail. Client-only contracts such as the New World UI, `WorldMap`, section tables, and `MapRenderer` were reconciled against the matching retail decompile/binary.

Old 1.4.0.x public decompiles are no longer treated as authority for this branch. Historical compatibility fallbacks may remain in code, but current 1.4.5.8 behavior is explicitly identified as current and kept separate from those fallbacks.

Runtime patches are fail-closed: if a private member or audited IL/source shape no longer matches the expected Terraria build, the patch throws instead of silently inventing a replacement rule.

## Why XL is 12,600 instead of 12,000

Several Terraria rules use `maxTilesX / 4200` as a horizontal world-size scale or, in some places, an integer width quantum. Choosing exact 4,200-tile multiples makes the expanded widths clean continuations of those rules:

```text
Small    4200
Large    8400
XL      12600
Huge    16800
```

`12,000 / 4,200` is still integer quantum `2`, the same as Large. `12,600` is the next exact quantum (`3`), while Huge is quantum `4`.

The widths also divide exactly into Terraria's 200-tile network sections:

```text
Large   8400 / 200 = 42 sections
XL     12600 / 200 = 63 sections
Huge   16800 / 200 = 84 sections
```

## Vanilla category remains Large

Terraria 1.4.5.8 `WorldGen.GetWorldSize()` returns:

- `0` through width `4200`;
- `1` through width `6400`;
- `2` for anything wider.

XL and Huge therefore remain categorically **Large** without introducing a fake fourth or fifth Terraria size enum. That is intentional. Code that asks for Small/Medium/Large sees a legal vanilla value; code that uses the physical world dimensions sees the real expanded canvas.

The New World UI keeps Terraria's own size selection at Large while Expanded Worlds carries the physical XL/Huge selection separately until generation starts.

## Scaling model

Vanilla normally grows width and height together, so source code can sometimes use one axis as a proxy for overall scale. Expanded Worlds breaks that relationship intentionally. When a source rule needs disambiguation, it is classified by what the quantity physically represents:

```text
horizontal geometry/counts = width / 4200
vertical geometry          = height / 1200
area-density counts        = width*height / (4200*1200)
isotropic linear geometry  = sqrt(width*height / (4200*1200))
```

Relative to Small:

| Size | Horizontal | Vertical | Tile area | Isotropic linear |
| --- | ---: | ---: | ---: | ---: |
| Small | 1x | 1x | 1x | 1x |
| Medium | 1.5238x | 1.5x | 2.2857x | 1.5119x |
| Large | 2x | 2x | 4x | 2x |
| XL | 3x | 2x | 6x | 2.4495x |
| Huge | 4x | 2x | 8x | 2.8284x |

This is not a universal multiplier. If vanilla already consumes the correct physical dimension, Expanded Worlds leaves the generator alone.

## Generation lifecycle and backing storage

`Main.cs`:

1. adds XL and Huge to the New World size row;
2. keeps Terraria's categorical selection at Large;
3. arms a custom preset only when `CreateNewWorld` begins;
4. applies the real width and Large height before generation;
5. reapplies the dimensions at `clearWorld` as a safety boundary;
6. updates `WorldFileData` so the in-memory metadata matches the physical canvas;
7. disarms the preset in a `GenerateWorld` finalizer even if generation throws.

Changing `Main.maxTilesX` alone is not enough. Terraria 1.4.5.8 has world-sized storage created from startup dimensions. `WorldStorage.cs` handles the actual canvas/storage contract:

- `Main.tile` is enlarged to logical dimensions plus Terraria's one-tile backing margin;
- client `WorldMap` storage is recreated at the expanded dimensions;
- `ActiveSections.LastActiveTime` and `LeashedEntity.BySection` are given Huge-capable section storage at type initialization;
- client `MapRenderer` target columns and its hard-coded DrawMap X loop are extended far enough to render Huge without treating Huge's real final target as vanilla's special 400-tile tail.

These are storage/rendering changes only. They do not generate content.

## World metadata

No custom `.wld` format is introduced. Terraria's normal world header stores the real physical width and height.

`WorldMetadata.cs` presents the two recognized physical dimension pairs as **XL** and **Huge** instead of `Unknown`. Full seed text intentionally uses the vanilla **Large** size prefix because the seed format only knows the three vanilla categories; the physical XL/Huge choice remains a separate creation choice.

## Source-backed worldgen behavior

### Terraria-native scaling that needs no patch

Terraria 1.4.5.8 already uses the physical dimensions for many rules. Examples include:

- Life Crystals — physical tile area;
- Surface Chests — width;
- Floating Islands — width;
- Marble count — `WorldGenRange` with `WorldArea`;
- Granite count — `WorldGenRange` with `WorldWidth`;
- Cave Houses/Cabins and Cave Chests — `WorldArea`;
- Dead Man's Chests — `WorldWidth`;
- Living Tree micro-biomes — `WorldWidth`;
- minecart track counts/lengths according to the embedded 1.4.5.8 configuration.

The clean embedded configuration resolves the previous minecart uncertainty:

| Minecart rule | 1.4.5.8 source scaling |
| --- | --- |
| StandardTrackCount `4..7` | `WorldArea` |
| StandardTrackLength `150..300` | `WorldWidth` |
| LongTrackCount `1..2` | `WorldWidth` |
| LongTrackLength `400..1000` | `WorldWidth` |

Because `WorldGenRange` reads the real `Main.maxTilesX/maxTilesY`, these automatically continue onto XL/Huge. No custom minecart-length patch is needed.

### Underground Desert

`DesertDescription.CreateFromPlacement` uses one **double** width-derived scalar (`maxTilesX / 4200.0`) for both axes because vanilla widths/heights normally co-grow.

`DesertScaling.cs` preserves Terraria's source arithmetic and RNG, but on expanded aspect ratios it keeps horizontal uses width-driven and changes only the vertical uses to the actual height scale. XL/Huge therefore get a wider Underground Desert while retaining Large-height vertical geometry.

### Jungle

`JungleScaling.cs` preserves the original `JunglePass`, RNG stream, seed branches, and placement logic while separating its overloaded `_worldScale` by dimensional meaning:

- X displacement and horizontal margins -> width;
- Y displacement -> height;
- axis-neutral linear body strength/repetition -> area-equivalent linear scale.

One main Jungle remains one main Jungle unless a secret seed changes that rule.

### Drunk-world Hive tunnel geometry

Clean 1.4.5.8 source is explicit:

```text
num3 = (double)Main.maxTilesX / 4200.0
num3 = (num3 + 1.0) / 2.0
```

The division is **not integer division**. `BeeScaling.cs` preserves that exact result for Small/Medium/Large. Beyond Large, using the raw width-only scalar would incorrectly increase both horizontal and vertical tunnel geometry just because the canvas became wider, so Expanded Worlds continues the axis-neutral tunnel body from Large by the area-equivalent linear factor.

Hive count itself remains Terraria-owned and width-driven. Drunk World's `0.667` hive-count multiplier and larva behavior remain downstream and untouched.

### Don't Starve Wavy Caves

The 1.4.5.8 source derives Wavy Cave count from `(maxTilesX / 4200.0)^2`, which is a valid area proxy only while the axes co-grow. `SecretSeedScaling.cs` preserves vanilla results through Large, then continues the count from Large by actual tile area for wider-only worlds. Remix's vanilla `/3` remains downstream.

### Axis-neutral feature geometry

`FeatureGeometryScaling.cs` repairs source rules such as Neon Moss, Shroom Patch, and PlantAlch where one width-derived linear scalar is applied to geometry that is not purely horizontal. The source generator and RNG remain in control.

## Discrete Small/Medium/Large sequences

Some source rules are genuinely categorical rather than continuous. Expanded Worlds extends only sequences whose next terms are unambiguous and keeps seed multipliers downstream.

| Rule | Small | Medium | Large | XL | Huge |
| --- | ---: | ---: | ---: | ---: | ---: |
| Statue multiplier | 2 | 3 | 4 | **5** | **6** |
| Glow Tulips | 2 | 4 | 6 | **8** | **10** |
| Boulder Pet base quota | 2 | 4 | 6 | **8** | **10** |
| Spike Cave base | 3 | 5 | 7 | **9** | **11** |
| Chillet Eggs | 6 | 9 | 12 | **15** | **18** |
| Dirtiest Block base | 3 | 6 | 9 | **12** | **15** |

Important source details:

- the statue multiplier is calculated in **`WorldGen.Reset()`**, not `GenerateWorld`; the patch runs as a postfix after Terraria has produced Large's value `4` and verifies that boundary before setting `5/6`;
- No Traps doubles the Boulder Pet quota **after** the base 2/4/6 rule, so Expanded Worlds extends only the base and leaves the seed multiplier untouched;
- Spike Caves add vanilla `genRand.Next(2)` after their base count, and that draw remains untouched;
- Celebration multiplies the Dirtiest Block base by five downstream, and that remains untouched.

## Temple

Clean 1.4.5.8 `makeTemple` uses:

```text
scale = (double)Main.maxTilesX / 4200.0
rooms = Next((int)(10*scale), (int)(16*scale))
```

The room arrays are **dynamic** in 1.4.5.8: both are allocated from `roomCount + 10`. There is no modern fixed Temple room buffer to enlarge.

Source room-count ranges are:

| Size | Rooms |
| --- | ---: |
| Small | 10–15 |
| Medium | 15–23 |
| Large | 20–31 |
| XL | **30–47** |
| Huge | **40–63** |

`TempleScaling.cs` validates that modern dynamic allocation shape and leaves it alone. A legacy fixed-array compatibility fallback remains only for older Terraria layouts if that exact old shape is encountered.

## Dungeon

Terraria 1.4.5.8 does **not** use the old fixed 100-room / 500-door Dungeon scratch arrays. Current Dungeon generation stores growing state in per-Dungeon `List<T>` collections (`dungeonRooms`, `dungeonHalls`, `dungeonFeatures`, doors, platforms, protected bounds) plus a dynamic `List<DungeonGenVars>`.

`GenerationCapacity.cs` validates those current 1.4.5.8 list shapes and performs **no Dungeon resize** on the current build. The historical fixed-array capacity math remains only as a fail-closed compatibility fallback for older source layouts.

## Fixed-capacity records that really do matter

A clean-source pass found two current 1.4.5.8 fixed-capacity cases that can be exceeded by accepted expanded generation:

### Floating Island metadata

Vanilla arrays have 300 records. Error World can triple the Floating Island count, and Care Bears can then apply its full x10 multiplier to the islands plus Large-category sky lakes. The source writes the metadata before its later clamp can protect the arrays.

Worst audited storage requirements:

- XL: **330** records;
- Huge: **420** records.

`GenerationCapacity.cs` enlarges all four parallel Floating Island metadata arrays together without changing generation counts or RNG.

### Crimson heart positions

`WorldGen.heartPos` has 100 entries. Crimson region attempts scale from physical width; Remix doubles them, and each `CrimStart` can produce up to eight `CrimVein` records.

- XL Remix upper bound: **96** — still fits vanilla 100;
- Huge Remix upper bound: **128** — requires enlargement.

Only the scratch array is resized. Crimson generation itself remains untouched.

Other audited fixed records such as larva positions, Mountain Caves, Jungle Shrine chests, tunnels, mushroom regions, ordinary Lakes, Oases, and surface ore-patch records remain within their 1.4.5.8 capacities at Huge under the accepted source rules and therefore are not enlarged without need.

## Floating Lakes remain intentionally categorical

`GenVars.skyLakes` in 1.4.5.8 is still established by explicit vanilla width thresholds that produce the Small/Medium/Large sequence `1/2/3`. Because XL/Huge intentionally remain categorical Large, the source continues to produce `3`.

There is no source-backed fourth/fifth term to invent. Expanded Worlds therefore does not curve-fit a synthetic Floating Lake count.

## Secret seeds

**The seed wins.**

Expanded Worlds changes physical dimensions and repairs only source assumptions invalidated by the changed aspect ratio or by fixed storage. Terraria still decides which passes run and what each secret seed does.

Not the Bees, Drunk, Remix, For the Worthy, Celebration, No Traps, Don't Starve, Error World, Care Bears, Dual Dungeons, and combinations remain Terraria-owned. Expanded Worlds patches the underlying source rule rather than replacing those seeds with hand-authored approximations.

## Verification

The repository has three different kinds of checks:

1. **pure regression tests** — verify source-derived arithmetic and Small/Medium/Large parity before accepting XL/Huge continuation;
2. **1.4.5.8 source audits** — independently decompile the official dedicated server and enforce exact source-shape contracts where server and retail overlap;
3. **retail/client contract fixtures** — compile the client gmod surface and guard client-only members/patch assumptions.

A real retail launch is still the final execution test for Harmony application, memory/allocation behavior, save/load, map rendering, and multiplayer. It should answer "does this exact build execute the audited contract?", not "does the generated map feel right?"
