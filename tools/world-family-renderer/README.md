# World Family Renderer

A tiny Windows GUI for one job: point it at a Terraria `.wld`, regenerate the same seed at all six gloader world sizes, and turn the fresh worlds into TEdit-style PNG maps.

## Output

Every run creates one timestamped folder containing exactly seven PNGs:

- `ExpandedWorlds_SameSeed_AllSizes.png` — comparison sheet like the Small/Medium/Large/XL/Huge/THICC example.
- `01_Small_4200x1200.png`
- `02_Medium_6400x1800.png`
- `03_Large_8400x2400.png`
- `04_XL_10600x3000.png`
- `05_Huge_12600x3600.png`
- `06_THICC_14800x4200.png`

The source world is never rendered or modified. It is opened header-only to recover its text seed, difficulty, evil, and the classic Terraria special-seed bit flags. Each output map comes from a freshly generated temporary world.

## Runtime

The tool is designed for current gloader 0.2+ and its private 64-bit Terraria runtime:

- `gloader.exe`
- `gdeps/x64-runtime/TerrariaRelease.dll` (or the Debug/Terraria.dll equivalent)
- `gmods/ExpandedWorlds`

Small, Medium, and Large are generated with an empty gmod directory. XL, Huge, and THICC are generated with only `ExpandedWorlds` staged. gloader is launched in `--server` mode and therefore uses the private 64-bit Terraria runtime automatically.

The GUI auto-detects common Steam Terraria installs and remembers the folder you pick manually.

## TEdit colors

Rendering uses TEdit's 1.4.5.8 world parser/configuration data and an MS-PL adaptation of TEdit's PixelMap color-composition algorithm. Tiles, walls, paints, coatings, liquids, wires, and the Space/Sky/Earth/Rock/Hell background zones use TEdit's palette. No Terraria texture rendering or reduced replacement palette is used.

TEdit is pinned by the build workflow to commit `cca62adbe37f8cbbd447061650f91a357836f5d0` so a future palette/parser change cannot silently alter an old build.

## Quality slider

The six PNGs all use the same pixels-per-tile sampling scale so their visual dimensions stay proportional to the actual world dimensions. The slider chooses the maximum width of the widest preset (THICC):

| Level | Max width |
| --- | ---: |
| Draft | 720 px |
| Normal | 1080 px |
| Good | 1440 px |
| High | 1920 px |
| Very High | 2880 px |
| Ultra | 3840 px |

Higher settings load the same world data and use the same TEdit colors; they simply sample more world tiles into the PNG.

## Build

The GitHub Actions workflow checks out the pinned TEdit source, publishes a self-contained Windows x64 single-file executable, runs `WorldFamilyRenderer.exe --self-test`, and uploads `WorldFamilyRenderer-win-x64.zip`.

The build does **not** contain Terraria or the user's private 64-bit Terraria runtime. World generation uses the user's local gloader/Terraria installation at runtime.
