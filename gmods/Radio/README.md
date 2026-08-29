# Radio

General-purpose client-side internet radio for gloader/Terraria. Radio lives in Terraria's pause/options UI. It does **not** add an item, tile, NPC, accessory, furniture object, or any other in-world radio mechanic.

## UI

Open Terraria's normal pause/options screen and click **Radio**. The Radio page provides:

- category-first browsing across a multi-tagged catalog;
- category-specific subfilters such as Rock -> Classic Rock/Alternative/Punk and Synthwave -> Chillsynth/Darksynth/Horrorsynth;
- decade filters from the 1940s through the 2020s, including useful combinations such as 1980s + Hip-Hop or Rock;
- ranked local search, with exact/name/tag/decade matches favored over weak matches;
- Favorites and Recent lists;
- a clearly separated live-directory search across laut.fm and Radio Browser;
- one-click station switching and favorites;
- play/pause, independent Radio volume, provider/health status, and a persistent now-playing strip;
- optional six-second song-change popups during gameplay.

Favorites rank first during normal browsing. Verified changing metadata and vetted built-in entries rank ahead of unknown live-directory entries. Provider names remain visible as provenance, but providers are not the primary browsing hierarchy.

Opening the Radio page does not apply the normal pause duck, so you can audition stations from the browser. Outside the Radio page, radio audio smoothly ducks while Terraria is paused.

## Catalog model

Every station has a stable ID, provider, tags, optional decades, one or more stream variants, and a metadata strategy. A station may appear in several categories at once. Search and filters operate on the same normalized tags instead of maintaining separate provider-specific menus.

Radio ranks stream variants by:

1. public/free and no authentication;
2. compatibility with the Windows Media Foundation + NAudio playback path;
3. codec/bitrate quality;
4. provider fallbacks when the preferred stream fails.

Unsupported Ogg/Opus/Vorbis/FLAC mounts are not selected merely because their nominal quality is higher. Switching stations increments a generation token, clears queued PCM, resets the dynamic output instance, and prevents a stale worker from feeding audio after the switch.

## Built-in provider coverage

The v1 provider adapters are intentionally split between small stable catalogs and refreshable catalogs. The public/provider assumptions below were re-verified during the 2026-08-29 implementation pass.

- **Rainwave** — All, Game, OC ReMix, Covers, Chiptunes, Chill. Official tune-in playlists with direct MP3 fallback; Rainwave API metadata.
- **Game That Tune Radio** — 320 kbps spoiler MP3 plus ICY metadata.
- **Nightride FM / REKT network** — current Icecast JSON supplies live mounts/quality/metadata, but Radio intersects it with the provider's current advertised station selector. The built-in catalog therefore contains the advertised Nightride/REKT stations and deliberately ignores reachable mystery/test mounts that the provider does not advertise.
- **PulsRadio** — Dance, Hits, Club, Lounge, Trance, 2000, 90s, 80s. Current official MP3 playlists are preferred where PulsRadio publishes them; the remaining named channels retain a public directory-backed fallback.
- **RadioSEGA** — best compatible public AAC stream first and MP3 fallback; ICY now playing. Unsupported higher-nominal-quality formats are not chosen by this playback backend.
- **Gensokyo Radio** — compatible public stream resolved at runtime; provider-page metadata fallback.
- **CVGM** — current official stream-page resolver, preferring the 192 kbps MP3 relays; ICY metadata.
- **SceneSat** — the current listen menu advertises a public high-bandwidth/max-quality MP3 option and lower MP3 fallback. Its legacy max-quality M3U link currently returns 404, so Radio uses the underlying public 320 kbps Icecast mounts directly, retains 128 kbps direct fallbacks, and has a public-directory fallback. GitHub-hosted Azure runners do not consistently reach SceneSat's web/Icecast hosts, so SceneSat reachability is reported as a non-gating advisory in the live CI rather than turning runner routing into a release blocker.
- **SLAY Radio** — public 128 kbps MP3 relays; ICY metadata.
- **181.FM** — full current public legacy catalog is parsed and cached. The current verification pass found 77 channels. 128 kbps MP3 is preferred over the 64 kbps AAC fallback.
- **Radio Caprice / RADCAP** — full current provider database is parsed and cached. The current verification pass found 517 station pages. Station pages resolve lazily and prefer the provider's 320 kbps compatible path when present.
- **113.FM** — current public stream families are probed in parallel instead of relying on the provider's intermittently available old browse routes. A candidate must expose an identifiable station name **and actual track-like ICY metadata**. Duplicate delivery-network copies of the same named station are merged into one logical catalog entry with multiple stream fallbacks. The live verification pass has consistently found well above the 60-station completeness floor, with counts varying as individual channels come and go.

