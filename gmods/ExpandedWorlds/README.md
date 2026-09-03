# Expanded Worlds

Expanded Worlds adds three larger world sizes to Terraria 1.4.5.8 through gloader:

| Size | Tiles | Area vs vanilla Large | Purpose |
| --- | ---: | ---: | --- |
| XL | `12,600 x 2,400` | `1.5x` | next exact horizontal size quantum after Large |
| Huge | `16,800 x 2,400` | `2x` | twice vanilla Large width at vanilla Large height |
| THICC | `16,800 x 4,800` | `4x` | Huge width with twice vanilla Large height |

The mod does not replace Terraria's world generator and does not run a subjective post-generation "make it look bigger" pass.

> **Expanded Worlds supplies the canvas; Terraria supplies the rulebook.**

## Using it

Enable `ExpandedWorlds` in gloader, launch Terraria, create a new world, and choose one of the six size buttons:

```text
Small | Medium | Large | XL | Huge | THICC
```

THICC is a real third custom preset. It is not a temporary alias for Huge and it does not replace Huge.

Headless/dedicated-server generation uses the same source mod with an environment variable:

```powershell
$env:GLOADER_EXPANDED_WORLD='XL'
$env:GLOADER_EXPANDED_WORLD='HUGE'
$env:GLOADER_EXPANDED_WORLD='THICC'
```

Run the server through gloader's normal server/target path after setting the value. Unset the variable to leave vanilla server sizing untouched.

## Ground truth

The source authority for this branch is the clean Terraria **1.4.5.8** retail decompile made from the matching retail binary. The clean decompile has zero known ILSpy decompilation-error markers from the audit that produced it.

Repository CI independently decompiles the official Terraria 1.4.5.8 dedicated server and asserts source contracts for APIs and worldgen rules shared with retail. Client-only contracts such as the New World UI, `WorldMap`, section tables, `RemoteClient`, and `MapRenderer` were reconciled against the matching retail decompile/binary.

Old 1.4.0.x public decompiles are not authority for this branch. Historical compatibility fallbacks can remain in code, but current 1.4.5.8 behavior is explicitly identified as current and kept separate from those fallbacks.

Runtime patches are fail-closed: if a private member or audited IL/source shape no longer matches the expected Terraria build, the patch throws instead of silently inventing a replacement rule.

## Why XL is 12,600 instead of 12,000

Several Terraria rules use `maxTilesX / 4200` as a horizontal world-size scale or, in some places, an integer width quantum. Exact 4,200-tile multiples make the expanded widths clean continuations:

```text
Small    4200
Large    8400
XL      12600
Huge    16800
THICC   16800   (same horizontal tier as Huge)
```

`12,000 / 4,200` is still integer quantum `2`, the same as Large. `12,600` is the next exact quantum (`3`), while Huge/THICC use quantum `4`.

The widths also divide exactly into Terraria's 200-tile network sections:

```text
Large   8400 / 200 = 42 sections
XL     12600 / 200 = 63 sections
Huge   16800 / 200 = 84 sections
THICC  16800 / 200 = 84 sections
```

THICC's `4,800` height divides into `32` of Terraria's 150-tile vertical network sections.

## Vanilla category remains Large

Terraria 1.4.5.8 `WorldGen.GetWorldSize()` returns:

- `0` through width `4200`;
- `1` through width `6400`;
- `2` for anything wider.

XL, Huge, and THICC therefore remain categorically **Large** without introducing fake Terraria size enum values. Code that asks for Small/Medium/Large sees a legal vanilla value; code that uses the physical dimensions sees the real expanded canvas.

The New World UI keeps Terraria's own categorical size selection at Large while Expanded Worlds carries the physical XL/Huge/THICC selection separately until generation starts.

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
| THICC | 4x | 4x | 16x | 4x |

This is not a universal multiplier. If vanilla already consumes the correct physical dimension, Expanded Worlds leaves the generator alone.

### THICC's important rule

THICC is **Huge's horizontal tier plus a taller physical canvas**.

That means:

- width-driven rules see the same `16,800` width as Huge;
- discrete source-backed width/tier continuations use the same fifth term as Huge;
- height-driven rules see `4,800` instead of `2,400`;
- area-driven rules see `80,640,000` tiles instead of Huge's `40,320,000`;
- axis-neutral geometry sees the corresponding area-equivalent linear scale.

This is why THICC does not invent a sixth term for categorical rules such as statues merely because the button appears after Huge.

## Generation lifecycle and backing storage

`Main.cs`:

1. adds XL, Huge, and THICC to Terraria's New World size row;
2. keeps Terraria's categorical selection at Large;
3. arms a custom preset only when `CreateNewWorld` begins;
4. applies the preset's real width and height before generation;
5. reapplies the dimensions at `clearWorld` as an allocation safety boundary;
6. updates `WorldFileData` so in-memory metadata matches the physical canvas;
7. disarms the preset in a `GenerateWorld` finalizer even if generation throws.

