# Expanded Worlds

Adds two wider world sizes to vanilla Terraria's normal New World screen through gLoader:

- **XL** — `12,000 x 2,400` tiles (~1.43x the tile area of Large)
- **Huge** — `16,800 x 2,400` tiles (2x the tile area of Large)

The height deliberately stays at vanilla Large's `2,400` tiles. The goal is **more Terraria, not vertically stretched Terraria**.

## Mathematical contract

Expanded Worlds does **not** tune worldgen by feel. XL and Huge are defined as dimensionally consistent continuations of Terraria's Small / Medium / Large family.

Vanilla normally grows width and height together, so some Re-Logic code uses width as a proxy for overall size. Expanded Worlds intentionally changes the aspect ratio. The generalized scaling model is therefore:

```text
horizontal geometry/counts = width / 4200
vertical geometry          = height / 1200
area-density counts        = width*height / (4200*1200)
```

For our two sizes, relative to Small:

| Size | Horizontal | Vertical | Tile area |
| --- | ---: | ---: | ---: |
| Small | 1x | 1x | 1x |
| Medium | 1.5238x | 1.5x | 2.2857x |
| Large | 2x | 2x | 4x |
| XL | 2.8571x | 2x | 5.7143x |
| Huge | 4x | 2x | 8x |

Relative to vanilla Large, XL is 1.4286x as wide/large in area and Huge is exactly 2x as wide/large in area, while both retain Large's vertical scale.

This is not a fake "world size 4/5" multiplier. Terraria uses several different scaling families, and conflating them would be wrong for a width-only world.

## Vanilla category stays Large

The mod does **not** add values to Terraria's private Small / Medium / Large size enum. XL/Huge continue to report categorical **Large** to code that expects one of those three values. Their true dimensions are armed only for the active world-generation job.

That protects unrelated gameplay/seed code while allowing generation code that reads `Main.maxTilesX`, `Main.maxTilesY`, or their product to receive the real canvas.

## Geography rule

> Scale territory, preserve geography, preserve vanilla seed semantics.

The intended result is not repeated copies of Terraria's major geography:

- one main Jungle remains **the Jungle**;
- one Snow/Ice region remains the Snow zone;
- one main Desert / Underground Desert remains that geographic region;
- unique landmarks remain unique unless vanilla seed logic explicitly changes that;
- repeatable structures/micro-biomes follow their own vanilla scaling family.

There is no subjective post-generation density pass. If output differs from the defined continuation, that is an implementation bug or a still-unresolved vanilla size assumption.

## Scaling families already verified

`GenerationMath.cs` contains pure Terraria-independent equations. `tests/ExpandedWorldMathCompile` first verifies known vanilla Small / Medium / Large outputs, then locks the XL/Huge continuation.

| Feature | Scaling family | Small | Medium | Large | XL | Huge |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Life Crystals | tile area | 100 | 230 | 403 | **576** | **806** |
| Surface Chests | width | 21 | 32 | 42 | **60** | **84** |
| Floating Islands | width | 3 | 5 | 6 | **9** | **13** |
| Floating Lakes | discrete width rule generalized | 1 | 2 | 3 | **4** | **6** |
| Marble caves | tile area | 4–8 | 9–18 | 16–32 | **22–45** | **32–64** |
| Granite caves | width | 4–8 | 6–12 | 8–16 | **11–22** | **16–32** |
| Underground Cabins | tile area | 35–40 | 80–91 | 140–160 | **200–228** | **280–320** |
| Cave Chests | tile area | 35–40 | 80–91 | 140–160 | **200–228** | **280–320** |
| Dead Man's Chests | width | 10–20 | 15–30 | 20–40 | **28–57** | **40–80** |
| Extra Desert Cabins | tile area | 2 | 4 | 8 | **11** | **16** |
| Living Tree micro-biomes | width | 6–11 | 9–16 | 12–22 | **17–31** | **24–44** |
| Long minecart tracks | width | 1–2 | 1–3 | 2–4 | **2–5** | **4–8** |
| Bee Hives | width | 6–8 | 8–12 | 11–16 | **15–22** | **21–32** |

Integer truncation is intentional. If Terraria gets an ugly boundary such as XL Cabins `200–228`, the mod keeps it; numbers are not rounded to prettier values.

The configuration system used by many worldgen passes (`WorldGenRange`) scales from physical world area or physical world width, so those families naturally consume XL/Huge dimensions and do not need duplicate placement code.

## Source audit: major world geometry

### Terrain — native, no patch

Current Terraria terrain generation derives surface/rock layers from `maxTilesY` and then generates those layers across every column up to `maxTilesX`. Because XL/Huge retain height 2400, their surface/Underground/Cavern/Underworld vertical progression is naturally Large-height across a wider map.

