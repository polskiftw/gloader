# Expanded Worlds

Adds two wider world sizes to vanilla Terraria's normal New World screen through gLoader:

- **XL** — `12,600 x 2,400` tiles (**1.5x** the tile area of Large)
- **Huge** — `16,800 x 2,400` tiles (**2x** the tile area of Large)

The height deliberately stays at vanilla Large's `2,400` tiles. The project is a mathematical continuation of Terraria's world-size rules, not a custom world generator and not a post-generation "make it feel bigger" pass.

## Why XL is 12,600, not 12,000

Terraria contains several source rules that use `maxTilesX / 4200`, sometimes with integer division, as a horizontal world-size quantum:

```text
Small   4200  -> tier 1
Large   8400  -> tier 2
XL     12600  -> tier 3
Huge   16800  -> tier 4
```

`12,000 / 4,200` would still be tier `2`, the same integer tier as Large. `12,600` is the next exact source quantum.

It is also an exact multiple of Terraria's 200-tile network section width:

```text
Large   8400 / 200 = 42 sections
XL     12600 / 200 = 63 sections
Huge   16800 / 200 = 84 sections
```

## Mathematical contract

Vanilla Small, Medium and Large normally grow in both dimensions, so old and current Terraria code can sometimes use one dimension as a proxy for overall size. Expanded Worlds intentionally changes that relationship. Every audited quantity is classified by what it physically represents:

```text
horizontal geometry/counts = width / 4200
vertical geometry          = height / 1200
area-density counts        = width*height / (4200*1200)
isotropic linear geometry  = sqrt(width*height / (4200*1200))
```

The isotropic rule is used only when a vanilla *linear* quantity genuinely has no preferred axis and the source overloaded one overall-size scalar. It is not a blanket multiplier. If both axes grow by the same factor `s`, `sqrt(area)` collapses back to `s`, reproducing ordinary vanilla scaling.

Relative to Small:

| Size | Horizontal | Vertical | Tile area | Isotropic linear |
| --- | ---: | ---: | ---: | ---: |
| Small | 1x | 1x | 1x | 1x |
| Medium | 1.5238x | 1.5x | 2.2857x | 1.5119x |
| Large | 2x | 2x | 4x | 2x |
| XL | 3x | 2x | 6x | 2.4495x |
| Huge | 4x | 2x | 8x | 2.8284x |

Relative to Large, XL is `1.5x` as wide and `1.5x` the area; Huge is `2x` as wide and `2x` the area. Both remain exactly Large-height.

There is deliberately **no synthetic Terraria size enum 4 or 5**. Different generators scale by different quantities, so one universal "world-size multiplier" would be mathematically wrong.

## Vanilla category stays Large

XL and Huge keep Terraria's categorical Small/Medium/Large state at **Large** while carrying their real physical dimensions separately for generation and the world file.

That protects gameplay and seed code that expects one of vanilla's three categories, while generation code that reads `Main.maxTilesX`, `Main.maxTilesY`, or their product sees the real expanded canvas.

## Source authority and fail-closed policy

The mod does not pretend the old public decompile is current Terraria source.

The available `AliceSavard/Terarria1405` decompile is Terraria **1.4.0.5** and is useful for identifying generator structure and historical formulas. Current Terraria/wiki data and current tModLoader signatures are used to cross-check behavior. A formula is not accepted into the XL/Huge contract merely because it can be fitted through three old numbers.

Every runtime patch that depends on a private method or a recognizable IL/source shape validates what it finds in the installed Terraria build and fails closed if that shape changed. gLoader compiles the raw gmod against the user's actual `Terraria.exe` at launch.

## Geography rule

> Extend the source rule; do not paint a bigger-looking world afterward.

The result should preserve Terraria's geography because the source does:

- one main Jungle remains **the Jungle**;
- one Snow/Ice region remains the Snow zone;
- one main Desert / Underground Desert remains that geographic region;
- one Aether remains one Aether;
- one Dungeon and one Jungle Temple remain unique unless a special/secret seed says otherwise;
- repeatable regions and micro-biomes follow their own source scaling family.

There is no subjective post-generation density pass. If generated output disagrees with an accepted mathematical continuation, that is an implementation bug or a still-unresolved source rule.

## Verified count/range families

`GenerationMath.cs` contains Terraria-independent equations. `tests/ExpandedWorldMathCompile` first verifies the known vanilla Small/Medium/Large rows before an equation is allowed to define XL/Huge values.