Remote provider-catalog refresh happens on background threads and never blocks Terraria startup. The last successful generated catalog is stored in `catalog-cache.json` for up to 14 days. It is a cache, not a hand-maintained source-of-truth list.

### Explicit exclusions

- **SomaFM is deliberately excluded.** Its July 30, 2026 Terms of Service explicitly say third-party apps/games are not permitted without permission, and its stream pages say direct links are not for video games.
- **AceRadio / GotRadio are not baked into v1.** During the live implementation survey no sufficiently stable official public catalog/stream contract was confirmed to justify hardcoding their endpoints. Compatible entries can still be discovered through Radio Browser without Radio presenting stale URLs as vetted built-ins.

## Live discovery

**Search live** runs the query against both:

- **laut.fm** — current station search plus the official `current_song` API; and
- **Radio Browser** — healthy, compatible-codec results using stable station UUIDs and rotating API mirrors.

Live-directory stations are visually labeled as such. They are not silently promoted into the vetted built-in catalog. Radio Browser click accounting is sent when one of its stations is selected, per the directory API guidance.

If a live-directory station becomes selected, favorited, or recent, Radio also persists the station definition and stream data needed to restore that entry after a restart. Saved live definitions are pruned once they are no longer selected, favorite, or recent, so the state file does not grow into a second directory cache.

## Metadata and health

Radio supports:

- provider API metadata (Rainwave, laut.fm);
- ICY `icy-metaint` / `StreamTitle` metadata;
- a limited provider-page fallback for stations whose public page exposes now-playing text.

A first plausible metadata title proves that a source can expose track-like data. A station is marked `MetadataVerified` only after Radio observes a **different** track-like title later; a static station slogan repeated forever is not treated as verified song metadata.

Metadata is independent from audio. If playback is healthy but usable track metadata is unavailable, the UI reports `MetadataUnavailable` rather than killing playback.

Audio health states are `Unknown`, `Online`, `Buffering`, `Reconnecting`, `Offline`, and `MetadataUnavailable`. Playback tries ranked fallbacks before exponential reconnect delays. Terraria's vanilla music volume is suppressed only while Radio has recent audio data; if the stream dies or Radio is paused, Terraria music is allowed to recover.

## Persistent settings

`Radio.state.json` is created next to the mod and stores:

- selected station;
- play/pause state;
- Radio volume;
- song-change popup preference;
- favorites;
- recents;
- the minimal live-directory station definitions needed to restore selected/favorite/recent live entries.

Writes are atomic.

**VGMRadio is retired and is no longer shipped as a separate mod.** On first run, if an old sibling `VGMRadio/VGMRadio.ini` still exists, Radio migrates the selected Rainwave/GTT station and now-playing-popup preference. gloader also suppresses a leftover legacy `VGMRadio` source folder whenever the new `Radio` mod is installed, so copying a new release over an old installation cannot accidentally start two radio clients. The old folder may be deleted after migration, but it does not need to be manually renamed before the first Radio launch.

## Custom stations

Edit `stations.json`. The shipped example is disabled. Invalid entries are skipped individually instead of breaking the whole mod.

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

## Tests and CI

`tests/RadioCompile` compiles every Radio source file with the same `GLOADER`/`GLOADER_CLIENT` symbols used by gloader and runs deterministic regressions for JSON, ICY, Rainwave, laut.fm, metadata-change verification, stream ranking, taxonomy, custom-station error isolation, persistence, VGMRadio settings migration, provider catalog parsers, station-page URL resolution, Radio Browser parsing, and station-switch generation/buffer invalidation.

`tests/RadioPolicyCompile` covers browser/provider policy that is easy to regress silently: advertised-only Nightride mounts, decade subfilters, taxonomy-aware free-text queries such as `80s rap`, favorites/verification ranking, quality/fallback ordering, and persistence of live-directory favorites/recents.

`tests/ModDiscoveryCompile` verifies the overlay-upgrade rule: a leftover `VGMRadio` folder is ignored only when `Radio` is actually installed, and unrelated mods remain discoverable.

`.github/workflows/radio-live-smoke.yml` is the separate network/provider truth check. It re-fetches current provider APIs/catalogs/streams/metadata so provider breakage is visible without turning deterministic compilation into an internet-dependent test. The normal `gloader` workflow still builds, tests, publishes, validates the package layout (including asserting that retired VGMRadio is absent), and runs the existing Host & Play integration fixture on GitHub's Windows runner.