### Snow — native, no patch

Vanilla chooses the Snow center from actual horizontal world position and scales the left/right Snow radius from `maxTilesX / 4200`. Its vertical Ice generation is bounded by the world/layers. This is already the desired axis behavior: XL/Huge get a wider single Snow zone without becoming vertically taller than Large.

### Underground Desert — patched at the formula

Vanilla derives one scalar from `maxTilesX / 4200` and historically uses it for **both** Desert width and Desert height because normal Terraria sizes grow both dimensions together. That is not dimensionally valid for our wider-only worlds.

For example, feeding Huge's width straight into that scalar can request an Underground Desert taller than the 2400-tile world.

`DesertScaling.cs` leaves horizontal arithmetic on `maxTilesX / 4200` and replaces only the three vertical uses with `maxTilesY / 1200`:

- normal Desert depth (`170 * scale`);
- Remix Desert depth (`340 * scale`);
- tenth-anniversary vertical offset (`20 * scale`).

The exact random draw, truncation, placement, surface scan and seed branches remain vanilla.

Resulting horizontal Desert width:

| Size | Width |
| --- | ---: |
| Small | 320 |
| Medium | 484 |
| Large | 640 |
| XL | **912** |
| Huge | **1280** |

XL/Huge Desert vertical geometry remains exactly Large-height because their world height is exactly Large-height.

The transpiler is fail-closed: if the installed Terraria build no longer has exactly the expected three vertical scale uses, the patch throws instead of guessing.

### Floating Lakes — patched at the source assignment

Vanilla initializes `GenVars.skyLakes` with hard thresholds producing `1 / 2 / 3` for Small / Medium / Large. The continuation `floor(worldWidth / 2800)` exactly reproduces all three values and yields XL `4`, Huge `6`.

`SkyLakeScaling.cs` changes the count before vanilla's Floating Island/Lake placement runs. It does not add lakes afterward. Nonstandard seed-assigned values are intended to remain authoritative.

### Jungle — audit still open

Jungle is deliberately **not guessed**.

Current Jungle generation uses one width-derived `_worldScale` for both horizontal and vertical movement and for isotropic `TileRunner` blob strength. That is harmless for vanilla sizes because width and height co-grow, but it is ambiguous for a changed aspect ratio: simply replacing one scalar cannot independently preserve horizontal and vertical geometry.

A correct Jungle patch therefore needs an axis-aware generalization of the generator itself (or an equivalent mathematically defined transformation), not a hand-picked multiplier. Until that derivation is complete, the Jungle audit remains explicitly open.

## Patch policy

For every worldgen subsystem:

1. classify each quantity as horizontal, vertical, area-density, unique-landmark, or discrete-tier behavior;
2. if vanilla already reads the correct physical dimension, **leave it alone**;
3. if vanilla uses width as an overall-size proxy, split it by physical axis for XL/Huge only;
4. if vanilla has a hard Small/Medium/Large cap, generalize the actual rule before placement rather than adding content afterward;
5. if the source cannot support one mathematically defensible continuation yet, mark it unresolved instead of guessing;
6. special/secret seed behavior wins over ordinary-world assumptions.

## Special / secret seeds

Expanded Worlds supplies dimensions and generalized size math. Terraria's own seed processing still decides which generation passes run and how they are transformed.

A seed that deliberately makes the world all Snow, creates a second Dungeon, changes Jungle placement, alters Hive geometry, or otherwise violates normal geography remains authoritative. The mod scales the seed's world; it does not normalize the seed into an ordinary world.

## Runtime implementation

`Main.cs`:

1. extends the New World size row from three choices to five;
2. keeps Terraria's categorical state at vanilla **Large** for XL/Huge;
3. arms the chosen dimensions only for the active Create -> Generate job;
4. applies them immediately before `CreateNewWorld` and again before `clearWorld` allocates world storage;
5. disarms them in a `GenerateWorld` finalizer even if generation throws, preventing later ordinary world loads from inheriting a custom preset.

No custom `.wld` format is introduced; Terraria stores actual dimensions in its normal world header.

## Verification policy

The user should not need to inspect a map and decide whether it "feels right." The intended math is source-derived and regression-tested.

A retail launch is still necessary to validate **runtime compatibility** with the exact installed Terraria build: Harmony target signatures/IL, memory/allocation behavior, save/load, map arrays and multiplayer. That test answers "does this build execute the proven rules?" — it does not decide what the rules should be.

The public source audit currently targets the available Terraria 1.4.5.6 decompile. gLoader itself recompiles against the exact installed Terraria executable; every source-sensitive transpiler is written to fail closed if that build's method shape no longer matches the audited source.
