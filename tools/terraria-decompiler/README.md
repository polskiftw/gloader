# Terraria Decompiler

A reproducible two-pass ILSpy pipeline for Terraria's Windows client **and** dedicated server.

The normal GUI workflow treats `Terraria.exe` and `TerrariaServer.exe` as a matched pair. One click decompiles both executables, gives each target its own embedded-dependency recovery pass, and produces one combined audit.

## Recommended local use

Download **`TerrariaDecompilerOffline-win-x64.zip`** from the latest gloader GitHub release. It is the optional decompiler asset beside the normal `gloader-<version>.zip` download.

Extract it and double-click:

`TerrariaDecompiler.exe`

The GUI:

- auto-detects the normal Steam Terraria install when possible;
- lets you browse directly to `Terraria.exe`;
- automatically finds `TerrariaServer.exe` beside it;
- verifies the client and server file versions match before starting;
- shows the managed/native sibling-DLL inventory;
- lets you choose one output directory;
- runs client + server from one **DECOMPILE BOTH** button;
- shows target-aware progress;
- has a cancellable run and a captured **Show Details** log;
- enables **Open Output** and **View Audit** when finished;
- reports the combined tracked issue count directly in the window.

The GUI targets .NET Framework 4.8. The private .NET 10 runtime bundled with the tool is used by ILSpy internally.

Running the finished bundle performs **no dependency downloads** and does not require separate installs of ILSpy, .NET 10, PowerShell 7, 7-Zip, XNA, or reference packs.

## Output contract

The selected output folder has three visible top-level outputs:

```text
client/
server/
audit/
```

`client/` contains:

- `source/` — decompiled `Terraria.exe` C# tree.
- `TerrariaClientDecomp-<version>-clean.zip` — clean client source ZIP.

`server/` contains:

- `source/` — decompiled `TerrariaServer.exe` C# tree.
- `TerrariaServerDecomp-<version>-clean.zip` — clean server source ZIP.

`audit/` contains:

- `audit.md` — combined client + server summary.
- `audit.json` — combined machine-readable result with `total_tracked_issues`.
- `client/audit.md` and `client/audit.json` — detailed client audit.
- `server/audit.md` and `server/audit.json` — detailed server audit.
- `reference-sources.json` — install DLL provenance plus target-specific embedded managed references.

The output directory also contains a hidden ownership marker used only to prevent destructive cleanup of arbitrary user folders.

## Reference resolution

The pipeline deliberately avoids sharing recovered embedded dependencies between client and server.

For each run:

1. **Install-directory baseline** — scan DLLs sitting beside the executables, keep valid managed .NET assemblies, and record native/non-managed DLLs as ignored for ILSpy metadata resolution.
2. **Client bootstrap pass** — decompile `Terraria.exe` with error-tolerant ILSpy, recover its embedded managed DLL resources, and canonicalize them by assembly name.
3. **Client clean pass** — decompile again using install refs + client embedded refs + bundled framework/XNA fallbacks.
4. **Server bootstrap pass** — start again from the same clean install/framework baseline and recover only `TerrariaServer.exe` embedded managed dependencies.
5. **Server clean pass** — decompile using install refs + server embedded refs + bundled fallbacks.
6. **Audit** — audit each source tree separately, then combine their counts into one top-level report.

Separate target reference directories matter because a dependency embedded only in the client must not accidentally hide a missing-reference problem in the server decompile, or vice versa.

## Terraria install DLLs and Content Pipeline

Current Terraria installations ship several DLLs beside the executables. Managed DLLs are temporarily used as metadata references; native DLLs are not useful for CLR type reconstruction.

The bundle does **not** contain `Microsoft.Xna.Framework.Content.Pipeline.dll` and contains no compatibility shim. Current Terraria installs provide the genuine assembly, which the decompiler harvests from the user's own install directory.

Nothing harvested from the Terraria install is copied into either generated source ZIP.

## Public server validation

Re-Logic publishes the Terraria dedicated server separately at `terraria.org`. The offline-bundle GitHub Actions workflow queries Re-Logic's current dedicated-server archive name, downloads that public package on the Windows runner, extracts its `TerrariaServer.exe`, performs a real server-only smoke decompile with the just-built bundle, and requires:

- at least one generated C# file;
- a successful clean pass;
- `total_tracked_issues = 0`.

The temporary server decompile is not published. The workflow publishes only the decompiler bundle artifact and updates the optional decompiler asset on the matching gloader release.

## Updating Terraria

Open the same `TerrariaDecompiler.exe` after Terraria updates. Point it at the updated `Terraria.exe`; the server executable is picked up automatically from the same directory.

If the two executable versions do not match, the GUI refuses to run rather than silently combining mismatched client/server source.

If Re-Logic adds new managed sibling dependencies, the install scanner picks them up automatically. If a future version introduces another reference or reconstruction problem, the audit should expose it instead of silently declaring the output clean.

## Internal engine

`Run-TerrariaDecompiler.ps1` is packaged beside the GUI as the internal/debug engine. Its default `TargetMode` is `Pair`, matching the GUI. Maintainer/CI-only modes are also available:

```powershell
# Normal pair
powershell -File Run-TerrariaDecompiler.ps1 `
  -TerrariaInput 'C:\Games\Terraria\Terraria.exe' `
  -OutputDirectory 'C:\Temp\TerrariaDecomp'

# Server-only smoke/debug path
powershell -File Run-TerrariaDecompiler.ps1 `
  -TerrariaInput 'C:\ServerPackage\Windows' `
  -OutputDirectory 'C:\Temp\ServerDecomp' `
  -TargetMode Server
```

The repository also retains `Invoke-TerrariaDecompile.ps1` and `Prepare-References.ps1` as the online/bootstrap maintainer path for rebuilding references and diagnosing toolchain changes.

## Pinned bundle inputs

- ILSpyCmd: `11.0.0.9375`
- Bundled .NET runtime: `10.0.11` win-x64
- GUI target: `.NET Framework 4.8` WinForms
- Microsoft .NET Framework reference assemblies: `net40 1.0.3`
- Microsoft XNA Framework Redistributable: `4.0 Refresh`

## Files

- `offline/Build-OfflineBundle.ps1` — assembles the self-contained Windows bundle.
- `offline/Invoke-Offline.ps1` — network-free paired runtime engine.
- `offline/Audit-Offline.ps1` — per-target reconstruction audit.
- `offline/Gui/` — `TerrariaDecompiler.exe` WinForms source.
- `Invoke-TerrariaDecompile.ps1` — online/bootstrap maintainer decompile path.
- `Prepare-References.ps1` — prepares maintainer fallback references.
- `Audit-Decompile.ps1` — maintainer-path audit.
- `DGD.md` — short operational instructions.
