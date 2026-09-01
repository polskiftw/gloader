# Expanded Worlds

Adds two wider world sizes to vanilla Terraria's normal New World screen through gLoader:

- **XL** — `12,000 x 2,400` tiles (~1.43x the tile area of Large)
- **Huge** — `16,800 x 2,400` tiles (2x the tile area of Large)

The height deliberately stays at vanilla Large's `2,400` tiles. The goal is **more Terraria, not vertically stretched Terraria**: normal surface/underground/cavern/Underworld progression is preserved while the horizontal world gets much more room.

## Design rule

> Scale territory, preserve geography, preserve vanilla seed semantics.

Expanded Worlds does **not** replace Terraria's world generator. It changes the canvas size immediately before vanilla allocates the world, then lets Terraria run its normal generation pipeline.

That matters for compatibility:

- Terraria still categorizes XL/Huge as **Large** anywhere vanilla asks for the Small/Medium/Large tier.
- The real `Main.maxTilesX` is `12000` or `16800` during generation, so passes that scale from actual width/area naturally see the expanded canvas.
- Special and secret seed logic is not bypassed or reimplemented. Vanilla applies its seed-specific pass changes normally.
- Existing Small/Medium/Large behavior is untouched when one of the vanilla buttons is selected.

This is intentional. We do **not** invent a fourth/fifth value inside Terraria's private world-size enum because unrelated vanilla systems may assume the only valid categorical sizes are Small, Medium, and Large.

## Expected worldgen feel

The target is a normal Terraria world that got steroids:

- one main Jungle becomes a much larger Jungle territory rather than creating `Jungle #2`;
- one Snow biome remains the Snow side/zone;
- the main Desert / Underground Desert remains geographically meaningful;
- unique landmarks remain unique unless a special/secret seed explicitly says otherwise;
- repeatable content should scale wherever vanilla derives its count/placement attempts from actual world width or area.

Because this mod deliberately leaves vanilla worldgen in control, the first runtime test pass is also an audit: any feature whose vanilla code caps itself at the Large tier rather than scaling from `Main.maxTilesX` can be identified and supplemented surgically instead of replacing the generator wholesale.

## Special / secret seeds

The compatibility policy is simple: **the seed wins**.

Expanded Worlds only supplies the larger dimensions. Terraria's own seed processing still decides terrain/pass behavior, so seeds that intentionally replace or radically reshape normal geography should remain weird rather than being normalized back into a standard world.

The important torture tests are:

1. normal XL
2. normal Huge
3. Drunk Huge
4. Remix / Don't Dig Up Huge
5. Not the Bees Huge
6. For the Worthy Huge
7. Get Fixed Boi / Zenith Huge
8. representative 1.4.5 secret-seed combinations

## Multiplayer

`16,800` is below the signed 16-bit coordinate ceiling (`32,767`) used by important parts of Terraria's networking protocol. Host & Play should therefore remain representable without inventing a new network format.

Both the host and server should still be launched through gLoader as usual. Joining-player requirements depend on whether later revisions add client-visible behavior beyond vanilla world data; the current implementation only changes world creation/generation.

## Current implementation

`Main.cs` does four things:

1. extends the New World size row from three choices to five;
2. keeps Terraria's internal categorical selection at vanilla **Large** when XL/Huge is selected;
3. applies the real dimensions at `WorldGen.CreateNewWorld`;
4. reapplies them at `WorldGen.clearWorld` immediately before world storage is allocated.

No world file format is patched. Terraria already saves the real tile dimensions in the world header.

## Testing status

The source is written against the current 1.4.5-era UI/worldgen shape and uses reflection for private Terraria UI members so visibility changes are less likely to break compilation.

gLoader compiles raw mods against the exact installed `Terraria.exe` at launch, so the decisive test is launching the branch build against the current game and generating the matrix above. The repository's ordinary CI validates gLoader itself but does not possess a retail Terraria executable with which to compile every raw gmod.

If the first generated Huge map reveals a Large-tier density cap (for example too few of a specific micro-biome or structure), fix that feature specifically. Do not solve it by duplicating the whole vanilla generator or by blindly multiplying every unique structure.
