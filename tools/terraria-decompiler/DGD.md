# DGD — Terraria Decompiler

## I just want the good source

Download **`TerrariaDecompilerOffline-win-x64.zip`** from the latest gloader GitHub release. It is the optional tool download beside the normal gloader ZIP.

You do **not** need to install ILSpy, .NET 10, PowerShell 7, 7-Zip, XNA, or reference packs.

1. Extract the ZIP.
2. Double-click `TerrariaDecompiler.exe`.
3. Pick `Terraria.exe` if it was not auto-detected.
4. Pick the output folder.
5. Click **DECOMPILE BOTH**.

`TerrariaServer.exe` is picked up automatically from the same Terraria folder. The GUI refuses to start if the server EXE is missing or its file version does not match the client.

## What you get

The selected output folder has three visible top-level outputs:

```text
client\
server\
audit\
```

Client:

```text
client\source\
client\TerrariaClientDecomp-<version>-clean.zip
```

Server:

```text
server\source\
server\TerrariaServerDecomp-<version>-clean.zip
```

Audit:

```text
audit\audit.md
audit\audit.json
audit\reference-sources.json
audit\client\audit.md
audit\client\audit.json
audit\server\audit.md
audit\server\audit.json
```

The top-level audit combines both targets. **0 tracked issues means both client and server are clean in the tracked categories.**

## Why client and server are separate passes

Both targets start from the same install-folder/framework reference baseline, but each gets its own temporary reference directory.

Client flow:

`Terraria.exe -> recover client embedded DLLs -> clean client pass -> client audit`

Server flow:

`TerrariaServer.exe -> recover server embedded DLLs -> clean server pass -> server audit`

We do **not** let DLLs recovered from one executable leak into the other target's reference set. That keeps the audit honest.

## Terraria folder DLLs

Before either target runs, the tool scans DLLs beside `Terraria.exe` and `TerrariaServer.exe`.

- Managed .NET DLLs become temporary ILSpy references.
- Native DLLs are ignored for CLR type resolution and recorded in `reference-sources.json`.
- The genuine `Microsoft.Xna.Framework.Content.Pipeline.dll` shipped with current Terraria is used directly from your install.
- There is **no Content Pipeline shim** in the bundle.

Nothing harvested from your Terraria install is added to the generated source ZIPs.

## Terraria updated

Open the same program after an update and point it at the new `Terraria.exe`.

The version is detected automatically. The matching `TerrariaServer.exe` is found beside it. If the pair does not match, fix/update the Terraria installation first instead of generating mixed-version source.

## Server testing in CI

The offline-bundle GitHub workflow also downloads Re-Logic's latest public PC dedicated-server archive from `terraria.org`, extracts the Windows `TerrariaServer.exe`, performs a real server-only decompile, and requires a zero-issue tracked audit before the bundle is attached to the gloader release.

The temporary decompiled server source is not published.

## Internal / maintainer mode

The GUI launches `Run-TerrariaDecompiler.ps1` internally. Normal use does not require touching it.

Its default mode is the client/server pair. CI can use `-TargetMode Server` to test a public dedicated-server package by itself.

The older online/bootstrap maintainer scripts remain in the repository for rebuilding references and debugging dependency changes.

## What a good combined audit looks like

`audit\audit.json` should report:

```text
total_tracked_issues = 0
```

These combined categories should also be zero:

- `unknown_result_type`
- `encoded_constructor`
- `ref_cast_artifact`
- `failed_decompile`
- `expected_unknown`
- `invalid_unknown_comparison`
- the three older-guide signature checks