Changing `Main.maxTilesX/maxTilesY` alone is not enough. Terraria 1.4.5.8 has world-sized storage created from startup dimensions. `WorldStorage.cs` handles the actual storage contract:

- `Main.tile` is enlarged to logical dimensions plus Terraria's one-tile backing margin;
- client `WorldMap` storage is recreated at the expanded physical dimensions;
- `ActiveSections.LastActiveTime` and `LeashedEntity.BySection` are initialized with THICC-capable section storage;
- any `RemoteClient.TileSections` / `TileSectionsCheckTime` arrays that were constructed at startup dimensions are resized once the expanded physical dimensions are known;
- client `MapRenderer` is extended from its vanilla `5 x 2` target grid to a guarded `10 x 4` backing grid;
- `DrawMap`'s hard-coded X loop is extended through physical target column `8`; its Y loop is already derived from `Main.maxTilesY`, so THICC naturally reaches physical target row `2`.

The extra map-renderer guard column/row are intentional. Terraria treats the final allocated target as a short `400 x 600` tail. Keeping one unused target beyond each expanded physical edge prevents Huge/THICC's real last target from being incorrectly truncated.

These are storage/rendering changes only. They do not generate content.

## Address space

The proven THICC server generation/save/reload path used a 32-bit gloader process with the PE `IMAGE_FILE_LARGE_ADDRESS_AWARE` flag enabled. `build.ps1` now makes that flag part of the normal distributed `gloader.exe` and verifies the bit after writing it.

This is a launcher/process-host requirement, not a `.wld` format change.

## World metadata

No custom `.wld` format is introduced. Terraria's normal world header stores the real physical width and height.

`WorldMetadata.cs` presents the three recognized physical dimension pairs as **XL**, **Huge**, and **THICC** instead of `Unknown`. Full seed text intentionally uses the vanilla **Large** size prefix because Terraria's seed format only knows the three vanilla categories; the physical custom preset remains a separate creation choice.

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

The clean embedded configuration resolves minecart scaling directly:

| Minecart rule | 1.4.5.8 source scaling |
| --- | --- |
| StandardTrackCount `4..7` | `WorldArea` |
| StandardTrackLength `150..300` | `WorldWidth` |
| LongTrackCount `1..2` | `WorldWidth` |
| LongTrackLength `400..1000` | `WorldWidth` |

Because `WorldGenRange` reads the real `Main.maxTilesX/maxTilesY`, those rules automatically respond to THICC's true physical area/height where appropriate.

### Underground Desert

`DesertDescription.CreateFromPlacement` uses one **double** width-derived scalar (`maxTilesX / 4200.0`) for both axes because vanilla widths/heights normally co-grow.

`DesertScaling.cs` preserves Terraria's source arithmetic and RNG, but on expanded aspect ratios it keeps horizontal uses width-driven and changes only vertical uses to the actual height scale. XL/Huge remain Large-height vertically; THICC receives the intended taller vertical geometry.

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

The division is **not integer division**. `BeeScaling.cs` preserves the exact source result for Small/Medium/Large. Beyond Large, using the raw width-only scalar for axis-neutral tunnel geometry would be dimensionally wrong, so Expanded Worlds continues from Large by the area-equivalent linear factor.

Hive count itself remains Terraria-owned and width-driven. THICC therefore has the same count range as Huge while its tunnel geometry can respond to the larger canvas. Drunk World's `0.667` hive-count multiplier and larva behavior remain downstream and untouched.

### Don't Starve Wavy Caves

The 1.4.5.8 source derives Wavy Cave count from `(maxTilesX / 4200.0)^2`, which is a valid area proxy only while axes co-grow. `SecretSeedScaling.cs` preserves vanilla results through Large, then continues the count from Large by actual tile area. THICC therefore doubles Huge's normal area-derived continuation. Remix's vanilla `/3` remains downstream.

### Axis-neutral feature geometry

`FeatureGeometryScaling.cs` repairs source rules such as Neon Moss, Shroom Patch, and PlantAlch where one width-derived linear scalar is applied to geometry that is not purely horizontal. The source generator and RNG remain in control.

## Discrete Small/Medium/Large sequences

Some source rules are genuinely categorical rather than continuous. Expanded Worlds extends only sequences whose next horizontal-tier terms are unambiguous and keeps seed multipliers downstream.

