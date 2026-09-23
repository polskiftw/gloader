# AGENTS.md

## Scope

This repository is the Linux-native gloader line.

The Windows 0.2.x maintenance snapshot lives under `polskiftw/gpages/gloader-windows`. Do not reintroduce Windows architecture here.

## Non-negotiable architecture

- Public launcher: native x86-64 ELF named `gloader`.
- Native launcher behavior: exec the existing Terraria MonoKickstart ELF; do not embed or start a second runtime.
- Injection: `MONO_BUNDLED_OPTIONS=--profile=gloader` plus `gdeps/libmono-profiler-gloader.so`.
- Managed loader: `gdeps/GLoader.dll`.
- Runtime: Terraria's own embedded Mono/FNA runtime.
- Steam launch option: `./gloader %command%`.
- Installation root: the existing Terraria directory.
- Mod contract: raw C# folders directly under `gmods/`; `.disabled` directories are skipped.
- Original Terraria files remain unmodified.
- Paths are anchored to the `gloader` ELF, never a home-directory config tree.

Do not add:

- a public `gloader.exe`;
- CoreCLR Terraria hosting;
- a private converted/rebuilt Terraria runtime;
- a system Mono runtime dependency;
- WinForms or another launcher UI;
- PowerShell/MinGit runtime builders;
- Windows DLL probing rules;
- `$HOME/.config/gloader`.

## Source layout

- `src/native/gloader.c`: small native exec/environment wrapper.
- `src/native/profiler.c`: Mono profiler injected into Terraria's existing runtime; resolves Mono APIs with `dlsym(RTLD_DEFAULT, ...)`.
- `src/GLoader/`: managed resolver, Roslyn source compiler, Harmony loading, Host & Play redirect, logs.
- `third_party/mono-facades/`: pinned minimal managed compatibility facades required by Roslyn inside Terraria Mono.
- `gmods/`: historical source mods / porting inputs.
- `build.sh`: produces `dist/gloader`, `dist/gdeps/`, and `dist/gmods/`.

## Runtime flow

1. `gloader` resolves its own directory.
2. It parses gloader-only options and consumes Steam's Terraria command token.
3. It prepares `MONO_IOMAP`, gloader environment, `LD_LIBRARY_PATH`, and `MONO_BUNDLED_OPTIONS`.
4. It `execv`s `Terraria.bin.x86_64` or `TerrariaServer.bin.x86_64`.
5. Terraria's MonoKickstart host loads `libmono-profiler-gloader.so`.
6. The profiler waits for `Terraria` / `TerrariaServer`, then invokes `GLoader.Entry.Initialize()` in that same Mono AppDomain.
7. Managed GLoader installs resolution/patches/compiled mods and returns to Terraria.

Do not change this into a manual libmono embedding host unless exact Terraria/runtime evidence makes the current boundary impossible.

## Assembly resolution

The exact Terraria 1.4.5.8 Linux decomp shows `LinuxLaunch.Main()` resolves private managed libraries from manifest resources by matching a suffix of `<requested simple name>.dll` and calling `Assembly.Load(byte[])`.

GLoader mirrors that behavior because it injects before Terraria's normal bootstrap resolver is guaranteed to have run.

Resolution order:

1. preferred already-loaded Terraria target;
2. already-loaded matching assembly;
3. matching embedded target resource;
4. requesting assembly directory;
5. registered mod directories;
6. Terraria root;
7. `gdeps/`.

Embedded images must also remain available to Roslyn as metadata references.

## Roslyn compatibility payload

Roslyn 2.10 is intentionally used as the in-runtime compiler because newer Roslyn payloads bring compatibility contracts that do not fit Terraria's Mono profile.

The required Mono facade set is pinned under `third_party/mono-facades/`.

Rules:

- verify `SHA256SUMS` during build;
- package those exact DLLs into `gdeps/`;
- do not depend on `/usr/lib/mono` at install/runtime;
- do not replace them with .NET Framework reference assemblies;
- do not ship a second Mono runtime.

## Host & Play

The exact client decomp proves Linux `Main.HostAndPlay()` sets the dedicated server filename to `TerrariaServer`, and direct/Steam/WeGame paths converge on instance `System.Diagnostics.Process.Start()`.

Patch that instance method only.

When the pending process basename begins with `TerrariaServer`, rewrite it to:

```text
<root>/gloader --server -- <original server args>
```

Preserve the Terraria root as working directory. Leave unrelated processes untouched.

## Build symbols

Every source mod gets:

- `GLOADER`
- `GLOADER_LINUX`
- `GLOADER_MONO`

and exactly one of:

- `GLOADER_CLIENT`
- `GLOADER_SERVER`

## Validation

Normal CI must prove:

- `dist/gloader` is x86-64 ELF;
- `dist/gdeps/libmono-profiler-gloader.so` exports `mono_profiler_init_gloader`;
- no public `dist/gloader.exe`;
- neither native artifact links libmono;
- all pinned facade hashes verify and packaged copies are identical;
- wrapper handoff and profiler smoke tests pass.

Exact Steam 1.4.5.8 integration has already proven server raw-source compile/load plus client attachment/Host & Play installation. Do not regress those semantics.

Do not claim graphical title-screen acceptance from a headless CI run. That remains a real desktop test.
