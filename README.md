# gloader

gloader is a native Linux/Mono raw C# source mod loader for the Linux Steam build of Terraria.

This repository is the Linux line. The former Windows 0.2.x implementation has been moved to the \`gloader-windows/\` maintenance snapshot in \`polskiftw/gpages\`.

## Product contract

Everything installs directly into the existing Terraria directory:

\`\`\`text
Terraria/
├── Terraria.bin.x86_64
├── Terraria.exe
├── FNA.dll
├── lib64/
├── gloader
├── gdeps/
│   ├── GLoader.dll
│   ├── 0Harmony.dll
│   ├── Microsoft.CodeAnalysis*.dll
│   └── ...
└── gmods/
\`\`\`

Normal Steam use is one launch option:

\`\`\`text
./gloader %command%
\`\`\`

There is no public \`gloader.exe\`, no launcher UI, no private rebuilt Terraria runtime, and no per-user gloader configuration tree.

## Build

Requirements for building the loader itself:

- a C compiler
- .NET SDK 10

Run:

\`\`\`bash
./build.sh
\`\`\`

The package is written to \`dist/\`.

The native \`gloader\` ELF does **not** link against a system Mono. At runtime it locates and dynamically loads the Mono runtime shipped with the Terraria installation, then invokes \`gdeps/GLoader.dll\`.

## Install

Copy the contents of \`dist/\` into the Linux Terraria installation directory. Do not replace or modify Terraria's own files.

Then set Terraria's Steam launch options to:

\`\`\`text
./gloader %command%
\`\`\`

For a no-mods diagnostic run:

\`\`\`text
./gloader --vanilla %command%
\`\`\`

The dedicated-server loader path is:

\`\`\`text
./gloader --server -- <Terraria server arguments>
\`\`\`

## gmods

A raw source mod is enabled when its directory is directly under \`gmods/\`:

\`\`\`text
gmods/Foo/
\`\`\`

Rename the directory to disable it:

\`\`\`text
gmods/Foo.disabled/
\`\`\`

At startup, enabled mods are compiled against the assemblies actually present in the running Linux Terraria/Mono installation. Linux builds define:

- \`GLOADER\`
- \`GLOADER_LINUX\`
- \`GLOADER_MONO\`
- \`GLOADER_CLIENT\` or \`GLOADER_SERVER\`

The historical gmods remain in this repository as porting inputs. They are deliberately **not** copied into \`dist/gmods\` automatically until their Linux compatibility is verified. In particular, Radio's old Windows decoder path is a separate porting workstream, and the archived Windows Expanded Worlds release remains in \`gpages/gloader-windows/gmods/ExpandedWorlds\`.

## Architecture

See \`DGD.md\` for the canonical Linux design and implementation status.

GitHub Actions proves that the repository produces a real x86-64 ELF plus the managed loader/package shape. The decisive runtime proof still requires an installed Linux copy of Terraria: Steam -> \`./gloader %command%\` -> bundled Mono -> untouched Terraria title screen.
