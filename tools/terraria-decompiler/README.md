# Terraria Decompiler

A reproducible ILSpy pipeline for Terraria's Windows `.exe`.

The problem this tool solves is not ILSpy itself. Terraria references Microsoft XNA 4.0 and embeds several managed dependency DLLs inside `Terraria.exe`. If ILSpy is given only the EXE, unresolved references can produce enormous numbers of misleading decompiler diagnostics and malformed expressions.

This tool performs two passes:

1. A bootstrap ILSpy pass extracts Terraria's embedded managed DLL resources.
2. A clean pass runs ILSpy again with those DLLs, the exact XNA Game Studio 4.0 Refresh assemblies, and Microsoft's .NET Framework 4.0 reference assemblies available through `--referencepath`.

It then writes a machine-readable and human-readable audit of common decompiler artifacts.

## Local use

Requires Windows and .NET 10.

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 `
  -TerrariaInput 'C:\Games\Terraria\Terraria.exe' `
  -OutputDirectory './artifacts/terraria-decompile' `
  -ExpectedVersion '1.4.5.8'
```

`TerrariaInput` may also be a ZIP containing `Terraria.exe`.

The clean source is written to `source/`, the audit to `audit/`, and a clean source ZIP is created beside them.

## GitHub Actions

`.github/workflows/terraria-decompiler.yml` has two modes:

- With no Terraria URL, it performs a dependency smoke test. This is what runs automatically when the tool changes.
- With a Terraria URL, it downloads the EXE/ZIP, performs the full decompile, and uploads **only the audit report**. The decompiled game source is intentionally not published as an artifact from this public repository.

For private input URLs, prefer the repository secret `TERRARIA_BINARY_URL` instead of a workflow input, because workflow input values can be visible in run metadata. An optional `TERRARIA_BINARY_SHA256` secret can pin the exact input bytes.

## Versions currently pinned

- ILSpyCmd: `11.0.0.9375`
- Microsoft XNA Game Studio: `4.0 Refresh`
- Microsoft .NET Framework reference assemblies: `net40 1.0.3`

## Files

- `Prepare-References.ps1` — obtains and extracts .NET 4.0 + XNA reference assemblies.
- `Invoke-TerrariaDecompile.ps1` — two-pass clean decompile pipeline.
- `Audit-Decompile.ps1` — counts common reconstruction/missing-reference artifacts.
- `DGD.md` — short operational instructions.
