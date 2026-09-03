# Expanded World Maker — DGD

## The one thing you launch

```text
<Terraria folder>\tools\ExpandedWorldMaker.exe
```

Do **not** move just that EXE somewhere random. Leave it inside the extracted gloader package so it can find `gloader.exe` and `gmods\ExpandedWorlds`.

## Make a world

1. Pick **XL**, **Huge**, or **THICC**.
2. Type the world name.
3. Pick Classic / Expert / Master / Journey.
4. Type a normal seed, or leave Seed blank for random.
5. Tick any **Special Seeds** you want.
6. Tick any **Secret Seeds (1.4.5)** you want. You do not need to type their phrases.
7. Under **SAVE .WLD TO**, pick your Terraria Worlds folder.
8. Click **GENERATE WORLD**.

Typical Worlds folder:

```text
%USERPROFILE%\Documents\My Games\Terraria\Worlds
```

If Windows redirected Documents into OneDrive, the GUI uses that redirected Documents folder automatically.

## The three sizes

```text
XL     10,600 x 3,000
Huge   12,600 x 3,600
THICC  14,800 x 4,200
```

## Special Seeds

The nine Terraria Special Seeds have their own checkbox section:

```text
Not the Bees
Drunk
Celebration Mk10
The Constant
For the Worthy
No Traps
Remix / Don't Dig Up
Zenith / Get Fixed Boi
Skyblock
```

These use Terraria's normal dedicated-server special-seed configuration when no Secret Seeds are selected.

## Secret Seeds (1.4.5)

The World Maker exposes all **37 Terraria 1.4.5 Secret Seeds** as visible checkboxes in their own section. You can combine them with each other and with the nine Special Seeds. You do **not** need to remember or type the secret phrases.

When one or more Secret Seeds are checked, the World Maker builds Terraria's native copied-seed payload so the headless dedicated-server generation path actually activates the selected Secret Seeds. Selected Special Seeds are serialized into that same payload so mixed combinations survive world generation.

A normal seed can still be typed in the Seed box. If it is left blank, the World Maker supplies a random base seed before constructing the copied-seed payload.

## What happens when you press Generate

The GUI makes a temporary server config, copies **only ExpandedWorlds** into a private temporary gmods folder, and launches:

```text
gloader.exe --target TerrariaServer.exe --mods <private-gmods> -- -config <temporary-config>
```

It also sets:

```text
GLOADER_EXPANDED_WORLD=XL
```

or `HUGE` / `THICC`.

TerrariaServer does the actual worldgen. The normal Terraria game client is never launched, so the renderer/content/UI side of the game is not sitting in memory at the same time.

## How it decides the world succeeded

When TerrariaServer reaches ready state, the World Maker opens the **actual generated `.wld` file** and reads Terraria's binary world header. It checks the saved physical width and height against the preset you picked:

```text
XL     must be 10,600 x 3,000
Huge   must be 12,600 x 3,600
THICC  must be 14,800 x 4,200
```

It no longer waits for a particular Expanded Worlds log sentence. If the `.wld` header reports the wrong dimensions, the app refuses to copy it into your Worlds folder.

## Existing world with the same filename

The app warns you first. Your old file stays untouched while the replacement generates. The overwrite happens only after the new server run reaches ready state and the generated `.wld` header itself confirms the requested dimensions.

## If it fails

Click **Show log**. The same log is also here:

```text
%LOCALAPPDATA%\gloader\ExpandedWorldMaker\last-generation.log
```

If the runtime status says **WRONG VERSION**, point **Server...** at Terraria 1.4.5.8's `TerrariaServer.exe`.
