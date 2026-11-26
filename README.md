# gloader

gloader is a native Linux/Mono raw C# source-mod loader for the Linux Steam build of Terraria.

This repository is the Linux-native line. The former Windows 0.2.x implementation lives in the `gloader-windows/` maintenance snapshot in `polskiftw/gpages`.

## Install contract

Everything lives directly inside the existing Terraria directory:

```text
Terraria/
├── Terraria
├── Terraria.bin.x86_64
├── Terraria.exe
├── TerrariaServer
├── TerrariaServer.bin.x86_64
├── TerrariaServer.exe
├── FNA.dll
├── lib64/
├── gloader
├── gdeps/
│   ├── GLoader.dll
│   ├── libmono-profiler-gloader.so
│   ├── 0Harmony.dll
│   ├── Microsoft.CodeAnalysis*.dll
│   ├── selected Mono compatibility facades
│   └── ...
└── gmods/
```

Normal Steam use is one launch option:

```text
./gloader %command%
```

There is no public `gloader.exe`, launcher UI, private rebuilt Terraria runtime, system-Mono runtime dependency, or per-user gloader configuration tree.

## How it launches

`gloader` is a small native x86-64 ELF. It does not host or `dlopen` Mono itself.

For a normal client launch it:

1. anchors all paths to its own directory;
2. strips the Terraria executable token supplied by Steam's `%command%`;
3. sets Terraria's normal `MONO_IOMAP=all`;
4. adds `gdeps/` to the native library search path;
5. injects `--profile=gloader` through `MONO_BUNDLED_OPTIONS`;
6. directly `execv`s the existing `Terraria.bin.x86_64`.

Terraria's own MonoKickstart host then starts its embedded Mono runtime. That runtime loads `gdeps/libmono-profiler-gloader.so`. The profiler waits for the real `Terraria` or `TerrariaServer` managed assembly, loads `gdeps/GLoader.dll` into the same Mono AppDomain, and calls `GLoader.Entry.Initialize()`.

GLoader installs resolution/patching/mod support and then returns control to Terraria's normal startup.

## Build

Build requirements:

- C compiler
- .NET SDK 10

Run:

```bash
./build.sh
```

The installable package is written to `dist/`.

The repository pins 30 tiny architecture-neutral Mono compatibility facade assemblies under `third_party/mono-facades/`. Their SHA-256 hashes are checked by `build.sh`, then the exact bytes are copied into `dist/gdeps/`. They allow Roslyn 2.10 to run inside Terraria's stripped Mono framework profile; they are not a second Mono runtime.

## Dedicated server

Direct server launch:

```text
./gloader --server -- <Terraria server arguments>
```

The client also patches the instance `System.Diagnostics.Process.Start()` used by Terraria's Host & Play path. A launch whose filename is `TerrariaServer*` is rewritten to the same `gloader --server -- ...` path, preserving Terraria's original server arguments.

## Vanilla escape hatch

```text
./gloader --vanilla %command%
```

This removes the gloader profiler option and launches Terraria's native MonoKickstart host without injecting GLoader.

## gmods

An enabled raw-source mod is a directory directly under `gmods/`:

```text
gmods/Foo/
```

Rename the directory to disable it:

```text
gmods/Foo.disabled/
```

Linux source builds define:

- `GLOADER`
- `GLOADER_LINUX`
- `GLOADER_MONO`
- exactly one of `GLOADER_CLIENT` or `GLOADER_SERVER`

The historical gmods in this repository are porting inputs and are deliberately not copied into `dist/gmods/` as enabled defaults until each one is verified on Linux.

## Validation status

### Steam Linux runtime (1.4.5.8)

The Linux package has been tested in CI against the exact Steam Linux Terraria 1.4.5.8 client/server runtime used for development.

That proof covers:

- native MonoKickstart/profiler injection;
- source-aligned embedded managed-DLL resolution;
- exact dedicated-server attachment;
- raw C# mod compilation and `Mod.Load()` using only the packaged dependencies;
- client attachment;
- Host & Play redirect installation.

The headless Actions client proceeds through GLoader initialization and then stops when FNA/SDL reports that no video device exists. Reaching the actual graphical Terraria title screen remains an on-machine acceptance test.

### Standalone Linux dedicated server (1.4.6.1)

GLoader 0.3.0-alpha (commit `7fc44122dae7c9d52edf88658ca7f08dfc1779aa`) passed a smoke test against Re-Logic's [official Terraria 1.4.6.1 dedicated-server archive](https://terraria.org/api/download/pc-dedicated-server/terraria-server-1461.zip) on Linux x86-64. The stock server, GLoader's modded server, and its vanilla mode each started, accepted a local TCP connection, saved a disposable world, and shut down cleanly. The modded run compiled and loaded a minimal raw C# mod; the vanilla run did not load it. Original server files were unchanged.

This was a dedicated-server smoke test, not gameplay validation. Steam Linux client and graphical Host & Play on 1.4.6.1 have not been verified.

See `DGD.md` for the canonical architecture and implementation record.

## Licensing

Original gloader source code and documentation are licensed under the **Apache License 2.0**; see [LICENSE.md](LICENSE.md). Bundled Mono facades and other third-party dependencies remain under their own terms, as described in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Terraria and its components are not relicensed by gloader.
