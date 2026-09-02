# DGD — Terraria Decompiler

## I just want the good source

On Windows, from the gloader repo:

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 -TerrariaInput 'C:\path\to\Terraria.exe' -OutputDirectory './artifacts/terraria-decompile'
```

That is it. The script downloads/extracts the reference assemblies, runs ILSpy twice, and leaves the clean source in:

`artifacts/terraria-decompile/source/`

The ZIP is:

`artifacts/terraria-decompile/TerrariaDecomp-<version>-clean.zip`

The error/artifact audit is:

`artifacts/terraria-decompile/audit/audit.md`

## I am on my phone

Run the **Terraria Decompiler** GitHub Action.

- No binary URL = dependency smoke test only.
- Binary URL/secret supplied = full temporary decompile + audit.
- The public repo workflow deliberately uploads only the audit, not Terraria's decompiled source.

For a private/signed binary URL, put it in the repository secret:

`TERRARIA_BINARY_URL`

Optional exact-byte check:

`TERRARIA_BINARY_SHA256`

## What a good audit looks like

The important counters are:

- `unknown_result_type` — ideally 0 or very small.
- `encoded_constructor` — should be 0.
- `ref_cast_artifact` — should be 0.
- `failed_decompile` — should be 0.

A huge `unknown_result_type` count usually means references were missing during decompilation, not that Terraria contains broken IL.
