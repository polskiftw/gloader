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
- `Rendering/RaceRenderer.cs` — client-only race drawing adapter contract.
- `Rendering/RaceRendererRegistry.cs` — deterministic renderer lookup by upstream race identity.
- `Rendering/RaceRenderPipeline.cs` — selected-race and appearance-state bridge into rendering.
- `Rendering/RaceRenderHooks.cs` — direct pre-render Harmony seam into vanilla player drawing.
- `Rendering/VanillaPlayerDrawClassifier.cs` — identifies finished vanilla skin/hair draw records without mutating global texture tables.
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

`tests/AuthenticRacesCompile` is an executable Release-build regression fixture. The normal gloader solution build checks Harmony target resolution, upstream race identity preservation, unresolved-race preservation, schema 1 -> 2 migration, full custom appearance round-trips, local save/backups, cloud movement, local restoration, and erase behavior.

## Rendering checkpoint

The direct renderer uses a **finished-draw-cache rewrite seam** instead of recreating tModLoader's `PlayerDrawLayer` framework.

The clean Terraria 1.4.5.8 decompile establishes this vanilla order in `LegacyPlayerRenderer.DrawPlayer`:

1. `PlayerDrawSet.BoringSetup(...)` calculates player visual state.
2. vanilla `DrawPlayer_UseNormalLayers(ref drawInfo)` creates the ordered `DrawDataCache`;
3. `PlayerDrawLayers.DrawPlayer_TransformDrawData(ref drawInfo)` applies player rotation/transforms;
4. optional `DrawPlayer_ScaleDrawData` applies requested player scale;
5. `PlayerDrawLayers.DrawPlayer_RenderAllLayers(ref drawInfo)` submits the finished cache to the GPU.

Authentic Races prefixes step 5. At that point vanilla has already solved armour ordering, frame selection, mount offsets, sitting/composite-arm geometry, lighting, rotation, direction and scale. A race renderer can therefore replace or expand only race-owned `DrawData` records **in place** while preserving Terraria's own transforms.

`VanillaPlayerDrawClassifier` identifies those race-owned records by texture identity against the active vanilla player texture slots plus primary/alternate hair. It does not edit `TextureAssets`.

That is materially cleaner than the upstream implementation. Upstream race layers suppress vanilla body pieces by globally replacing entries in `TextureAssets.Players`, `TextureAssets.PlayerHair`, and `TextureAssets.PlayerHairAlt` with a blank texture. The direct port never poisons those shared global asset tables.

This also matches the upstream data model: `RaceSheet` contains texture plus sheet/category/colour/hairstyle identity, but **no position, rotation, frame, origin or draw-order metadata**. Reusing Terraria's finished `DrawData` geometry therefore preserves upstream intent while deleting a large amount of duplicated vanilla positioning code.

Human registers a deliberate pass-through renderer, so this checkpoint changes no normal player pixels.

`tests/AuthenticRacesClientCompile` is a separate `GLOADER_CLIENT` executable fixture. The normal solution build now verifies that the client Harmony target resolves, vanilla player/primary-hair/alternate-hair records classify correctly, an unrelated draw record stays untouched, a probe renderer can rewrite one finished draw record through the real Harmony prefix, and resetting the registry restores Human's pass-through behavior.

### Deliberate scope boundary

This first render seam covers the normal full-player path. Vanilla's dedicated head-only/UI renderer uses a separate head draw path; do not fake coverage for character-list/head-preview rendering. Add that as a small companion seam when the first actual race sheet needs head-only previews.

## Next seam

**One real race-sheet substitution is next.**

Do not port every asset table yet. First add the smallest client asset loader that can resolve one bundled race texture without `ModContent`, then use a deliberately simple substitution to prove one classified vanilla player record can be replaced by race art in-game while armour and unrelated layers remain vanilla.

Once that proof is stable, expand the sheet vocabulary (16 colour channels, hairstyle tracks, glow masks, clothing) and then begin moving individual non-Human races across.
