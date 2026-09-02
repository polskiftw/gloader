# DGD — Terraria Decompiler

## I just want the good source

Use **TerrariaDecompilerOffline-win-x64.zip** from the rolling **Terraria Decompiler Offline** GitHub Release.

You do **not** need to install ILSpy, .NET 10, PowerShell 7, 7-Zip, XNA, or reference packs.

1. Extract the ZIP.
2. Drag `Terraria.exe` **or the whole Terraria install folder** onto `RUN-DECOMPILER.cmd`.
3. Open `output\` when it finishes.

If Terraria is installed in the normal Steam location, you can also just double-click `RUN-DECOMPILER.cmd`.

The source is:

`output\source\`

The source ZIP is:

`output\TerrariaDecomp-<detected-version>-clean.zip`

The audit is:

`output\audit\audit.md`

The reference report is:

`output\audit\reference-sources.json`

The bundle does **not** fetch dependencies while you use it.

## What it does with the Terraria folder

Before decompiling, it scans DLLs sitting next to `Terraria.exe`.

- Managed .NET DLLs are copied into a temporary ILSpy reference directory.
- Native DLLs are ignored for ILSpy type resolution and recorded in `reference-sources.json`.
- The genuine `Microsoft.Xna.Framework.Content.Pipeline.dll` that current Terraria ships is used directly from your own install.
- There is **no Content Pipeline shim** in the bundle anymore.

Then it extracts Terraria's embedded managed DLLs and runs the clean second ILSpy pass.

Nothing harvested from your Terraria install is added to the generated source ZIP.

## Terraria updated

Run the same offline bundle against the new install / new `Terraria.exe`.

The version is detected automatically and used in the new source ZIP filename. New managed sibling DLLs should be picked up automatically. Check `audit\audit.md` afterward. Zeroes are good; if a future Terraria version adds some dependency the tool still cannot resolve, update the decompiler bundle instead of ignoring the diagnostics.

## Maintainer mode

The older script path still exists:

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 -TerrariaInput 'C:\path\to\Terraria.exe' -OutputDirectory './artifacts/terraria-decompile'
```

That mode now also harvests sibling managed DLLs first, but it is for rebuilding/debugging the tool and **can download fallback dependencies**. It is not the recommended everyday local path.

## Phone / GitHub Actions

- **Terraria Decompiler Offline Bundle** builds and republishes the portable Windows package.
- **Terraria Decompiler** is the decompile/reference smoke tester. Its public workflow uploads only the audit, not Terraria's decompiled source.

## What a good audit looks like

These should be zero:

- `unknown_result_type`
- `encoded_constructor`
- `ref_cast_artifact`
- `failed_decompile`
- `expected_unknown`
- `invalid_unknown_comparison`
