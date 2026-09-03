# gloader x64 Terraria runtime

`gloader.exe` is now a 64-bit .NET 10 host. Stock Terraria 1.4.5.8 is still a legacy 32-bit/XNA managed executable, so it cannot be loaded into that process directly.

This tool builds a **private, user-derived 64-bit Terraria runtime** from the Terraria installation you already own. gloader does not ship Terraria source or a rebuilt Terraria binary.

## What it borrows

The builder pins `gold-meridian/terraria-unified` tag `v0.3.3` at commit:

```text
f98c9a42a59c15022cea3f6ad3750d1f85578f61
```

That tag targets Terraria 1.4.5.8.

The builder stops after the upstream patch stages:

```text
vanilla decompile
    -> patch terraria
    -> patch netcore
    -> build
```

It intentionally does **not** run:

```text
patch unified
patch tml
```

So we are borrowing the modern .NET/FNA platform port, not Terraria Unified gameplay/QoL changes and not tModLoader's mod system.

## Result

The private runtime is installed under:

```text
gdeps\x64-runtime\
```

The important managed target is:

```text
gdeps\x64-runtime\TerrariaRelease.dll
```

When present, gloader selects it automatically. The same managed Terraria build acts as client or dedicated server; server mode is selected with Terraria's `-server` launch parameter.

## One-time build

Requirements on the Windows machine performing the build:

- the normal Steam Terraria 1.4.5.8 installation, including `Terraria.exe` and `TerrariaServer.exe`
- Git
- .NET 10 SDK

From the Terraria directory after extracting gloader:

```powershell
powershell -ExecutionPolicy Bypass -File .\gdeps\tools\x64-runtime\Build-X64Runtime.ps1 -TerrariaDirectory .
```

The script:

1. verifies the local Terraria installation;
2. clones the exact pinned upstream workspace into `%LOCALAPPDATA%\gloader\x64-runtime-workspace`;
3. decompiles your own Terraria executable;
4. applies only the vanilla cleanup and TerrariaNetCore platform patches;
5. builds Release with .NET 10/FNA;
6. redirects the upstream install target into `gdeps\x64-runtime` instead of overwriting Steam Terraria;
7. writes `gloader-x64-runtime.json` with the source Terraria SHA-256 and the exact upstream revision used.

The original Steam `Terraria.exe` is left in place and untouched.

## Why not just mark Terraria.exe x64?

The old executable is built against the legacy .NET Framework/XNA stack. The memory ceiling is not fixed by changing a PE bit. The real port replaces that platform layer with modern CoreCLR/FNA and matching native libraries, which is the same class of conversion that made modern tModLoader 64-bit.
