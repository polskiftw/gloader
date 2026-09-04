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

## Short version

**Six seed problem = pick one seed, load ExpandedWorlds before Terraria starts world loading, run all six sizes, make the picture.**
