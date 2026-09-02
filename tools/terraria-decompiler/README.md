# Terraria Decompiler

A reproducible two-pass ILSpy pipeline for Terraria's Windows install.

Terraria references Microsoft XNA 4.0, embeds several managed dependency DLLs inside `Terraria.exe`, and also ships some managed assemblies beside the executable. Decompiling only the EXE can therefore produce misleading `Unknown result type` diagnostics and malformed C# when the references are not resolved.

The preferred pipeline now resolves references in this order:

1. **Terraria install directory** — scan DLLs sitting next to `Terraria.exe`, keep every valid managed .NET assembly, and ignore native DLLs for ILSpy metadata resolution.
2. **Terraria embedded managed DLLs** — bootstrap ILSpy pass extracts managed DLL resources from `Terraria.exe` and canonicalizes them by real assembly name.
3. **Bundled/prepared framework references** — .NET Framework 4.0 and redistributable XNA runtime metadata fill the remaining platform references.
4. **Clean ILSpy pass** — decompile again with the combined reference directory.

It then writes both machine-readable and human-readable audits of common decompiler artifacts plus a reference-provenance report.

## Recommended local use: offline bundle

Use **TerrariaDecompilerOffline-win-x64.zip** from the rolling **Terraria Decompiler Offline** GitHub Release produced by `.github/workflows/terraria-decompiler-offline.yml`.

The finished Windows x64 bundle is self-contained. Running it requires only Windows; it does **not** download dependencies and does not require you to separately install:

- ILSpy
- .NET 10
- PowerShell 7
- 7-Zip
- XNA / XNA Game Studio
- .NET Framework reference packs

The bundle contains a private .NET runtime, ILSpyCmd, .NET Framework 4.0 reference assemblies, and the redistributable XNA Framework 4.0 runtime assemblies.

It does **not** contain `Microsoft.Xna.Framework.Content.Pipeline.dll` and does **not** contain a compatibility shim. Current Terraria installs ship the genuine Microsoft Content Pipeline assembly alongside `Terraria.exe`; the decompiler temporarily harvests that managed DLL from the user's own game directory like any other sibling managed assembly.

### Easiest use

1. Extract `TerrariaDecompilerOffline-win-x64.zip`.
2. Drag either `Terraria.exe` **or the Terraria install folder** onto `RUN-DECOMPILER.cmd`.
3. Open the generated `output` folder.

If Terraria uses the normal Steam path, double-clicking `RUN-DECOMPILER.cmd` with no argument also works.

The result includes:

- `output/source/` — decompiled source tree.
- `output/audit/audit.md` and `audit.json` — decompiler-artifact audit.
- `output/audit/reference-sources.json` — which sibling DLLs were accepted as managed references, which native DLLs were ignored, and which embedded managed DLLs were recovered.
- `output/TerrariaDecomp-<detected-version>-clean.zip` — clean source ZIP.

The source ZIP contains only the decompiled source. DLLs harvested from the user's Terraria install are temporary reference inputs and are not copied into the source ZIP.

The version comes from the supplied executable, so the same bundle can be used when Terraria updates. If Re-Logic adds another managed sibling dependency later, the tool should pick it up automatically without a decompiler update. If a future update introduces some other reference or reconstruction issue, the audit should expose it instead of silently declaring the output clean.

## Native DLLs

The install scan intentionally attempts to load every sibling `.dll` as a managed assembly. Files such as `ReLogic.Native.dll`, `nfd.dll`, Logitech/Corsair SDK DLLs, and other native binaries fail that managed-assembly check and are recorded as ignored native/non-managed DLLs. They can matter to Terraria at runtime, but they do not provide CLR type metadata that improves ILSpy's C# reconstruction.

## Maintainer / rebuild path

`Invoke-TerrariaDecompile.ps1` and `Prepare-References.ps1` remain as the reproducible online/bootstrap path. The maintainer script now uses the same install-first sibling-DLL harvesting before its embedded-DLL pass.

That path requires Windows, PowerShell 7, .NET 10, and 7-Zip and may download the pinned reference/tool packages while it runs.

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 `
  -TerrariaInput 'C:\Games\Terraria\Terraria.exe' `
  -OutputDirectory './artifacts/terraria-decompile'
```

`TerrariaInput` may be a `Terraria.exe`, a Terraria install directory, or a ZIP containing `Terraria.exe` and any sibling DLLs you want harvested.

## GitHub Actions

There are two workflows:

- `.github/workflows/terraria-decompiler-offline.yml` builds and republishes the self-contained offline Windows x64 bundle. Network access is used only while **building the bundle**; the finished bundle performs no dependency downloads.
- `.github/workflows/terraria-decompiler.yml` is the reference/decompiler smoke-test workflow. With a Terraria URL it can temporarily decompile the supplied EXE/ZIP and publish only the audit report. It deliberately does not publish Terraria's decompiled source from this public repository.

For the second workflow, private input URLs should use the repository secret `TERRARIA_BINARY_URL`; `TERRARIA_BINARY_SHA256` can optionally pin the exact input bytes.

## Pinned tool inputs

- ILSpyCmd: `11.0.0.9375`
- Bundled .NET runtime: `10.0.11` win-x64
- Microsoft .NET Framework reference assemblies: `net40 1.0.3`
- Microsoft XNA Framework Redistributable: `4.0 Refresh`

## Files

- `Invoke-TerrariaDecompile.ps1` — two-pass maintainer decompile pipeline with install-directory reference harvesting.
- `Prepare-References.ps1` — obtains/extracts the online fallback reference set.
- `Audit-Decompile.ps1` — artifact audit for the maintainer path.
- `offline/Build-OfflineBundle.ps1` — assembles the self-contained Windows bundle.
- `offline/Invoke-Offline.ps1` — network-free runtime decompiler with install-directory reference harvesting.
- `offline/Audit-Offline.ps1` — Windows PowerShell compatible offline audit.
- `DGD.md` — short operational instructions.
