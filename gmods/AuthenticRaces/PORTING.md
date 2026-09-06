# Authentic Races port map

This directory is a staging workspace while MrPlagueRaces is being ported from tModLoader to direct Terraria/gloader hooks.

## Layout

- `source/` — upstream MrPlagueRaces 1.4.4 reference snapshot. Do not port in place.
- `source/.gloaderignore` — local gloader marker so the reference `.cs` files are never compiled as part of the live gmod.
- `port/` — the new direct Terraria implementation. All port work stays here until the port is ready to be flattened into its final layout.

## First seam: race ownership and dispatch

Upstream spreads the core concept across:

- `Common/Races/Race.cs` — race lifecycle contract and sprite/asset helpers.
- `Common/Races/RaceLoader.cs` — race registration and lookup.
- `Common/Players/RaceHookPlayer.cs` — tModLoader `ModPlayer` hooks forwarded to the selected race.
- `MrPlagueRacesPlayer.cs` — selected race plus persistence, appearance state, networking, rendering helpers, and effects.

The first port deliberately separates those responsibilities instead of recreating the tModLoader abstraction layer.

Current `port/` equivalents:

- `Core/Race.cs` — loader-independent race contract.
- `Core/RaceRegistry.cs` — deterministic registry and upstream-name lookup.
- `Core/RacePlayerState.cs` — per-`Terraria.Player` selected-race state via `ConditionalWeakTable`.
- `Core/RaceHooks.cs` — direct Harmony bridge from verified vanilla `Player` lifecycle points into the selected race.
- `Core/RaceAppearanceState.cs` — custom detail colours and auxiliary hairstyle state, independent of rendering.
- `Core/RacePersistence.cs` — independent `.arplr` player sidecar storage and schema migration.
- `Core/RacePersistenceHooks.cs` — vanilla player save/load/cloud/local/delete bridge.
- `Core/RaceSaveData.cs` — version-independent in-memory sidecar payload.
- `Races/HumanRace.cs` — intentionally boring proof race and default race.

## Verified lifecycle hooks

- `Race.ResetEffects(Player)` runs after vanilla `Player.ResetEffects()`.
- `Race.PostUpdate(Player)` runs after vanilla `Player.Update(int)`.

Those match the corresponding tModLoader hook locations closely enough to use as the first proof of the dispatcher.

`PreUpdate` is intentionally **not** wired yet. tModLoader inserts its `PreUpdate` call inside `Player.Update(int)` after vanilla misc-counter/hair-dye work, so a simple Harmony prefix would be convenient but wrong. It should be added only with a verified insertion point.

## Human checkpoint

Human registers first as race ID `0` and is the default for any player without valid race state. The registry accepts both:

- `Human`
- `MrPlagueRaces/Human`

This preserves the upstream string identity as an alias while keeping gloader's implementation independent of tModLoader `ModType` registration.

Human currently has no race stats, assets, UI, or abilities. That is intentional: the early checkpoints prove architecture before a complicated race can hide framework mistakes.

## Persistence checkpoint

Race persistence is deliberately separate from `RacePlayerState`. Runtime race changes use `SetRace`, which fires race-change behavior; deserialization uses `TryRestoreRace`, which assigns the saved race without pretending that loading a character is a live race switch. This matches the upstream intent more closely than routing loads through the change lifecycle.

Authentic Races does **not** alter the vanilla `.plr` file and does not depend on tModLoader's `.tplr`/`TagCompound` format. It stores only its own state in a tiny adjacent sidecar:

```text
Alice.plr
Alice.arplr
Alice.arplr.bak
```

The persistence bridge follows Terraria's own `PlayerFileData`/`FileUtilities` storage seam:

- write after a successful vanilla `Player.SavePlayer`;
- restore after vanilla `Player.LoadPlayer` returns a valid `PlayerFileData`;
- preserve the previous sidecar as `.arplr.bak` before replacement;
- move both sidecar and backup when a player moves local -> cloud or cloud -> local;
- erase both when the character is deleted.

This intentionally mirrors the *storage lifecycle* tModLoader uses for `.tplr`, but the format and implementation are ours and have no tModLoader runtime dependency.

An unavailable race now falls back to Human **without losing its saved identity**. For example, loading `MrPlagueRaces/Tabaxi` before Tabaxi has been ported runs Human temporarily but continues to save `MrPlagueRaces/Tabaxi`, so an incomplete port cannot silently destroy future race data.

## Appearance checkpoint

Vanilla `.plr` already owns the ordinary player appearance fields: primary hair, hair colour, skin colour, eye colour, shirt/undershirt/pants/shoe colours, and clothing/skin variant. Authentic Races therefore stores only MrPlague's custom appearance vocabulary outside vanilla:

- detail colour;
- auxiliary detail colours 1-3;
- auxiliary hairstyle IDs 1-3.

`RaceAppearanceState` keeps those values per vanilla `Player` without modifying `Player` or introducing rendering dependencies. Its initial custom colour values match the corresponding `MrPlagueRacesPlayer` field defaults; race-selection-specific defaults can replace them later when that seam is ported.

`.arplr` **schema 2** adds those custom appearance fields after the race identity. The reader remains backward-compatible with schema 1 race-only sidecars and upgrades them in memory using default appearance values. Corrupt, truncated, negative-hairstyle, or unknown-future schema data remains non-fatal to the vanilla character.

`tests/AuthenticRacesCompile` is an executable Release-build regression fixture. The normal gloader solution build now checks Harmony target resolution, upstream race identity preservation, unresolved-race preservation, schema 1 -> 2 migration, full custom appearance round-trips, local save/backups, cloud movement, local restoration, and erase behavior.

## Next seam

**Rendering is the next architectural knot.**

Do not start by copying every race's sprites or tModLoader player-layer classes. First isolate the smallest direct-Terraria rendering bridge that can answer: given a vanilla `Player`, its selected `Race`, and `RaceAppearanceState`, how do we substitute one race sheet while preserving vanilla armour, frames, lighting, direction, and draw ordering?

That bridge should replace the upstream `ModContent`/`PlayerDrawLayer` dependency rather than emulating it. Once a single deliberately simple visual substitution works, expand the race sheet/asset loader and only then begin moving individual non-Human races across.
