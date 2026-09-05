# tModLoader developer tools

This repository tracks the full tModLoader `1.4.5` development workspace as a git submodule at `tools/tmodloader-dev`.

Upstream: https://github.com/tModLoader/tModLoader

Pinned upstream commit: `2534f5682a46661c9aec633bea0852020e4fa796`

## Why the full workspace is included

The tModLoader Setup GUI is not a standalone patch editor. It operates on the surrounding tModLoader workspace and uses the repository's `setup/`, `patches/`, and generated `src/` layout. It also references PatchReviewer and DiffPatch internally.

The useful developer-facing buttons include:

- Setup
- Decompile
- Patch Terraria
- Diff Terraria
- Patch TerrariaNetCore
- Diff TerrariaNetCore
- Patch tModLoader
- Diff tModLoader
- Regenerate Source

The Tools menu also exposes utilities such as the formatter, HookGen, simplifier, server decompiler, and localization updater.

## Build in GitHub

Run **tModLoader Dev Tools** from the Actions tab. Repository updates to the submodule or workflow should follow the normal branch-first process described in `AGENTS.md`: make the change on a temporary branch, validate it there, then merge it to `main` through a pull request.

The workflow:

1. Checks out gloader and all nested submodules recursively.
2. Publishes `setup/GUI/Setup.GUI.csproj` as a self-contained Windows x64 application using .NET 10.
3. Copies the complete pinned tModLoader workspace into the artifact while removing git metadata.
4. Adds `RUN-SETUP.bat`, which launches the prebuilt Setup GUI from the correct working directory.
5. Uploads `tModLoader-dev-workspace-win-x64`.

After downloading and extracting the artifact, run:

```text
RUN-SETUP.bat
```

The first setup/decompile run will ask for the installed Terraria executable/path as needed. Generated/decompiled Terraria source stays in the extracted workspace and is not committed to gloader.

## Use from a recursive gloader clone

```powershell
git clone --recursive https://github.com/polskiftw/gloader.git
cd gloader\tools\tmodloader-dev
```

The upstream `setup.bat` can build and launch Setup.GUI locally if desired. The GitHub Actions artifact exists so a local build is not required just to use the GUI.

## Update the pinned tModLoader revision

Start from current `main`, create a temporary working branch, then update the submodule:

```powershell
git switch main
git pull --ff-only
git switch -c chore/update-tmodloader-dev
git submodule update --init --recursive
git -C tools/tmodloader-dev fetch origin 1.4.5
git -C tools/tmodloader-dev checkout origin/1.4.5
git add tools/tmodloader-dev
git commit -m "Update tModLoader dev tools"
git push -u origin chore/update-tmodloader-dev
```

Then open a pull request from `chore/update-tmodloader-dev` into `main`, verify the relevant checks, and merge it when ready. GitHub can delete the merged head branch automatically.