| Rule | Small | Medium | Large | XL | Huge | THICC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Statue multiplier | 2 | 3 | 4 | **5** | **6** | **6** |
| Glow Tulips | 2 | 4 | 6 | **8** | **10** | **10** |
| Boulder Pet base quota | 2 | 4 | 6 | **8** | **10** | **10** |
| Spike Cave base | 3 | 5 | 7 | **9** | **11** | **11** |
| Chillet Eggs | 6 | 9 | 12 | **15** | **18** | **18** |
| Dirtiest Block base | 3 | 6 | 9 | **12** | **15** | **15** |

THICC deliberately repeats Huge in this table because these are tier/width rules, not area rules.

Important source details:

- the statue multiplier is calculated in **`WorldGen.Reset()`**, not `GenerateWorld`; the patch runs as a postfix after Terraria produces Large's value `4` and verifies that boundary before extending it;
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
| THICC | **40–63** |

THICC intentionally shares Huge's temple room-count distribution because the source rule is width-driven. Its deeper world does not imply a doubled Temple.

## Dungeon

Terraria 1.4.5.8 does **not** use the old fixed 100-room / 500-door Dungeon scratch arrays. Current Dungeon generation stores growing state in per-Dungeon `List<T>` collections (`dungeonRooms`, `dungeonHalls`, `dungeonFeatures`, doors, platforms, protected bounds) plus a dynamic `List<DungeonGenVars>`.

`GenerationCapacity.cs` validates those current 1.4.5.8 list shapes and performs **no Dungeon resize** on the current build. Historical fixed-array capacity math remains only as a fail-closed compatibility fallback for older source layouts; that fallback uses both physical width and height.

## Fixed-capacity records that really do matter

A clean-source pass found two current 1.4.5.8 generation-record capacities that expanded widths can exceed:

### Floating Island metadata

Vanilla arrays have 300 records. Error World can triple Floating Islands, and Care Bears can apply its full x10 multiplier to islands plus Large-category sky lakes. The source writes metadata before its later clamp can protect those arrays.

Worst audited storage requirements:

- XL: **330** records;
- Huge: **420** records;
- THICC: **420** records (same physical width/category as Huge).

`GenerationCapacity.cs` enlarges all four parallel Floating Island metadata arrays together without changing generation counts or RNG.

### Crimson heart positions

`WorldGen.heartPos` has 100 entries. Crimson region attempts scale from physical width; Remix doubles them, and each `CrimStart` can produce up to eight `CrimVein` records.

- XL Remix upper bound: **96**;
- Huge Remix upper bound: **128**;
- THICC Remix upper bound: **128**.

Only the scratch array is resized. Crimson generation itself remains untouched.

Other audited fixed generation records such as larva positions, Mountain Caves, Jungle Shrine chests, tunnels, mushroom regions, ordinary Lakes, Oases, and surface ore-patch records are width-driven/capped in current 1.4.5.8 and therefore THICC does not exceed Huge's already-audited bounds merely by being taller.

## Floating Lakes remain intentionally categorical

`GenVars.skyLakes` in 1.4.5.8 is established by explicit vanilla width thresholds that produce the Small/Medium/Large sequence `1/2/3`. XL/Huge/THICC intentionally remain categorically Large, so source continues to produce `3`.

There is no source-backed fourth/fifth/sixth term to invent. Expanded Worlds does not curve-fit a synthetic Floating Lake count.

## Secret seeds

**The seed wins.**

Expanded Worlds changes physical dimensions and repairs only source assumptions invalidated by the changed aspect ratio or fixed storage. Terraria still decides which passes run and what each secret seed does.

Not the Bees, Drunk, Remix, For the Worthy, Celebration, No Traps, Don't Starve, Error World, Care Bears, Dual Dungeons, and combinations remain Terraria-owned. Expanded Worlds patches the underlying source rule rather than replacing those seeds with hand-authored approximations.

## Verification

The repository uses several complementary checks:

1. **pure regression tests** — verify source-derived arithmetic and Small/Medium/Large parity before accepting expanded continuation;
2. **client raw-source compile fixture** — compiles the complete Expanded Worlds client source against an intentional Terraria API fixture, including THICC UI/storage/map contracts;
3. **server raw-source compile fixture** — compiles the complete server source and checks the THICC headless/storage contract;
4. **1.4.5.8 source audits** — independently inspect the official dedicated server for shared managed contracts;
5. **real world-generation probes** — launch the official Terraria 1.4.5.8 dedicated server through gloader.

The isolated `16,800 x 4,800` proof generated a complete world, saved it, exited, then loaded the same `.wld` successfully in a fresh process with Large Address Aware enabled. A same-seed six-size pass also generated and parsed a real `16,800 x 4,800` world and produced expected source-backed density behavior.

A real retail graphical launch remains the final execution test for Harmony application in `Terraria.exe`, GPU map-target creation/drawing, New World button behavior, and Host & Play. The source/storage contracts are wired for THICC; CI does not pretend a headless Windows runner is a human clicking through the retail UI.