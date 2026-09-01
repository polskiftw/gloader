# Expanded Worlds

Adds two wider world sizes to vanilla Terraria's normal New World screen through gLoader:

- **XL** — `12,000 x 2,400` tiles (~1.43x the tile area of Large)
- **Huge** — `16,800 x 2,400` tiles (2x the tile area of Large)

The height deliberately stays at vanilla Large's `2,400` tiles. The goal is **more Terraria, not vertically stretched Terraria**: normal surface/underground/cavern/Underworld progression is preserved while the horizontal world gets much more room.

## Design rule

> Scale territory, preserve geography, preserve vanilla seed semantics.

Expanded Worlds does **not** define worldgen by feel. XL and Huge are mathematical continuations of Terraria's existing Small / Medium / Large rules.

Terraria does not have one universal "world size multiplier." Different generators scale from different physical quantities:

- world **width**;
- world **tile area** (`width x height`);
- world **height**;
- a discrete Small / Medium / Large rule.

Expanded Worlds preserves that distinction. `GenerationMath.cs` contains pure dimension-aware formulas, and CI first requires each extrapolated family to reproduce the known vanilla Small / Medium / Large outputs. A formula that cannot reproduce vanilla is not accepted as the definition of XL / Huge behavior.

This is also why the mod does **not** invent a fake Terraria world-size enum value such as 3 or 4. Gameplay code that expects Small / Medium / Large continues to see **Large**. During world generation, systems that correctly scale from `Main.maxTilesX`, `Main.maxTilesY`, or their product see the real expanded dimensions.

## Geography

The intended geography follows from extending vanilla's generator, not from manually painting extra biomes:

- one main Jungle remains **the Jungle** and receives the larger territory implied by the generator's dimension math;
- one Snow/Ice region remains the Snow side/zone;
- the main Desert / Underground Desert remains one geographic region;
- unique landmarks remain unique unless vanilla seed logic explicitly changes that;
- repeatable structures and micro-biomes use their own vanilla scaling family.

There is no subjective post-generation density tuning. If an XL/Huge result disagrees with the mathematically extended vanilla rule, that is a bug in the mod or a still-unpatched vanilla Large-tier cap.

## Validated scaling matrix

The following formulas reproduce the existing Small / Medium / Large rows before extrapolating XL / Huge:

| Feature | Scaling family | Small | Medium | Large | XL | Huge |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Life Crystals | tile area | 100 | 230 | 403 | **576** | **806** |
| Surface Chests | width | 21 | 32 | 42 | **60** | **84** |
| Floating Islands | width | 3 | 5 | 6 | **9** | **13** |
| Floating Lakes | width-density continuation | 1 | 2 | 3 | **4** | **6** |
| Marble caves | tile area | 4–8 | 9–18 | 16–32 | **22–45** | **32–64** |
| Granite caves | width | 4–8 | 6–12 | 8–16 | **11–22** | **16–32** |
| Underground Cabins | tile area | 35–40 | 80–91 | 140–160 | **200–228** | **280–320** |
| Cave Chests | tile area | 35–40 | 80–91 | 140–160 | **200–228** | **280–320** |
| Dead Man's Chests | width | 10–20 | 15–30 | 20–40 | **28–57** | **40–80** |
| Extra Desert Cabins | tile area | 2 | 4 | 8 | **11** | **16** |
| Living Tree micro-biomes | width | 6–11 | 9–16 | 12–22 | **17–31** | **24–44** |
| Long minecart tracks (count) | width | 1–2 | 1–3 | 2–4 | **2–5** | **4–8** |
| Bee Hives | width | 6–8 | 8–12 | 11–16 | **15–22** | **21–32** |

The integer boundaries are intentional. Terraria's worldgen scaling commonly multiplies the Small-world base by a width/area ratio and truncates to an integer; Expanded Worlds preserves that behavior instead of rounding to prettier values.

Features not in this table are **not guessed**. They remain vanilla until their actual scaling rule is established. For example, if public/current data for a feature cannot reproduce all three vanilla tiers with the proposed formula, that formula stays out of the mod until the discrepancy is resolved.

## Native scalers vs Large-tier caps

Many vanilla passes already consume physical dimensions directly. Once XL/Huge set `Main.maxTilesX` before generation, those passes mathematically extrapolate on their own and should not be replaced.

The mod only needs an explicit patch where vanilla expresses the same Small / Medium / Large progression as a hard cap or tier branch. Floating Lakes are the clearest example: vanilla's 1 / 2 / 3 sequence can be represented by `floor(worldWidth / 2800)`, which exactly reproduces all three existing sizes and continues to 4 / 6 for XL / Huge.

That is the patching policy throughout this mod: **generalize the existing rule, do not supplement content after the fact.**

## Special / secret seeds

The compatibility policy is simple: **the seed wins**.

Expanded Worlds supplies dimensions and generalized size math. Terraria's own special/secret-seed processing still decides which generation passes run and how they are transformed. A seed that deliberately makes the world all Snow, adds a second Dungeon, changes Jungle placement, shrinks/enlarges Hives, or otherwise violates normal geography remains authoritative.

Expanded Worlds must scale the seed's world; it must not normalize the seed back into an ordinary one.

## Multiplayer

`16,800` is below the signed 16-bit coordinate ceiling (`32,767`) used by important parts of Terraria's networking protocol. No custom world-file or network format is introduced.

## Implementation

`Main.cs` currently:

1. extends the New World size row from three choices to five;
2. keeps Terraria's categorical Small / Medium / Large state at vanilla **Large** for XL/Huge;
3. arms the chosen expanded dimensions only for the active world-generation job;
4. applies those dimensions at `WorldGen.CreateNewWorld` and again immediately before `WorldGen.clearWorld` allocates world storage;
5. disarms the custom dimensions in a `GenerateWorld` finalizer even if generation throws, so later ordinary world loads cannot inherit them.

`GenerationMath.cs` is Terraria-independent and defines the verified scaling math. `tests/ExpandedWorldMathCompile` runs the vanilla-parity and XL/Huge target matrix in CI.

No world file format is patched. Terraria already stores the real tile dimensions in the world header.

## Verification policy

The user should not have to inspect a generated map and decide whether it "looks right." Mathematical worldgen behavior is regression-tested in CI.

A retail Terraria launch is still useful for **runtime compatibility** — e.g. confirming that a Harmony target still exists in the exact installed build and that vanilla does not contain an unrelated fixed-size buffer that rejects the larger canvas. That is different from using playtesting to decide the intended numbers: the intended numbers are determined before runtime from the vanilla scaling rules.
