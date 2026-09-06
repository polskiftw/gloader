# gmods/Radio

Radio is a deliberately small internet-radio mod for gloader.

## Runtime target

Radio targets the same **64-bit CoreCLR/FNA Terraria runtime** that gloader loads. It is not designed or tested for stock 32-bit/XNA Terraria, and no x86 compatibility path is maintained.

## Content policy

There are exactly two content sources:

1. **RadioMonster.fm**
2. **Rainwave**

No live directories, Radio Browser, custom-station file, provider scraping, cached mega-catalog, or third-party station augmentation is used.

## Stations

### RadioMonster.fm

The built-in RadioMonster catalog contains its ten public channels:

- Tophits
- Dance
- Evergreens
- Rock
- R&B
- Schlager
- Deutsch
- 80's
- 90's
- 2000's

Each channel prefers the official **Ultra 320 kbps MP3** stream, with the official 128 kbps AAC and 64 kbps AAC+ streams available as fallbacks.

### Rainwave

The built-in Rainwave catalog contains its six stations:

- Game
- OC ReMix
- Covers
- Chiptunes
- All
- Chill

Rainwave playback resolves the station's official `tune_in/<sid>.mp3.m3u` playlist at connection time.

## Metadata

RadioMonster uses ICY metadata embedded in the audio stream.

Rainwave is intentionally **stream-first** for metadata too. If the Rainwave MP3 stream exposes a usable ICY title, that title is displayed immediately because it follows the audio path instead of the schedule path.

If stream metadata is unavailable, Radio falls back to Rainwave's anonymous `/api4/info?sid=<sid>` schedule endpoint. That fallback is held for **10 seconds before publication**. Rainwave's schedule can advance before the listener's buffered MP3 reaches the song boundary; delaying only the schedule fallback prevents the old behavior where the next title appeared roughly ten seconds before the next song was audible.

## UI

Open Terraria's pause/options menu and choose **Radio**. The Radio screen has only three views:

- RadioMonster.fm
- Rainwave
- Favorites

The screen keeps play/pause, volume, favorites, song-change popups, and the vanilla mouse cursor. There is no provider search or directory browser.

## Persistence

`Radio.state.json` stores only:

- selected station
- playing state
- song-popup preference
- volume
- favorites
- recent stations

Old saved stations from removed providers are pruned automatically when the mod loads.

## Live smoke test

`tests/RadioLiveSmoke` and `.github/workflows/radio-live-smoke.yml` verify the two-source invariant, the 10+6 catalog shape, representative live audio, Rainwave API parsing, and the schedule-fallback delay contract.
