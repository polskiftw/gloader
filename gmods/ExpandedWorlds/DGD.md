# Expanded Worlds — DGD

This is the short version. `README.md` is the canonical technical document.

## What this mod does

It adds three real world-size buttons to Terraria 1.4.5.8:

```text
Small | Medium | Large | XL | Huge | THICC
```

Exact custom sizes:

```text
XL     12600 x 2400
Huge   16800 x 2400
THICC  16800 x 4800
```

THICC does **not** replace Huge. It is a separate third custom size.

## How to use it

1. Put/keep the `ExpandedWorlds` folder under `gmods/`.
2. Run `gloader.exe`.
3. Make sure **ExpandedWorlds** is enabled in the gloader launcher.
4. Launch Terraria.
5. Choose **Single Player -> New** and make a world.
6. Pick **XL**, **Huge**, or **THICC** in the normal world-size row.

Nothing else is required for ordinary client world creation.

## What THICC actually means

THICC is:

```text
16800 tiles wide
 4800 tiles tall
80640000 total tiles
```

Compared with vanilla Large (`8400 x 2400`), THICC is:

```text
2x as wide
2x as tall
4x the tile area
```

Compared with Huge (`16800 x 2400`), THICC has:

```text
same width
2x height
2x tile area
```

## Why some things do not double from Huge to THICC

That is intentional.

Terraria does not scale every feature by total world area. Expanded Worlds follows the source rule for each feature:

```text
width rule       -> THICC behaves like Huge
height rule      -> THICC sees the doubled height
area rule        -> THICC sees the doubled area vs Huge
discrete tier    -> THICC uses Huge's tier
```

Examples of THICC deliberately using Huge's same width/tier behavior include the Temple room-count range, statue tier, Glow Tulips, Boulder Pet quota, Spike Cave tier, Chillet Eggs, and Dirtiest Block tier.

Examples that respond to THICC's extra area include Life Crystals, Cave Houses/Cabins, Cave Chests, Marble counts, and other source rules based on `WorldArea`.

## Dedicated server

For headless generation, set one of these before launching the dedicated server through gloader:

```powershell
$env:GLOADER_EXPANDED_WORLD='XL'
$env:GLOADER_EXPANDED_WORLD='HUGE'
$env:GLOADER_EXPANDED_WORLD='THICC'
```

If the variable is absent, Expanded Worlds leaves vanilla dedicated-server sizing alone.

## Saves

Expanded Worlds does **not** invent a new `.wld` format. Terraria saves the real width and height in the normal world file.

The mod labels recognized custom worlds as:

```text
XL
Huge
THICC
```

Terraria still categorizes all three as vanilla **Large** internally when code asks for Small/Medium/Large. That is deliberate compatibility behavior.

## Memory / giant-world plumbing

THICC is big enough that changing two numbers is not sufficient. The mod also handles the relevant Terraria 1.4.5.8 backing storage:

```text
Main.tile
WorldMap
ActiveSections
LeashedEntity section storage
RemoteClient multiplayer section storage
MapRenderer target columns and rows
```

The distributed `gloader.exe` is also marked **Large Address Aware** by `build.ps1`, because the successful `16800 x 4800` generation/save/reload proof used that address-space setup.

## What has already been proven

A real official Terraria 1.4.5.8 dedicated server has successfully:

```text
generated 16800 x 4800
saved the .wld
exited
started in a fresh process
reloaded the saved world successfully
```

The same-size world was also included in a six-size same-seed generation/statistics pass.

## What still needs a literal eyeball test

CI cannot physically click Terraria's retail graphical UI or inspect GPU rendering like a person can. The code includes the THICC button and the audited map/storage support, but the final practical smoke test is still launching the retail client and making/opening one THICC world.

If that graphical smoke test exposes anything, fix the exact failure. Do not redesign the scaling model unless the evidence says the model is wrong.