| Feature | Accepted family | Small | Medium | Large | XL | Huge |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Life Crystals | tile area | 100 | 230 | 403 | **604** | **806** |
| Surface Chests | width | 21 | 32 | 42 | **63** | **84** |
| Floating Islands | width | 3 | 5 | 6 | **10** | **13** |
| Marble caves | tile area | 4–8 | 9–18 | 16–32 | **24–48** | **32–64** |
| Granite caves | width | 4–8 | 6–12 | 8–16 | **12–24** | **16–32** |
| Underground Cabins | tile area | 35–40 | 80–91 | 140–160 | **210–240** | **280–320** |
| Cave Chests | tile area | 35–40 | 80–91 | 140–160 | **210–240** | **280–320** |
| Dead Man's Chests | width | 10–20 | 15–30 | 20–40 | **30–60** | **40–80** |
| Extra Desert Cabins | tile area | 2 | 4 | 8 | **12** | **16** |
| Living Tree micro-biomes | width | 6–11 | 9–16 | 12–22 | **18–33** | **24–44** |
| Long minecart-track count | width | 1–2 | 1–3 | 2–4 | **3–6** | **4–8** |
| Bee Hives | width/source family | 6–8 | 8–12 | 11–16 | **16–24** | **21–32** |

Terraria-style truncation is intentional. Numbers are not rounded to look prettier.

Many of these families are already implemented by Terraria's own `WorldGenRange`: historical/current implementations scale from physical world area or physical world width. When current vanilla already consumes the correct physical dimension, Expanded Worlds leaves it alone instead of duplicating placement.

### Deliberately unresolved examples

**Floating Lakes** currently expose the vanilla `1 / 2 / 3` Small/Medium/Large sequence, but those three values do not uniquely define what comes after Large. The earlier `floor(width / 2800)` idea was removed because it was curve-fitting, not source proof. Until a current source-backed continuation is established, Expanded Worlds does not invent one.

Minecart-track length is another example of why this matters. The old 1.4.0.5 configuration says width scaling, while current published Small/Medium/Large lengths follow a cleaner `1x / 1.5x / 2x` progression. Until the current rule is resolved, no custom track-length formula is asserted by this mod.

## Major world-geometry audit

### Terrain — native, no patch

Terrain/layer boundaries are height-driven and generation spans the actual width. XL/Huge therefore keep Large vertical Surface/Underground/Cavern/Underworld progression across a wider canvas.

### Snow/Ice — native, no patch

The Snow region's horizontal generation is width/tier driven, while its downward Ice propagation is constrained by the actual vertical world/layers. XL/Huge naturally produce one wider Snow zone while retaining Large-depth vertical progression.

### Underground Desert — axis patch

Historical/current-shape Desert generation derives an overall scalar from width and uses it for both horizontal and vertical geometry because vanilla world axes normally co-grow. That becomes invalid in a wider-only world.

`DesertScaling.cs` preserves the original arithmetic and random draw but feeds:

- horizontal uses from `maxTilesX / 4200`;
- vertical uses from `maxTilesY / 1200`.

The accepted horizontal Desert widths are:

| Size | Width |
| --- | ---: |
| Small | 320 |
| Medium | 484 |
| Large | 640 |
| XL | **960** |
| Huge | **1280** |

XL/Huge vertical Desert geometry remains exactly Large-height.

### Jungle — axis-aware patch

Jungle is the important case where one vanilla `_worldScale` is overloaded across multiple jobs. `JungleScaling.cs` separates those uses rather than replacing the generator:

- X displacement/ranges and horizontal margins -> width;
- Y displacement/ranges -> height;
- axis-neutral linear body strength -> `sqrt(area)`;
- axis-neutral one-dimensional repetition counts -> `sqrt(area)`.

Where vanilla nests two scale-driven counts, using the isotropic linear factor for both preserves the original algebra:

```text
sqrt(area) * sqrt(area) = area
```

The original Jungle pass, seed branches, random stream and placement logic remain in charge; the patch only disambiguates the overloaded scale for expanded aspect ratios.

### Evil biome — native, no patch

Normal pre-Hardmode evil-region generation is repeatable and its attempt count is proportional to actual world width. The expanded canvas therefore receives more evil-region work naturally rather than one manually enlarged or duplicated replacement biome.

### Dungeon — native geometry + capacity patch

The Dungeon remains one Dungeon. Its main generation budget is already width-driven, so a wider world naturally gives the source more room/hall work.

What does break is scratch storage: historical Dungeon generation stores candidate doors/rooms/platforms in fixed arrays sized for vanilla worlds. `GenerationCapacity.cs` does **not** change generation counts; it only enlarges source scratch buffers when a proven XL/Huge upper bound can exceed vanilla capacity.

The pure capacity regression currently proves, among other bounds:

- XL Dungeon candidate-door upper bound: **878**;
- Huge Dungeon candidate-door upper bound: **1140**;
- vanilla door scratch capacity: **500**;
- Huge room-record upper bound: **100**, which still fits the vanilla 100-room buffer;
- Huge platform upper bound: **200**, which still fits the vanilla 500-platform buffer.

Buffers that provably still fit are not enlarged merely because the world is bigger.

### Jungle Temple — source scaling + capacity support

The Temple remains one Temple. `TempleScaling.cs` preserves the audited source rule while separating horizontal, vertical and axis-neutral geometry for the changed aspect ratio. `GenerationCapacity.cs` also makes its room scratch storage large enough for the source's width-tier room-count continuation.

Audited room-count ranges:

| Size | Temple rooms |
| --- | ---: |
| Large | 20–31 |
| XL | **30–47** |
| Huge | **40–63** |

### Aether — native fixed-size unique mini-biome

The Aether is intentionally **not** given steroid dimensions. Its documented generator creates one approximately 200x200 protected mini-biome across vanilla sizes; its search zones use actual world-width fractions and vertical layers.

The mathematical continuation is therefore one same-sized Aether with a wider valid horizontal search region. No Aether size patch is justified.

### Hardmode Hallow/evil V — native, no patch

The Hardmode stripe start locations already use world-width-relative positions and the runners traverse the actual world height. The runner thickness historically scales with the width tier.

An isotropic `sqrt(area)` replacement was investigated and rejected: because each stripe spans the fixed-height world, converted stripe area is approximately `height * thickness`. When width/total area grows while height stays fixed, width-proportional thickness is what preserves the converted-area fraction. The source width-tier rule is therefore retained.

## Fixed-buffer audit

The source contains many scary-looking fixed arrays, but a bigger array is only required if the accepted XL/Huge generator can exceed it.

The audit currently shows the Huge mathematical maxima remain below the historical capacities for floating-island metadata, mountain caves, mushroom patches, tunnels, ordinary lakes, oases, Jungle chests, surface ore patches and larva records. Those buffers are intentionally left alone.

Dungeon door scratch storage is the notable proven exception and is patched.

## Special / secret seeds

**The seed wins.**

Expanded Worlds supplies the physical dimensions and only generalizes size math whose vanilla assumption becomes invalid. Terraria's own special/secret-seed processing still decides which passes run and what weirdness they introduce.

A seed that makes the world Snow-heavy, replaces ordinary generation, changes Jungle placement, adds a second Dungeon, alters Hive rules, or otherwise violates normal geography remains authoritative. Expanded Worlds scales that seed's world; it does not normalize it back into an ordinary world.

Source-sensitive patches are applied to the underlying generator method rather than to a hand-authored replacement pass so seed branches remain active wherever the original generator remains active.

## Runtime implementation

`Main.cs`:

1. extends the New World size row from three choices to five;
2. keeps Terraria's categorical state at vanilla **Large** for XL/Huge;
3. carries the selected real dimensions separately;
4. arms them only for the current Create -> Generate job;
5. reapplies them immediately before `CreateNewWorld` and before `clearWorld` allocates world storage;
6. disarms in a `GenerateWorld` finalizer even when generation throws, preventing a later ordinary world load from inheriting the custom dimensions.

No custom `.wld` format is introduced. Terraria's normal world header stores the real physical width and height.

`WorldMetadata.cs` exposes saved expanded worlds as **XL** or **Huge** instead of vanilla's `Unknown` label. For full-seed text it deliberately preserves the categorical **Large** prefix. That means copied seed text reproduces the seed/category semantics, but the physical XL/Huge choice is still a separate world-creation selection; the mod does not currently invent a private `4.x.x` / `5.x.x` seed-text dialect.

## Network/dimension bounds

The chosen widths are below the historical signed-16-bit tile-coordinate ceiling (`32767`) used by important Terraria network fields:

```text
XL    = 12600
Huge  = 16800
```

Both also divide exactly into 200-tile network sections. Current retail multiplayer behavior still requires execution testing because packet implementation details are version-sensitive, but no protocol extension is introduced by the mod.

## Verification policy

The user should **not** need to inspect a generated map and decide whether it "feels right."

The mathematical behavior is source-derived and regression-tested. Retail testing answers a narrower question:

> Does this exact Terraria build successfully execute the audited rules?

A retail launch is still required to validate runtime/API compatibility with the installed `Terraria.exe`: Harmony targets/IL, allocation/memory behavior, save/load, map data and multiplayer. That is execution verification, not a tuning process.

The repository CI can compile gLoader and the Terraria-independent regression projects, but raw gmods are compiled against the user's installed retail Terraria assembly at gLoader launch. A green repository CI run therefore proves the pure math/build regressions, not that every private Terraria runtime patch still matches 1.4.5.8.
