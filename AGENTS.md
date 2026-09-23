# AGENTS.md

## Scope

This repository is the Linux-native gloader line.

Do not reintroduce Windows gloader architecture here. The Windows 0.2.x maintenance snapshot lives under \`polskiftw/gpages/gloader-windows\`.

## Non-negotiable architecture

- Public launcher: native x86-64 ELF named \`gloader\`.
- Managed loader: \`gdeps/GLoader.dll\`.
- Runtime: the Mono/FNA runtime already shipped with Linux Terraria.
- Steam launch option: \`./gloader %command%\`.
- Installation root: the existing Terraria directory.
- Mod contract: raw C# folders directly under \`gmods/\`; \`.disabled\` directories are skipped.
- The original Terraria files must remain unmodified.

Do not add:

- a public \`gloader.exe\`;
- CoreCLR Terraria hosting;
- a private converted/rebuilt Terraria tree;
- WinForms or another launcher UI;
- PowerShell/MinGit runtime builders;
- Windows DLL probing rules;
- a \`$HOME/.config/gloader\` configuration hierarchy.

## Source layout

- \`src/native/gloader.c\`: small ELF/Mono embedding host. Keep policy out of this layer.
- \`src/GLoader/\`: managed Mono loader, assembly resolution, source compilation, Harmony patching, Terraria entrypoint.
- \`gmods/\`: source mods and porting inputs.
- \`build.sh\`: produces \`dist/gloader\`, \`dist/gdeps/\`, and \`dist/gmods/\`.

## Runtime resolution

Managed resolution order is:

1. assembly already loaded in the current AppDomain;
2. requesting/mod directory when applicable;
3. Terraria installation root;
4. \`gdeps/\`.

Compile mods against the assemblies actually used by the installed game. Do not synthesize a CoreCLR reference universe.

## Build symbols

Every source mod gets:

- \`GLOADER\`
- \`GLOADER_LINUX\`
- \`GLOADER_MONO\`

and exactly one of:

- \`GLOADER_CLIENT\`
- \`GLOADER_SERVER\`

## Validation

CI must continue proving:

- \`dist/gloader\` is an x86-64 ELF;
- no \`dist/gloader.exe\` exists;
- \`dist/gdeps/GLoader.dll\` exists;
- the ELF carries relative lookup paths for the Terraria-local native libraries.

Do not claim the title-screen/runtime milestone is verified merely because CI builds. That proof requires an actual Linux Terraria installation.
