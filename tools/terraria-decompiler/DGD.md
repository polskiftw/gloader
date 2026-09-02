# DGD — Terraria Decompiler

## I just want the good source

Use **TerrariaDecompilerOffline-win-x64.zip** from the **Terraria Decompiler Offline Bundle** GitHub Action/release.

You do **not** need to install ILSpy, .NET 10, PowerShell 7, 7-Zip, XNA, or reference packs.

1. Extract the ZIP.
2. Drag `Terraria.exe` onto `RUN-DECOMPILER.cmd`.
3. Open `output\` when it finishes.

If Terraria is installed in the normal Steam location, you can also just double-click `RUN-DECOMPILER.cmd`.

The source is:

`output\source\`

The source ZIP is:

`output\TerrariaDecomp-<detected-version>-clean.zip`

The audit is:

`output\audit\audit.md`

The bundle does **not** fetch dependencies while you use it. Everything ILSpy needs is already inside the bundle.

## Terraria updated

Run the same offline bundle against the new `Terraria.exe`.

The version is detected automatically and used in the new source ZIP filename. Check `audit\audit.md` afterward. Zeroes are good; if a future Terraria version adds a new dependency and the audit stops being clean, update the decompiler bundle instead of ignoring the diagnostics.

## Why there is an XNA shim

The normal redistributable XNA runtime DLLs are bundled.

The XNA Game Studio Content Pipeline developer DLL is not redistributed. We provide a tiny metadata-only gloader compatibility DLL instead so ILSpy can resolve the Content Pipeline types Terraria references. It contains no working Content Pipeline implementation.

In Terraria 1.4.5.8 that developer API is referenced only by `Terraria.Testing\FxReader.cs`. The shim can make ILSpy emit redundant casts in that one testing helper; it does not affect worldgen/network/save/gameplay source.

## Maintainer mode

The older script path still exists:

```powershell
pwsh ./tools/terraria-decompiler/Invoke-TerrariaDecompile.ps1 -TerrariaInput 'C:\path\to\Terraria.exe' -OutputDirectory './artifacts/terraria-decompile'
```

That mode is for rebuilding/debugging the tool and **can download dependencies**. It is no longer the recommended everyday local path.

## Phone / GitHub Actions

- **Terraria Decompiler Offline Bundle** builds the portable Windows package.
- **Terraria Decompiler** is the decompile/reference smoke tester. Its public workflow uploads only the audit, not Terraria's decompiled source.

## What a good audit looks like

These should be zero:

- `unknown_result_type`
- `encoded_constructor`
- `ref_cast_artifact`
- `failed_decompile`
- `expected_unknown`
- `invalid_unknown_comparison`
