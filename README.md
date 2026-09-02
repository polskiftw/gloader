# gloader

Tiny raw-C# runtime mod loader for vanilla Terraria.

gloader loads the installed Terraria executable, compiles each source mod in memory against that exact game build, applies Harmony patches, and then invokes Terraria normally. Terraria's executables are not rewritten on disk and mods are not precompiled DLLs.

## Install layout

Copy the built gloader files directly into the Terraria installation folder. `gloader.exe` sits beside `Terraria.exe`; `gmods/` contains only mod folders, and `gdeps/` contains GLoader's runtime/support files and logs.

```text
Terraria/
  Terraria.exe
  TerrariaServer.exe
  gloader.exe
  gmods/
    ExpandedWorlds/
    InfiniteAngler/
    NoLiquidDupe/
    Radio/
    DVDLogo/
  gdeps/
    [gloader runtime/support files]
    logs/
```

## gmods and gdeps folder contract

`gmods/` is for mods only. Each immediate subfolder containing enabled `.cs` files is treated as one mod. There should be no GLoader dependency DLLs or log files loose in this directory.

```text
gmods/
  ExpandedWorlds/
    [Expanded Worlds source files]
    README.md
    DGD.md
  InfiniteAngler/
    Main.cs
    InfiniteAngler.ini
  NoLiquidDupe/
    Main.cs
  Radio/
    [Radio source files]
    README.md
    stations.json
  DVDLogo/
    Main.cs
    DVDLogo.ini
    dvd-logo.png
```

`gdeps/` is GLoader's runtime/support directory. Published dependency files and GLoader's client/server logs live there.

```text
gdeps/
  [gloader dependency/support files]
  logs/
    gloader-client.log
    gloader-server.log
```

Everything belonging specifically to a mod stays inside that mod's folder: `.cs` source, `.ini`/other configuration, images, data files, and any mod-specific documentation. All `.cs` files beneath one mod folder are compiled together as one in-memory assembly.

Disable one mod by renaming its folder:

```text
Thing/ -> Thing.disabled/
```

A source file inside a mod may also be individually disabled with the `.disabled.cs` suffix, although the normal unit of organization is the whole mod folder.

There is no manifest, custom scripting language, required base class, or gloader-specific inheritance tree. Mods are normal C# and may use Terraria types, reflection, unsafe code, Harmony, and managed dependencies available to the loader.

Each compile receives:

```text
GLOADER
GLOADER_CLIENT   // compiling against Terraria.exe
GLOADER_SERVER   // compiling against TerrariaServer.exe
```

Optional one-time initialization is:

```csharp
public static class Mod
{
    public static void Load()
    {
        // setup
    }
}
```

Harmony attributes are discovered and applied automatically.

## Launcher GUI

Run `gloader.exe` with no arguments to open the native Windows launcher. The launcher is built directly into the same `gloader.exe`; there is no second launcher/helper executable.

The launcher is intentionally just a friendly front end for the existing filesystem contract:

- checking or unchecking a mod renames `Thing/` <-> `Thing.disabled/` immediately;
- **Configure** appears when a mod has one or more top-level `.ini` files; one file opens directly and multiple files are offered in a small menu;
- **Mods Folder** opens `gmods/` and **Refresh** rescans it;
- **Logs** can open the newest log, the client log, the server log, or the logs folder;
- **Show console** is a one-run debug option and is not persisted;
- **Launch Vanilla** launches with mods disabled for that run without changing the checkboxes;
- **Launch Terraria** starts normally with the checked mods.

If both `Thing/` and `Thing.disabled/` exist at the same time, the launcher shows a conflict and refuses to rename either one instead of guessing which folder should win.

To bypass the GUI and launch directly with the current mod state:

```powershell
.\gloader.exe --run
```

## Host & Play server support

Run the visible client through gloader normally from the Terraria folder:

```powershell
.\gloader.exe
```

When Terraria starts `TerrariaServer.exe` for **Multiplayer -> Host & Play**, gloader redirects that child process through another gloader instance using the same `gmods` folder. The original server arguments, working directory, Steam environment, and process relationship are preserved.

This lets server-authoritative mods work for Host & Play without rewriting `TerrariaServer.exe` and without requiring joining players to install anything.

Dedicated server:

```powershell
.\gloader.exe --server
```

Explicit target:

```powershell
.\gloader.exe --target "C:\Games\Terraria\Terraria.exe"
```

Explicit mods folder override:

```powershell
.\gloader.exe --mods "C:\Games\Terraria\gmods"
```

Disable all mods for one run:

```powershell
.\gloader.exe --no-mods
```

Arguments after `--` are passed to Terraria's entry point.

Client and server logs are separate:

```text
gdeps/logs/gloader-client.log
gdeps/logs/gloader-server.log
```

## Included mods

### Expanded Worlds

`gmods/ExpandedWorlds/` adds three larger Terraria 1.4.5.8 world sizes directly to the New World size row:

```text
XL     12600 x 2400
Huge   16800 x 2400
THICC  16800 x 4800
```

The visible row becomes:

```text
Small | Medium | Large | XL | Huge | THICC
```

All three custom presets remain categorically vanilla **Large** for compatibility while the mod carries their real physical dimensions into world generation, storage, metadata, map rendering, and section bookkeeping. THICC shares Huge's width/discrete tier but doubles the physical height, so width-driven rules stay Huge-shaped while height- and area-driven rules see the larger canvas.

