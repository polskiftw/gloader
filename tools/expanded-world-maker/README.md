# Expanded World Maker

A native Windows GUI for generating **Expanded Worlds** outside the Terraria client. It launches the official `TerrariaServer.exe` through gloader with **only** `gmods/ExpandedWorlds` staged for the headless process, then copies the validated `.wld` into the folder you chose.

## World sizes

| Preset | Dimensions | Area vs vanilla Large |
| --- | ---: | ---: |
| XL | 12,600 × 2,400 | 1.5× |
| Huge | 16,800 × 2,400 | 2× |
| THICC | 16,800 × 4,800 | 4× |

All three stay categorically **Large** inside Terraria. Expanded Worlds changes the physical canvas only.

## What the GUI exposes

- XL / Huge / THICC
- World name
- Classic / Expert / Master / Journey
- Normal seed text (blank = random)
- A dedicated **Special Seeds** section with Terraria's nine Special Seeds:
  - Not the Bees
  - Drunk
  - Celebration Mk10
  - The Constant
  - For the Worthy
  - No Traps
  - Remix / Don't Dig Up
  - Zenith / Get Fixed Boi
  - Skyblock
- A separate **Secret Seeds (1.4.5)** section with all 37 current Terraria 1.4.5 Secret Seeds as checkboxes
- Arbitrary combinations of Secret Seeds with each other and with Special Seeds
- Output-folder picker for the finished `.wld`
- `TerrariaServer.exe` picker if auto-detection does not find it
- Live worldgen progress, collapsible server log, cancellation, and open-output-folder button

You do not need to remember or type Secret Seed phrases. When Secret Seeds are selected, the World Maker encodes them through Terraria 1.4.5.8's native copied-seed format so the unattended dedicated-server path activates them correctly. Any checked Special Seeds are serialized into the same payload so mixed combinations survive generation.

If no Secret Seeds are selected, ordinary seeds and Special-Seed-only worlds keep using Terraria's normal dedicated-server configuration path.

## Install / run

The release package places the executable here:

```text
<Terraria folder>\tools\ExpandedWorldMaker.exe
```

Keep the complete gloader package together. The World Maker expects:

```text
<Terraria folder>\gloader.exe
<Terraria folder>\gdeps\...
<Terraria folder>\gmods\ExpandedWorlds\...
<Terraria folder>\TerrariaServer.exe
<Terraria folder>\tools\ExpandedWorldMaker.exe
```

`TerrariaServer.exe` normally already exists in the Steam Terraria install. If yours is elsewhere, use the **Server...** button.

## Safety behavior

Generation happens in a private job directory under `%LOCALAPPDATA%\gloader\ExpandedWorldMaker`. The finished `.wld` is copied into your selected output folder **only after**:

1. the headless server reaches ready state,
2. the World Maker opens Terraria's saved `.wld` header and confirms its physical width and height exactly match the requested XL/Huge/THICC preset, and
3. the generated file exists and is non-empty.

The validator does **not** depend on a particular Expanded Worlds console/log sentence. It validates the file Terraria actually wrote, so a successful server-ready world is not rejected just because a diagnostic line was absent or arrived at a different time. A successful check is also written into **Show log** as a `[World Maker] Validated generated .wld header` entry with the saved dimensions and file size.

If a destination `.wld` already exists, the GUI asks before starting but does not overwrite the existing file until the replacement has finished and validated.

The most recent detailed log is kept at:

```text
%LOCALAPPDATA%\gloader\ExpandedWorldMaker\last-generation.log
```

## Runtime contract

This build is intentionally pinned to **Terraria 1.4.5.8**, matching the current Expanded Worlds source audits and headless-generation tests. The GUI refuses a server executable reporting a different version rather than guessing that the patches are still safe.
