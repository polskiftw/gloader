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
- `Races/HumanRace.cs` — intentionally boring proof race and default race.

## Verified lifecycle hooks in this checkpoint

- `Race.ResetEffects(Player)` runs after vanilla `Player.ResetEffects()`.
- `Race.PostUpdate(Player)` runs after vanilla `Player.Update(int)`.

Those match the corresponding tModLoader hook locations closely enough to use as the first proof of the dispatcher.

`PreUpdate` is intentionally **not** wired yet. tModLoader inserts its `PreUpdate` call inside `Player.Update(int)` after vanilla misc-counter/hair-dye work, so a simple Harmony prefix would be convenient but wrong. It should be added only with a verified insertion point.

## Human checkpoint

Human registers first as race ID `0` and is the default for any player without valid race state. The registry accepts both:

- `Human`
- `MrPlagueRaces/Human`

This preserves the upstream string identity as an alias while keeping gloader's implementation independent of tModLoader `ModType` registration.

Human currently has no race stats, assets, UI, or abilities. That is intentional: this checkpoint proves the ownership/dispatch architecture before a complicated race can hide framework mistakes.

## Next seam

**Persistence is next.**

Do not mix persistence back into `RacePlayerState`. Add a separate serializer/storage layer that maps Terraria player files to race state, then bridge it to vanilla player load/save entry points. Account for local and cloud player saves before declaring that layer complete.

After persistence works, the next useful proof is appearance state for Human. Only then should individual non-Human races, race-selection UI, custom rendering, abilities, projectiles, sounds, or multiplayer race packets start moving across.
