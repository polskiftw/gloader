# Six Seed Problem

The **six seed problem** means:

Use **one seed number** across **all six world sizes**, generate all six worlds, render them as PNGs, and produce **one final comparison image** showing all six results.

## Critical loading requirement

**Make sure ExpandedWorlds is fully loaded and Harmony-patched before Terraria queues or enters the world-load callback.**

Do **not** initialize ExpandedWorlds from inside `WorldGen.serverLoadWorldCallBack` immediately before `WorldFile.LoadWorld`. That is too late and can deadlock while Harmony patches world/server code that is already active.

For runner-based generation, bootstrap ExpandedWorlds **before** the callback is queued — in practice, initialize the mod immediately before the `serverLoadWorld` code creates/references the `serverLoadWorldCallBack` delegate. Only start world generation after ExpandedWorlds initialization and `Harmony.PatchAll(...)` have completed successfully.

In short: **ExpandedWorlds first, world-load callback second, world generation third.**

## Default mode

If Claire does **not** provide a seed:

1. Pick one random seed number.
2. Use that exact same seed for all six world sizes.
3. Generate all six worlds fresh.
4. Render all six worlds to PNG.
5. Combine the six renders into one comparison image.

## Supplied-seed mode

If Claire provides a seed:

1. Use that exact seed for all six world sizes.
2. Generate all six worlds fresh.
3. Render all six worlds to PNG.
4. Combine the six renders into one comparison image.

## Rules

- The same seed is used for every size.
- Do not search for a better seed.
- Do not try to make the layouts match.
- Do not treat biome/layout differences between sizes as failures.
- Do not edit generated worlds to force similarity.
- The purpose is simply to see what one seed produces at every world size.

## Required world sizes

Generate exactly these six sizes:

1. **Small** — 4200 x 1200
2. **Medium** — 6400 x 1800
3. **Large** — 8400 x 2400
4. **XL** — 10600 x 3000
5. **Huge** — 12600 x 3600
6. **THICC** — 14800 x 4200

## Proven runner workflow

This is the known-good path from the successful seed `1337420` run. Prefer this path instead of rediscovering the Windows/XNA dead ends.

1. Start from the current ExpandedWorlds code, including relevant unmerged branches/PRs when "latest/current" is requested.
2. Download the official Terraria **1.4.5.8 dedicated-server package**.
3. Use the package's **Linux** server tree, specifically its native `TerrariaServer.bin.x86_64` launcher and bundled **FNA** stack.
4. Build the current ExpandedWorlds dedicated-server bootstrap against that Linux/FNA server bundle.
5. Inject the bootstrap call into `Terraria.WorldGen.serverLoadWorld()` immediately **before** the code takes/references `serverLoadWorldCallBack` (the `ldftn` site used by the proven patcher).
6. Run one isolated GitHub Actions job per world size so each generator gets its own runner memory budget.
7. Pass the seed literally through Terraria's server config. For expanded sizes, set `GLOADER_EXPANDED_WORLD` to `XL`, `HUGE`, or `THICC`; leave it unset for Small/Medium/Large.
8. Require proof that ExpandedWorlds loaded early and `Environment.Is64BitProcess` is `True`.
9. Require the expanded generation completion log for XL/Huge/THICC at the exact expected dimensions. Do not accept a run merely because a `.wld` file exists.
10. Upload each fresh `.wld` separately.
11. In the compose job, download all six worlds and independently verify every world header/dimension before rendering.
12. Render with the existing **pinned TEdit render-only compositor** using the full pinned TEdit palette.
13. Produce the six individual PNGs plus one final combined comparison PNG.

## Do not repeat these dead ends

The successful run established several things that should be treated as settled unless the underlying Terraria runtime changes:

- **Do not use the ordinary Windows dedicated-server path for six-seed generation.** A normal Windows launch can still be 32-bit (`Is64BitProcess=False`).
- Clearing the Windows executable's 32-bit CLR preference is **not enough**. Microsoft XNA 4 then becomes the next 32-bit/runtime blocker.
- Do not spend time retargeting Terraria's Windows XNA references to FNA or trying to fake strong-named XNA assembly binding. The official Linux package already provides the clean **x86_64 + FNA** route.
- Do not load ExpandedWorlds late from inside the world-load callback. It must be patched in **before Terraria queues worldgen**.
- Do not use file existence alone as success. A truncated, wrong-size, or vanilla-size world is a failed six-seed result.

## Required validation gates

A six-seed job is accepted only when all of these are true:

- The exact requested/random seed was used for all six worlds.
- All six worlds were generated fresh.
- ExpandedWorlds reports successful early load.
- The generator process reports **64-bit**.
- Small is 4200 x 1200.
- Medium is 6400 x 1800.
- Large is 8400 x 2400.
- XL is 10600 x 3000.
- Huge is 12600 x 3600.
- THICC is 14800 x 4200.
- The compositor independently verifies all six world dimensions before rendering.
- All six renders use the pinned TEdit palette implementation.
- The final combined PNG is produced successfully.

## Reference success

The seed `1337420` run proved this workflow end to end:

- official Terraria 1.4.5.8 Linux x64 server path
- FNA instead of Windows XNA
- early ExpandedWorlds injection
- six isolated generation jobs
- all six dimension gates passing through THICC
- final pinned-TEdit render/composite job passing

## Short version

**Six seed problem = pick one seed, load ExpandedWorlds before Terraria queues worldgen, run all six sizes on the official Linux x64/FNA server path, verify every dimension, render with pinned TEdit, make the picture.**
