# gloader Linux Native Port - DGD

## Status

The repository has been split by platform.

- Windows 0.2.x is a maintenance line in \`polskiftw/gpages/gloader-windows\`.
- This repository is the Linux-native line.
- The implementation architecture is now native ELF + Terraria-bundled Mono + managed \`GLoader.dll\`.
- Build/packaging proof is automated in GitHub Actions.
- Title-screen proof on an actual Linux Terraria installation is still an on-machine acceptance test.

## User experience

Normal launch is:

\`\`\`text
Steam -> Terraria -> Play
        -> ./gloader %command%
        -> Terraria-bundled Mono
        -> gdeps/GLoader.dll
        -> Terraria.exe
        -> compile gmods
        -> Harmony
        -> modded Terraria
\`\`\`

There is no separate launcher and no normal-use terminal step.

All paths are anchored to the directory containing the \`gloader\` ELF, not Steam's working directory.

## Installed layout

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
│   └── loader/runtime dependencies
└── gmods/
\`\`\`

The loader never creates a public \`gloader.exe\`.

## Native host

\`src/native/gloader.c\` is intentionally small.

Responsibilities:

1. Resolve \`/proc/self/exe\` and treat its directory as the Terraria root.
2. Change cwd to that root.
3. Find and \`dlopen\` Terraria's bundled \`libmonosgen-2.0\` / \`libmono-2.0\`.
4. Configure the bundled Mono assembly directory when present.
5. Parse Terraria's \`monoconfig\` when present.
6. Initialize Mono profile \`v4.0.30319\`.
7. Load \`gdeps/GLoader.dll\`.
8. Invoke \`GLoader.Entry.Run()\`.
9. Return the managed/game exit code to Steam.

The host does not link to or require a system Mono development package. Its ELF RPATH is relative to the Terraria directory:

\`\`\`text
$ORIGIN/lib64:$ORIGIN/lib:$ORIGIN
\`\`\`

Steam arguments are passed to managed code through a process-local hex-encoded environment value so arbitrary UTF-8 argv entries survive the embedding boundary without inventing a public managed bootstrap executable.

## Managed loader

\`src/GLoader\` targets the Mono-compatible .NET Framework API surface instead of CoreCLR.

Startup:

1. Decode the original Steam arguments.
2. Parse gloader options.
3. Install \`AppDomain.AssemblyResolve\`.
4. Locate the actual Linux \`Terraria.exe\` or dedicated-server managed target.
5. Load it into the current Mono AppDomain.
6. Discover enabled source mods.
7. Compile each mod against loaded Mono/BCL assemblies plus the installed Terraria/FNA/gdeps assemblies.
8. Invoke optional \`Mod.Load()\`.
9. Apply Harmony patches.
10. Invoke Terraria's own managed entry point.

## Assembly resolution

Resolution order:

1. already loaded assembly;
2. explicitly preferred current Terraria target;
3. requesting assembly directory;
4. registered mod directories;
5. Terraria root;
6. \`gdeps/\`.

This deliberately replaces the Windows line's \`AssemblyLoadContext\`, \`.deps.json\`, RID probing, and private CoreCLR runtime logic.

## Mod contract

Enabled:

\`\`\`text
gmods/Foo/
\`\`\`

Disabled:

\`\`\`text
gmods/Foo.disabled/
\`\`\`

Individual \`*.disabled.cs\` files are also ignored.

Compile symbols:

\`\`\`text
GLOADER
GLOADER_LINUX
GLOADER_MONO
GLOADER_CLIENT
\`\`\`

or, for dedicated server:

\`\`\`text
GLOADER
GLOADER_LINUX
GLOADER_MONO
GLOADER_SERVER
\`\`\`

A mod-specific directory is included in resolution/reference scanning so a source mod can carry explicit local managed dependencies.

## Steam semantics

Product launch option:

\`\`\`text
./gloader %command%
\`\`\`

gloader strips the original Terraria executable token supplied by \`%command%\` and forwards the remaining game arguments to Terraria's managed entry point.

Vanilla diagnostic escape hatch:

\`\`\`text
./gloader --vanilla %command%
\`\`\`

This skips source mods without renaming anything.

## Dedicated server and Host & Play

Direct server form:

\`\`\`text
./gloader --server -- <server arguments>
\`\`\`

The client installs a best-effort Harmony redirect around Terraria's Host & Play process launcher. When Terraria attempts to start a \`TerrariaServer*\` executable, that process request is rewritten to:

\`\`\`text
gloader --server -- <original arguments>
\`\`\`

The same Terraria root and \`gmods/\` are therefore used on both sides.

The redirect is intentionally best-effort: a future Terraria change to the launch method should warn rather than prevent the client from reaching the title screen.

## What was removed from this repository

The Linux clean break intentionally removed:

- WinForms launcher code;
- CoreCLR \`AssemblyLoadContext\` hosting;
- private x64 Terraria runtime building;
- PowerShell build/runtime machinery;
- Windows apphost patching;
- Windows native DLL search logic;
- old Windows-only CI/test/tool trees.

The source gmods remain because they are the inputs for the Linux porting work.

## Bundled-mod status

The Linux loader architecture is not the same thing as declaring every historical gmod Linux-ready.

- Infinite Angler: expected low porting work; needs real runtime validation.
- No Liquid Dupe: expected low porting work; needs signature/server validation.
- DVD Logo: expected low porting work; verify FNA/resource assumptions.
- Radio: separate workstream; replace the old Windows Media Foundation/NAudio decode path.
- Expanded Worlds: the known-good Windows version is preserved with Windows gloader in \`gpages\`; Linux compatibility is not asserted by the loader rewrite.

For that reason \`build.sh\` creates the installation \`gmods/\` directory but does not silently ship the repository's unverified historical mods as enabled Linux defaults.

## Acceptance ladder

Implemented in source/CI:

- [x] public launcher source is a native x86-64 Linux ELF host;
- [x] managed implementation is \`gdeps/GLoader.dll\`;
- [x] paths are anchored to the ELF location;
- [x] bundled Mono is dynamically loaded rather than system Mono/CoreCLR;
- [x] raw gmod discovery and folder enable/disable contract;
- [x] Mono/AppDomain assembly resolver;
- [x] Linux/Mono/client/server compile symbols;
- [x] Roslyn source compilation and Harmony loading path;
- [x] \`--vanilla\` path;
- [x] direct dedicated-server path;
- [x] Host & Play redirect implementation;
- [x] GitHub Actions ELF/package verification.

Requires real Linux Terraria installation:

- [ ] Steam \`./gloader %command%\` reaches untouched Terraria title screen.
- [ ] harmless Harmony patch executes in a real client run.
- [ ] tiny raw-source gmod compiles and executes in a real client run.
- [ ] dedicated server reaches normal startup.
- [ ] Host & Play starts the server through gloader.
- [ ] each retained historical gmod is audited/ported individually.

## Design rule

Prefer Linux-native simplicity over cross-platform symmetry. Do not add an abstraction solely to preserve a detail of the archived Windows implementation.
