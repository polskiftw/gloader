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

We only extend a Small/Medium/Large lookup when its three values form one obvious arithmetic sequence.

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

## Examples that DO NOT continue

If the vanilla terms are weird/random/ambiguous, we stop at Large instead of making shit up.

Examples:

```text
Dual Dungeon orb/heart quota: 8,14,18
Spider specialized rooms:     2,6,8
Lihzahrd painting cap:         1,2,2+random
```

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
- extend only the exact discrete size tables described above.

## Secret seeds

Terraria owns them. We do not have our own replacement list or our own fake secret-seed generator.

If a secret seed changes a vanilla pass, that vanilla pass still runs. Expanded Worlds only supplies the larger canonical canvas and the small set of source-backed discrete/capacity continuations.

## Client and server

Same generation math now.

The shared generation context, tier continuations, and capacity guards compile for both client and server. The 64-bit headless generator should not be a different flavor of Expanded Worlds from clicking the size button in the client.

## OCD check

CI locks:

```text
XL     10600 x 3000 = 53 x 20 sections
Huge   12600 x 3600 = 63 x 24 sections
THICC  14800 x 4200 = 74 x 28 sections
```

It also tests the discrete sequences, capacity bounds, rejects the three obsolete dimensions, and syntax-parses every Expanded Worlds `.cs` file once as client and once as server.
