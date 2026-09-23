# gloader Linux Native Port - DGD

## Status

The repository is the Linux-native gloader line.

- Windows 0.2.x is preserved separately under `polskiftw/gpages/gloader-windows`.
- Public entrypoint: native x86-64 ELF `gloader`.
- Game runtime: the Mono/FNA runtime already shipped with Linux Terraria.
- Managed loader: `gdeps/GLoader.dll`.
- Injection boundary: Terraria's own MonoKickstart host plus a native Mono profiler.
- Raw C# compiler: Roslyn 2.10 running inside Terraria's Mono runtime.
- Compatibility payload: 30 pinned Mono facade assemblies, not a second Mono runtime.
- Exact Steam Linux Terraria 1.4.5.8 client/server integration has been exercised in CI.
- The final graphical-title-screen check remains a real-machine test because GitHub Actions has no video device.

## Product rules

Normal use:

```text
Steam -> ./gloader %command%
      -> Terraria.bin.x86_64
      -> Terraria embedded Mono
      -> libmono-profiler-gloader.so
      -> GLoader.dll
      -> return to Terraria startup
      -> modded Terraria
```

Everything is installed directly in the existing Terraria directory.

Do not introduce:

- a public `gloader.exe`;
- a private CoreCLR/runtime conversion of Terraria;
- system Mono as an install/runtime requirement;
- a launcher GUI;
- Windows DLL probing/build concepts;
- a `$HOME/.config/gloader` tree.

## Installed layout

```text
Terraria/
├── Terraria
├── Terraria.bin.x86_64
├── Terraria.exe
├── TerrariaServer
├── TerrariaServer.bin.x86_64
├── TerrariaServer.exe
├── monoconfig
├── monomachineconfig
├── FNA.dll
├── lib64/
├── gloader
├── gdeps/
│   ├── GLoader.dll
│   ├── libmono-profiler-gloader.so
│   ├── 0Harmony.dll
│   ├── Microsoft.CodeAnalysis.dll
│   ├── Microsoft.CodeAnalysis.CSharp.dll
│   ├── System.Collections.Immutable.dll
│   ├── System.Reflection.Metadata.dll
│   ├── selected System.* Mono facades
│   └── notices/licenses
└── gmods/
```

## Native launcher

`src/native/gloader.c` is an exec wrapper, not a Mono embedding host.

Responsibilities:

1. resolve `/proc/self/exe` and use its directory as the Terraria root;
2. change cwd to that root;
3. parse gloader-only options;
4. consume Steam's Terraria executable token when supplied by `%command%`;
5. choose `Terraria.bin.x86_64` or `TerrariaServer.bin.x86_64`;
6. set `MONO_IOMAP=all`;
7. set `GLOADER_ROOT`, `GLOADER_MODE`, and loader state for injected launches;
8. prepend `gdeps/` to `LD_LIBRARY_PATH`;
9. preserve existing `MONO_BUNDLED_OPTIONS`, remove stale gloader profiler options, and add exactly one `--profile=gloader`;
10. `execv` the original Terraria MonoKickstart ELF with the original game arguments.

`--vanilla`/ `--no-mods` removes the gloader profiler injection and clears gloader-specific environment state before executing Terraria.

The launcher neither links nor dynamically loads a system Mono library.

## Native profiler

`src/native/profiler.c` is loaded by Terraria's already-running embedded Mono through `--profile=gloader`.

It deliberately does not link libmono. Instead it resolves the required `mono_*` API exports from `RTLD_DEFAULT`.

The profiler registers an assembly-loaded callback. When `Terraria` or `TerrariaServer` loads, it:

1. points Mono's assembly search path at the Terraria root plus `gdeps/`;
2. opens `gdeps/GLoader.dll` in the current root domain;
3. finds `GLoader.Entry.Initialize()`;
4. invokes it inside the same Mono runtime;
5. returns control to MonoKickstart when initialization succeeds.

There is only one Mono runtime in the process: Terraria's.

## Managed loader startup

`GLoader.Entry.Initialize()` runs while the Terraria target assembly is already loaded.

Startup:

1. resolve Terraria root and `gdeps/`/`gmods/`;
2. initialize logging;
3. install GLoader's `AppDomain.AssemblyResolve` handler;
4. find the already-loaded `Terraria` or `TerrariaServer` assembly;
5. index that target's embedded managed-DLL resource names;
6. on client, install Host & Play process redirection;
7. discover enabled source mods;
8. compile mods with Roslyn against the running framework, Terraria, FNA, gdeps, local mod dependencies, and embedded game-library images;
9. invoke optional static `Mod.Load()`;
10. apply Harmony patches;
11. return to Terraria's Mono host.

GLoader does not invoke Terraria's `Main` itself.

