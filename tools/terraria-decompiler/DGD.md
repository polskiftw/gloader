# DGD — Terraria Decompiler

## I just want the good source

Use **TerrariaDecompilerOffline-win-x64.zip** from the rolling **Terraria Decompiler Offline** GitHub Release.

You do **not** need to install ILSpy, .NET 10, PowerShell 7, 7-Zip, XNA, or reference packs.

1. Extract the ZIP.
2. Double-click `TerrariaDecompiler.exe`.
3. Pick `Terraria.exe` if it was not auto-detected.
4. Pick the output folder.
5. Click **DECOMPILE**.

That is the normal workflow. No dragging EXEs onto CMD files.

The GUI shows the Terraria version, how many sibling DLLs are managed/native, progress, final audit status, and buttons for **Open Output** and **View Audit**. **Show Details** exposes the captured engine log when needed.

The selected output folder contains:

`source\`

`TerrariaDecomp-<detected-version>-clean.zip`

`audit\audit.md`

`audit\audit.json`

`audit\reference-sources.json`

The bundle does **not** fetch dependencies while you use it.

## What it does with the Terraria folder

Before decompiling, it scans DLLs sitting next to `Terraria.exe`.

- Managed .NET DLLs are copied into a temporary ILSpy reference directory.
- Native DLLs are ignored for ILSpy type resolution and recorded in `reference-sources.json`.
- The genuine `Microsoft.Xna.Framework.Content.Pipeline.dll` that current Terraria ships is used directly from your own install.
- There is **no Content Pipeline shim** in the bundle.

Then it extracts Terraria's embedded managed DLLs and runs the clean second ILSpy pass.

Nothing harvested from your Terraria install is added to the generated source ZIP.

## Terraria updated

Open the same `TerrariaDecompiler.exe` and point it at the updated `Terraria.exe`.

The version is detected automatically. New managed sibling DLLs should be picked up automatically. Check the final GUI audit status or `audit\audit.md`. Zeroes are good; if a future Terraria version adds something the tool still cannot resolve, update the decompiler bundle instead of ignoring the diagnostics.

## Internal / maintainer mode

The GUI launches `Run-TerrariaDecompiler.ps1` internally. You normally never touch it.

The older repository maintainer path still exists:

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 -TerrariaInput 'C:\path\to\Terraria.exe' -OutputDirectory './artifacts/terraria-decompile'
```

That mode also harvests sibling managed DLLs first, but it is for rebuilding/debugging the tool and **can download fallback dependencies**.

## Phone / GitHub Actions

- **Terraria Decompiler Offline Bundle** builds, self-tests, and republishes the GUI Windows package.
- **Terraria Decompiler** is the decompile/reference smoke tester. Its public workflow uploads only the audit, not Terraria's decompiled source.

## What a good audit looks like

These should be zero:

- `unknown_result_type`
- `encoded_constructor`
- `ref_cast_artifact`
- `failed_decompile`
- `expected_unknown`
- `invalid_unknown_comparison`
