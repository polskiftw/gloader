# Expanded Worlds - DGD

## The six size buttons

```text
Small   4200 x 1200
Medium  6400 x 1800
Large   8400 x 2400
XL     10600 x 3000
Huge   12600 x 3600
THICC  14800 x 4200
```

Those are the canonical sizes.

## Why those numbers

Terraria splits worlds into `200 x 150` tile network sections.

Vanilla:

```text
Small   21 x  8 sections
Medium  32 x 12 sections
Large   42 x 16 sections
```

Continue the same pattern:

```text
XL      53 x 20 sections
Huge    63 x 24 sections
THICC   74 x 28 sections
```

Horizontal section jumps repeat `+11, +10`. Vertical always adds `+4`.

That is why the custom widths look slightly weird. We are preserving the same tiny width-vs-height wobble Terraria already has at Medium instead of picking prettier numbers and then patching around the consequences.

## The rule

**If Terraria already uses a physical formula, leave it the hell alone.**

Width formula? It sees the real width.

Height formula? It sees the real height.

Area formula? It sees the real area.

`maxTilesX / 4200.0`? Leave the exact source math alone.

Integer `maxTilesX / 4200`? Also leave it alone. Do not "improve" it into floating point.

For categorical Small/Medium/Large lookups we have two allowed continuation classes:

1. an obvious arithmetic sequence; or
2. a documented high-confidence inference that is independently supported by Terraria's surrounding size math.

The second kind lives in `InferredTierContinuity.cs` so it can never get confused with the exact stuff.

## Examples that DO continue

```text
Sky lakes:       1,2,3 -> 4,5,6
Statue mult:     2,3,4 -> 5,6,7
Glow Tulips:     2,4,6 -> 8,10,12
Boulder Pet:     2,4,6 -> 8,10,12
Spike base:      3,5,7 -> 9,11,13
Chillet Eggs:    6,9,12 -> 15,18,21
Dirtiest Block:  3,6,9 -> 12,15,18
```

Terraria still applies its own special-seed multipliers/random rolls after those base values.

The clean 1.4.5 Dual Dungeon code has more obvious sequences and those continue too. See README.md for the table.

## High-confidence promoted inferences

Two formerly-ambiguous 1.4.5 Dual Dungeon tables are now promoted because they line up with the same vertical-section math used all over that generator.

Vertical sections are:

```text
Small 8, Medium 12, Large 16, XL 20, Huge 24, THICC 28
```

### Shadow Orb / Crimson Heart quota

Vanilla is `8,14,18`.

Medium and Large are exactly `vertical sections + 2`; Small is the compact-world exception.

```text
8,14,18 -> 22,26,30
```

### Spider specialized rooms

Vanilla is `2,6,8`.

Medium and Large are exactly `vertical sections / 2`; Small is the compact-world exception.

```text
2,6,8 -> 10,12,14
```

These are still quotas. Terraria's existing room availability and placement logic decides how many can actually be realized.

## Lihzahrd painting compromise

Vanilla:

```text
Small   1
Medium  2
Large   2 or 3
```

There is no convincing tier formula, so we do not invent one. All three expanded sizes use:

```text
XL      3 or 4
Huge    3 or 4
THICC   3 or 4
```

The code keeps vanilla Large's exact `Next(2)` roll and just adds one to its already-randomized result. That means no extra RNG call and no fake growth curve.

## What still DOES NOT continue

If the vanilla terms are weird/random/ambiguous and we cannot independently cross-check a rule, we stop at Large instead of making shit up.

Tree/cave background regions also stop at Large's four styles because Terraria's `.wld` format and runtime only store three boundaries + four styles. Changing that would mean inventing a new file/network format. Nope.

## What got deleted

The old sizes were super wide, so we had patches trying to decide whether a vanilla width-derived number was "really" horizontal, vertical, area, or isotropic.

Those aspect-ratio repair layers are gone:

- no custom Desert scaling layer;
- no custom Jungle scaling layer;
- no Hive geometry reinterpretation;
- no generic feature-geometry reinterpretation;
- no Don't Starve Wavy Cave area rewrite;
- no other secret-seed width-proxy rewrite.

With the canonical sizes, width and height grow together again like vanilla. Terraria gets to use its own math.

## What still has to be patched

Stuff that is not worldgen policy:

- set the bigger physical dimensions before Terraria allocates the world;
- enlarge startup-sized tile/map/section storage;
- enlarge two fixed worldgen scratch arrays if Terraria can legitimately overflow them;
- extend the client map renderer's fixed target grid;
- label custom dimensions XL/Huge/THICC;
- add the three buttons;
- extend the exact discrete size tables described above;
- apply the two documented high-confidence inferred quotas and the flat 3-or-4 Lihzahrd painting policy.

## Secret seeds

Terraria owns them. We do not have our own replacement list or our own fake secret-seed generator.

If a secret seed changes a vanilla pass, that vanilla pass still runs. Expanded Worlds only supplies the larger canonical canvas and the small set of source-backed discrete/capacity continuations.

## Client and server

Same generation math now.

The shared generation context, tier continuations, inferred promotions, and capacity guards compile for both client and server. The 64-bit headless generator should not be a different flavor of Expanded Worlds from clicking the size button in the client.

## OCD check

CI locks:

```text
XL     10600 x 3000 = 53 x 20 sections
Huge   12600 x 3600 = 63 x 24 sections
THICC  14800 x 4200 = 74 x 28 sections
```

It also tests the exact discrete sequences, the promoted `22/26/30` and `10/12/14` rules, the `3/4` painting mapping, capacity bounds, rejects the three obsolete dimensions, and syntax-parses every Expanded Worlds `.cs` file once as client and once as server.