## Terraria-aligned embedded assembly resolution

The exact 1.4.5.8 Linux client/server decomp shows `Terraria.LinuxLaunch.Main()` installs an `AssemblyResolve` handler that:

1. obtains the requested simple assembly name;
2. appends `.dll`;
3. finds a manifest resource whose name ends with that filename;
4. reads the resource bytes;
5. returns `Assembly.Load(bytes)`.

GLoader mirrors that suffix-based behavior because it attaches before Terraria's own managed bootstrap has necessarily resolved all private libraries.

GLoader resolution order is:

1. explicitly preferred already-loaded Terraria target;
2. any already-loaded assembly with the requested simple name;
3. matching embedded `*.dll` resource from the target assembly;
4. requesting assembly directory;
5. registered mod directories;
6. Terraria root;
7. `gdeps/`.

The same embedded resource bytes are available as Roslyn metadata references, so source mods can compile against libraries that Terraria carries only as embedded resources.

## Roslyn compatibility facades

Roslyn 2.10's full-framework assemblies reference contract assemblies such as `System.Runtime` that Terraria's compact Linux Mono payload does not ship as standalone files.

GLoader therefore pins a minimal set of 30 Mono facade assemblies under:

```text
third_party/mono-facades/
```

Properties:

- architecture-neutral managed DLLs;
- sourced from Mono 6.8.0.105+dfsg-3.6ubuntu2;
- exact SHA-256 list retained in `SHA256SUMS`;
- verified by `build.sh`;
- copied into `gdeps/`;
- total payload is small;
- no Mono runtime/native engine is included.

The set was derived from the direct `System.*` contracts referenced by GLoader's Roslyn payload, intersected with Mono's runtime facade directory, then proven by compiling and executing a raw C# mod inside the exact Steam Linux TerrariaServer 1.4.5.8 runtime.

## Host & Play

The exact client decomp shows `Main.HostAndPlay()` creates a `System.Diagnostics.Process`.

On Linux it sets:

```text
ProcessStartInfo.FileName = "TerrariaServer"
```

The direct path calls instance `Process.Start()`. Steam and WeGame append their network arguments and also converge on the same instance `Process.Start()`.

Therefore GLoader patches only that proven convergence point.

Immediately before an instance `Process.Start()`, if the requested executable basename starts with `TerrariaServer`, GLoader rewrites:

```text
FileName         -> <Terraria root>/gloader
WorkingDirectory -> <Terraria root>
Arguments        -> --server -- <Terraria's original arguments>
```

Unrelated process launches are untouched.

## Mod contract

Enabled:

```text
gmods/Foo/
```

Disabled:

```text
gmods/Foo.disabled/
```

Individual `*.disabled.cs` files are ignored.

Compiler symbols:

Client:

```text
GLOADER
GLOADER_LINUX
GLOADER_MONO
GLOADER_CLIENT
```

Server:

```text
GLOADER
GLOADER_LINUX
GLOADER_MONO
GLOADER_SERVER
```

## Dedicated server

Direct form:

```text
./gloader --server -- <Terraria server arguments>
```

The same Terraria installation and `gmods/` tree are used for direct server and Host & Play launches.

## Validation record

Automated normal CI proves:

- `dist/gloader` is an x86-64 ELF;
- no public `gloader.exe` exists;
- the native launcher/profiler do not link system Mono;
- wrapper/argument/environment behavior;
- native profiler attach behavior;
- pinned facade hashes and packaged bytes.

The completed private exact-runtime proof against Steam Linux Terraria 1.4.5.8 proves:

- exact MonoKickstart host exports the profiler APIs GLoader uses;
- GLoader attaches to the exact client and server assemblies;
- embedded private libraries resolve with the source-aligned resolver;
- a raw C# server mod compiles and its `Mod.Load()` executes using only the packaged dependency set;
- client initialization reaches completion;
- Host & Play redirection installs successfully;
- the headless client then proceeds into FNA and stops only because Actions has no SDL video device.

Still requires the developer machine:

- [ ] graphical client reaches the untouched title screen;
- [ ] harmless real-client mod/Harmony behavior is observed in-game;
- [ ] Host & Play is exercised by clicking through the UI and spawning the server;
- [ ] historical gmods are audited/ported individually.

## Historical gmods

The loader architecture being Linux-ready does not imply every old Windows gmod is Linux-ready.

Historical gmods remain source/porting inputs and are not silently shipped enabled in `dist/gmods/`. Radio remains a distinct porting task because its former Windows media path is not a Linux implementation.

## Design rule

Prefer the actual Linux Terraria control flow over cross-platform symmetry or inherited Windows abstractions. When exact Terraria Linux source/runtime evidence exists, use it instead of heuristics.
