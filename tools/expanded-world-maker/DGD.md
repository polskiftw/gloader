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
4. Type a seed, or leave Seed blank for random.
5. Tick any special seeds you want. They can be combined.
6. Under **SAVE .WLD TO**, pick your Terraria Worlds folder.
7. Click **GENERATE WORLD**.

Typical Worlds folder:

```text
%USERPROFILE%\Documents\My Games\Terraria\Worlds
```

If Windows redirected Documents into OneDrive, the GUI uses that redirected Documents folder automatically.

## The three sizes

```text
XL     12,600 x 2,400
Huge   16,800 x 2,400
THICC  16,800 x 4,800
```

## Secret seeds

The checkboxes are the real Terraria 1.4.5 dedicated-server flags, not fake presets in the World Maker. You can tick multiple at once.

```text
Not the Bees        seed_notthebees=1
Drunk               seed_drunk=1
Celebration Mk10    seed_celebration=1
The Constant        seed_theconstant=1
For the Worthy      seed_fortheworthy=1
No Traps            seed_notraps=1
Remix               seed_remix=1
Zenith              seed_zenith=1
Skyblock            seed_skyblock=1
```

You can also type old magic values such as `getfixedboi`, `dontdigup`, `skyblock`, or `5162020` into the normal Seed box. Terraria itself interprets them.

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

## Existing world with the same filename

The app warns you first. Your old file stays untouched while the replacement generates. The overwrite happens only after the new server run reaches ready state and Expanded Worlds verifies the expected dimensions after `.wld` reload.

## If it fails

Click **Show log**. The same log is also here:

```text
%LOCALAPPDATA%\gloader\ExpandedWorldMaker\last-generation.log
```

If the runtime status says **WRONG VERSION**, point **Server...** at Terraria 1.4.5.8's `TerrariaServer.exe`.