For headless generation, set `GLOADER_EXPANDED_WORLD` to `XL`, `HUGE`, or `THICC`. See `gmods/ExpandedWorlds/README.md` for the source-backed scaling model and technical audit, or `gmods/ExpandedWorlds/DGD.md` for the short version.

### Infinite Angler

`gmods/InfiniteAngler/Main.cs` is a server-authoritative shared endless Angler quest mod for multiplayer. Joining clients can remain completely vanilla. Vanilla's dawn quest rollover is suppressed, so the current quest stays active until every required, fully connected player has completed it. Each finisher is kept locked out of repeating that same quest. When the whole required group is finished, the server performs one normal Angler quest swap and broadcasts the next quest immediately. Players who join count immediately; players who disconnect stop counting.

Participation commands are enabled by default:

```ini
# gmods/InfiniteAngler/InfiniteAngler.ini
EnableParticipationCommands=true
```

Completely vanilla clients can control whether they are required for the shared-round quorum with normal chat commands:

- `!fish out` — stay connected and keep full Angler functionality, but stop blocking the next shared quest.
- `!fish in` — count toward the current shared quest again.
- `!fish` or `!fish status` — privately show your IN/OUT state plus who is waiting, finished, or opted out.

Set `EnableParticipationCommands=false` if you want the original strict behavior where every connected player is always required and `!fish` messages are left as ordinary chat.

Participation and completion are separate states. An OUT player may still catch and turn in the current quest fish, receives normal rewards, and is marked finished so they cannot repeat that quest. Their completion simply is not required for the round to advance. If they use `!fish in` later during the same round, a completion earned while OUT is preserved.

OUT state lasts across quest swaps for that connection. Disconnecting clears it, so a reconnect starts IN. If everybody opts OUT, the current quest is parked; an empty required group never causes automatic repeated quest swaps.

### No Liquid Dupe

`gmods/NoLiquidDupe/Main.cs` is a server-authoritative fix for the regular-bucket water/lava/honey duplication loop. It keeps the liquid volume conserved for partial regular-bucket scoops while leaving full scoops, Bottomless Buckets, pumps, and normal liquid simulation alone. Joining clients can remain vanilla.

### Radio

`gmods/Radio/` is the general-purpose client-side internet-radio mod. Its station browser lives in Terraria's pause/options UI; there are no radio items, tiles, NPCs, accessories, furniture objects, or other in-world mechanics.

Radio includes:

- a categorized, multi-tagged, decade-aware browser with subcategories, Favorites, Recent, and ranked search;
- complete refreshable catalogs for supported public providers plus small stable built-in networks;
- compatible highest-quality free stream selection with ranked fallbacks and reconnect/backoff behavior;
- ICY and provider/API now-playing metadata;
- a persistent now-playing strip and optional song-change popup;
- live discovery through laut.fm and Radio Browser;
- persistent live-directory favorites/recents and custom stations in `gmods/Radio/stations.json`;
- migration of the old VGMRadio Rainwave/GTT selection and now-playing preference.

See `gmods/Radio/README.md` for the provider list, quality policy, exclusions, metadata rules, custom-station schema, live-directory behavior, and CI details.

**VGMRadio is retired and is no longer shipped as a separate mod.** If an older install still has `gmods/VGMRadio/`, the new Radio mod can read its `VGMRadio.ini` on first migration. While Radio is installed, gloader deliberately ignores that leftover legacy source folder so an overlay upgrade cannot start two radio clients. The old folder can be deleted after migration.

### DVD Logo

`gmods/DVDLogo/` is client-only. It loads `dvd-logo.png` directly at runtime, bounces it around the screen, and changes to a different bright color on each wall hit.

Its size setting lives beside the mod:

```ini
# gmods/DVDLogo/DVDLogo.ini
Width=192
```

`Width` is the rendered width in pixels; height keeps the PNG's aspect ratio. With the current 2:1 logo, the default renders at 192x96.

## Updates

There is no hardcoded Terraria version check. Each launch recompiles source against the exact installed `Terraria.exe` or `TerrariaServer.exe`.

That does not make a patch immune to game updates: if Re-Logic renames or changes a method/field a mod patches, that mod may need a source edit. It does avoid distributing replacement compiled mod DLLs for routine changes.

## Build

Requirements:

- Windows
- .NET SDK capable of building `net48`

From the `gloader` source folder:

```powershell
.\build.ps1
```

Output staging folder:

```text
dist/gloader/
```

Copy the **contents** of `dist/gloader/` directly into the Terraria installation folder. The package adds `gloader.exe` plus two sibling folders: `gmods/` for mods and `gdeps/` for GLoader runtime/support files.

`build.ps1` marks the distributed `gloader.exe` **Large Address Aware** and verifies the PE flag. That is required by the proven THICC `16,800 x 4,800` world-generation path and is harmless for normal/other mod use.

Raw source mods execute with the same privileges as Terraria. Only use code you trust.

## License and third-party software

Original code and assets in this repository are licensed under the PolyForm Noncommercial License 1.0.0 unless a file says otherwise; see `LICENSE.md`. Third-party components bundled with GLoader or Gelatin remain under their own licenses, which are collected in `THIRD-PARTY-NOTICES.txt` and are not replaced by the PolyForm terms.