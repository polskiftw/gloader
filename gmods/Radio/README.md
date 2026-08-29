# Radio

General-purpose client-side internet radio for gloader/Terraria. Radio lives in Terraria's pause/options UI. It does **not** add an item, tile, NPC, accessory, furniture object, or any other in-world radio mechanic.

## UI

Open Terraria's normal pause/options screen and click **Radio**. The Radio page provides:

- category-first browsing with multi-tagged stations;
- decade filters;
- local search;
- Favorites and Recent lists;
- a clearly separated live-directory search across laut.fm and Radio Browser;
- one-click station switching and favorites;
- play/pause, independent radio volume, provider/health status, and a persistent now-playing strip;
- optional six-second song-change popups during gameplay.

Opening the Radio page does not apply the normal pause duck, so you can audition stations from the browser. Outside the Radio page, radio audio smoothly ducks while Terraria is paused.

## Catalog model

Every station has a stable ID, provider, tags, optional decades, one or more stream variants, and a metadata strategy. A station may appear in several categories at once. Providers are shown as provenance, not used as the primary browsing hierarchy.

Radio ranks stream variants by:

1. public/free and no authentication;
2. compatibility with the Windows Media Foundation + NAudio playback path;
3. codec/bitrate quality;
4. provider fallbacks when the preferred stream fails.

Unsupported Ogg/Opus/Vorbis test mounts are not selected just because their nominal quality is higher. Switching stations increments a generation token, clears queued PCM, resets the dynamic output instance, and prevents a stale worker from feeding audio after the switch.

## Built-in provider coverage

The v1 provider adapters are intentionally split between small stable catalogs and refreshable catalogs:

- **Rainwave** — All, Game, OC ReMix, Covers, Chiptunes, Chill. Official tune-in playlists with direct MP3 fallback; Rainwave API metadata.
- **Game That Tune Radio** — 320 kbps MP3 plus ICY metadata.
- **Nightride FM / REKT network** — fetched from the provider's current Icecast JSON status and grouped into stations/codec variants, so new public mounts do not require a code release.
- **PulsRadio** — Dance, Hits, Club, Lounge, Trance, 2000, 90s, 80s. Current official MP3 playlists are preferred where PulsRadio publishes them; the remaining named channels have a public directory-backed resolver fallback.
- **RadioSEGA** — compatible HE-AAC first, MP3 fallback; ICY now playing.
- **Gensokyo Radio** — public compatible stream resolved at runtime; provider page metadata fallback.
- **CVGM** — current official stream page resolver, preferring the 192 kbps MP3 relays; ICY metadata.
- **SceneSat** — official 320 kbps max-quality MP3 playlist, 128 kbps MP3 fallback; ICY metadata.
- **SLAY Radio** — 128 kbps MP3 relays; ICY metadata.
- **181.FM** — full current provider link page is parsed and cached; 128 kbps MP3 is preferred over the 64 kbps AAC fallback.
- **Radio Caprice / RADCAP** — full current provider index is parsed and cached; station pages are resolved lazily, preferring the provider's 320 kbps server-2/AAC path when present.
- **113.FM** — full current provider browse page is parsed and cached; streams resolve from the station page/public directory without requiring an account.

Remote provider-catalog refresh happens on a background thread and never blocks Terraria startup. The last successful generated catalog is stored in `catalog-cache.json` for up to 14 days. It is a cache, not a hand-maintained source-of-truth list.

### Explicit exclusions

- **SomaFM is deliberately excluded.** Its July 30, 2026 Terms of Service explicitly say third-party apps/games are not permitted without permission, and its stream pages say direct links are not for video games.
- **AceRadio / GotRadio are not baked into v1.** During the 2026-08-28 verification pass no sufficiently stable official public catalog/stream contract was confirmed to justify shipping hardcoded endpoints. If compatible entries exist in Radio Browser, users can still discover them live without Radio pretending stale URLs are vetted built-ins.

## Live discovery

`Search live` runs the query against both:

- **laut.fm** — station search plus the official `current_song` API; and
- **Radio Browser** — healthy, compatible-codec results using stable station UUIDs and rotating API mirrors.

Live-directory stations are visually labeled as such. They are not silently promoted into the vetted built-in catalog. Radio Browser click accounting is sent when one of its stations is selected, per the directory's API guidance.

## Metadata and health

Radio supports:

- provider API metadata (Rainwave, laut.fm);
- ICY `icy-metaint` / `StreamTitle` metadata;
- a limited provider-page fallback for stations whose public page exposes now-playing text.

Metadata is independent from audio. If playback is healthy but usable track metadata is unavailable, the UI reports `MetadataUnavailable` rather than killing playback.

Audio health states are `Unknown`, `Online`, `Buffering`, `Reconnecting`, `Offline`, and `MetadataUnavailable`. Playback tries ranked fallbacks before exponential reconnect delays. Terraria's vanilla music volume is suppressed only while Radio has recent audio data; if the stream dies or Radio is paused, Terraria music is allowed to recover.

## Persistent settings

`Radio.state.json` is created next to the mod and stores:

- selected station;
- play/pause state;
- Radio volume;
- song-change popup preference;
- favorites;
- recents.

Writes are atomic. On first run, if a sibling `VGMRadio/VGMRadio.ini` still exists, Radio migrates the selected Rainwave/GTT station and now-playing-popup preference before VGMRadio is retired.

## Custom stations

Edit `stations.json`. Invalid entries are skipped individually instead of breaking the whole mod.

```json
[
  {
    "name": "My Station",
    "enabled": true,
    "url": "https://radio.example/live.mp3",
    "codec": "mp3",
    "bitrate": 192,
    "tags": ["Rock", "80s", "Custom"],
    "metadata": "icy"
  }
]
```

Fields:

- `name` and an `http`/`https` `url` are required.
- `id` is optional; a stable custom ID is generated when omitted.
- `codec`, `bitrate`, `homepage`, and `tags` are optional.
- `resolver` defaults to `direct`; `playlist` is also supported for M3U/PLS URLs.
- `metadata` may be `icy`, `web`, or `none`; `metadataUrl` is optional for `web`.
- `enabled:false` keeps a saved entry without loading it.

## Tests

`tests/RadioCompile` compiles every Radio source file with the same `GLOADER`/`GLOADER_CLIENT` symbols used by gloader and runs deterministic regressions for JSON, ICY, Rainwave, laut.fm, stream ranking, taxonomy, custom-station error isolation, persistence, VGMRadio migration, catalog parsers, Radio Browser parsing, and station-switch generation/buffer invalidation.

`.github/workflows/radio-live-smoke.yml` is the separate network/provider truth check. It re-fetches representative provider APIs/catalogs/streams so provider breakage is visible without making ordinary builds depend on the public internet beyond NuGet/GitHub.
