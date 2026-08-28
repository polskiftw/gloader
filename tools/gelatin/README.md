# Gelatin 0.1.0

Gelatin is a standalone Windows 11 x64 editor for preparing images as deformable `.gel` assets. It does not need Terraria or GLoader, and it never launches or modifies either program.

The compact workflow is:

```text
prepare image -> place jello cores / paint rigidity -> abuse it in the Lab -> save .gel
```

## Run the packaged app

1. Download the `gelatin-0.1.0-win-x64` Actions artifact.
2. Extract `gelatin-0.1.0-win-x64.zip` to any normal folder.
3. Run `Gelatin.exe`.

The package is self-contained. A separate .NET installation is not required.

## Workspaces

### Asset

- Open or drag/drop PNG, JPEG, WebP, and `.gel` files.
- Draw and apply a crop rectangle. Existing normalized cores and rigidity strokes are remapped; elements wholly outside the crop are removed.
- Resize with high-quality sampling and an optional aspect-ratio lock.
- Trim transparent edges using the editable alpha threshold.
- Pick a background color with the eyedropper, preview tolerance and feather changes live, then apply or cancel.
- Export the current processed PNG or pretty-printed JSON for inspection.

The canvas has a transparency checkerboard. Use the mouse wheel to zoom and middle/right drag to pan.

### Gel

- Create axis-aligned elliptical jello cores by dragging.
- Select, move, resize, duplicate, delete, name, and numerically edit cores.
- Edit per-core mass, coupling, damping, local softness, and influence falloff.
- Paint or erase gradient rigidity strokes with adjustable radius and strength.
- Toggle core, combined influence heatmap, and rigidity overlays.
- Tune Softness, Damping, Area preservation, Shape memory, Bend resistance, Max stretch, and optional self-collision.

Core coordinates, radii, and rigidity stroke points are normalized, so resizing the image does not invalidate the programming.

### Lab

The Lab runs the same UI-independent XPBD solver used to interpret the saved material and core configuration. The PNG is texture-mapped over the live triangulated mesh; it is not a scaled rectangle animation.

- Drag a local point and release to throw the gel.
- Use directional **SMACK** controls for large impulses.
- Enable **Hammer** and click a side of the gel for a localized inward hit.
- Toggle gravity, pause, reset, and run at 0.1x, 0.25x, 0.5x, or 1x.
- Inspect mesh, core, heatmap, rigidity, alpha contour/contact, and velocity diagnostics independently.
- Use Clean/game view to hide every diagnostic.

Quality presets:

| Preset | Mesh target | Physics | Iterations | Self collision |
|---|---:|---:|---:|---:|
| Sane | ~24x24 | 240 Hz | 8 | every 2 substeps |
| High | ~32x32 | 480 Hz | 12 | every substep |
| Overkill | ~48x48 | 720 Hz | 16 | every substep |
| Claire | ~64x64 | 960 Hz | 24 | every substep with denser contour |

Extreme aspect ratios keep roughly equivalent vertex counts while avoiding degenerate cells.

## Shortcuts

Global:

| Shortcut | Action |
|---|---|
| `Ctrl+O` | Open image or `.gel` |
| `Ctrl+S` | Save current `.gel` |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |

Lab (when a text or numeric field is not active):

| Shortcut | Action |
|---|---|
| `Space` | Pause/resume |
| `R` | Reset |
| `H` | Toggle Hammer |
| `M` | Toggle mesh |

Gelatin tracks dirty state and prompts before discarding unsaved work. Undo/redo covers image operations, material changes, core editing, and rigidity editing through a bounded snapshot history.

## GEL1 format

`.gel` is deliberately unencrypted, uncompressed at the container level, and free of external paths:

```text
Offset  Size  Meaning
0       4     ASCII GEL1
4       4     JSON byte length (uint32 little-endian)
8       4     PNG byte length (uint32 little-endian)
12      N     UTF-8 JSON without BOM
12+N    M     exact PNG bytes
```

The loader rejects incorrect magic, unsafe or impossible lengths, truncation, trailing bytes, invalid UTF-8/JSON, unsupported schema versions, invalid PNG data, and dimension mismatches. Saves are atomic. The complete JSON schema is in `gel.schema.json`.

Lab-only state—gravity, chamber size, simulation quality/speed, pause state, velocity, deformation, and editor pan/zoom—is not serialized.

## Source layout

```text
tools/gelatin/
  Gelatin.sln
  src/Gelatin.Core/   format, image operations, authoring math, contour and XPBD physics
  src/Gelatin.App/    Avalonia desktop UI and Skia textured rendering
  tests/Gelatin.Tests/
  scripts/publish.ps1
```

`Gelatin.Core` contains no Avalonia window/control types and has no Terraria, XNA, MonoGame, or GLoader dependency.

## Build and test

Requirements: .NET 10 SDK. From `tools/gelatin/`:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = "1"
dotnet restore Gelatin.sln
dotnet build Gelatin.sln -c Release --no-restore
dotnet test Gelatin.sln -c Release --no-build
```

## Publish

From the repository root on Windows:

```powershell
tools\gelatin\scripts\publish.ps1
```

Outputs:

```text
tools/gelatin/dist/gelatin/Gelatin.exe
tools/gelatin/dist/gelatin-0.1.0-win-x64.zip
```

The dedicated `.github/workflows/gelatin.yml` workflow performs restore, Release build, tests, self-contained Windows x64 publish, verification, and artifact upload without changing the GLoader package.
