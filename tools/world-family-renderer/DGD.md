# World Family Renderer — DGD

## Put it somewhere

`WorldFamilyRenderer.exe` is portable. Putting it directly in:

`Documents\My Games\Terraria\Worlds`

is fine.

## Make the pictures

1. Double-click `WorldFamilyRenderer.exe`.
2. Click **Browse...** next to **Source .wld** and pick any modern Terraria world whose seed is still stored in the file. You can also drag a `.wld` onto the window.
3. Check the line under the file. It shows the recovered seed, difficulty, evil, and size.
4. The tool tries to find your Terraria/gloader folder itself. If it does not, click **Browse...** next to **Terraria / gloader folder** and select the folder that contains `gloader.exe`.
5. Pick the **Quality**. `High — 1,920px max width` is the default.
6. The output folder defaults to the folder containing the source `.wld`. Change it if you want.
7. Click **GENERATE SIX PNGs**.

It now makes six brand-new worlds from the source seed. Nobody joins them. After each world is generated, the tool loads it through TEdit's world parser, turns it into a PNG with TEdit's palette, and deletes that temporary `.wld`.

When all six are done it also creates the big vertically stacked comparison PNG.

## What needs to exist in the Terraria folder

These have to exist:

`gloader.exe`

`gdeps\x64-runtime\TerrariaRelease.dll`

`gmods\ExpandedWorlds\...`

If your private runtime is named `TerrariaDebug.dll` or `Terraria.dll`, that is accepted too.

## What it does NOT do

- It does not edit the source world.
- It does not use the source world's existing tiles.
- It does not keep generated `.wld` files.
- It does not launch the Terraria client.
- It does not use your normal gmods for Small/Medium/Large.
- It does not use any gmod except `ExpandedWorlds` for XL/Huge/THICC.
- It does not replace TEdit's map palette with a tiny homemade Terraria palette.

## Output names

A successful run creates a folder like:

`WorldFamily_MyWorld_20260903_031500`

Inside are only:

`ExpandedWorlds_SameSeed_AllSizes.png`

`01_Small_4200x1200.png`

`02_Medium_6400x1800.png`

`03_Large_8400x2400.png`

`04_XL_10600x3000.png`

`05_Huge_12600x3600.png`

`06_THICC_14800x4200.png`
