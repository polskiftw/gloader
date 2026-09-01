# PatchReviewer

This repository tracks the tModLoader PatchReviewer developer tool as a git submodule at `tools/patchreviewer`.

Upstream: https://github.com/Chicken-Bones/PatchReviewer

Pinned upstream commit: `b4e30aeefa8f11dad99d1c97a31447c5b6cd428f`

## Build in GitHub

Run the **PatchReviewer** workflow from the Actions tab, or update the submodule/workflow on `main`.

The workflow uses a Windows runner, checks out submodules recursively (including PatchReviewer's DiffPatch dependency), installs .NET 10, and publishes a self-contained Windows x64 build.

Download the `PatchReviewer-win-x64` artifact from the completed workflow run and launch `PatchReviewer.exe` from the extracted folder.

## Update the pinned PatchReviewer revision locally

```powershell
git submodule update --init --recursive
git -C tools/patchreviewer fetch origin master
git -C tools/patchreviewer checkout origin/master
git add tools/patchreviewer
git commit -m "Update PatchReviewer"
git push
```
