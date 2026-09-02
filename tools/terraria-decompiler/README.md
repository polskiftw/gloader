# Terraria Decompiler

A reproducible two-pass ILSpy pipeline for Terraria's Windows `.exe`.

Terraria references Microsoft XNA 4.0 and embeds several managed dependency DLLs inside `Terraria.exe`. Decompiling only the EXE can therefore produce huge numbers of misleading `Unknown result type` diagnostics and malformed C# when the references are not resolved.

The pipeline performs two passes:

1. A bootstrap ILSpy pass extracts Terraria's embedded managed DLL resources.
2. A clean pass runs ILSpy again with those embedded libraries plus the required framework/XNA metadata references.

It then writes both machine-readable and human-readable audits of common decompiler artifacts.

## Recommended local use: offline bundle

Use the **Terraria Decompiler Offline Bundle** produced by `.github/workflows/terraria-decompiler-offline.yml`.

The finished Windows x64 bundle is self-contained. Running it requires only Windows; it does **not** download dependencies and does not require you to separately install:

- ILSpy
- .NET 10
- PowerShell 7
- 7-Zip
- XNA / XNA Game Studio
- .NET Framework reference packs

The bundle already contains a private .NET runtime, ILSpyCmd, .NET Framework 4.0 reference assemblies, and the redistributable XNA Framework 4.0 runtime assemblies.

The XNA Game Studio `Microsoft.Xna.Framework.Content.Pipeline.dll` developer binary is **not redistributed**. The bundle instead contains a tiny gloader-built metadata-only compatibility assembly with the public type/member signatures Terraria currently references. It has no Content Pipeline implementation.

### Easiest use

1. Extract `TerrariaDecompilerOffline-win-x64.zip`.
2. Drag `Terraria.exe` onto `RUN-DECOMPILER.cmd`.
3. Open the generated `output` folder.

If Terraria uses the normal Steam path, double-clicking `RUN-DECOMPILER.cmd` with no argument also works.

The result includes:

- `output/source/` — decompiled source tree.
- `output/audit/audit.md` and `audit.json` — decompiler-artifact audit.
- `output/TerrariaDecomp-<detected-version>-clean.zip` — clean source ZIP.

The version comes from the supplied executable, so the same bundle can be used when Terraria updates. If a future update introduces a new dependency or ILSpy reconstruction issue, the audit should expose it instead of silently declaring the output clean.

### Content Pipeline shim fidelity

Terraria 1.4.5.8 references the Content Pipeline API from only `Terraria.Testing/FxReader.cs`. With the public-safe metadata shim, that file can contain redundant compiler-safe casts around `EffectProcessor.Process`; the worldgen, networking, save/load, gameplay, and other Terraria source do not reference that developer assembly. The audit still reports zero unresolved-type/decompiler-error diagnostics for the current game.

## Maintainer / rebuild path

`Invoke-TerrariaDecompile.ps1` and `Prepare-References.ps1` remain as the reproducible online/bootstrap path. They are useful for rebuilding the offline package, investigating a future dependency change, or reproducing exactly how the reference set was obtained.

That path requires Windows, PowerShell 7, .NET 10, and 7-Zip and may download the pinned reference/tool packages while it runs.

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 `
  -TerrariaInput 'C:\Games\Terraria\Terraria.exe' `
  -OutputDirectory './artifacts/terraria-decompile'
```

`TerrariaInput` may also be a ZIP containing `Terraria.exe`.

## GitHub Actions

There are two workflows:

- `.github/workflows/terraria-decompiler-offline.yml` builds the self-contained offline Windows x64 bundle. Network access is used only while **building the bundle**; the finished bundle performs no dependency downloads.
- `.github/workflows/terraria-decompiler.yml` is the reference/decompiler smoke-test workflow. With a Terraria URL it can temporarily decompile the supplied EXE/ZIP and publish only the audit report. It deliberately does not publish Terraria's decompiled source from this public repository.

For the second workflow, private input URLs should use the repository secret `TERRARIA_BINARY_URL`; `TERRARIA_BINARY_SHA256` can optionally pin the exact input bytes.

## Pinned tool inputs

- ILSpyCmd: `11.0.0.9375`
- Bundled .NET runtime: `10.0.11` win-x64
- Microsoft .NET Framework reference assemblies: `net40 1.0.3`
- Microsoft XNA Framework Redistributable: `4.0 Refresh`

## Files

- `Invoke-TerrariaDecompile.ps1` — two-pass maintainer decompile pipeline.
- `Prepare-References.ps1` — obtains/extracts the online reference set.
- `Audit-Decompile.ps1` — artifact audit for the maintainer path.
- `offline/Build-OfflineBundle.ps1` — assembles the self-contained Windows bundle.
- `offline/Invoke-Offline.ps1` — network-free runtime decompiler.
- `offline/Audit-Offline.ps1` — Windows PowerShell compatible offline audit.
- `offline/ContentPipelineStub/` — public metadata compatibility shim; no Microsoft Content Pipeline implementation code.
- `DGD.md` — short operational instructions.
